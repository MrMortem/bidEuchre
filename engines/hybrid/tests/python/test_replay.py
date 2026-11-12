import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from hybrid_training.replay import ReplayStore, ARRAYS
from hybrid_training.worker import run_job
from test_worker import job, WORKER

class ReplayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.generated = tempfile.TemporaryDirectory()
        cls.source = Path(cls.generated.name)/'complete.jsonl'
        result = run_job(job(), WORKER, cls.source)
        if result.status != 'complete':
            raise AssertionError(result)
        cls.rows = [json.loads(line) for line in cls.source.read_text().splitlines()]
    @classmethod
    def tearDownClass(cls):
        cls.generated.cleanup()
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.store = ReplayStore(self.root/'replay', 64<<20)
    def tearDown(self):
        self.temp.cleanup()
    def altered(self, rows):
        spool = self.root/'altered.jsonl'
        spool.write_text(''.join(json.dumps(row)+'\n' for row in rows))
        spool.with_name(spool.name+'.status.json').write_text(json.dumps(dict(version=1,status='complete',job_id=rows[0]['job']['job_id'])))
        return spool
    def test_full_match_typed_arrays_labels_and_dedupe(self):
        name = self.store.commit_match(self.source)
        self.assertEqual(name, self.store.commit_match(self.source))
        directory = self.store.root/name
        manifest = json.loads((directory/'manifest.json').read_text())
        self.assertEqual(set(ARRAYS), set(manifest['arrays']))
        self.assertEqual('conditional_executed_teacher_branch', manifest['behavior_probability_kind'])
        decisions = [row for row in self.rows if row['type']=='decision']
        terminals = {row['deal']:row for row in self.rows if row['type']=='hand_end'}
        for key,(dtype,_) in ARRAYS.items():
            values = np.load(directory/(key+'.npy'), mmap_mode='r')
            self.assertEqual(dtype, values.dtype.str)
            self.assertEqual(hashlib.sha256((directory/(key+'.npy')).read_bytes()).hexdigest(), manifest['arrays'][key]['sha256'])
        batches = list(self.store.iter_batches('train', 31))
        self.assertEqual(len(decisions), sum(len(batch['features']) for batch in batches if 'features' in batch))
        self.assertEqual(4*len(terminals), sum(len(batch['boundary_features']) for batch in batches if 'boundary_features' in batch))
        features = np.concatenate([b['features'] for b in batches if 'features' in b])
        np.testing.assert_array_equal(features, np.asarray([r['features'] for r in decisions],dtype='<f4'))
        tricks = np.load(directory/'trick_target.npy')
        wins = np.load(directory/'win_target.npy')
        owner = np.load(directory/'ownership_target.npy')
        for i,row in enumerate(decisions):
            self.assertEqual(terminals[row['deal']]['tricks'][row['seat']%2], tricks[i])
            self.assertEqual(float(row['seat']%2==self.rows[-1]['winner']), wins[i])
            np.testing.assert_array_equal(row['labels']['ownership_target'], owner[i])
        self.assertEqual([], list(self.store.iter_batches('validation', 8)))
    def test_interrupted_and_pending_ignored(self):
        request = job(); request['max_decisions'] = 1
        interrupted = run_job(request, WORKER, self.root/'interrupted.jsonl')
        self.assertEqual('interrupted', interrupted.status)
        with self.assertRaises(ValueError):
            self.store.commit_match(interrupted.spool_path)
        (self.store.root/'.pending-dead').mkdir()
        (self.store.root/'.pending-dead'/'junk').write_text('incomplete')
        self.assertEqual([], list(self.store.iter_batches('train', 8)))
        spool = self.altered(self.rows)
        spool.with_name(spool.name+'.status.json').unlink()
        with self.assertRaises((ValueError, FileNotFoundError)):
            self.store.commit_match(spool)
    def test_capacity_preserves_good_shard(self):
        name = self.store.commit_match(self.source)
        path = self.store.root/name/'manifest.json'
        before = path.read_bytes()
        rows = copy.deepcopy(self.rows)
        rows[0]['job']['job_id'] = rows[-1]['job_id'] = 'another-job'
        limited = ReplayStore(self.store.root, 1024)
        with self.assertRaises(OSError):
            limited.commit_match(self.altered(rows))
        self.assertEqual(before, path.read_bytes())
        self.assertEqual(name, limited.commit_match(self.source))
    def test_invalid_rows_rejected(self):
        edits = [lambda r:r[1]['features'].pop(),
                 lambda r:r[1].update(action=61),
                 lambda r:r[1].update(behavior_prob=0),
                 lambda r:r[1].update(policy_weight=1),
                 lambda r:r[1]['labels']['ownership_target'].__setitem__(0, 4),
                 lambda r:r[1].update(version=2),
                 lambda r:r[-1].update(status='interrupted'),
                 lambda r:r.pop(-2),
                 lambda r:r[1]['legal'].__setitem__(0, 1)]
        for edit in edits:
            with self.subTest(edit=edit):
                rows = copy.deepcopy(self.rows); edit(rows)
                with self.assertRaises(ValueError):
                    self.store.commit_match(self.altered(rows))
    def test_corrupt_checksum_and_array_schema_rejected(self):
        name = self.store.commit_match(self.source)
        directory = self.store.root/name
        path = directory/'action.npy'
        data = path.read_bytes(); path.write_bytes(data[:-1]+bytes([data[-1]^1]))
        with self.assertRaisesRegex(ValueError, 'checksum'):
            list(self.store.iter_batches('train', 8))
        path.write_bytes(data)
        manifest_path = directory/'manifest.json'
        manifest = json.loads(manifest_path.read_text()); manifest['arrays']['features']['dtype'] = '>f4'
        manifest_path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, 'specification'):
            list(self.store.iter_batches('train', 8))
    def test_job_id_conflict(self):
        self.store.commit_match(self.source)
        rows = copy.deepcopy(self.rows); rows[0]['job']['seed'] += 1
        with self.assertRaisesRegex(ValueError, 'reused'):
            self.store.commit_match(self.altered(rows))
