from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from hybrid_training.checkpoint import save_checkpoint, load_checkpoint

class CheckpointTests(unittest.TestCase):
    def test_atomic_replacement_and_interruption(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'checkpoint.pt'
            before = {'generation': 4, 'pending_job': {'seed': 123}}
            save_checkpoint(before, path)
            with patch('hybrid_training.checkpoint.os.replace', side_effect=KeyboardInterrupt):
                with self.assertRaises(KeyboardInterrupt):
                    save_checkpoint({'generation': 5}, path)
            self.assertEqual(before, load_checkpoint(path))
            with patch('hybrid_training.checkpoint.os.fsync', side_effect=[None,KeyboardInterrupt]):
                with self.assertRaises(KeyboardInterrupt):
                    save_checkpoint({'generation': 5}, path)
            self.assertEqual(5, load_checkpoint(path)['generation'])
            self.assertEqual([], list(Path(tmp).glob('*.pending-*')))

    def test_corruption_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'checkpoint.pt'
            save_checkpoint({'generation': 1}, path)
            data = bytearray(path.read_bytes()); data[-1] ^= 1; path.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'checksum'):
                load_checkpoint(path)
