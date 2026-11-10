import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from hybrid_training.cli import parser
from hybrid_training.device import choose_device

class CliTests(unittest.TestCase):
    def test_invalid_config_and_resume_errors_are_structured(self):
        for args in [('train','--workers','0'),('resume','--output','/missing/task12-checkpoint')]:
            result=subprocess.run([sys.executable,'-m','hybrid_training.cli',*args],capture_output=True,text=True)
            self.assertEqual(2,result.returncode)
            self.assertEqual('failed',json.loads(result.stderr)['status'])
            self.assertNotIn('Traceback',result.stderr)

    def test_commands_and_bounded_defaults(self):
        for command in ('generate','train','cycle','benchmark-device','resume'):
            args=parser().parse_args([command])
            self.assertEqual(1800,args.duration_seconds)
            self.assertEqual(2,args.workers)
            self.assertEqual('/tmp/explicit-registry',parser().parse_args([command,'--registry','/tmp/explicit-registry']).registry_root)
        with self.assertRaises(ValueError): choose_device('bogus',{})
        with patch('torch.backends.mps.is_available',return_value=False):
            self.assertEqual('cpu',choose_device('cpu',{}).device)
            with self.assertRaisesRegex(ValueError,'unavailable'): choose_device('mps',{})

    def test_real_tiny_cycle_reopen_and_resume(self):
        with tempfile.TemporaryDirectory() as tmp:
            cmd=[sys.executable,'-m','hybrid_training.cli','cycle','--output',tmp,'--device','cpu',
                 '--execution-mode','fixed_seeded','--duration-seconds','15','--max-updates','2','--batch-size','8']
            result=subprocess.run(cmd,capture_output=True,text=True,timeout=25)
            self.assertEqual(0,result.returncode,result.stderr)
            report=json.loads(result.stdout); self.assertEqual('complete',report['status'])
            from hybrid_training.export import validate_artifact
            validate_artifact(Path(report['export_manifest']))
            self.assertGreater(report['rows_seen'],0)
            result=subprocess.run([sys.executable,'-m','hybrid_training.cli','resume','--output',tmp,
                '--duration-seconds','10','--max-updates','1'],capture_output=True,text=True,timeout=20)
            self.assertEqual(0,result.returncode,result.stderr)

    def test_explicit_bootstrap_ignores_ambient_registry_and_persists_on_resume(self):
        import os
        from hybrid_training.checkpoint import load_checkpoint
        with tempfile.TemporaryDirectory() as tmp:
            env=dict(os.environ,HYBRID_REGISTRY='/missing/unit-user-registry')
            command=[sys.executable,'-m','hybrid_training.cli','cycle','--no-registry','--output',tmp,
                '--device','cpu','--execution-mode','fixed_seeded','--duration-seconds','10','--max-updates','1','--batch-size','8']
            result=subprocess.run(command,capture_output=True,text=True,env=env,timeout=20)
            self.assertEqual(0,result.returncode,result.stderr)
            saved=load_checkpoint(Path(tmp)/'checkpoint.pt')
            self.assertFalse(saved['config']['registry_enabled'])
            self.assertIsNone(saved['population_lineage']['registry_root'])
            result=subprocess.run([sys.executable,'-m','hybrid_training.cli','resume','--output',tmp,
                '--duration-seconds','10','--max-updates','1'],capture_output=True,text=True,env=env,timeout=20)
            self.assertEqual(0,result.returncode,result.stderr)
            self.assertFalse(load_checkpoint(Path(tmp)/'checkpoint.pt')['config']['registry_enabled'])
