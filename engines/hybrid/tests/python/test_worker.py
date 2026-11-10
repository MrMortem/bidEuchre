import json
from pathlib import Path
import sys
import shlex
import tempfile
import unittest
from hybrid_training.worker import run_job, JobResult

ROOT = Path(__file__).resolve().parents[2]
WORKER = ROOT/'build/bideuchre-hybrid-worker'

def job(job_id='worker-test', **overrides):
    return dict(job_id=job_id, split='train', seed=901, schedule_index=1, max_hands=1000,
                max_decisions=20000, duration_seconds=30, disk_bytes=64<<20,
                population=[dict(id='incumbent',kind='fixed',version='v1'),
                            dict(id='uniform',kind='uniform',version='v1')], **overrides)

class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
    def tearDown(self):
        self.temp.cleanup()
    def script(self, source):
        path = self.root/'fake-worker'
        path.write_text('#!/bin/sh\nexec '+shlex.quote(sys.executable)+' -c '+shlex.quote(source)+'\n')
        path.chmod(0o755)
        return path
    def test_real_full_match_and_interruption(self):
        result = run_job(job(), WORKER, self.root/'complete.jsonl')
        self.assertIsInstance(result, JobResult)
        self.assertEqual('complete', result.status)
        self.assertGreater(result.reported_memory_bytes, 0)
        rows = [json.loads(line) for line in result.spool_path.read_text().splitlines()]
        self.assertEqual('complete', rows[-1]['status'])
        self.assertGreater(rows[-1]['hands'], 1)
        request = job(); request['max_decisions'] = 1
        interrupted = run_job(request, WORKER, self.root/'interrupted.jsonl')
        self.assertEqual('interrupted', interrupted.status)
        rows = [json.loads(line) for line in interrupted.spool_path.read_text().splitlines()]
        self.assertNotIn('winner', rows[-1])
        self.assertNotIn('win_target', rows[1])
    def test_missing_worker_and_nonzero_exit(self):
        self.assertEqual('failed', run_job(job(), self.root/'missing', self.root/'missing.jsonl').status)
        fake = self.script("import sys\nsys.stdin.readline()\nprint('{\"type\":\"footer\",\"job_id\":\"worker-test\",\"status\":\"complete\"}')\nsys.exit(1)\n")
        self.assertEqual('failed', run_job(job(), fake, self.root/'failure.jsonl').status)
    def test_supervisor_timeout_and_capacity(self):
        fake = self.script('import time\ntime.sleep(10)\n')
        request = job(); request['duration_seconds'] = .1
        result = run_job(request, fake, self.root/'timeout.jsonl')
        self.assertEqual('interrupted', result.status)
        fake = self.script("import sys\nsys.stdin.readline()\nprint('x'*200000)\n")
        request = job(); request['disk_bytes'] = 1024
        self.assertEqual('capacity', run_job(request, fake, self.root/'capacity.jsonl').status)
    def test_truncated_output_and_exclusive_spool(self):
        fake = self.script("import sys\nsys.stdin.readline()\nsys.stdout.write('{')\n")
        path = self.root/'truncated.jsonl'
        self.assertEqual('failed', run_job(job(), fake, path).status)
        before = path.read_bytes()
        with self.assertRaises(FileExistsError):
            run_job(job(), WORKER, path)
        self.assertEqual(before, path.read_bytes())
