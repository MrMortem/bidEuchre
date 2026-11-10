"""Frozen paired evaluation schedules and fail-closed Task 3 promotion evidence.

The seed manifest contains registry_root, run_seed, start_index, primary_blocks,
replacement_blocks, bootstrap_seed and (for promotion) verification_report.
Re-run evaluate_and_report with the SAME returned manifest to resume a trial.
"""
import json
import math
import os
from pathlib import Path
import subprocess
import signal
import time
from . import schema
from .registry import Registry, canonical, digest, file_hash, publish, read, reject_test_provenance


def integer(value, name, minimum=0, maximum=2**31-1):
    if type(value) is not int or not minimum<=value<=maximum: raise ValueError('invalid '+name)
    return value


def validate_gate(report):
    if report.get('Gate')!=1 or report.get('CanPromote') is not True or report.get('Complete') is not True or report.get('GateErrors')!=[]:
        raise ValueError('only clean complete promotion evidence is eligible')
    for kind,count in [('Primary',1000),('Replacement',500)]:
        registered=integer(report['Registered'+kind+'Blocks'],kind,count)
        completed=integer(report['Completed'+kind+'Blocks'],kind,count)
        if registered!=completed or (kind=='Primary' and registered!=1000): raise ValueError('incomplete/full-size blocks required')
    legs=2*(report['RegisteredPrimaryBlocks']+report['RegisteredReplacementBlocks'])
    if report['RegisteredLegs']!=legs or report['CompletedLegs']!=legs: raise ValueError('incomplete legs')
    for key,bound in [('PrimaryLower95',.5),('ReplacementLower95',-.02)]:
        value=report[key]
        if type(value) not in (int,float) or not math.isfinite(value) or value<=bound: raise ValueError('strict confidence gate failed')
    conditions=report['Conditions']
    if not conditions or len({c['Id'] for c in conditions})!=len(conditions): raise ValueError('missing/duplicate conditions')
    counts=[integer(c['RegisteredBlocks'],'condition blocks',1) for c in conditions]
    if max(counts)-min(counts)>1 or sum(counts)!=report['RegisteredReplacementBlocks'] or any(c['CompletedBlocks']!=c['RegisteredBlocks'] for c in conditions):
        raise ValueError('replacement conditions are not complete and balanced')


