"""Completed-match replay v1, immutable typed shards and bounded mmap batches.

behavior_prob is conditional on the recorded executed teacher branch, including
its exploration mixture. It is NOT a cross-branch importance-sampling propensity.
"""
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
from typing import Iterator
import numpy as np
from .schema import SCHEMA_SHA256, RULE_SHA256
from .worker import MAX_LINE

ARRAYS = {
    'features': ('<f4', (1188,)), 'legal': ('|b1', (61,)),
    'action': ('|u1', ()), 'behavior_prob': ('<f4', ()),
    'policy_target': ('<f4', (61,)), 'policy_weight': ('<f4', ()),
    'ownership_target': ('|i1', (24,)), 'ownership_mask': ('|b1', (24,)),
    'trick_target': ('|u1', ()), 'win_target': ('<f4', ()),
    'boundary_features': ('<f4', (38,)), 'boundary_win_target': ('<f4', ()),
}

def _digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def _json(path):
    with Path(path).open('rb') as stream:
        data = stream.read(MAX_LINE + 1)
    if len(data) > MAX_LINE:
        raise ValueError('JSON exceeds limit')
    return json.loads(data)


def _rows(path):
    with Path(path).open('rb') as stream:
        while line := stream.readline(MAX_LINE+1):
            if len(line) > MAX_LINE or not line.endswith(b'\n'):
                raise ValueError('oversized or truncated row')
            row = json.loads(line)
            if row.get('version') != 1:
                raise ValueError('unsupported row version')
            yield row


def _array(value, key):
    dtype, shape = ARRAYS[key]
    raw = np.asarray(value)
    if raw.shape != shape:
        raise ValueError(f'{key} shape mismatch')
    if dtype == '|b1':
        if raw.dtype.kind != 'b':
            raise ValueError(f'{key} requires booleans')
    elif raw.dtype.kind not in 'iuf' or not np.isfinite(raw).all():
        raise ValueError(f'{key} requires finite numbers')
    elif dtype in ('|i1', '|u1'):
        low, high = (-128,127) if dtype == '|i1' else (0,255)
        if np.any(raw < low) or np.any(raw > high) or np.any(raw != np.floor(raw)):
            raise ValueError(f'{key} integer out of range')
    result = np.asarray(value, dtype=dtype)
    if result.dtype.kind == 'f' and not np.isfinite(result).all():
        raise ValueError(f'{key} float32 overflow')
    return result


def _decision(row):
    values = {key: _array(row[key], key) for key in
              ('features','legal','action','behavior_prob','policy_target','policy_weight')}
    values.update({key: _array(row['labels'][key], key)
                   for key in ('ownership_target','ownership_mask')})
    a = int(values['action'])
    p, w, target = values['behavior_prob'], values['policy_weight'], values['policy_target']
    legal = values['legal']
    if a >= 61 or not legal[a] or not 0 < p <= 1 or w < 0:
        raise ValueError('illegal action or probability/weight')
    if not np.array_equal(values['features'][1075:1136], legal):
        raise ValueError('features/legal mismatch')
    if np.any(target < 0) or np.any(target[~legal] != 0):
        raise ValueError('illegal policy target')
    if (w == 0 and np.any(target)) or (w > 0 and not np.isclose(target.sum(), 1, atol=1e-5)):
        raise ValueError('policy target/weight mismatch')
    mask, owner = values['ownership_mask'], values['ownership_target']
    if np.any(owner[~mask] != -1) or np.any((owner[mask] < 0) | (owner[mask] > 3)):
        raise ValueError('ownership mask/classes mismatch')
    meta = row['metadata']
    if meta.get('probability_conditioning') != 'executed_teacher_branch':
        raise ValueError('ambiguous behavior likelihood')
    branch = meta.get('branch_probability', 0)
    if not 0 < branch <= 1 or not np.isclose(meta.get('joint_branch_action_prob', -1), branch*float(p), atol=1e-7):
        raise ValueError('branch likelihood mismatch')
    if meta.get('teacher_kind') not in ('none','baseline','search') or (w > 0 and meta['teacher_kind'] == 'none'):
        raise ValueError('invalid teacher')
    if meta['teacher_kind'] == 'search' and not meta.get('search', {}).get('balanced'):
        raise ValueError('partial search teacher')
    return values


