"""Bounded local approximate policy iteration, with transactional CPU continuation.

Only terminal, committed training shards enter SGD. Native job seeds identify a
restart from the beginning of an unfinished match, never a fabricated reward.
Replay likelihoods are conditional branch probabilities and are not IS weights.
"""
from dataclasses import asdict, dataclass
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import random
import signal
import subprocess
import sys
import threading
import time
import numpy as np
import torch
from torch.nn import functional as F
from .checkpoint import save_checkpoint, load_checkpoint
from .device import DeviceReport, choose_device, benchmark_batch
from .export import export_model, validate_artifact, tensor_specs
from .models import HybridNet, BoundaryNet
from .replay import ReplayStore, ARRAYS
from .worker import run_job

ROLES = {'training':0, 'tuning':1, 'screening':2, 'promotion':3}
SPLITS = {'train':'training', 'validation':'tuning', 'test':'screening'}
ATTEMPT_LIMITS = ('duration_seconds','memory_bytes','disk_bytes')
RESERVE_BYTES = 32 << 20  # serialization, replacement checkpoint and export


@dataclass(frozen=True)
class TrainConfig:
    device: str = 'auto'
    seed: int = 1
    workers: int = 2
    max_memory_bytes: int = 2147483648
    max_disk_bytes: int = 5368709120
    duration_seconds: float = 1800
    checkpoint_seconds: float = 300
    learning_rate: float = .0003
    loss_weights: tuple = (1., .5, .25, .25)
    batch_size: int = 64
    max_updates: int = 1000
    generation_matches: int = 1
    execution_mode: str = 'seeded_timed'
    recent_fraction: float = .5
    registry_root: str | None = None
    registry_enabled: bool = True

    def __post_init__(self):
        if type(self.registry_enabled) is not bool: raise ValueError('registry_enabled must be boolean')
        if self.device not in ('auto','cpu','mps') or not 1 <= self.workers <= 2:
            raise ValueError('device or worker limit invalid')
        if self.execution_mode not in ('fixed_seeded','seeded_timed'):
            raise ValueError('invalid execution mode')
        if not 0 <= self.seed < (1 << 40) or any(type(x) is not int or x < 1 for x in
                (self.batch_size,self.max_updates,self.generation_matches,self.max_memory_bytes,self.max_disk_bytes)):
            raise ValueError('positive integer limits and bounded seed required')
        if not 0 < self.duration_seconds <= 86400 or not 0 < self.checkpoint_seconds <= 300:
            raise ValueError('bounded duration and checkpoint interval <= 300 required')
        if not math.isfinite(self.learning_rate) or self.learning_rate <= 0 or len(self.loss_weights)!=4 or any(not math.isfinite(w) or w<0 for w in self.loss_weights):
            raise ValueError('invalid optimization configuration')
        if not 0 <= self.recent_fraction <= 1:
            raise ValueError('invalid replay mixture')


@dataclass(frozen=True)
class CycleReport:
    status: str
    export_manifest: str | None
    rows_seen: int
    updates: int
    device_report: DeviceReport
    duration_seconds: float


def seed_for(seed, index, role):
    """Disjoint modulo-four namespaces; no promotion rows belong in replay."""
    if role not in ROLES or type(index) is not int or index < 0:
        raise ValueError('invalid seed allocation')
    value = 4*(seed+index)+ROLES[role]
    if not 0 <= value <= 9007199254740991:
        raise ValueError('seed exceeds exact native JSON range')
    return value


def teacher_eligible(metadata):
    if metadata.get('exploration_selected', True) or metadata.get('behavior_kind') == 'exploration':
        return False
    kind = metadata.get('teacher_kind')
    if kind == 'baseline':
        return metadata.get('behavior_kind') == 'baseline'
    return kind == 'search' and metadata.get('behavior_kind') == 'search' and bool((metadata.get('search') or {}).get('balanced'))


