import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import torch
from hybrid_training.export import export_model
from hybrid_training.models import HybridNet, BoundaryNet
from hybrid_training.registry import Registry, digest, canonical

class RegistryTests(unittest.TestCase):
    def setUp(self):
        torch.set_num_threads(1)
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name); self.registry_root=self.root/'registry'; self.registry_root.mkdir()
        binary=self.root/'engine'; binary.write_bytes(b'fixture executable'); binary.chmod(0o755)
        source=self.root/'source.cpp'; source.write_text('// frozen baseline fixture')
        config={'baseline':{'executable':str(binary),'arguments':[],'source_files':[str(source)]},
                'hybrid':{'executable':str(binary),'arguments':['--decision-ms','3000','--stop-ms','3500','--max-simulations','0']},
                'arena':str(binary),'budget':{'DecisionMs':3000,'StopMs':3500,'ProcessMs':5000,'StartupMs':5000},
                'conditions':[{'Id':'baseline','PartnerId':'baseline','Opponent0Id':'baseline','Opponent1Id':'baseline'}]}
        (self.registry_root/'config.json').write_text(json.dumps(config))
        self.registry=Registry(self.registry_root)
        self.artifact=export_model(HybridNet(),BoundaryNet(),{'purpose':'registry unit fixture'},self.root/'model')
        self.candidate=self.registry.register_candidate(self.artifact)

    def test_frozen_baseline_and_canonical_candidate(self):
        self.assertEqual('baseline',self.registry.current()['kind'])
        model=json.loads(self.artifact.read_text())
        self.assertEqual(digest(canonical(model)),self.candidate)
        self.artifact.write_text(json.dumps(model,indent=2))
        self.assertEqual(self.candidate,self.registry.register_candidate(self.artifact))
        (self.artifact.parent/'weights.bin').write_bytes(b'damaged')
        self.assertEqual(model,self.registry.record(self.candidate)['artifact'])
        with self.assertRaises(ValueError): self.registry.register_candidate(self.artifact)

    def test_test_provenance_and_corruption_rejected(self):
        m=json.loads(self.artifact.read_text()); m['provenance']['metadata']['test_only']=True
        self.artifact.write_text(json.dumps(m))
        with self.assertRaises(ValueError): self.registry.register_candidate(self.artifact)
        path=self.registry_root/'candidates'/self.candidate/'weights.bin'; path.write_bytes(b'bad')
        with self.assertRaises(ValueError): self.registry.record(self.candidate)

    def test_atomic_pointer_and_rollback_history(self):
        original=self.registry.current()['id']
        with patch('hybrid_training.registry.os.replace',side_effect=OSError('crash before rename')):
            with self.assertRaises(OSError): self.registry._point(self.candidate,'synthetic test pointer')
        self.assertEqual(original,self.registry.current()['id'])
        original_fsync=__import__('os').fsync
        def interrupted_directory(fd):
            import os, stat
            if stat.S_ISDIR(os.fstat(fd).st_mode) and (self.registry_root/'champion.json').exists() and json.loads((self.registry_root/'champion.json').read_text())['id']==self.candidate:
                raise OSError('crash after pointer rename')
            original_fsync(fd)
        with patch('hybrid_training.registry.os.fsync',side_effect=interrupted_directory):
            with self.assertRaises(OSError): self.registry._point(self.candidate,'synthetic post-rename failure')
        self.assertEqual(self.candidate,self.registry.current()['id'])
        self.registry._point(self.candidate,'synthetic test pointer')
        self.registry.rollback(original)
        self.assertEqual(original,self.registry.current()['id'])
        self.assertGreaterEqual(len(list((self.registry_root/'history').glob('*.json'))),3)
        with self.assertRaises(ValueError): self.registry.rollback('missing')