def _inspect(spool):
    rows = iter(_rows(spool))
    try:
        header = next(rows)
    except StopIteration as exc:
        raise ValueError('empty spool') from exc
    if header.get('type') != 'header' or header.get('schema_sha256') != SCHEMA_SHA256 or header.get('rule_sha256') != RULE_SHA256:
        raise ValueError('incompatible header')
    job = header['job']
    if job.get('split') not in ('train','validation','test') or not isinstance(job.get('job_id'), str):
        raise ValueError('invalid job')
    status = _json(spool.with_name(spool.name+'.status.json'))
    if status.get('version') != 1 or status.get('status') != 'complete' or status.get('job_id') != job['job_id']:
        raise ValueError('worker is not complete')
    count = boundaries = 0
    hand = 1
    ended = False
    seen = set()
    hands = {}
    footer = None
    final_scores = None
    for row in rows:
        if footer is not None:
            raise ValueError('rows after footer')
        kind = row['type']
        if kind == 'decision':
            if ended:
                if seen != {0,1,2,3}:
                    raise ValueError('missing predeal boundaries')
                hand += 1
                ended, seen = False, set()
            if row.get('deal') != hand or row.get('decision') != count or row.get('seat') not in range(4):
                raise ValueError('decision sequence mismatch')
            _decision(row)
            count += 1
        elif kind == 'hand_end':
            if ended or row.get('deal') != hand or count == 0:
                raise ValueError('hand terminal sequence mismatch')
            tricks = row['tricks']
            if len(tricks) != 2 or any(type(v) is not int or not 0 <= v <= 6 for v in tricks) or sum(tricks) != 6:
                raise ValueError('invalid terminal tricks')
            scores = row.get('scores')
            if not isinstance(scores, list) or len(scores) != 2 or any(type(v) is not int for v in scores):
                raise ValueError('invalid terminal scores')
            final_scores = scores
            hands[hand] = tricks
            ended = True
        elif kind == 'boundary':
            seat = row.get('seat')
            if not ended or row.get('deal') != hand or seat not in range(4) or seat in seen:
                raise ValueError('boundary before terminal or duplicate')
            _array(row['features'], 'boundary_features')
            seen.add(seat)
            boundaries += 1
        elif kind == 'footer':
            if row.get('status') != 'complete' or row.get('job_id') != job['job_id'] or row.get('winner') not in (0,1) or not ended or seen != {0,1,2,3} or row.get('decisions') != count or row.get('hands') != hand:
                raise ValueError('incomplete terminal footer')
            winner = row['winner']
            if final_scores[winner] < 40 or final_scores[winner] <= final_scores[1-winner]:
                raise ValueError('winner unsupported by terminal scores')
            footer = row
        else:
            raise ValueError('unknown row type')
        if count > 10000000 or hand > 100000:
            raise ValueError('match exceeds bounds')
    if footer is None:
        raise ValueError('missing completion footer')
    return header, footer, count, boundaries, hands