def unknown_ownership_mask(features, remaining):
    """Exclude all directly known own/recall cards and every public played card."""
    features = np.asarray(features)
    known = features[:,27:51] != 0
    for start in (51,76,101):
        known |= (features[:,start:start+24] != 0) & (features[:,start+24:start+25] != 0)
    for start in [204+29*i for i in range(4)] + [329+125*t+29*i for t in range(6) for i in range(4)]:
        known |= (features[:,start+5:start+29] != 0) & (features[:,start:start+1] != 0)
    return np.asarray(remaining,dtype=bool) & ~known


def training_losses(raw, batch):
    """No score-delta value target and no behavior-probability importance weight."""
    device = raw['win_logit'].device
    def tensor(key, dtype=None): return torch.tensor(np.asarray(batch[key]),dtype=dtype,device=device)
    wins = tensor('win_target',torch.float32)
    if not torch.all((wins == 0) | (wins == 1)):
        raise ValueError('win labels must be exact terminal team victories')
    legal = tensor('legal',torch.bool)
    if not legal.any(-1).all(): raise ValueError('empty legal training mask')
    target = tensor('policy_target',torch.float32)
    eligible = tensor('teacher_eligible',torch.bool)
    weights = tensor('policy_weight',torch.float32)*eligible
    logits = raw['policy_logits'].masked_fill(~legal,-1e9)
    policy = (-(target*F.log_softmax(logits,-1)).sum(-1)*weights).sum()/weights.sum().clamp_min(1)
    mask = torch.tensor(unknown_ownership_mask(batch['features'],batch['ownership_mask']),device=device)
    owners = tensor('ownership_target',torch.long)
    if torch.any((owners[mask]<0)|(owners[mask]>3)): raise ValueError('invalid ownership classes')
    ownership = raw['ownership_logits'].reshape(-1,24,4)
    owner_loss = F.cross_entropy(ownership[mask],owners[mask]) if mask.any() else ownership.sum()*0
    return dict(policy=policy,win=F.binary_cross_entropy_with_logits(raw['win_logit'].flatten(),wins),
                trick=F.cross_entropy(raw['trick_logits'],tensor('trick_target',torch.long)),ownership=owner_loss)


def _processes():
    result = subprocess.run(['ps','-axo','pid=,ppid=,rss=,comm='],capture_output=True,text=True,check=True,timeout=2)
    return [(int(p[0]),int(p[1]),int(p[2])*1024,p[3]) for line in result.stdout.splitlines() if len(p:=line.split(None,3))==4]


def memory_bytes():
    # Current RSS, not lifetime high-water RSS. Count MPS driver allocations
    # conservatively in addition to host RSS on Apple's unified memory.
    rss = next(row[2] for row in _processes() if row[0] == os.getpid())
    if torch.backends.mps.is_available():
        rss += torch.mps.driver_allocated_memory()
    return rss


def _disk_bytes(output, replay):
    roots = [Path(output).resolve(),replay.root.resolve()]
    if roots[0].is_relative_to(roots[1]): roots = roots[1:]
    elif roots[1].is_relative_to(roots[0]): roots = roots[:1]
    return sum(p.stat().st_size for root in roots if root.exists() for p in root.rglob('*') if p.is_file())


def _capacity(config, replay, output):
    return memory_bytes()+RESERVE_BYTES <= config.max_memory_bytes and _disk_bytes(output,replay)+RESERVE_BYTES <= config.max_disk_bytes


def versions():
    return dict(python=sys.version,torch=torch.__version__,numpy=np.__version__,platform=platform.platform(),
                machine=platform.machine(),cpu_threads=torch.get_num_threads())


def _rng():
    return dict(python=random.getstate(),numpy=np.random.get_state(),torch=torch.get_rng_state(),
                mps=torch.mps.get_rng_state() if torch.backends.mps.is_available() else None)


def _restore_rng(state):
    random.setstate(state['python']); np.random.set_state(state['numpy']); torch.set_rng_state(state['torch'])
    if state['mps'] is not None and torch.backends.mps.is_available(): torch.mps.set_rng_state(state['mps'])


def _cpu_copy(value):
    if isinstance(value,torch.Tensor): return value.detach().cpu().clone()
    if isinstance(value,dict): return {key:_cpu_copy(v) for key,v in value.items()}
    if isinstance(value,(list,tuple)): return type(value)(_cpu_copy(v) for v in value)
    return copy.deepcopy(value)


