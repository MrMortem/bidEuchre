"""Versioned, immutable float32 artifacts. No pickle or Python runtime at inference."""
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import numpy as np
from . import schema

LAYERS = (('main.fc1',256,1188,'relu'),('main.fc2',128,256,'relu'),
          ('main.policy',61,128,'none'),('main.win',1,128,'none'),
          ('main.trick',7,128,'none'),('main.ownership',96,128,'none'),
          ('boundary.fc1',64,38,'relu'),('boundary.fc2',32,64,'relu'),('boundary.out',1,32,'none'))

def tensor_specs():
    result=[]; offset=0
    for name,out,inputs,activation in LAYERS:
        for suffix,shape,act in [('weight',[out,inputs],activation),('bias',[out],'none')]:
            size=math.prod(shape)*4
            result.append({'name':name+'.'+suffix,'offset':offset,'shape':shape,'activation':act,'byte_length':size})
            offset+=size
    return result

def _digest(data): return hashlib.sha256(data).hexdigest()
def _hex(value, length=64): return isinstance(value,str) and re.fullmatch('[0-9a-f]{'+str(length)+'}',value) is not None

def validate_artifact(manifest):
    """Validate exact layout, fingerprints and all weights before publication."""
    manifest=Path(manifest)
    try:
        if manifest.stat().st_size>65536: raise ValueError('manifest exceeds 65536 bytes')
        m=json.loads(manifest.read_text())
        if not isinstance(m,dict) or type(m.get('version')) is not int: raise ValueError('invalid manifest version')
        expected={'version':1,'architecture':'hybrid-mlp-v1','dtype':'float32','byte_order':'little',
                  'weights':'weights.bin','schema_sha256':schema.SCHEMA_SHA256,'rule_sha256':schema.RULE_SHA256}
        if any(m.get(k)!=v for k,v in expected.items()): raise ValueError('incompatible artifact')
        for field,choices,default in [('search_mode',('root','information-tree'),'root'),('leaf_mode',('policy','double-dummy'),'policy')]:
            if m.get(field,default) not in choices: raise ValueError('invalid '+field)
        for tensor in m['tensors']:
            numbers=[tensor['offset'],tensor['byte_length'],*tensor['shape']]
            if any(type(value) not in (int,float) or not math.isfinite(value) or int(value)!=value for value in numbers):
                raise ValueError('invalid numeric tensor metadata')
        if m['tensors']!=tensor_specs(): raise ValueError('invalid tensor layout')
        if not _hex(m['model_sha256']) or not _hex(m['source_sha256']): raise ValueError('invalid fingerprint')
        provenance=m['provenance']
        if not _hex(provenance['git_revision'],40) or not _hex(provenance['source_diff_sha256']): raise ValueError('invalid provenance')
        if not isinstance(provenance['metadata'],dict): raise ValueError('invalid metadata')
        data=(manifest.parent/m['weights']).read_bytes()
        if type(m['byte_length']) is not int or m['byte_length']!=sum(t['byte_length'] for t in tensor_specs()) or len(data)!=m['byte_length']:
            raise ValueError('invalid weight length')
        if _digest(data)!=m['model_sha256']: raise ValueError('weight hash mismatch')
        if not np.isfinite(np.frombuffer(data,dtype='<f4')).all(): raise ValueError('nonfinite weights')
        return m
    except (KeyError,TypeError,OSError,json.JSONDecodeError) as error:
        raise ValueError('invalid artifact: '+str(error)) from error

def _provenance():
    root=Path(__file__).resolve().parents[4]
    def git(*args): return subprocess.check_output(['git','-C',str(root),*args])
    revision=git('rev-parse','HEAD').decode().strip()
    diff=git('diff','HEAD','--binary')
    sources=bytearray()
    files=git('ls-files','--cached','--others','--exclude-standard','-z').split(b'\0')
    for encoded in sorted(set(files)):
        if not encoded: continue
        name=encoded.decode()
        if name.startswith('engines/hybrid/') and Path(name).suffix in {'.py','.cpp','.hpp','.json','.sh','.toml'}:
            sources.extend(encoded+b'\0'+(root/name).read_bytes()+b'\0')
    return revision,_digest(diff+sources),_digest(sources)

def export_model(main, boundary, metadata: dict, destination: Path, *, search_mode="root", leaf_mode="policy") -> Path:
    """Create destination/manifest.json atomically; refuse to replace a candidate."""
    destination=Path(destination)
    if destination.exists(): raise FileExistsError(destination)
    if not isinstance(metadata,dict): raise ValueError('metadata must be a dictionary')
    # Serialize before creating the candidate; NaN metadata is invalid JSON.
    json.dumps(metadata,allow_nan=False)
    destination.parent.mkdir(parents=True,exist_ok=True)
    temporary=Path(tempfile.mkdtemp(prefix='.'+destination.name+'-',dir=destination.parent))
    try:
        tensors={**{'main.'+k:v for k,v in main.state_dict().items()},**{'boundary.'+k:v for k,v in boundary.state_dict().items()}}
        specs=tensor_specs()
        if set(tensors)!={t['name'] for t in specs}: raise ValueError('unexpected model tensors')
        chunks=[]
        for spec in specs:
            array=tensors[spec['name']].detach().cpu().numpy()
            if list(array.shape)!=spec['shape'] or array.dtype!=np.float32 or not np.isfinite(array).all():
                raise ValueError('invalid tensor '+spec['name'])
            chunks.append(np.asarray(array,dtype='<f4',order='C').tobytes(order='C'))
        data=b''.join(chunks)
        revision,diff,source=_provenance()
        manifest={'version':1,'architecture':'hybrid-mlp-v1','dtype':'float32','byte_order':'little',
            'search_mode':search_mode,'leaf_mode':leaf_mode,
            'weights':'weights.bin','byte_length':len(data),'tensors':specs,'model_sha256':_digest(data),
            'schema_sha256':schema.SCHEMA_SHA256,'rule_sha256':schema.RULE_SHA256,'source_sha256':source,
            'provenance':{'git_revision':revision,'source_diff_sha256':diff,'metadata':metadata}}
        (temporary/'weights.bin').write_bytes(data)
        path=temporary/'manifest.json'
        path.write_text(json.dumps(manifest,sort_keys=True,separators=(',',':'),allow_nan=False)+'\n')
        validate_artifact(path)
        temporary.rename(destination)
        return destination/'manifest.json'
    finally:
        if temporary.exists(): shutil.rmtree(temporary)
