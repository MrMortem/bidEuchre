"""Immutable equal-budget runtime screens; never retraining or promotion evidence.

All arms use the SAME frozen weight bytes. Turning off the ownership sampler,
policy continuation or boundary value does not measure removing that training loss.
A retraining ablation must separately retrain with matched data/seeds/updates/compute
and freeze new artifacts before an evaluation; these runtime manifests do not do so.
"""
from copy import deepcopy
import json
import hashlib
import shutil
from .export import validate_artifact
from pathlib import Path

def make_ablation_manifests(base_manifest: dict, output: Path) -> list[Path]:
    for key in ('artifact','seed','decision_ms','stop_ms','max_simulations'):
        if key not in base_manifest: raise ValueError('missing frozen '+key)
    artifact_path=Path(base_manifest['artifact']).resolve()
    artifact=validate_artifact(artifact_path)
    if any(type(base_manifest[k]) is not int or base_manifest[k]<0 for k in ('seed','decision_ms','stop_ms','max_simulations')):
        raise ValueError('invalid fixed budget/seed')
    if not 20<base_manifest['decision_ms']<=base_manifest['stop_ms']<=3500: raise ValueError('invalid decision budget')
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    arms=[('baseline',{}),('uniform-beliefs',{'belief_mode':'uniform'}),
          ('uniform-continuations',{'continuation_mode':'uniform'}),('score-value',{'value_mode':'score'}),
          ('information-tree',{'search_mode':'information-tree'}),('double-dummy',{'leaf_mode':'double-dummy'})]
    if 'executable' in base_manifest:
        binary=output/'engine';shutil.copy2(base_manifest['executable'],binary)
        executable_hash=hashlib.sha256(binary.read_bytes()).hexdigest()
    paths=[]
    for name,toggle in arms:
        manifest=deepcopy(base_manifest)
        manifest.update(search_mode='root',leaf_mode='policy',belief_mode='learned',continuation_mode='learned',value_mode='match')
        manifest.update(toggle);manifest.update(arm=name,ablation_kind='runtime',can_promote=False,
            interpretation='same weights; runtime component replacement, not retraining')
        directory=output/name;directory.mkdir()
        frozen_artifact=deepcopy(artifact)
        frozen_artifact.update(search_mode=manifest['search_mode'],leaf_mode=manifest['leaf_mode'])
        shutil.copyfile(artifact_path.parent/'weights.bin',directory/'weights.bin')
        frozen=directory/'manifest.json';frozen.write_text(json.dumps(frozen_artifact,sort_keys=True,separators=(',',':'))+'\n')
        validate_artifact(frozen)
        manifest.update(artifact=str(frozen),model_sha256=artifact['model_sha256'],artifact_sha256=hashlib.sha256(frozen.read_bytes()).hexdigest())
        if 'executable' in base_manifest:manifest.update(executable=str(binary),executable_sha256=executable_hash)
        path=output/(name+'.json');path.write_text(json.dumps(manifest,sort_keys=True,indent=2,allow_nan=False)+'\n');paths.append(path)
    return paths