def _initial(config):
    random.seed(config.seed); np.random.seed(config.seed % (1 << 32)); torch.manual_seed(config.seed)
    return dict(version=1,config=asdict(config),versions=versions(),generation=0,next_job=0,pending_job=None,
                native_rng={'restart':'pre_job_seed'},split_manifest={'mapping':SPLITS,'seed_rule':'4*(run_seed+job_index)+role_offset',
                'offsets':ROLES,'promotion_replay':False},population=None,rng=_rng(),updates=0,rows_seen=0,
                replay=None,export_manifest=None,stage='generate',resources={'configured_workers':config.workers,'active_workers':1,
                'training_processes':1,'staged':True,'peak_memory_bytes':0})


def _state(config, output):
    path = Path(output)/'checkpoint.pt'
    if not path.exists(): return _initial(config)
    state = load_checkpoint(path)
    if state.get('version') != 1: raise ValueError('unsupported training checkpoint')
    # Runtime budgets may change; semantics and deterministic batch partition may not.
    for key in ('seed','learning_rate','loss_weights','batch_size','execution_mode','recent_fraction'):
        if state['config'][key] != asdict(config)[key]: raise ValueError('resume configuration mismatch: '+key)
    if state['versions']['torch'] != torch.__version__ or state['versions']['numpy'] != np.__version__:
        raise ValueError('resume requires the recorded PyTorch/NumPy versions')
    if state.get('pending_job'):
        for key in ATTEMPT_LIMITS: state['pending_job'].pop(key,None)
    _restore_rng(state['rng'])
    return state


def _snapshot(replay, config):
    shards=[]; seeds={}
    for directory in sorted(replay.root.iterdir()):
        if not directory.is_dir() or directory.name.startswith('.'): continue
        manifest=replay._validate(directory)
        job=manifest['job']; role=SPLITS.get(job['split']); seed=job['seed']
        if role is None or seed % 4 != ROLES[role]:
            raise ValueError('replay seed violates disjoint training/tuning/screening/promotion namespace')
        if seed in seeds and seeds[seed] != role: raise ValueError('seed appears in another split')
        seeds[seed]=role
        if role != 'training': continue
        shards.append(dict(name=directory.name,manifest_sha256=hashlib.sha256((directory/'manifest.json').read_bytes()).hexdigest(),
                           decisions=manifest['footer']['decisions'],boundaries=manifest['footer']['hands']*4,
                           schedule_index=job['schedule_index'],seed=seed))
        if len(shards)>10000: raise MemoryError('replay snapshot capacity')
    shards.sort(key=lambda s:(s['schedule_index'],s['name']))
    recent=max(1,math.ceil(len(shards)/4))
    return dict(shards=shards,recent_names=[s['name'] for s in shards[-recent:]],recent_fraction=config.recent_fraction,
                cursor=0,mixture_counts={'recent':0,'historical':0},last_choice=None)


def _verify_snapshot(replay, snapshot):
    for shard in snapshot['shards']:
        directory=replay.root/shard['name']
        if hashlib.sha256((directory/'manifest.json').read_bytes()).hexdigest()!=shard['manifest_sha256']:
            raise ValueError('frozen replay snapshot changed')
        replay._validate(directory)


def _next_batch(replay, snapshot, batch_size):
    # Transactional caller passes a copy: interruption must not advance the cursor.
    recent=[s for s in snapshot['shards'] if s['name'] in snapshot['recent_names']]
    history=[s for s in snapshot['shards'] if s['name'] not in snapshot['recent_names']]
    group='recent' if random.random()<snapshot['recent_fraction'] or not history else 'historical'
    pool=recent if group=='recent' else history
    shard=random.choice(pool)
    boundary=bool(snapshot['cursor']%2)
    size=shard['boundaries' if boundary else 'decisions']
    start=random.randrange(math.ceil(size/batch_size))*batch_size
    stop=min(size,start+batch_size); directory=replay.root/shard['name']
    keys=[k for k in ARRAYS if k.startswith('boundary_')==boundary]
    batch={k:np.load(directory/(k+'.npy'),mmap_mode='r',allow_pickle=False)[start:stop] for k in keys}
    if not boundary:
        eligible=[]
        with (directory/'metadata.jsonl').open() as stream:
            for index,line in enumerate(stream):
                if index>=stop: break
                if index>=start: eligible.append(teacher_eligible(json.loads(line)['metadata']))
        if len(eligible)!=stop-start: raise ValueError('missing decision metadata')
        batch['teacher_eligible']=np.asarray(eligible,dtype=bool)
    snapshot['cursor']+=1; snapshot['mixture_counts'][group]+=stop-start
    snapshot['last_choice']=dict(shard=shard['name'],boundary=boundary,start=start,stop=stop,group=group)
    return batch


