"""Bounded, private-information-safe counterfactual rollouts for Bid Euchre.

The host remains authoritative.  This module is deliberately independent of
PyTorch and treats the supplied legal action IDs as the root action set.  It
determinizes only cards which are hidden from the acting seat, applies one root
action, and finishes the current hand with a small heuristic policy.
"""

from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
import hashlib
import json
import random
from typing import Any, Iterable, Mapping, Sequence

from .actions import (
    BID_NAMES,
    CARD_CODES,
    EXCHANGE_ACTION_OFFSET,
    PLAY_ACTION_OFFSET,
)


RANKS = "9TJQKA"
SUITS = "CDHS"
SUIT_NAMES = ("Clubs", "Diamonds", "Hearts", "Spades")
SUIT_LETTERS = dict(zip(SUIT_NAMES, SUITS, strict=True))
BID_STRENGTH = {
    "Three": 3,
    "Four": 4,
    "Five": 5,
    "Six": 6,
    "PartnersBest": 7,
    "Alone": 8,
}


def legal_cards(
    hand: Iterable[str | Mapping[str, Any]],
    current_trick: Sequence[Any],
    contract: Mapping[str, Any],
) -> list[str]:
    """Return legal card codes, including Left-Bower effective-suit rules."""

    cards = sorted((_card_code(card) for card in hand), key=_card_sort_key)
    if not current_trick:
        return cards
    led = _effective_suit(_play_card(current_trick[0]), contract)
    following = [card for card in cards if _effective_suit(card, contract) == led]
    return following or cards


def determine_trick_winner(
    plays: Sequence[Any], contract: Mapping[str, Any]
) -> int:
    """Return the absolute seat winning a non-empty three/four-card trick."""

    if not plays:
        raise ValueError("cannot determine the winner of an empty trick")
    normalized = [(_play_seat(play), _play_card(play)) for play in plays]
    led = _effective_suit(normalized[0][1], contract)
    mode = contract.get("mode")
    if mode == "Low":
        candidates = [item for item in normalized if _effective_suit(item[1], contract) == led]
        return min(candidates, key=lambda item: RANKS.index(item[1][0]))[0]
    if mode == "Trump":
        trump = _trump_letter(contract)
        trumps = [item for item in normalized if _effective_suit(item[1], contract) == trump]
        if trumps:
            return max(trumps, key=lambda item: _trump_strength(item[1], trump))[0]
    candidates = [item for item in normalized if _effective_suit(item[1], contract) == led]
    return max(candidates, key=lambda item: RANKS.index(item[1][0]))[0]


@dataclass
class _State:
    phase: str
    dealer: int
    current_seat: int
    hands: list[list[str]]
    auction: list[dict[str, Any]]
    high_bid: str | None
    bidder: int | None
    contract: dict[str, Any] | None
    current_trick: list[dict[str, Any]]
    tricks_by_team: list[int]
    pending_exchange: str | None = None
    completed: bool = False


class CounterfactualEvaluator:
    """Evaluate legal root actions with bounded deterministic rollouts.

    Returned values are ordered exactly like ``legal_action_ids`` and are score
    deltas from the acting seat's team perspective.  Duplicate IDs are allowed
    and produce duplicate aligned results.
    """

    def __init__(self, samples: int = 8, seed: int = 1946) -> None:
        if isinstance(samples, bool) or not isinstance(samples, int) or samples < 1:
            raise ValueError("samples must be a positive integer")
        self.samples = samples
        self.seed = int(seed)

    def evaluate(
        self, position: dict[str, Any], legal_action_ids: list[int]
    ) -> list[float]:
        if not isinstance(position, dict) or not isinstance(position.get("game"), dict):
            raise ValueError("position.game must be an object")
        seat = _seat(position.get("seat"))
        if not legal_action_ids:
            return []
        phase = position["game"].get("phase")
        for action_id in legal_action_ids:
            _validate_action_for_phase(action_id, phase)

        fingerprint = json.dumps(position, sort_keys=True, separators=(",", ":"), default=str)
        position_seed = int.from_bytes(hashlib.sha256(fingerprint.encode()).digest()[:8], "big")
        totals = [0.0] * len(legal_action_ids)
        for sample in range(self.samples):
            # Every action gets the identical determinization in a sample.
            deal_rng = random.Random(self.seed ^ position_seed ^ (sample * 0x9E3779B1))
            base = _state_from_position(position, deal_rng)
            for index, action_id in enumerate(legal_action_ids):
                state = deepcopy(base)
                # Common random numbers reduce variance between root actions.
                policy_rng = random.Random(
                    self.seed ^ position_seed ^ (sample * 0x85EBCA77)
                )
                _apply_action(state, action_id)
                _rollout(state, policy_rng)
                totals[index] += _utility(state, seat % 2)
        return [total / self.samples for total in totals]


