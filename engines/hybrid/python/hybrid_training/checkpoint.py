"""Atomic, checksummed local training checkpoints (trusted local pickle only).

Checkpoints contain Python/NumPy RNG objects and optimizer state. Never load an
untrusted checkpoint; deployment artifacts use the separate non-pickle exporter.
"""
import hashlib
import io
import os
from pathlib import Path
import tempfile
import torch

HEADER = b'HYBRID-CHECKPOINT-1\n'
MAX_CHECKPOINT_BYTES = 64 << 20


def save_checkpoint(state: dict, path: Path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    buffer = io.BytesIO()
    torch.save(state, buffer)
    payload = buffer.getvalue()
    if len(payload) > MAX_CHECKPOINT_BYTES:
        raise ValueError('checkpoint exceeds bounded format')
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(prefix=path.name+'.pending-', dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(HEADER + hashlib.sha256(payload).hexdigest().encode() + b'\n')
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def load_checkpoint(path: Path) -> dict:
    with Path(path).open('rb') as stream:
        if stream.readline(len(HEADER)+1) != HEADER:
            raise ValueError('incompatible checkpoint')
        digest = stream.readline(66).strip()
        payload = stream.read(MAX_CHECKPOINT_BYTES+1)
    if len(payload) > MAX_CHECKPOINT_BYTES or hashlib.sha256(payload).hexdigest().encode() != digest:
        raise ValueError('checkpoint checksum mismatch')
    result = torch.load(io.BytesIO(payload), map_location='cpu', weights_only=False)
    if not isinstance(result, dict):
        raise ValueError('invalid checkpoint state')
    return result