def _save(state, output):
    save_checkpoint(state,Path(output)/'checkpoint.pt')


def _population(config, state):
    from .registry import Registry, discover_registry, adapter_options, file_hash, canonical, digest
    if state.get('population_generation')==state['generation']:
        if config.registry_root and str(Path(config.registry_root).resolve())!=state['population_lineage'].get('registry_root'):
            raise ValueError('resume registry differs from frozen population')
        return state['population']
    def model_member(record, previous=False):
        return dict(id=record['id'],kind='model',version=record['id'],model_path=record['manifest'],
            model_sha256=record['artifact']['model_sha256'],model_manifest_sha256=file_hash(record['manifest']),previous_champion=previous)
    root=discover_registry(config.registry_root) if config.registry_enabled else None; baseline_options={}
    if root:
        if config.execution_mode=='fixed_seeded':
            raise ValueError('registry population includes timed C++ baseline; use seeded_timed')
        registry=Registry(root)
        with registry.lock():
            incumbent=registry.current(); baseline=registry.record(registry.frozen['baseline']['id'])
            prior=registry.previous_champions(incumbent['id'])
            base=dict(id=baseline['id'],kind='baseline',version=baseline['source_sha256'])
            population=[base if incumbent['kind']=='baseline' else model_member(incumbent)]
            population.extend(model_member(record,True) for record in prior)
            if incumbent['id']!=baseline['id']: population.append(base)
            options=adapter_options(baseline)
            for option,key,default,low,high in [('--samples','baseline_samples',160,16,4096),
                    ('--play-ms','baseline_play_ms',700,20,5000),('--search-depth','baseline_depth',12,1,30),
                    ('--search-nodes','baseline_nodes',12000,1000,2000000)]:
                baseline_options[key]=max(low,min(high,options.get(option,default)))
            lineage=dict(registry_root=str(root),incumbent=incumbent['id'],previous_champions=[r['id'] for r in prior],
                baseline=baseline['id'],registry_config_sha256=registry.frozen['config_sha256'])
    else:
        base=dict(id='baseline',kind='baseline',version='unchanged') if config.execution_mode=='seeded_timed' else dict(id='bootstrap-fixed',kind='fixed',version='v1')
        population=[base];lineage=dict(registry_root=None,incumbent=base['id'],previous_champions=[],bootstrap=True)
    population.extend(dict(id='variety-'+kind,kind=kind,version='v1') for kind in ('fixed','reverse','uniform'))
    if state['export_manifest']:
        path=Path(state['export_manifest']).resolve();artifact=validate_artifact(path);identity=digest(canonical(artifact))
        population.append(dict(id='learner-'+identity,kind='model',version=identity,model_path=str(path),
            model_sha256=artifact['model_sha256'],model_manifest_sha256=file_hash(path)))
    else:
        population.append(dict(id='learner-bootstrap',kind='uniform',version='v1'))
    state.update(population=population,population_generation=state['generation'],population_lineage=lineage,
        incumbent=0,learner=len(population)-1,population_policy=baseline_options)
    return population