def _state_from_position(position: Mapping[str, Any], rng: random.Random) -> _State:
    game = position["game"]
    viewer = _seat(position["seat"])
    players = game.get("players")
    if not isinstance(players, list) or len(players) != 4:
        raise ValueError("game.players must contain four players")

    known: dict[int, list[str]] = {}
    counts = [0, 0, 0, 0]
    for player in players:
        if not isinstance(player, dict):
            raise ValueError("each player must be an object")
        seat = _seat(player.get("seat"))
        count = player.get("cardCount")
        if isinstance(count, bool) or not isinstance(count, int) or count < 0:
            raise ValueError("player.cardCount must be a non-negative integer")
        counts[seat] = count
        cards = player.get("cards")
        if cards is not None:
            if not isinstance(cards, list):
                raise ValueError("player.cards must be an array or null")
            known[seat] = [_card_code(card) for card in cards]
    if viewer not in known:
        raise ValueError("the acting player's private hand is missing")

    public_card_list: list[str] = []
    for trick in game.get("completedTricks", []):
        for play in trick.get("plays", []):
            public_card_list.append(_play_card(play))
    for play in game.get("currentTrick", []):
        public_card_list.append(_play_card(play))
    all_known_cards = public_card_list + [
        card for cards in known.values() for card in cards
    ]
    known_cards = set(all_known_cards)
    if len(known_cards) != len(all_known_cards):
        raise ValueError("position contains duplicate cards")
    remaining = [card for card in CARD_CODES if card not in known_cards]
    rng.shuffle(remaining)
    hands: list[list[str]] = [[], [], [], []]
    cursor = 0
    for seat in range(4):
        if seat in known:
            if len(known[seat]) != counts[seat]:
                raise ValueError("visible hand does not match cardCount")
            hands[seat] = known[seat].copy()
        else:
            end = cursor + counts[seat]
            hands[seat] = remaining[cursor:end]
            cursor = end
    if cursor != len(remaining):
        raise ValueError("card counts are inconsistent with the public history")

    memory = position.get("cfrPrivateMemory")
    if isinstance(memory, Mapping) and game.get("contract", {}).get("isPartnersBest"):
        received = memory.get("partnersBestReceived")
        if isinstance(received, str):
            received = _card_code(received)
            bidder_value = game.get("bidder")
            if bidder_value is not None:
                partner = (_seat(bidder_value) + 2) % 4
                # The viewer's visible hand is authoritative. If it contains
                # the remembered card, the partner returned that same card and
                # determinization must never move it away.
                if received in hands[viewer]:
                    partner = viewer
                owner = next((candidate for candidate, hand in enumerate(hands) if received in hand), None)
                if owner is not None and owner != partner:
                    replacement = next(
                        (card for card in hands[partner] if card not in known_cards),
                        None,
                    )
                    if replacement is not None:
                        hands[owner].remove(received)
                        hands[partner].remove(replacement)
                        hands[owner].append(replacement)
                        hands[partner].append(received)

    current = game.get("currentSeat")
    if current is None:
        current = viewer
    contract = deepcopy(game.get("contract"))
    if contract is not None and not isinstance(contract, dict):
        raise ValueError("game.contract must be an object or null")
    bidder = game.get("bidder")
    if bidder is not None:
        bidder = _seat(bidder)
    return _State(
        phase=str(game.get("phase")),
        dealer=_seat(game.get("dealer")),
        current_seat=_seat(current),
        hands=hands,
        auction=deepcopy(game.get("auction", [])),
        high_bid=game.get("highBid"),
        bidder=bidder,
        contract=contract,
        current_trick=[_normalized_play(play) for play in game.get("currentTrick", [])],
        tricks_by_team=[int(value) for value in game.get("tricksByTeam", [0, 0])],
    )