def _schedule(registry,candidate_id,incumbent_id,seeds,mode):
    candidate=registry.record(candidate_id); incumbent=registry.record(incumbent_id)
    if candidate['kind']!='hybrid' or candidate_id==incumbent_id: raise ValueError('distinct hybrid candidate required')
    config=registry.frozen['config']; budget=config['budget']
    for name in ('DecisionMs','StopMs','ProcessMs','StartupMs'): integer(budget[name],name,1)
    if not budget['DecisionMs']<=budget['StopMs']<=budget['ProcessMs']: raise ValueError('invalid budgets')
    primary=integer(seeds['primary_blocks'],'primary blocks',1)
    replacement=integer(seeds['replacement_blocks'],'replacement blocks',1)
    if mode=='promotion' and (primary!=1000 or replacement<500): raise ValueError('promotion requires 1000 primary and >=500 replacement blocks')
    start=integer(seeds['start_index'],'start index'); run=integer(seeds['run_seed'],'run seed')
    bootstrap=integer(seeds['bootstrap_seed'],'bootstrap seed')
    engines={candidate_id:candidate['engine'],incumbent_id:incumbent['engine']}
    alias={'baseline':registry.frozen['baseline']['id'],'incumbent':incumbent_id,'candidate':candidate_id}
    conditions=[]
    for condition in config['conditions']:
        row={'Id':condition['Id']}
        for name in ('PartnerId','Opponent0Id','Opponent1Id'):
            identity=alias.get(condition[name],condition[name]); engines[identity]=registry.record(identity)['engine']; row[name]=identity
        conditions.append(row)
    if not conditions or len({x['Id'] for x in conditions})!=len(conditions): raise ValueError('unique declared conditions required')
    blocks=[]; legs=[]
    for index in range(primary+replacement):
        seed=integer(4*(run+start+index)+(3 if mode=='promotion' else 2),'arena seed')
        is_primary=index<primary; condition=None if is_primary else conditions[(index-primary)%len(conditions)]
        block=f'{"primary" if is_primary else "replacement"}-{index:06d}'
        blocks.append({'Id':block,'Kind':0 if is_primary else 1,'ConditionId':condition['Id'] if condition else None})
        dealer=(index if is_primary else (index-primary)//len(conditions))%4
        for leg in range(2):
            ids=([candidate_id,incumbent_id,candidate_id,incumbent_id] if leg==0 else [incumbent_id,candidate_id,incumbent_id,candidate_id]) if is_primary else [candidate_id if leg==0 else incumbent_id,condition['Opponent0Id'],condition['PartnerId'],condition['Opponent1Id']]
            legs.append({'BlockId':block,'LegId':leg,'Seed':seed,'Dealer':dealer,'Seats':[engines[i] for i in ids],'Budget':budget})
    return {'SchemaFingerprint':schema.SCHEMA_SHA256,'SourceFingerprint':digest(canonical(registry.frozen['source'])),
            'RuleFingerprint':schema.RULE_SHA256,'CandidateId':candidate_id,'IncumbentId':incumbent_id,'Engines':list(engines.values()),
            'Budget':budget,'Conditions':conditions,'Blocks':blocks,'Legs':legs,'Gate':1 if mode=='promotion' else 0,
            'BootstrapSeed':bootstrap,'Format':'arena-manifest-v1'}


def create_evaluation(candidate_id: str, incumbent_id: str, seed_manifest: Path, mode: str) -> Path:
    if mode not in ('screening','promotion'): raise ValueError('mode must be screening or promotion')
    seeds=read(seed_manifest)
    if seeds.get('test_only') or seeds.get('synthetic'): raise ValueError('test provenance is forbidden')
    registry=Registry(Path(seeds['registry_root']))
    with registry.lock():
        if incumbent_id!=registry.current()['id']: raise ValueError('evaluation incumbent must be current champion')
        manifest=_schedule(registry,candidate_id,incumbent_id,seeds,mode)
        verification=Path(seeds['verification_report']).resolve() if seeds.get('verification_report') else None
        descriptor={'registry_root':str(registry.root),'candidate':candidate_id,'incumbent':incumbent_id,
                    'mode':mode,'seed_manifest':seeds,'config_sha256':registry.frozen['config_sha256'],
                    'verification_sha256':file_hash(verification) if verification else None}
        identity=digest(canonical({'manifest':manifest,'descriptor':descriptor}))
        descriptor['id']=identity; directory=registry.root/'evaluations'/identity
        # A screening batch can be reused; promotion trials must reserve fresh seeds.
        if directory.exists():
            if mode=='promotion': raise ValueError('promotion seeds already reserved; resume the existing manifest')
            if read(directory/'manifest.json')!=manifest: raise ValueError('frozen screening manifest changed')
            return directory/'manifest.json'
        if mode=='promotion':
            for leg in manifest['Legs'][::2]:
                reservation=registry.root/'reservations'/str(leg['Seed'])
                try:
                    with reservation.open('x') as stream: stream.write(identity); stream.flush(); os.fsync(stream.fileno())
                except FileExistsError as exc: raise ValueError('promotion seed reused') from exc
        if mode=='promotion':
            fd=os.open(registry.root/'reservations',os.O_RDONLY)
            try: os.fsync(fd)
            finally: os.close(fd)
        directory.mkdir()
        if verification:
            with (directory/'verification.json').open('xb') as stream:
                stream.write(verification.read_bytes()); stream.flush(); os.fsync(stream.fileno())
        publish(directory/'evaluation.json',descriptor); publish(directory/'manifest.json',manifest)
        return directory/'manifest.json'


def _context(manifest):
    manifest=Path(manifest).resolve(); descriptor=read(manifest.parent/'evaluation.json'); registry=Registry(Path(descriptor['registry_root']))
    if manifest!=registry.root/'evaluations'/descriptor['id']/'manifest.json': raise ValueError('unregistered evaluation path')
    expected=_schedule(registry,descriptor['candidate'],descriptor['incumbent'],descriptor['seed_manifest'],descriptor['mode'])
    if read(manifest)!=expected or digest(canonical({'manifest':expected,'descriptor':{k:v for k,v in descriptor.items() if k!='id'}}))!=descriptor['id'] or descriptor['config_sha256']!=registry.frozen['config_sha256']:
        raise ValueError('frozen evaluation differs')
    for path,sha in registry.frozen['authority'].items():
        if file_hash(path)!=sha: raise ValueError('arena authority changed')
    return registry,descriptor,expected


def resource_usage(root, pid):
    rows=[list(map(int,line.split())) for line in subprocess.check_output(['ps','-axo','pid=,ppid=,rss='],text=True,timeout=2).splitlines()]
    owned={pid}; previous=set()
    while owned!=previous:
        previous=set(owned);owned.update(row[0] for row in rows if row[1] in owned)
    memory=sum(row[2]*1024 for row in rows if row[0] in owned or row[0]==os.getpid())
    disk=0
    for path in root.rglob('*'):
        try:
            if path.is_file(): disk+=path.stat().st_size
        except FileNotFoundError: pass  # Arena atomically renames its own temporary files.
    return memory,disk


def _arena(manifest, arena, max_seconds, audit=False):
    integer(max_seconds,'duration seconds',1,1800)
    registry,descriptor,schedule=_context(manifest)
    if Path(arena).resolve()!=Path(registry.frozen['arena']): raise ValueError('unregistered arena executable')
    output=Path(manifest).parent/'run'
    command=[str(arena),'audit' if audit else 'tournament','--manifest',str(manifest),'--output',str(output)]
    if not audit: command+=['--max-duration-seconds',str(max_seconds)]
    resources=registry.frozen['config'].get('resources',{})
    limits={'workers':1,'training_processes':0,'max_memory_bytes':2<<30,'max_disk_bytes':5<<30,'max_seconds':1800,'device':'cpu',**resources}
    if limits['workers']!=1 or limits['training_processes']!=0 or limits['device']!='cpu':
        raise ValueError('evaluation uses one arena job, no trainer, CPU engines')
    for key in ('max_memory_bytes','max_disk_bytes'): integer(limits[key],key,1,2**63-1)
    integer(limits['max_seconds'],'max seconds',1,1800)
    if max_seconds>limits['max_seconds']: raise ValueError('invocation exceeds frozen duration budget')
    peak_memory,disk=resource_usage(registry.root,-1)
    if peak_memory+(32<<20)>limits['max_memory_bytes'] or disk+(32<<20)>limits['max_disk_bytes']:
        publish(Path(manifest).parent/'invocation.json',{'command':command,'status':'capacity','reason':'capacity before launch','resources':limits})
        raise ValueError('evaluation resource capacity exhausted before launch')
    process=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True)
    began=time.monotonic();reason=None;stdout=stderr=""
    try:
        while True:
            try: stdout,stderr=process.communicate(timeout=.25);break
            except subprocess.TimeoutExpired:
                memory,disk=resource_usage(registry.root,process.pid);peak_memory=max(peak_memory,memory)
                if memory+(32<<20)>limits['max_memory_bytes'] or disk+(32<<20)>limits['max_disk_bytes']: reason='resource capacity'
                elif time.monotonic()-began>max_seconds+60: reason='outer duration'
                if reason:
                    os.killpg(process.pid,signal.SIGINT)
                    try: stdout,stderr=process.communicate(timeout=2)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid,signal.SIGKILL);stdout,stderr=process.communicate()
                    break
    except (OSError,subprocess.SubprocessError,KeyboardInterrupt) as exc:
        reason='supervisor interrupted: '+str(exc)
    finally:
        if process.poll() is None:
            os.killpg(process.pid,signal.SIGKILL);stdout,stderr=process.communicate()
    publish(Path(manifest).parent/('audit-invocation.json' if audit else 'invocation.json'),
        {'command':command,'returncode':process.returncode,'stdout':stdout,'stderr':stderr,'reason':reason,
         'peak_memory_bytes':peak_memory,'disk_bytes':disk,'duration_seconds':time.monotonic()-began,'resources':limits})
    if reason or process.returncode: raise ValueError('arena failed: '+(reason or stderr[-4000:]))
    report=output/'report.json'
    # Retain snapshots for failed, incomplete and successful bounded invocations.
    import uuid
    publish(Path(manifest).parent/('report-'+uuid.uuid4().hex+'.json'),read(report))
    return report


