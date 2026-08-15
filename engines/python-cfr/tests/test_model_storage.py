from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bideuchre_cfr.actions import ACTION_COUNT  # noqa: E402
from bideuchre_cfr.encoding import FEATURE_DIM  # noqa: E402
from bideuchre_cfr.storage import LearningStore  # noqa: E402


class StorageTests(unittest.TestCase):
    def test_every_decision_is_durable_and_terminal_reward_is_attached(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = LearningStore(Path(directory))
            identifier = store.append_decision(
                information_key="test",
                features=[0.0] * FEATURE_DIM,
                legal_actions=[0, 1],
                action_id=0,
                strategy=[0.5, 0.5] + [0.0] * (ACTION_COUNT - 2),
                counterfactual=[0.0] * ACTION_COUNT,
                hand_number=3,
                seat=2,
            )
            self.assertGreater(identifier, 0)
            self.assertEqual(1, store.count_decisions())
            self.assertEqual(1, store.apply_terminal_reward(3, 2, 4.0))
            self.assertEqual(4.0, store.untrained()[0]["reward"])

    def test_terminal_reward_remains_trainable_after_rollout_training(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = LearningStore(Path(directory))
            identifier = store.append_decision(
                information_key="terminal",
                features=[0.0] * FEATURE_DIM,
                legal_actions=[0],
                action_id=0,
                strategy=[1.0] + [0.0] * (ACTION_COUNT - 1),
                counterfactual=[0.0] * ACTION_COUNT,
                hand_number=1,
                seat=0,
                game_key="unique-game",
            )
            store.mark_trained([identifier])
            self.assertEqual(
                1,
                store.apply_terminal_reward(
                    1, 0, 6.0, game_key="unique-game"
                ),
            )
            self.assertEqual([], store.untrained())
            rewarded = store.rewarded_untrained()
            self.assertEqual(1, len(rewarded))
            self.assertEqual(6.0, rewarded[0]["reward"])
            store.mark_rewards_trained([identifier])
            self.assertEqual([], store.rewarded_untrained())

    def test_game_keys_isolate_identical_hand_numbers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = LearningStore(Path(directory))
            for game_key in ("first", "second"):
                store.append_decision(
                    information_key=game_key,
                    features=[0.0] * FEATURE_DIM,
                    legal_actions=[0],
                    action_id=0,
                    strategy=[1.0] + [0.0] * (ACTION_COUNT - 1),
                    counterfactual=[0.0] * ACTION_COUNT,
                    hand_number=1,
                    seat=0,
                    game_key=game_key,
                )
            self.assertEqual(
                1, store.apply_terminal_reward(1, 0, 3.0, game_key="second")
            )

    def test_cumulative_cfr_information_set_round_trips(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = LearningStore(Path(directory))
            regrets = [0.0] * ACTION_COUNT
            strategy = [0.0] * ACTION_COUNT
            regrets[3] = 2.5
            strategy[3] = 0.75
            store.update_information_set("state", regrets, strategy, 7)
            loaded_regrets, loaded_strategy, visits = store.get_information_set(
                "state", ACTION_COUNT
            )
            self.assertEqual(7, visits)
            self.assertEqual(2.5, loaded_regrets[3])
            self.assertEqual(0.75, loaded_strategy[3])


@unittest.skipUnless(importlib.util.find_spec("torch"), "PyTorch is not installed")
class ModelTests(unittest.TestCase):
    def test_masked_strategies_are_normalized_and_never_select_illegal_actions(self) -> None:
        import torch
        from bideuchre_cfr.model import legal_mask, masked_average_strategy, regret_matching

        mask = legal_mask([2, 8, 60])
        regrets = torch.full((ACTION_COUNT,), -1.0)
        strategy = regret_matching(regrets, mask)
        self.assertAlmostEqual(1.0, float(strategy.sum()), places=6)
        self.assertTrue(torch.all(strategy[~mask] == 0))
        self.assertAlmostEqual(1.0 / 3.0, float(strategy[2]), places=6)

        logits = torch.arange(ACTION_COUNT, dtype=torch.float32)
        average = masked_average_strategy(logits, mask)
        self.assertAlmostEqual(1.0, float(average.sum()), places=6)
        self.assertTrue(torch.all(average[~mask] == 0))


if __name__ == "__main__":
    unittest.main()