def _apply_action(state: _State, action_id: int) -> None:
    seat = state.current_seat
    if action_id == 0:
        _bid(state, seat, None)
    elif 1 <= action_id <= 6:
        _bid(state, seat, BID_NAMES[action_id - 1])
    elif 7 <= action_id <= 12:
        mode = "High" if action_id == 7 else "Low" if action_id == 8 else "Trump"
        suit = None if action_id < 9 else SUIT_NAMES[action_id - 9]
        _choose_contract(state, seat, mode, suit)
    elif EXCHANGE_ACTION_OFFSET <= action_id < PLAY_ACTION_OFFSET:
        _exchange(state, seat, CARD_CODES[action_id - EXCHANGE_ACTION_OFFSET])
    elif PLAY_ACTION_OFFSET <= action_id < 61:
        _play(state, seat, CARD_CODES[action_id - PLAY_ACTION_OFFSET])
    else:
        raise ValueError(f"unknown action ID {action_id}")


def _bid(state: _State, seat: int, bid: str | None) -> None:
    state.auction.append({"seat": seat, "bid": bid})
    if bid is not None:
        state.high_bid = bid
        state.bidder = seat
    if len(state.auction) >= 4:
        if state.bidder is None:
            state.bidder = seat
            state.high_bid = "Three"
        state.phase = "ChoosingContract"
        state.current_seat = state.bidder
    else:
        state.current_seat = (seat + 1) % 4


def _choose_contract(state: _State, seat: int, mode: str, suit: str | None) -> None:
    bid = state.high_bid or "Three"
    state.bidder = seat
    state.contract = {
        "bid": bid,
        "mode": mode,
        "trump": suit,
        "isPartnersBest": bid == "PartnersBest",
        "isAlone": bid == "Alone",
        "requiredTricks": BID_STRENGTH[bid] if bid in ("Three", "Four", "Five", "Six") else 6,
    }
    if bid == "PartnersBest":
        state.phase = "ExchangingBidderCard"
        state.current_seat = seat
    else:
        _begin_play(state)


def _exchange(state: _State, seat: int, card: str) -> None:
    if card not in state.hands[seat]:
        raise ValueError(f"seat {seat} does not hold exchange card {card}")
    bidder = state.bidder
    if bidder is None:
        raise ValueError("Partners Best exchange has no bidder")
    partner = (bidder + 2) % 4
    state.hands[seat].remove(card)
    if state.phase == "ExchangingBidderCard":
        state.hands[partner].append(card)
        state.pending_exchange = card
        state.phase = "ExchangingPartnerCard"
        state.current_seat = partner
    elif state.phase == "ExchangingPartnerCard":
        state.hands[bidder].append(card)
        state.pending_exchange = None
        _begin_play(state)
    else:
        raise ValueError("exchange action outside an exchange phase")


def _begin_play(state: _State) -> None:
    state.phase = "Playing"
    state.current_seat = _next_active(state, state.dealer)


