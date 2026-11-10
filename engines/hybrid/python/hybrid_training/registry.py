"""Local immutable artifacts, frozen engines and an atomically replaced champion pointer.

Create root/config.json before opening a registry. Configuration is frozen on first
open; changing it requires a new registry. Evaluation evidence is never replay data.
"""
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import uuid
from .export import validate_artifact, _provenance


def canonical(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()


def digest(data): return hashlib.sha256(data).hexdigest()
def file_hash(path): return digest(Path(path).read_bytes())
def read(path): return json.loads(Path(path).read_text())


def sync_path(path):
    """Persist either a closed copied file or a directory's current entries."""
    fd=os.open(path,os.O_RDONLY)
    try: os.fsync(fd)
    finally: os.close(fd)


def durable_directory(path):
    path=Path(path)
    if not path.exists():
        durable_directory(path.parent)
        path.mkdir(exist_ok=True)
    sync_path(path)
    sync_path(path.parent)


def sync_candidate(path):
    sync_path(path/'weights.bin')
    sync_path(path/'manifest.json')
    sync_path(path)
    sync_path(path.parent)


def reject_test_provenance(value):
    if isinstance(value,dict):
        for key,item in value.items():
            if key in ('test_only','synthetic') and item is not False:
                raise ValueError('test provenance is forbidden')
            reject_test_provenance(item)
    elif isinstance(value,list):
        for item in value: reject_test_provenance(item)


def publish(path, value):
    """Durable complete-file replacement. Failure leaves the old or new whole file."""
    path=Path(path); durable_directory(path.parent)
    temporary=path.with_name('.'+path.name+'-'+uuid.uuid4().hex)
    try:
        with temporary.open('xb') as stream:
            stream.write(canonical(value)); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary,path)
        sync_path(path.parent)
    finally: temporary.unlink(missing_ok=True)


