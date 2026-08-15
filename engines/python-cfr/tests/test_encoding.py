from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bideuchre_cfr.actions import ACTION_COUNT  # noqa: E402
from bideuchre_cfr.encoding import (  # noqa: E402
    FEATURE_DIM,
    encode_position,
    information_state_key,
)


def sample_position() -> dict:
    cards = [
        {"suit": "Clubs", "rank": "Nine", "code": "9C"},
        {"suit": "Spades", "rank": "Ace", "code": "AS"},
    ]
    return {
        "seat": 1,
        "game": {
            "phase": "Bidding",
            "handNumber": 1,
            "dealer": 0,
            "currentSeat": 1,
            "scores": [5, 8],
            "players": [
                {"seat": 0, "name": "A", "team": 0, "cardCount": 6, "cards": None, "isSittingOut": False},
                {"seat": 1, "name": "B", "team": 1, "cardCount": 2, "cards": cards, "isSittingOut": False},
                {"seat": 2, "name": "C", "team": 0, "cardCount": 6, "cards": None, "isSittingOut": False},
                {"seat": 3, "name": "D", "team": 1, "cardCount": 6, "cards": None, "isSittingOut": False},
            ],
            "auction": [],
            "highBid": None,
            "bidder": None,
            "contract": None,
            "currentTrick": [],
            "completedTricks": [],
            "tricksByTeam": [0, 0],
            "gameWinner": None,
            "legalActions": {
                "canPass": True,
                "bids": ["Three", "Four"],
                "contractModes": [],
                "trumpSuits": [],
                "cards": [],
            },
            "events": ["human text"],
        },
    }


class EncodingTests(unittest.TestCase):
    def test_fixed_width_and_legal_mask(self) -> None:
        features = encode_position(sample_position())
        self.assertEqual(FEATURE_DIM, len(features))
        self.assertEqual(ACTION_COUNT, len(features[-ACTION_COUNT:]))
        self.assertEqual(1.0, features[-ACTION_COUNT + 0])
        self.assertEqual(1.0, features[-ACTION_COUNT + 1])
        self.assertEqual(1.0, features[-ACTION_COUNT + 2])
        self.assertEqual(3, sum(features[-ACTION_COUNT:]))

    def test_names_events_and_hidden_cards_do_not_change_information_state(self) -> None:
        original = sample_position()
        changed = copy.deepcopy(original)
        changed["game"]["events"] = ["anything", "else"]
        changed["game"]["players"][0]["name"] = "Renamed"
        changed["game"]["players"][0]["cards"] = [
            {"suit": "Hearts", "rank": "Ace", "code": "AH"}
        ]
        self.assertEqual(information_state_key(original), information_state_key(changed))

    def test_invalid_player_seat_is_rejected(self) -> None:
        value = sample_position()
        value["game"]["players"][0]["seat"] = 9
        with self.assertRaises(ValueError):
            encode_position(value)


if __name__ == "__main__":
    unittest.main()