def generate_burst(config: TrainConfig, replay: ReplayStore, output: Path, worker: Path) -> str:
    """One native worker at a time, complete matches only; restart pending jobs."""
    output=Path(output); started=time.monotonic(); deadline=started+config.duration_seconds
    if not _capacity(config,replay,output): return 'capacity'
    output.mkdir(parents=True,exist_ok=True)
    state=_state(config,output); status='complete'
    replay_root=str(replay.root.resolve())
    if state.get('replay_root',replay_root)!=replay_root: raise ValueError('resume replay root differs')
    state['replay_root']=replay_root
    # An interrupted train resumes training; generation is not repeated implicitly.
    if state['stage']=='train': return 'complete'
    if 'burst_remaining' not in state: state['burst_remaining']=config.generation_matches
    while state['burst_remaining']>0:
        remaining=deadline-time.monotonic()
        if remaining<1: return 'duration'
        if not _capacity(config,replay,output): return 'capacity'
        if state['pending_job'] is None:
            index=state['next_job']; population=_population(config,state)
            seed=seed_for(config.seed,index,'training')
            job=dict(job_id=f'run-{config.seed}-generation-{state["generation"]}-job-{index}',seed=seed,split='train',
                     schedule_index=index,population=population,learner=state['learner'],incumbent=state['incumbent'],execution_mode=config.execution_mode,
                     max_hands=1000,max_decisions=20000,workers=1,training_processes=1,device='cpu',
                     dealer=0,exploration=.1,search_fraction=.25,play_search_fraction=.05,decision_ms=3000,stop_ms=3500,
                     max_simulations=None,baseline_samples=160,baseline_play_ms=700,baseline_depth=12,baseline_nodes=12000)
            job.update(state['population_policy'])
            if config.execution_mode=='fixed_seeded': job.update(search_fraction=0.,play_search_fraction=0.)
            state=copy.deepcopy(state); state.update(pending_job=job,population=population,native_rng={'pre_job_seed':seed},stage='generate')
            _save(state,output)  # durable pre-job point, before any native random draw
        job=state['pending_job']; spool=output/'pending-match.jsonl'
        # A complete, validated shard may have committed just before interruption.
        shard=replay.root/hashlib.sha256(job['job_id'].encode()).hexdigest()
        if shard.exists():
            manifest=replay._validate(shard)
            for key,value in job.items():
                if key=='population':
                    saved=manifest['job'][key]
                    if len(saved)!=len(value) or any(any(member.get(k)!=v for k,v in requested.items()) for member,requested in zip(saved,value)):
                        raise ValueError('pending job conflict')
                elif manifest['job'].get(key)!=value: raise ValueError('pending job conflict')
        else:
            # Only the scheduler-owned unfinished spool is discarded. No champion
            # or committed shard is ever evicted to satisfy a capacity limit.
            spool.unlink(missing_ok=True); spool.with_name(spool.name+'.status.json').unlink(missing_ok=True)
            spool.with_name(spool.name+'.status.json.pending').unlink(missing_ok=True)
            allowance=dict(duration_seconds=max(1,int(deadline-time.monotonic())),
                memory_bytes=max(1,config.max_memory_bytes-memory_bytes()-RESERVE_BYTES),
                disk_bytes=max(1024,(config.max_disk_bytes-_disk_bytes(output,replay)-RESERVE_BYTES)//3))
            state['last_attempt']=dict(number=state.get('last_attempt',{}).get('number',0)+1,
                job_id=job['job_id'],allowance=allowance,configured=asdict(config))
            _save(state,output)
            attempt_snapshot=copy.deepcopy(state)
            stop=threading.Event(); exceeded=[]; peak=[memory_bytes()]
            def monitor():
                last_save=time.monotonic()
                while not stop.wait(.1):
                    try:
                        if time.monotonic()-last_save>=config.checkpoint_seconds:
                            _save(attempt_snapshot,output); last_save=time.monotonic()
                        rows=_processes(); children=[r for r in rows if r[1]==os.getpid() and Path(r[3]).name==Path(worker).name]
                        total=memory_bytes()+sum(r[2] for r in children); peak[0]=max(peak[0],total)
                        reason='capacity' if total+RESERVE_BYTES>config.max_memory_bytes or _disk_bytes(output,replay)+RESERVE_BYTES>config.max_disk_bytes else 'duration' if time.monotonic()>=deadline else None
                        if reason:
                            exceeded.append(reason)
                            for child in children:
                                try: os.kill(child[0],signal.SIGKILL)
                                except ProcessLookupError: pass
                            return
                    except (OSError,subprocess.SubprocessError):
                        exceeded.append('resource_monitor_failed')
                        return
            watcher=threading.Thread(target=monitor,daemon=True); watcher.start()
            try:
                result=run_job(dict(job,**allowance),worker,spool)
                status=exceeded[0] if exceeded else result.status
                state['resources']['worker_reported_memory_bytes']=max(result.reported_memory_bytes,state['resources'].get('worker_reported_memory_bytes',0))
                if status=='complete':
                    replay.commit_match(spool)
            except KeyboardInterrupt:
                status='interrupted'
            except OSError as exc:
                if exc.errno!=28: raise
                status='capacity'
            finally:
                stop.set(); watcher.join()  # never race a periodic snapshot with the final checkpoint
            state['resources']['peak_memory_bytes']=max(state['resources']['peak_memory_bytes'],peak[0])
            if status!='complete':
                _save(state,output)
                return status
        state=copy.deepcopy(state); state.update(next_job=state['next_job']+1,pending_job=None,replay=None,burst_remaining=state['burst_remaining']-1)
        _save(state,output)
        spool.unlink(missing_ok=True); spool.with_name(spool.name+'.status.json').unlink(missing_ok=True)
    state['stage']='train'; _save(state,output)
    return 'complete'


def train_cycle(config: TrainConfig, replay: ReplayStore, output: Path) -> CycleReport:
    started=time.monotonic(); output=Path(output)
    device_report=DeviceReport('cpu',True,0.,None,'not selected')
    def report(status,state=None,manifest=None):
        return CycleReport(status,manifest,0 if state is None else state['rows_seen'],0 if state is None else state['updates'],device_report,time.monotonic()-started)
    if config.device=='mps' and not torch.backends.mps.is_available():
        raise ValueError('MPS unavailable on this host')
    if not _capacity(config,replay,output): return report('capacity')
    output.mkdir(parents=True,exist_ok=True)
    torch.set_num_threads(1); torch.use_deterministic_algorithms(True)
    state=_state(config,output)
    replay_root=str(replay.root.resolve())
    if state.get('replay_root',replay_root)!=replay_root: raise ValueError('resume replay root differs')
    state['replay_root']=replay_root
    if state['pending_job'] is not None: raise ValueError('unfinished generation must resume before training')
    try:
        if state['replay'] is None: state['replay']=_snapshot(replay,config)
        else: _verify_snapshot(replay,state['replay'])
    except MemoryError: return report('capacity',state)
    if not state['replay']['shards']: return report('no_training_data',state)
    if 'device_report' in state:
        device_report=DeviceReport(**state['device_report'])
        if config.device not in ('auto',device_report.device): raise ValueError('resume device differs; no cross-device exact continuation')
        if device_report.device=='mps' and not torch.backends.mps.is_available(): raise ValueError('saved MPS device unavailable')
    else:
        probe=benchmark_batch(min(config.batch_size,64)); probe['_max_memory_bytes']=config.max_memory_bytes
        try: device_report=choose_device(config.device,probe)
        except MemoryError: return report('capacity',state)
    device=device_report.device
    main,boundary=HybridNet().to(device),BoundaryNet().to(device)
    optimizer=torch.optim.AdamW([*main.parameters(),*boundary.parameters()],lr=config.learning_rate)
    if 'main' in state:
        main.load_state_dict(state['main']); boundary.load_state_dict(state['boundary']); optimizer.load_state_dict(state['optimizer'])
        _restore_rng(state['rng'])  # model construction must not consume the saved stream
    state.update(main=_cpu_copy(main.state_dict()),boundary=_cpu_copy(boundary.state_dict()),optimizer=_cpu_copy(optimizer.state_dict()),
                 rng=_rng(),device_report=asdict(device_report),stage='train',config=asdict(config))
    _save(state,output)
    last_save=time.monotonic(); status='complete'; steps=0
    try:
        while steps<config.max_updates:
            if time.monotonic()-started>=config.duration_seconds: status='duration'; break
            current=memory_bytes()
            if current+RESERVE_BYTES>config.max_memory_bytes or _disk_bytes(output,replay)+RESERVE_BYTES>config.max_disk_bytes:
                status='capacity'; break
            # Estimate transient activation/gradient tensors conservatively and
            # reduce the requested batch before allocation, preserving RNG choice.
            allowed=(config.max_memory_bytes-current-RESERVE_BYTES)//65536
            if allowed<1: status='capacity'; break
            batch_size=state['resources'].get('actual_batch_size',min(config.batch_size,allowed))
            if allowed<batch_size: status='capacity'; break
            snapshot=copy.deepcopy(state['replay']); batch=_next_batch(replay,snapshot,batch_size)
            optimizer.zero_grad(set_to_none=True)
            if 'features' in batch:
                raw=main(torch.tensor(np.asarray(batch['features']),device=device))
                losses=training_losses(raw,batch)
                loss=sum(weight*losses[name] for weight,name in zip(config.loss_weights,('policy','win','trick','ownership')))
                count=len(batch['features'])
            else:
                target=torch.tensor(np.asarray(batch['boundary_win_target']),device=device)
                if not torch.all((target==0)|(target==1)): raise ValueError('boundary target must be terminal team victory')
                loss=F.binary_cross_entropy_with_logits(boundary(torch.tensor(np.asarray(batch['boundary_features']),device=device)).flatten(),target)
                count=len(batch['boundary_features'])
            if not torch.isfinite(loss): raise ValueError('nonfinite training loss')
            loss.backward(); optimizer.step()
            if device=='mps': torch.mps.synchronize()
            # Commit only a completed optimizer transaction. An interruption in
            # forward/backward/step saves the previous complete state and RNG.
            completed=dict(state,main=_cpu_copy(main.state_dict()),boundary=_cpu_copy(boundary.state_dict()),
                           optimizer=_cpu_copy(optimizer.state_dict()),rng=_rng(),replay=snapshot,updates=state['updates']+1,
                           rows_seen=state['rows_seen']+count,last_loss=float(loss.detach().cpu()),
                           resources=dict(state['resources'],peak_memory_bytes=max(current,memory_bytes(),state['resources']['peak_memory_bytes']),actual_batch_size=batch_size))
            state=completed; steps+=1
            if time.monotonic()-last_save>=config.checkpoint_seconds:
                _save(state,output); last_save=time.monotonic()
    except KeyboardInterrupt: status='interrupted'
    except (MemoryError,torch.OutOfMemoryError): status='capacity'
    finally:
        _save(state,output)
    if status in ('capacity','interrupted'): return report(status,state)
    if not _capacity(config,replay,output): return report('capacity',state)
    # Recover the last committed update even if the live modules were interrupted.
    main.load_state_dict(state['main']); boundary.load_state_dict(state['boundary'])
    destination=output/f'candidate-g{state["generation"]}-u{state["updates"]}'
    metadata=dict(versions=versions(),config=asdict(config),updates=state['updates'],rows_seen=state['rows_seen'],
                  replay_mixture={k:v for k,v in state['replay'].items() if k!='shards'},split_manifest=state['split_manifest'],
                  resources=state['resources'],device_report=asdict(device_report),population_lineage=state.get('population_lineage'),
                  population=state.get('population'),strength_claim=False)
    if destination.exists():
        manifest=destination/'manifest.json'; artifact=validate_artifact(manifest)
        tensors={**{'main.'+k:v for k,v in state['main'].items()},**{'boundary.'+k:v for k,v in state['boundary'].items()}}
        digest=hashlib.sha256()
        for spec in tensor_specs(): digest.update(tensors[spec['name']].numpy().astype('<f4',copy=False).tobytes())
        if artifact['model_sha256']!=digest.hexdigest(): raise ValueError('existing candidate differs from checkpoint weights')
    else: manifest=export_model(main,boundary,metadata,destination)
    state['export_manifest']=str(manifest.resolve()); state['stage']='complete'; _save(state,output)
    return report(status,state,str(manifest.resolve()))