def evaluate_and_report(manifest: Path, arena: Path, max_seconds: int) -> Path:
    return _arena(Path(manifest).resolve(),Path(arena).resolve(),max_seconds)


def _verification(registry,candidate,descriptor,path):
    report=read(path)
    if descriptor['verification_sha256'] is None or file_hash(path)!=descriptor['verification_sha256']:
        raise ValueError('verification report was not frozen before evaluation')
    reject_test_provenance(report)
    artifact=candidate['artifact']
    expected={'candidate_id':candidate['id'],'model_sha256':artifact['model_sha256'],
        'manifest_sha256':candidate['id'],'schema_sha256':schema.SCHEMA_SHA256,'rule_sha256':schema.RULE_SHA256,
        'source_sha256':artifact['source_sha256'],'native_source':registry.frozen['source'],
        'binary_sha256':candidate['engine']['ExecutableSha256'],'authority':registry.frozen['authority']}
    if report.get('bindings')!=expected: raise ValueError('verification bindings mismatch')
    checks=report['checks']
    for name in ('privacy','simulator','export'):
        check=checks[name]
        if check.get('passed') is not True or file_hash(check['path'])!=check['sha256']:
            raise ValueError(name+' verification failed or changed')


def verify_promotion(registry, candidate_id, gate_report, verification_report):
    manifest=gate_report.resolve().parent.parent/'manifest.json'
    try: other,descriptor,schedule=_context(manifest)
    except (OSError,KeyError) as exc: raise ValueError('report is not from a registered evaluation') from exc
    if other.root!=registry.root or descriptor['candidate']!=candidate_id or descriptor['incumbent']!=registry.current()['id'] or descriptor['mode']!='promotion':
        raise ValueError('candidate/incumbent/trial mismatch')
    if gate_report.resolve()!=manifest.parent/'run/report.json' or (manifest.parent/'consumed.json').exists(): raise ValueError('unregistered or spent trial')
    arguments=registry.record(candidate_id)['engine']['Arguments']
    for flag,expected in [('--decision-ms',3000),('--stop-ms',3500)]:
        if flag in arguments and (arguments.count(flag)!=1 or arguments[arguments.index(flag)+1]!=str(expected)):
            raise ValueError('native decision budgets differ from production gate')
    if schedule['Budget']['DecisionMs']!=3000 or schedule['Budget']['StopMs']!=3500: raise ValueError('promotion requires production decision budgets')
    for leg in schedule['Legs'][::2]:
        path=registry.root/'reservations'/str(leg['Seed'])
        if not path.exists() or path.read_text()!=descriptor['id'] or leg['Seed']%4!=3: raise ValueError('promotion seed reservation mismatch')
    _verification(registry,registry.record(candidate_id),descriptor,verification_report)
    # Never authorize from caller-provided summary fields: recompute using the
    # frozen C# authority and its persisted immutable results and attempt traces.
    _arena(manifest,Path(registry.frozen['arena']),1,audit=True)
    report=read(gate_report); validate_gate(report)
    if report['ManifestFingerprint']!=file_hash(gate_report.parent/'manifest.json'): raise ValueError('authority fingerprint mismatch')
    frozen_authority_manifest=read(gate_report.parent/'manifest.json')
    if frozen_authority_manifest!=schedule: raise ValueError('authority schedule mismatch')
    if {c['Id'] for c in report['Conditions']}!={c['Id'] for c in schedule['Conditions']}: raise ValueError('condition report mismatch')
    return descriptor['id']
