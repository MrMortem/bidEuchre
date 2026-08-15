from __future__ import annotations

import unittest

from bideuchre_cfr.actions import (
    BID_ACTION_IDS,
    CONTRACT_HIGH_ACTION_ID,
    EXCHANGE_ACTION_IDS,
    PLAY_ACTION_IDS,
    TRUMP_ACTION_IDS,
)
from bideuchre_cfr.simulator import (
    CounterfactualEvaluator,
    determine_trick_winner,
    legal_cards,
)


def card(code: str) -> dict[str, str]:
    suits = {"C": "Clubs", "D": "Diamonds", "H": "Hearts", "S": "Spades"}
    ranks = {"9": "Nine", "T": "Ten", "J": "Jack", "Q": "Queen", "K": "King", "A": "Ace"}
    return {"suit": suits[code[1]], "rank": ranks[code[0]], "code": code}


def position(
    phase: str,
    hand: list[str],
    *,
    seat: int = 0,
    dealer: int = 3,
    current_seat: int | None = None,
    counts: tuple[int, ...] | None = None,
    auction: list[dict] | None = None,
    high_bid: str | None = None,
    bidder: int | None = None,
    contract: dict | None = None,
    current_trick: list[dict] | None = None,
    completed_tricks: list[dict] | None = None,
    tricks: tuple[int, int] = (0, 0),
) -> dict:
    counts = counts or (len(hand), len(hand), len(hand), len(hand))
    players = []
    for player_seat in range(4):
        players.append(
            {
                "seat": player_seat,
                "name": str(player_seat),
                "team": player_seat % 2,
                "cardCount": counts[player_seat],
                "cards": [card(code) for code in hand] if player_seat == seat else None,
                "isSittingOut": False,
            }
        )
    return {
        "seat": seat,
        "game": {
            "phase": phase,
            "handNumber": 1,
            "dealer": dealer,
            "currentSeat": seat if current_seat is None else current_seat,
            "scores": [0, 0],
            "players": players,
            "auction": auction or [],
            "highBid": high_bid,
            "bidder": bidder,
            "contract": contract,
            "currentTrick": current_trick or [],
            "completedTricks": completed_tricks or [],
            "tricksByTeam": list(tricks),
            "gameWinner": None,
            "legalActions": {},
            "events": [],
        },
    }


class RuleTests(unittest.TestCase):
    def test_left_bower_must_follow_trump(self) -> None:
        contract = {"mode": "Trump", "trump": "Hearts", "bid": "Four"}
        hand = ["JD", "AS", "9H"]
        trick = [{"seat": 1, "card": card("AH")}]
        self.assertEqual(["JD", "9H"], legal_cards(hand, trick, contract))

    def test_left_bower_does_not_follow_printed_suit(self) -> None:
        contract = {"mode": "Trump", "trump": "Hearts", "bid": "Four"}
        hand = ["JD", "9D", "AS"]
        trick = [{"seat": 1, "card": card("AD")}]
        self.assertEqual(["9D"], legal_cards(hand, trick, contract))

    def test_right_then_left_bower_win_over_other_trump(self) -> None:
        contract = {"mode": "Trump", "trump": "Hearts", "bid": "Four"}
        plays = [(0, "AH"), (1, "JD"), (2, "JH"), (3, "9H")]
        self.assertEqual(2, determine_trick_winner(plays, contract))

    def test_low_chooses_lowest_card_in_led_suit(self) -> None:
        contract = {"mode": "Low", "trump": None, "bid": "Four"}
        plays = [(0, "AC"), (1, "9C"), (2, "AD"), (3, "TC")]
        self.assertEqual(1, determine_trick_winner(plays, contract))