class Registry:
    def __init__(self, root: Path):
        self.root=Path(root).resolve(); durable_directory(self.root)
        for name in ('candidates','history','evidence','evaluations','reservations'):
            durable_directory(self.root/name)
        if not (self.root/'frozen.json').exists():
            with self.lock():
                if not (self.root/'frozen.json').exists(): self._initialize()
        self.frozen=read(self.root/'frozen.json')
        if file_hash(self.root/'frozen.json')!=read(self.root/'frozen-seal.json')['sha256']:
            raise ValueError('frozen registry provenance corrupted')
        if digest(canonical(read(self.root/'config.json')))!=self.frozen['config_sha256']:
            raise ValueError('registry configuration changed; use a new registry')
        if not (self.root/'champion.json').exists():
            with self.lock():
                if not (self.root/'champion.json').exists(): self._point(self.frozen['baseline']['id'],'initialize')

    @contextmanager
    def lock(self):
        with (self.root/'writer.lock').open('a+b') as stream:
            fcntl.flock(stream,fcntl.LOCK_EX)
            try: yield
            finally: fcntl.flock(stream,fcntl.LOCK_UN)

    def _initialize(self):
        config=read(self.root/'config.json')
        sync_path(self.root/'config.json')
        if config.get('test_only'): raise ValueError('test provenance is forbidden')
        sources=config['baseline']['source_files']
        if not sources: raise ValueError('baseline sources must be declared')
        source={str(Path(p).resolve()):file_hash(p) for p in sources}
        for path,sha in source.items():
            target=self.root/'sources'/sha; durable_directory(target.parent)
            if not target.exists(): shutil.copyfile(path,target)
            if file_hash(target)!=sha: raise ValueError('frozen source corrupted')
            sync_path(target); sync_path(target.parent)
        revision,diff,hybrid_source=_provenance()
        binaries={}
        for kind in ('baseline','hybrid'):
            spec=config[kind]; executable=Path(spec['executable']).resolve(); sha=file_hash(executable)
            if not isinstance(spec['arguments'],list) or not all(isinstance(x,str) for x in spec['arguments']):
                raise ValueError('engine arguments must be strings')
            if kind=='hybrid' and any(x=='--model' or x.startswith('--model=') for x in spec['arguments']):
                raise ValueError('registry owns the frozen model argument')
            target=self.root/'binaries'/sha/executable.name; durable_directory(target.parent)
            if not target.exists(): shutil.copy2(executable,target)
            if file_hash(target)!=sha: raise ValueError('frozen executable corrupted')
            sync_path(target); sync_path(target.parent)
            binaries[kind]={'Executable':str(target),'Arguments':spec['arguments'],
                'WorkingDirectory':str(target.parent),'ExecutableSha256':sha,'ModelSha256':None,'ExpectedBackend':None}
        arena=Path(config['arena']).resolve()
        authority={str(arena):file_hash(arena)}
        # A .NET apphost delegates to adjacent assemblies; freeze their hashes too.
        for dependency in sorted(arena.parent.glob('*.dll')): authority[str(dependency)]=file_hash(dependency)
        baseline={'kind':'baseline','source_files':source,'source_sha256':digest(canonical(source)),
                  'config_sha256':digest(canonical(config)),'engine':binaries['baseline']}
        baseline['id']='baseline-'+digest(canonical(baseline)); baseline['engine']['Id']=baseline['id']
        frozen={'config_sha256':digest(canonical(config)),'config':config,'baseline':baseline,'hybrid':binaries['hybrid'],
                'arena':str(arena),'authority':authority,'source':{'git_revision':revision,'source_diff_sha256':diff,'source_sha256':hybrid_source}}
        publish(self.root/'frozen-seal.json',{'sha256':digest(canonical(frozen))})
        publish(self.root/'frozen.json',frozen)

    def record(self, identity):
        baseline=self.frozen['baseline']
        if identity==baseline['id']:
            result=json.loads(json.dumps(baseline))
        else:
            if not isinstance(identity,str) or re.fullmatch('[0-9a-f]{64}',identity) is None:
                raise ValueError('unknown candidate')
            path=self.root/'candidates'/identity/'manifest.json'
            artifact=validate_artifact(path)
            if digest(canonical(artifact))!=identity: raise ValueError('candidate manifest hash mismatch')
            self._check_modes(artifact)
            result={'id':identity,'kind':'hybrid','artifact':artifact,'manifest':str(path),
                    'engine':dict(self.frozen['hybrid'],Id=identity,ModelSha256=artifact['model_sha256'],ExpectedBackend='hybrid')}
            result['engine']['Arguments']=[*result['engine']['Arguments'],'--model',str(path)]
        if file_hash(result['engine']['Executable'])!=result['engine']['ExecutableSha256']:
            raise ValueError('frozen executable hash mismatch')
        return result

    def _check_modes(self, artifact):
        arguments=self.frozen['hybrid']['Arguments']
        for field,default in [('search_mode','root'),('leaf_mode','policy')]:
            option='--'+field.replace('_','-')
            if any(x.startswith(option+'=') for x in arguments) or arguments.count(option)>1:
                raise ValueError('ambiguous engine mode')
            if option in arguments and arguments.index(option)+1>=len(arguments):raise ValueError('missing engine mode')
            mode=arguments[arguments.index(option)+1] if option in arguments else default
            if mode!=artifact.get(field,default): raise ValueError('artifact/engine mode disagreement')

    def register_candidate(self, manifest: Path) -> str:
        artifact=validate_artifact(manifest)
        reject_test_provenance(artifact)
        self._check_modes(artifact)
        identity=digest(canonical(artifact)); target=self.root/'candidates'/identity
        with self.lock():
            if target.exists():
                self.record(identity); sync_candidate(target); return identity
            temporary=Path(tempfile.mkdtemp(prefix='.candidate-',dir=self.root/'candidates'))
            try:
                shutil.copyfile(Path(manifest).parent/'weights.bin',temporary/'weights.bin')
                sync_path(temporary/'weights.bin')
                publish(temporary/'manifest.json',artifact)
                validate_artifact(temporary/'manifest.json')
                temporary.rename(target)
                sync_path(target.parent)
            finally:
                if temporary.exists(): shutil.rmtree(temporary)
        return identity

    def current(self) -> dict:
        return self.record(read(self.root/'champion.json')['id'])

    def _point(self, identity, reason):
        record=self.record(identity)
        # Also cover a retry after directory publication succeeded but its fsync failed.
        if record['kind']=='hybrid': sync_candidate(Path(record['manifest']).parent)
        event={'id':identity,'reason':reason,'previous':read(self.root/'champion.json')['id'] if (self.root/'champion.json').exists() else None}
        event_id=uuid.uuid4().hex
        publish(self.root/'history'/(event_id+'.json'),event)
        # One atomic file binds identity to the complete frozen launch command.
        # Artifact/binary durability barriers precede this final publication.
        publish(self.root/'champion.json',dict(event,event=event_id,engine=record['engine']))

    def promote(self, candidate_id: str, gate_report: Path, verification_report: Path) -> None:
        from .promotion import verify_promotion
        with self.lock():
            evidence={'candidate':candidate_id,'gate_report':str(gate_report),'verification_report':str(verification_report)}
            try:
                for key,path in [('gate',gate_report),('verification',verification_report)]:
                    evidence[key]=read(path); evidence[key+'_sha256']=file_hash(path)
                evaluation=verify_promotion(self,candidate_id,Path(gate_report),Path(verification_report))
                evidence['status']='passing'; evidence['evaluation']=evaluation
                publish(self.root/'evidence'/(uuid.uuid4().hex+'.json'),evidence)
                # The trial is spent before pointer publication, including a crash here.
                publish(self.root/'evaluations'/evaluation/'consumed.json',{'candidate':candidate_id})
                self._point(candidate_id,'promotion '+evaluation)
            except (ValueError,OSError,KeyError,TypeError,subprocess.SubprocessError) as exc:
                evidence['status']='rejected'; evidence['reason']=str(exc)
                publish(self.root/'evidence'/(uuid.uuid4().hex+'.json'),evidence)
                raise ValueError(str(exc)) from exc

    def rollback(self, champion_id: str) -> None:
        with self.lock():
            if champion_id!=self.frozen['baseline']['id'] and not any(
                x.get('candidate')==champion_id and x.get('status')=='passing' for x in map(read,(self.root/'evidence').glob('*.json'))):
                raise ValueError('rollback requires a previously verified champion')
            self._point(champion_id,'rollback')
