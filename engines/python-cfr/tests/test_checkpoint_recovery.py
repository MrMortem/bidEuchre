from __future__ import annotations

from contextlib import closing
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from test_bot import bidding_position, payload
from bideuchre_cfr.actions import ACTION_COUNT
from bideuchre_cfr.encoding import FEATURE_DIM
from bideuchre_cfr.storage import LearningStore


@unittest.skipUnless(importlib.util.find_spec('torch'), 'PyTorch is not installed')
class CheckpointRecoveryTests(unittest.TestCase):
    def test_corrupt_checkpoint_process_recovers_and_replaces_without_losing_journal(self):
        import torch
        from bideuchre_cfr.trainer import CFRTrainer

        for corruption in ('empty', 'truncated', 'truncated-archive', 'non-torch', 'list', 'null',
                           'model', 'optimizer', 'optimizer-groups', 'optimizer-lr',
                           'optimizer-moments', 'optimizer-nan', 'counters'):
            with self.subTest(corruption=corruption), tempfile.TemporaryDirectory() as directory:
                store = LearningStore(Path(directory))
                fresh = CFRTrainer(store)
                expected = {key: value.clone() for key, value in fresh.model.state_dict().items()}
                fresh.save()
                checkpoint = torch.load(store.checkpoint_path, weights_only=True)
                # A bad payload must never leave partially loaded network weights.
                checkpoint['model_state'] = {key: torch.ones_like(value) for key, value in expected.items()}
                if corruption == 'empty':
                    store.checkpoint_path.write_bytes(b'')
                elif corruption == 'truncated':
                    store.checkpoint_path.write_bytes(b'\x80\x02')
                elif corruption == 'truncated-archive':
                    store.checkpoint_path.write_bytes(store.checkpoint_path.read_bytes()[:100])
                elif corruption == 'non-torch':
                    store.checkpoint_path.write_bytes(b'corrupt')
                elif corruption == 'list':
                    torch.save([], store.checkpoint_path)
                elif corruption == 'null':
                    torch.save(None, store.checkpoint_path)
                else:
                    if corruption == 'model':
                        checkpoint['model_state'].pop(next(iter(expected)))
                    elif corruption == 'optimizer':
                        checkpoint['optimizer_state'] = None
                    elif corruption == 'optimizer-groups':
                        checkpoint['optimizer_state']['param_groups'] = []
                    elif corruption == 'optimizer-lr':
                        checkpoint['optimizer_state']['param_groups'][0]['lr'] = 'broken'
                    elif corruption in ('optimizer-moments', 'optimizer-nan'):
                        parameter_id = checkpoint['optimizer_state']['param_groups'][0]['params'][0]
                        moment = torch.zeros(2) if corruption == 'optimizer-moments' else torch.full_like(
                            next(iter(expected.values())), float('nan'))
                        checkpoint['optimizer_state']['state'][parameter_id] = {
                            'step': torch.tensor(1.0), 'exp_avg': moment, 'exp_avg_sq': moment.clone(),
                        }
                    else:
                        checkpoint['decisions'] = None
                    torch.save(checkpoint, store.checkpoint_path)
                store.append_decision(
                    information_key='preserved', features=[0.0] * FEATURE_DIM,
                    legal_actions=[0], action_id=0,
                    strategy=[1.0] + [0.0] * (ACTION_COUNT - 1),
                    counterfactual=[0.0] * ACTION_COUNT, hand_number=9, seat=0,
                )
                recovered = CFRTrainer(store)
                self.assertEqual(0, recovered.state.decisions)
                for key, value in recovered.model.state_dict().items():
                    self.assertTrue(torch.equal(expected[key], value), key)
                self.assertEqual(1, store.count_decisions())
                environment = os.environ.copy()
                environment['PYTHONPATH'] = str(Path(__file__).resolve().parents[1])
                commands = '\n'.join(['beuci', 'isready', 'newgame',
                    f'position {payload(bidding_position())}', 'go', 'quit', ''])
                result = subprocess.run(
                    [sys.executable, '-u', '-m', 'bideuchre_cfr.bot', '--state-dir', directory,
                     '--rollouts', '1', '--train-batch', '2', '--deterministic'],
                    input=commands, text=True, capture_output=True, env=environment,
                    timeout=30, check=True,
                )
                lines = result.stdout.splitlines()
                self.assertIn('beuciok', lines)
                self.assertIn('readyok', lines)
                self.assertFalse(any(line.startswith('error ') for line in lines), result.stdout)
                self.assertTrue(any(line.startswith('bestaction ') for line in lines), result.stdout)
                self.assertEqual(2, store.count_decisions())
                with closing(store.connect()) as connection:
                    self.assertEqual(1, connection.execute(
                        "SELECT COUNT(*) FROM decisions WHERE information_key = 'preserved'"
                    ).fetchone()[0])
                replacement = torch.load(store.checkpoint_path, weights_only=True)
                self.assertGreater(replacement['updates'], 0)
                reloaded = CFRTrainer(store)
                self.assertEqual(replacement['decisions'], reloaded.state.decisions)
                self.assertGreater(reloaded.state.decisions, 0)