class ReplayStore:
    def __init__(self, root: Path, max_bytes: int):
        self.root = Path(root)
        self.max_bytes = max_bytes
        if max_bytes <= 0:
            raise ValueError('positive disk capacity required')
        self.root.mkdir(parents=True, exist_ok=True)

    @contextmanager
    def _lock(self):
        with (self.root/'.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            yield

    def commit_match(self, spool: Path) -> str:
        spool = Path(spool)
        with self._lock():
            header, footer, count, boundaries, hands = _inspect(spool)
            job = header['job']
            name = hashlib.sha256(job['job_id'].encode()).hexdigest()
            destination = self.root/name
            if destination.exists():
                manifest = self._validate(destination)
                if manifest['job'] != job:
                    raise ValueError('job ID reused with different frozen job')
                return name
            # Reserve complete typed output + source-sized metadata overhead before
            # writing anything. Never evict/replace a healthy shard to make room.
            needed = sum((boundaries if key.startswith('boundary_') else count)*np.dtype(dtype).itemsize*int(np.prod(shape) if shape else 1)+1024 for key,(dtype,shape) in ARRAYS.items()) + spool.stat().st_size + 65536
            used = sum(p.stat().st_size for p in self.root.rglob('*') if p.is_file())
            if used+needed > self.max_bytes or needed > shutil.disk_usage(self.root).free:
                raise OSError(28, 'replay capacity exhausted')
            source_digest = _digest(spool)
            temporary = Path(tempfile.mkdtemp(prefix='.pending-', dir=self.root))
            arrays = {}
            try:
                for key,(dtype,shape) in ARRAYS.items():
                    size = boundaries if key.startswith('boundary_') else count
                    arrays[key] = np.lib.format.open_memmap(temporary/(key+'.npy'), mode='w+', dtype=dtype, shape=(size,*shape))
                i = b = 0
                with (temporary/'metadata.jsonl').open('w') as metadata:
                    for row in _rows(spool):
                        if row['type'] == 'decision':
                            values = _decision(row)
                            values['trick_target'] = hands[row['deal']][row['seat']%2]
                            values['win_target'] = float(row['seat']%2 == footer['winner'])
                            for key,value in values.items():
                                arrays[key][i] = value
                            json.dump({key: row[key] for key in ('decision','deal','seat','metadata')}, metadata, allow_nan=False, separators=(',',':'))
                            metadata.write('\n')
                            i += 1
                        elif row['type'] == 'boundary':
                            arrays['boundary_features'][b] = row['features']
                            arrays['boundary_win_target'][b] = float(row['seat']%2 == footer['winner'])
                            b += 1
                    metadata.flush()
                    os.fsync(metadata.fileno())
                if i != count or b != boundaries or _digest(spool) != source_digest:
                    raise ValueError('spool changed during packing')
                manifest = {'version':1, 'schema_sha256':SCHEMA_SHA256, 'rule_sha256':RULE_SHA256,
                            'job':job, 'footer':footer, 'source_sha256':source_digest,
                            'behavior_probability_kind':'conditional_executed_teacher_branch', 'arrays':{}}
                for key,arr in arrays.items():
                    arr.flush()
                    path = temporary/(key+'.npy')
                    with path.open('rb') as stream:
                        os.fsync(stream.fileno())
                    manifest['arrays'][key] = {'version':1, 'dtype':arr.dtype.str, 'shape':list(arr.shape), 'file':path.name, 'sha256':_digest(path)}
                arrays.clear()
                manifest['metadata'] = {'file':'metadata.jsonl', 'sha256':_digest(temporary/'metadata.jsonl')}
                with (temporary/'manifest.json').open('w') as stream:
                    json.dump(manifest, stream, sort_keys=True, allow_nan=False)
                    stream.flush()
                    os.fsync(stream.fileno())
                self._validate(temporary)
                pending_fd = os.open(temporary, os.O_RDONLY)
                try:
                    os.fsync(pending_fd)
                finally:
                    os.close(pending_fd)
                os.rename(temporary, destination)
                directory = os.open(self.root, os.O_RDONLY)
                try:
                    os.fsync(directory)
                finally:
                    os.close(directory)
                return name
            finally:
                arrays.clear()
                if temporary.exists():
                    shutil.rmtree(temporary)

    def _validate(self, directory):
        manifest = _json(directory/'manifest.json')
        if manifest.get('version') != 1 or manifest.get('schema_sha256') != SCHEMA_SHA256 or manifest.get('rule_sha256') != RULE_SHA256 or set(manifest.get('arrays',{})) != set(ARRAYS):
            raise ValueError('incompatible shard manifest')
        if manifest.get('footer', {}).get('status') != 'complete' or manifest.get('behavior_probability_kind') != 'conditional_executed_teacher_branch':
            raise ValueError('shard is not a complete match')
        for key,(dtype,tail) in ARRAYS.items():
            info = manifest['arrays'][key]
            expected = manifest['footer']['hands']*4 if key.startswith('boundary_') else manifest['footer']['decisions']
            if info.get('version') != 1 or info.get('dtype') != dtype or info.get('shape') != [expected,*tail] or info.get('file') != key+'.npy':
                raise ValueError('invalid array specification')
            path = directory/info['file']
            if _digest(path) != info['sha256']:
                raise ValueError('array checksum mismatch')
            a = np.load(path, mmap_mode='r', allow_pickle=False)
            if a.dtype.str != dtype or a.shape != tuple(info['shape']):
                raise ValueError('array format mismatch')
        meta = manifest['metadata']
        if meta['file'] != 'metadata.jsonl' or _digest(directory/meta['file']) != meta['sha256']:
            raise ValueError('metadata checksum mismatch')
        return manifest

    def iter_batches(self, split: str, batch_size: int) -> Iterator[dict[str,np.ndarray]]:
        if split not in ('train','validation','test') or batch_size < 1:
            raise ValueError('invalid split/batch size')
        for directory in sorted(self.root.iterdir()):
            if directory.name.startswith('.') or not directory.is_dir():
                continue
            manifest = self._validate(directory)
            if manifest['job']['split'] != split:
                continue
            arrays = {key:np.load(directory/(key+'.npy'), mmap_mode='r', allow_pickle=False) for key in ARRAYS}
            # Boundary batches have their own row count; never align them to
            # arbitrary decision indices or duplicate boundary examples.
            for keys in ([k for k in ARRAYS if not k.startswith('boundary_')], [k for k in ARRAYS if k.startswith('boundary_')]):
                for start in range(0, len(arrays[keys[0]]), batch_size):
                    yield {key:arrays[key][start:start+batch_size] for key in keys}
