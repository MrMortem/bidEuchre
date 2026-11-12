"""Trainable shapes and normalization are part of the frozen contract."""
import sys
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / 'engines/hybrid/python'))
import torch
from hybrid_training.models import HybridNet, BoundaryNet, normalize_output

class ModelTests(unittest.TestCase):
    def test_shapes_and_gradients(self):
        model = HybridNet()
        result = model(torch.zeros(3, 1188))
        self.assertEqual({k: tuple(v.shape) for k,v in result.items()},
                         {'policy_logits': (3,61), 'win_logit': (3,1), 'trick_logits': (3,7), 'ownership_logits': (3,96)})
        sum(v.sum() for v in result.values()).backward()
        self.assertTrue(all(p.grad is not None for p in model.parameters()))
        self.assertEqual(tuple(BoundaryNet()(torch.zeros(3,38)).shape), (3,1))
        for model, width in [(model,1187),(BoundaryNet(),39)]:
            with self.assertRaises(ValueError): model(torch.zeros(2,width))

    def test_mask_and_stable_normalization(self):
        raw = {'policy_logits': torch.full((2,61),10000.), 'win_logit': torch.tensor([[10000.],[-10000.]]),
               'trick_logits': torch.zeros(2,7), 'ownership_logits': torch.zeros(2,96)}
        legal = torch.zeros(2,61,dtype=torch.bool); legal[0,[1,4]]=True
        p = normalize_output(raw,legal)
        self.assertEqual(float(p['policy'][0,0]),0.)
        self.assertEqual(float(p['policy'][0,1]),.5)
        self.assertEqual(float(p['policy'][1].sum()),0.)
        torch.testing.assert_close(p['trick'].sum(-1),torch.ones(2))
        torch.testing.assert_close(p['ownership'].reshape(2,24,4).sum(-1),torch.ones(2,24))
        self.assertEqual(p['win'].tolist(),[[1.],[0.]])
        raw['policy_logits'][0,0]=float('nan')
        with self.assertRaises(ValueError): normalize_output(raw,legal)

class PackagingTests(unittest.TestCase):
    def test_built_metadata_preserves_python310_and_numpy_markers(self):
        # Exercise the actual build backend so these are published wheel metadata,
        # rather than assertions against TOML source spelling alone.
        import email
        import shutil
        import subprocess
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            shutil.copy(ROOT/'engines/hybrid/pyproject.toml',project/'pyproject.toml')
            package = project/'python/hybrid_training'
            package.mkdir(parents=True)
            (package/'__init__.py').write_text('')
            metadata = project/'metadata'; metadata.mkdir()
            result = subprocess.run([sys.executable,'-c',
                'from setuptools.build_meta import prepare_metadata_for_build_wheel; '
                'prepare_metadata_for_build_wheel("metadata")'],cwd=project,capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr)
            message = email.message_from_string(next(metadata.glob('*.dist-info/METADATA')).read_text())
            self.assertEqual(message['Requires-Python'],'<3.14,>=3.10')
            requirements = set(message.get_all('Requires-Dist'))
            self.assertEqual(requirements,{'torch==2.9.1',
                'numpy==2.2.6; python_version < "3.11"',
                'numpy==2.3.5; python_version >= "3.11"'})

    def test_installer_version_gate_accepts310_rejects39(self):
        # Simulate only the version reported to the gate; this does not claim an
        # actual Python 3.10 runtime test. Stub pip prevents dependency mutation.
        import os
        import shlex
        import shutil
        import subprocess
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            project=Path(directory)
            shutil.copy(ROOT/'engines/hybrid/install.sh',project/'install.sh')
            stub=project/'.venv/bin/python'; stub.parent.mkdir(parents=True)
            stub.write_text('#!/bin/bash\nexit 0\n'); stub.chmod(0o755)
            interpreter=project/'version-python'
            for version,expected in [((3,10),0),((3,9),1)]:
                code=f'import sys; sys.version_info={version!r}; exec(sys.argv[1])'
                interpreter.write_text('#!/bin/bash\nexec '+shlex.quote(sys.executable)+' -c '+shlex.quote(code)+' "$2"\n')
                interpreter.chmod(0o755)
                result=subprocess.run(['bash',str(project/'install.sh')],
                    env={**os.environ,'HYBRID_PYTHON':str(interpreter)},capture_output=True,text=True)
                self.assertEqual(result.returncode,expected,result.stderr)
