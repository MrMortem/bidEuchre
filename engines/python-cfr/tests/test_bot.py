from __future__ import annotations

import base64
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def bidding_position() -> dict:
    return {
        "seat": 1,
        "game": {
            "phase": "Bidding",
            "handNumber": 1,
            "dealer": 0,
            "currentSeat": 1,
            "scores": [0, 0],
            "players": [
                {"seat": 0, "name": "A", "team": 0, "cardCount": 6, "cards": None, "isSittingOut": False},
                {"seat": 1, "name": "B", "team": 1, "cardCount": 6, "cards": [
                    {"suit": "Clubs", "rank": "Nine", "code": "9C"},
                    {"suit": "Clubs", "rank": "Ten", "code": "TC"},
                    {"suit": "Diamonds", "rank": "Jack", "code": "JD"},
                    {"suit": "Hearts", "rank": "Queen", "code": "QH"},
                    {"suit": "Spades", "rank": "King", "code": "KS"},
                    {"suit": "Spades", "rank": "Ace", "code": "AS"},
                ], "isSittingOut": False},
                {"seat": 2, "name": "C", "team": 0, "cardCount": 6, "cards": None, "isSittingOut": False},
                {"seat": 3, "name": "D", "team": 1, "cardCount": 6, "cards": None, "isSittingOut": False},
            ],
            "auction": [], "highBid": None, "bidder": None, "contract": None,
            "currentTrick": [], "completedTricks": [], "tricksByTeam": [0, 0],
            "gameWinner": None,
            "legalActions": {"canPass": True, "bids": ["Three"], "contractModes": [], "trumpSuits": [], "cards": []},
            "events": [],
        },
    }


def payload(value: dict) -> str:
    raw = json.dumps(value, separators=(",", ":")).encode()
    return base64.urlsafe_b64encode(raw).decode().rstrip("=")


@unittest.skipUnless(importlib.util.find_spec("torch"), "PyTorch is not installed")
class BotTests(unittest.TestCase):
    def test_handshake_play_autosave_and_reload(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            commands = "\n".join([
                "beuci", "isready", "newgame",
                f"position {payload(bidding_position())}",
                "go", "quit", "",
            ])
            environment = os.environ.copy()
            environment["PYTHONPATH"] = str(Path(__file__).resolve().parents[1])
            completed = subprocess.run(
                [sys.executable, "-u", "-m", "bideuchre_cfr.bot", "--state-dir", directory, "--rollouts", "1", "--train-batch", "1", "--deterministic"],
                input=commands,
                text=True,
                capture_output=True,
                env=environment,
                timeout=20,
                check=True,
            )
            lines = completed.stdout.splitlines()
            self.assertEqual('id name "PyTorch CFR Bot"', lines[0])
            self.assertIn("beuciok", lines)
            self.assertIn("readyok", lines)
            self.assertTrue(any(line in ("bestaction pass", "bestaction bid 3") for line in lines))

            from bideuchre_cfr.storage import LearningStore
            store = LearningStore(Path(directory))
            self.assertEqual(1, store.count_decisions())
            self.assertTrue(store.checkpoint_path.exists())
            from bideuchre_cfr.trainer import CFRTrainer
            reloaded = CFRTrainer(store)
            self.assertGreaterEqual(reloaded.state.updates, 1)


if __name__ == "__main__":
    unittest.main()
