from __future__ import annotations

import base64
import json
import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bideuchre_cfr.actions import (  # noqa: E402
    ACTION_COMMANDS,
    ACTION_COUNT,
    CARD_CODES,
    decode_position,
    format_action,
    legal_action_ids,
)


def position(phase: str, **legal: object) -> dict[str, object]:
    defaults: dict[str, object] = {
        "canPass": False,
        "bids": [],
        "contractModes": [],
        "trumpSuits": [],
        "cards": [],
    }
    defaults.update(legal)
    return {"seat": 0, "game": {"phase": phase, "legalActions": defaults}}


class ActionVocabularyTests(unittest.TestCase):
    def test_vocabulary_has_fixed_61_action_layout(self) -> None:
        self.assertEqual(61, ACTION_COUNT)
        self.assertEqual(61, len(ACTION_COMMANDS))
        self.assertEqual("bestaction pass", format_action(0))
        self.assertEqual("bestaction bid 3", format_action(1))
        self.assertEqual("bestaction bid partnersbest", format_action(5))
        self.assertEqual("bestaction bid alone", format_action(6))
        self.assertEqual("bestaction contract high", format_action(7))
        self.assertEqual("bestaction contract low", format_action(8))
        self.assertEqual("bestaction contract trump clubs", format_action(9))
        self.assertEqual("bestaction contract trump spades", format_action(12))
        self.assertEqual("bestaction exchange 9C", format_action(13))
        self.assertEqual("bestaction exchange AS", format_action(36))
        self.assertEqual("bestaction play 9C", format_action(37))
        self.assertEqual("bestaction play AS", format_action(60))
        self.assertEqual("9C", CARD_CODES[0])
        self.assertEqual("AC", CARD_CODES[5])
        self.assertEqual("9D", CARD_CODES[6])
        self.assertEqual("AS", CARD_CODES[-1])

    def test_invalid_action_ids_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            format_action(-1)
        with self.assertRaises(ValueError):
            format_action(ACTION_COUNT)
        with self.assertRaises(TypeError):
            format_action(True)
        with self.assertRaises(TypeError):
            format_action(1.0)  # type: ignore[arg-type]

    def test_bidding_uses_only_authoritative_legal_values(self) -> None:
        value = position(
            "Bidding",
            canPass=True,
            bids=["Alone", "Four", "Four", "FutureBid"],
            cards=[{"code": "AS"}],
        )
        self.assertEqual([0, 2, 6], legal_action_ids(value))

        value["game"]["legalActions"]["canPass"] = False  # type: ignore[index]
        self.assertEqual([2, 6], legal_action_ids(value))

    def test_contract_actions_require_listed_modes_and_suits(self) -> None:
        value = position(
            "ChoosingContract",
            contractModes=["Low", "Trump", "FutureMode"],
            trumpSuits=["Spades", "Clubs", "FutureSuit"],
        )
        self.assertEqual([8, 9, 12], legal_action_ids(value))

        no_trump_mode = position(
            "ChoosingContract",
            contractModes=["High"],
            trumpSuits=["Clubs", "Diamonds", "Hearts", "Spades"],
        )
        self.assertEqual([7], legal_action_ids(no_trump_mode))

    def test_card_actions_use_phase_specific_namespaces(self) -> None:
        cards = [{"code": "as"}, {"code": "9C"}, {"code": "AS"}, {"code": "ZZ"}]
        exchange = position("ExchangingPartnerCard", cards=cards)
        playing = position("Playing", cards=cards)
        self.assertEqual([13, 36], legal_action_ids(exchange))
        self.assertEqual([37, 60], legal_action_ids(playing))

    def test_inactive_phase_does_not_infer_actions(self) -> None:
        value = position(
            "HandComplete",
            canPass=True,
            bids=["Three"],
            cards=[{"code": "AS"}],
        )
        self.assertEqual([], legal_action_ids(value))

    def test_malformed_legal_action_shape_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            legal_action_ids({"seat": 0, "game": {"phase": "Bidding"}})
        with self.assertRaises(ValueError):
            legal_action_ids(position("Bidding", bids="Three"))


class PositionDecodeTests(unittest.TestCase):
    def test_decodes_unpadded_base64url_utf8_json(self) -> None:
        original = {
            "seat": 2,
            "game": {"phase": "Playing", "label": "naïve ♠"},
        }
        raw = json.dumps(original, separators=(",", ":")).encode("utf-8")
        payload = base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")
        self.assertEqual(original, decode_position(payload))

    def test_rejects_invalid_base64_utf8_json_and_json_shape(self) -> None:
        invalid_payloads = (
            "",
            "not+base64!",
            base64.urlsafe_b64encode(b"\xff").decode("ascii").rstrip("="),
            base64.urlsafe_b64encode(b"not json").decode("ascii").rstrip("="),
            base64.urlsafe_b64encode(b"[]").decode("ascii").rstrip("="),
        )
        for payload in invalid_payloads:
            with self.subTest(payload=payload), self.assertRaises(ValueError):
                decode_position(payload)


if __name__ == "__main__":
    unittest.main()

