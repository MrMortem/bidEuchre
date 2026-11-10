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
    def test_commands_and_bounded_defaults(self):
        for command in ('generate','train','cycle','benchmark-device','resume'):
            args=parser().parse_args([command])
            self.assertEqual(1800,args.duration_seconds)
            self.assertEqual(2,args.workers)
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
