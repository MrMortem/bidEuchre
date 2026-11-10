"""Bounded native JSONL supervision. Failed/pending spools are never training data."""
from dataclasses import dataclass
import json
import os
from pathlib import Path
import selectors
import subprocess
import time

MAX_LINE = 1 << 20

@dataclass(frozen=True)
class JobResult:
    status: str
    job_id: str
    spool_path: Path
    reported_memory_bytes: int


def run_job(job: dict, worker: Path, spool: Path) -> JobResult:
    """Run one frozen job; stream at most 64 KiB per read, with a 1 MiB row cap.

    Caller schedules at most job['workers'] (default two) simultaneous jobs.
    Native memory checks cover decisions; supervisor bounds uncooperative baseline
    calls by the entire job duration. The .status.json sidecar is authoritative
    about process completion, including a worker killed after emitting a footer.
    """
    job_id = job['job_id']
    payload = json.dumps(job, allow_nan=False).encode() + b'\n'
    if len(payload) > 65536:
        raise ValueError('job exceeds 64 KiB')
    spool = Path(spool)
    spool.parent.mkdir(parents=True, exist_ok=True)
    duration = job.get('duration_seconds', 1800)
    capacity = job.get('disk_bytes', 5 << 30)
    if not isinstance(duration, (int, float)) or not 0 < duration <= 86400:
        raise ValueError('invalid duration')
    if not isinstance(capacity, int) or capacity < 1024:
        raise ValueError('invalid spool capacity')
    state = 'failed'
    memory = 0
    process = None
    total = 0
    buffer = b''
    stderr = bytearray()
    footer = None
    reason = ''
    started = time.monotonic()
    # Exclusive creation prevents accidental replacement of any prior evidence.
    with spool.open('xb') as output:
        try:
            process = subprocess.Popen([str(worker)], stdin=subprocess.PIPE,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       bufsize=0)
            os.set_blocking(process.stdin.fileno(), False)
            sent = 0
            with selectors.DefaultSelector() as select:
                select.register(process.stdin, selectors.EVENT_WRITE, 'in')
                select.register(process.stdout, selectors.EVENT_READ, 'out')
                select.register(process.stderr, selectors.EVENT_READ, 'err')
                while select.get_map():
                    if time.monotonic() - started >= duration:
                        state, reason = 'interrupted', 'supervisor duration limit'
                        break
                    remaining = max(0, duration-(time.monotonic()-started))
                    for key, _ in select.select(timeout=min(.1, remaining)):
                        if key.data == 'in':
                            try:
                                sent += os.write(key.fileobj.fileno(), payload[sent:sent+16384])
                            except BlockingIOError:
                                continue
                            if sent == len(payload):
                                select.unregister(key.fileobj)
                                process.stdin.close()
                            continue
                        chunk = os.read(key.fileobj.fileno(), 65536)
                        if not chunk:
                            select.unregister(key.fileobj)
                            continue
                        if key.data == 'err':
                            stderr.extend(chunk[:max(0, 65536-len(stderr))])
                            continue
                        total += len(chunk)
                        if total > capacity:
                            state, reason = 'capacity', 'spool byte limit'
                            raise BufferError(reason)
                        buffer += chunk
                        while b'\n' in buffer:
                            line, buffer = buffer.split(b'\n', 1)
                            if len(line) > MAX_LINE:
                                raise ValueError('row exceeds bound')
                            row = json.loads(line)
                            if not isinstance(row, dict):
                                raise ValueError('protocol record must be an object')
                            if footer is not None:
                                raise ValueError('data after footer')
                            if row.get('type') == 'footer':
                                footer = row
                            output.write(line + b'\n')
                        if len(buffer) > MAX_LINE:
                            raise ValueError('unterminated row exceeds bound')
                else:
                    remaining = max(.001, duration-(time.monotonic()-started))
                    code = process.wait(timeout=remaining)
                    if buffer:
                        raise ValueError('truncated final row')
                    if footer and footer.get('job_id') == job_id:
                        expected = {'complete': 0, 'interrupted': 2, 'capacity': 3, 'failed': 1}
                        status = footer.get('status')
                        if status in expected and code == expected[status]:
                            state = status
                        else:
                            reason = 'footer/process status mismatch'
                    else:
                        reason = 'worker exited without matching footer'
        except (OSError, ValueError, BufferError, subprocess.TimeoutExpired) as exc:
            if isinstance(exc, subprocess.TimeoutExpired):
                state = 'interrupted'
            elif isinstance(exc, OSError) and exc.errno == 28:
                state = 'capacity'
            reason = str(exc)
        except KeyboardInterrupt:
            state, reason = 'interrupted', 'user interruption'
        finally:
            if process is not None:
                if process.poll() is None:
                    process.kill()
                process.wait()
                process.stdin.close()
                process.stdout.close()
                process.stderr.close()
            try:
                output.flush()
                os.fsync(output.fileno())
            except OSError as exc:
                state = 'capacity' if exc.errno == 28 else 'failed'
                reason = str(exc)
    for line in bytes(stderr).decode(errors='replace').splitlines():
        if line.startswith('reported_memory_bytes='):
            try:
                memory = int(line.partition('=')[2])
            except ValueError:
                pass
    status_path = spool.with_name(spool.name + '.status.json')
    temporary = status_path.with_name(status_path.name + '.pending')
    try:
        with temporary.open('x') as stream:
            json.dump({'version': 1, 'job_id': job_id, 'status': state,
                       'reported_memory_bytes': memory, 'reason': reason,
                       'stderr': bytes(stderr).decode(errors='replace')}, stream)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, status_path)
    except OSError as exc:
        # Missing sidecar remains explicitly uncommittable, even if a footer
        # reached disk before the storage device filled.
        state = 'capacity' if exc.errno == 28 else 'failed'
    return JobResult(state, job_id, spool, memory)