def _play(state: _State, seat: int, card: str) -> None:
    if state.contract is None:
        raise ValueError("play requires a contract")
    if card not in legal_cards(state.hands[seat], state.current_trick, state.contract):
        raise ValueError(f"illegal play {card} for seat {seat}")
    state.hands[seat].remove(card)
    state.current_trick.append({"seat": seat, "card": card})
    active_players = 3 if _partner_sits_out(state) else 4
    if len(state.current_trick) < active_players:
        state.current_seat = _next_active(state, seat)
        return
    winner = determine_trick_winner(state.current_trick, state.contract)
    state.tricks_by_team[winner % 2] += 1
    state.current_trick.clear()
    if sum(state.tricks_by_team) >= 6:
        state.completed = True
        state.phase = "HandComplete"
    else:
        state.current_seat = winner


def _rollout(state: _State, rng: random.Random) -> None:
    for _ in range(40):
        if state.completed:
            return
        seat = state.current_seat
        if state.phase == "Bidding":
            raises = _legal_raises(state.high_bid)
            forced = seat == state.dealer and len(state.auction) == 3 and state.high_bid is None
            if forced:
                _bid(state, seat, raises[0])
            elif raises and rng.random() < _bid_probability(state.hands[seat], raises):
                _bid(state, seat, raises[min(len(raises) - 1, _hand_strength(state.hands[seat]) // 7)])
            else:
                _bid(state, seat, None)
        elif state.phase == "ChoosingContract":
            mode, suit = _best_contract(state.hands[seat], state.high_bid)
            _choose_contract(state, seat, mode, suit)
        elif state.phase == "ExchangingBidderCard":
            _exchange(state, seat, min(state.hands[seat], key=lambda card: _card_power(card, state.contract)))
        elif state.phase == "ExchangingPartnerCard":
            _exchange(state, seat, max(state.hands[seat], key=lambda card: _card_power(card, state.contract)))
        elif state.phase == "Playing":
            cards = legal_cards(state.hands[seat], state.current_trick, state.contract or {})
            if not cards:
                raise ValueError("rollout reached a turn without a legal card")
            if state.contract and state.contract.get("mode") == "Low":
                card = min(cards, key=lambda item: _card_power(item, state.contract))
            else:
                card = max(cards, key=lambda item: _card_power(item, state.contract))
            _play(state, seat, card)
        else:
            raise ValueError(f"cannot roll out phase {state.phase}")
    raise RuntimeError("bounded rollout exceeded 40 actions")


def _utility(state: _State, acting_team: int) -> float:
    if not state.completed or state.contract is None or state.bidder is None:
        raise ValueError("utility requires a completed hand")
    bidding_team = state.bidder % 2
    defending_team = 1 - bidding_team
    bidding_tricks = state.tricks_by_team[bidding_team]
    defending_tricks = state.tricks_by_team[defending_team]
    bid = state.contract["bid"]
    if bid == "PartnersBest":
        bid_delta = 12 if bidding_tricks == 6 else -12
    elif bid == "Alone":
        bid_delta = 24 if bidding_tricks == 6 else -24
    else:
        target = BID_STRENGTH[bid]
        bid_delta = bidding_tricks if bidding_tricks >= target else -target
    deltas = [0, 0]
    deltas[bidding_team] = bid_delta
    deltas[defending_team] = defending_tricks
    opponent = 1 - acting_team
    return float(deltas[acting_team] - deltas[opponent])


def _legal_raises(high_bid: str | None) -> list[str]:
    strength = BID_STRENGTH.get(high_bid, -1)
    return [bid for bid in BID_NAMES if BID_STRENGTH[bid] > strength]


def _bid_probability(hand: Sequence[str], raises: Sequence[str]) -> float:
    return 0.15 + min(0.65, _hand_strength(hand) / 30) if raises else 0.0


def _hand_strength(hand: Sequence[str]) -> int:
    best_trump = max(
        sum(_card_power(card, {"mode": "Trump", "trump": suit}) for card in hand)
        for suit in SUIT_NAMES
    )
    return best_trump // 10


def _best_contract(hand: Sequence[str], bid: str | None) -> tuple[str, str | None]:
    best_suit = max(
        SUIT_NAMES,
        key=lambda suit: sum(_card_power(card, {"mode": "Trump", "trump": suit}) for card in hand),
    )
    if bid in ("Three", "PartnersBest"):
        return "Trump", best_suit
    high = sum(RANKS.index(card[0]) for card in hand)
    low = sum(5 - RANKS.index(card[0]) for card in hand)
    trump = sum(_card_power(card, {"mode": "Trump", "trump": best_suit}) for card in hand)
    if trump >= max(high * 8, low * 8):
        return "Trump", best_suit
    return ("High", None) if high >= low else ("Low", None)


def _card_power(card: str, contract: Mapping[str, Any] | None) -> int:
    if contract and contract.get("mode") == "Trump":
        trump = _trump_letter(contract)
        if _effective_suit(card, contract) == trump:
            return _trump_strength(card, trump)
    rank = RANKS.index(card[0])
    return rank


def _partner_sits_out(state: _State) -> bool:
    return bool(state.contract and state.contract.get("bid") in ("PartnersBest", "Alone"))


def _next_active(state: _State, seat: int) -> int:
    next_seat = (seat + 1) % 4
    if _partner_sits_out(state) and state.bidder is not None and next_seat == (state.bidder + 2) % 4:
        next_seat = (next_seat + 1) % 4
    return next_seat


def _effective_suit(card: str, contract: Mapping[str, Any]) -> str:
    if contract.get("mode") == "Trump" and card[0] == "J":
        trump = _trump_letter(contract)
        if card[1] != trump and _same_color(card[1], trump):
            return trump
    return card[1]


def _trump_strength(card: str, trump: str) -> int:
    if card == f"J{trump}":
        return 100
    if card[0] == "J" and card[1] != trump and _same_color(card[1], trump):
        return 99
    return RANKS.index(card[0])


def _same_color(first: str, second: str) -> bool:
    return (first in "DH") == (second in "DH")


def _trump_letter(contract: Mapping[str, Any]) -> str:
    trump = contract.get("trump")
    if trump not in SUIT_LETTERS:
        raise ValueError("trump contract must name a suit")
    return SUIT_LETTERS[trump]


def _card_code(card: str | Mapping[str, Any]) -> str:
    value = card if isinstance(card, str) else card.get("code")
    if not isinstance(value, str):
        raise ValueError("card must contain a code")
    value = value.upper()
    if value not in CARD_CODES:
        raise ValueError(f"unknown card code {value}")
    return value


def _play_card(play: Any) -> str:
    if isinstance(play, (tuple, list)) and len(play) == 2:
        return _card_code(play[1])
    if not isinstance(play, Mapping):
        raise ValueError("card play must be an object")
    return _card_code(play.get("card"))


def _play_seat(play: Any) -> int:
    if isinstance(play, (tuple, list)) and len(play) == 2:
        return _seat(play[0])
    if not isinstance(play, Mapping):
        raise ValueError("card play must be an object")
    return _seat(play.get("seat"))


def _normalized_play(play: Any) -> dict[str, Any]:
    return {"seat": _play_seat(play), "card": _play_card(play)}


def _seat(value: Any) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value not in range(4):
        raise ValueError("seat must be between 0 and 3")
    return value


def _card_sort_key(card: str) -> tuple[int, int]:
    return SUITS.index(card[1]), RANKS.index(card[0])


def _validate_action_for_phase(action_id: Any, phase: Any) -> None:
    if isinstance(action_id, bool) or not isinstance(action_id, int):
        raise TypeError("action IDs must be integers")
    valid = (
        phase == "Bidding" and 0 <= action_id <= 6
        or phase == "ChoosingContract" and 7 <= action_id <= 12
        or phase in ("ExchangingBidderCard", "ExchangingPartnerCard") and 13 <= action_id <= 36
        or phase == "Playing" and 37 <= action_id <= 60
    )
    if not valid:
        raise ValueError(f"action ID {action_id} is incompatible with phase {phase}")