class EvaluatorTests(unittest.TestCase):
    def test_partners_best_returned_card_stays_in_visible_bidder_hand(self) -> None:
        value = position(
            "Playing",
            ["JS"],
            seat=0,
            counts=(1, 1, 6, 1),
            dealer=3,
            current_seat=0,
            bidder=0,
            high_bid="PartnersBest",
            contract={
                "bid": "PartnersBest",
                "mode": "Trump",
                "trump": "Spades",
                "requiredTricks": 6,
                "isPartnersBest": True,
                "isAlone": False,
            },
            completed_tricks=[
                {
                    "number": number,
                    "leader": 0,
                    "winner": 0,
                    "plays": [
                        {"seat": seat, "card": card(code)}
                        for seat, code in zip((0, 1, 3), codes, strict=True)
                    ],
                }
                for number, codes in enumerate(
                    [
                        ["9C", "TC", "JC"],
                        ["QC", "KC", "AC"],
                        ["9D", "TD", "JD"],
                        ["QD", "KD", "AD"],
                        ["9H", "TH", "JH"],
                    ],
                    1,
                )
            ],
            tricks=(5, 0),
        )
        value["cfrPrivateMemory"] = {"partnersBestReceived": "JS"}
        self.assertEqual(
            [12.0],
            CounterfactualEvaluator(samples=1).evaluate(
                value, [PLAY_ACTION_IDS["JS"]]
            ),
        )

    def setUp(self) -> None:
        self.evaluator = CounterfactualEvaluator(samples=2, seed=77)

    def test_bidding_returns_aligned_deterministic_utilities(self) -> None:
        view = position("Bidding", ["9C", "TC", "JC", "QC", "KC", "AC"])
        actions = [0, BID_ACTION_IDS["Three"], BID_ACTION_IDS["Alone"]]
        first = self.evaluator.evaluate(view, actions)
        second = self.evaluator.evaluate(view, actions)
        self.assertEqual(3, len(first))
        self.assertEqual(first, second)
        self.assertTrue(all(isinstance(value, float) for value in first))

    def test_contract_high_and_trump_are_evaluated(self) -> None:
        view = position(
            "ChoosingContract",
            ["9C", "TC", "JC", "QC", "KC", "AC"],
            auction=[
                {"seat": 0, "bid": "Four"},
                {"seat": 1, "bid": None},
                {"seat": 2, "bid": None},
                {"seat": 3, "bid": None},
            ],
            high_bid="Four",
            bidder=0,
        )
        values = self.evaluator.evaluate(
            view, [CONTRACT_HIGH_ACTION_ID, TRUMP_ACTION_IDS["Clubs"]]
        )
        self.assertEqual(2, len(values))

    def test_both_partners_best_exchange_phases(self) -> None:
        contract = {
            "bid": "PartnersBest",
            "mode": "Trump",
            "trump": "Clubs",
            "requiredTricks": 6,
            "isPartnersBest": True,
            "isAlone": False,
        }
        bidder = position(
            "ExchangingBidderCard",
            ["9C", "TC", "JC", "QC", "KC", "AC"],
            high_bid="PartnersBest",
            bidder=0,
            contract=contract,
        )
        self.assertEqual(
            1,
            len(self.evaluator.evaluate(bidder, [EXCHANGE_ACTION_IDS["9C"]])),
        )

        # The partner has seven cards after receiving the bidder's card.  The
        # other hidden counts plus public history still conserve all 24 cards.
        partner = position(
            "ExchangingPartnerCard",
            ["9C", "9D", "TD", "JD", "QD", "KD", "AD"],
            seat=2,
            counts=(5, 6, 7, 6),
            high_bid="PartnersBest",
            bidder=0,
            contract=contract,
        )
        self.assertEqual(
            1,
            len(self.evaluator.evaluate(partner, [EXCHANGE_ACTION_IDS["9C"]])),
        )

    def test_play_preserves_input_order_and_duplicates(self) -> None:
        contract = {
            "bid": "Four",
            "mode": "High",
            "trump": None,
            "requiredTricks": 4,
            "isPartnersBest": False,
            "isAlone": False,
        }
        view = position(
            "Playing",
            ["9C", "AC"],
            counts=(2, 2, 2, 2),
            contract=contract,
            high_bid="Four",
            bidder=0,
            completed_tricks=[
                {
                    "number": number,
                    "leader": 0,
                    "winner": 0,
                    "plays": [
                        {"seat": seat, "card": card(code)}
                        for seat, code in enumerate(codes)
                    ],
                }
                for number, codes in enumerate(
                    [
                        ["TC", "9D", "9H", "9S"],
                        ["JC", "TD", "TH", "TS"],
                        ["QC", "JD", "JH", "JS"],
                        ["KC", "QD", "QH", "QS"],
                    ],
                    1,
                )
            ],
            tricks=(4, 0),
        )
        actions = [PLAY_ACTION_IDS["AC"], PLAY_ACTION_IDS["9C"], PLAY_ACTION_IDS["AC"]]
        values = self.evaluator.evaluate(view, actions)
        self.assertEqual(values[0], values[2])
        self.assertEqual(3, len(values))

    def test_alone_play_finishes_with_three_active_players(self) -> None:
        contract = {
            "bid": "Alone",
            "mode": "Trump",
            "trump": "Spades",
            "requiredTricks": 6,
            "isPartnersBest": False,
            "isAlone": True,
        }
        view = position(
            "Playing",
            ["JS"],
            counts=(1, 1, 6, 1),
            contract=contract,
            high_bid="Alone",
            bidder=0,
            completed_tricks=[
                {
                    "number": number,
                    "leader": 0,
                    "winner": 0,
                    "plays": [
                        {"seat": seat, "card": card(code)}
                        for seat, code in zip((0, 1, 3), codes, strict=True)
                    ],
                }
                for number, codes in enumerate(
                    [
                        ["9C", "TC", "JC"],
                        ["QC", "KC", "AC"],
                        ["9D", "TD", "JD"],
                        ["QD", "KD", "AD"],
                        ["9H", "TH", "JH"],
                    ],
                    1,
                )
            ],
            tricks=(5, 0),
        )
        value = self.evaluator.evaluate(view, [PLAY_ACTION_IDS["JS"]])
        self.assertEqual([24.0], value)

    def test_duplicate_public_card_is_rejected(self) -> None:
        view = position(
            "Playing",
            ["9C", "TC", "JC", "QC", "KC"],
            counts=(5, 5, 5, 5),
            contract={"bid": "Four", "mode": "High", "trump": None},
            high_bid="Four",
            bidder=0,
            completed_tricks=[
                {
                    "number": 1,
                    "leader": 0,
                    "winner": 0,
                    "plays": [
                        {"seat": 0, "card": card("AC")},
                        {"seat": 1, "card": card("9D")},
                        {"seat": 2, "card": card("9D")},
                        {"seat": 3, "card": card("9S")},
                    ],
                }
            ],
            tricks=(1, 0),
        )
        with self.assertRaisesRegex(ValueError, "duplicate cards"):
            self.evaluator.evaluate(view, [PLAY_ACTION_IDS["9C"]])

    def test_rejects_wrong_phase_action(self) -> None:
        view = position("Bidding", ["9C", "TC", "JC", "QC", "KC", "AC"])
        with self.assertRaises(ValueError):
            self.evaluator.evaluate(view, [PLAY_ACTION_IDS["9C"]])


if __name__ == "__main__":
    unittest.main()
