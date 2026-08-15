"""Canonical, fixed-width observations for the Python CFR engine.

Only machine-readable game fields are encoded.  Display names and ``events``
are deliberately excluded, and cards attached to any seat other than the
receiving engine's seat are ignored even if a non-standard host exposes them.
All seat and team features are expressed relative to the receiving seat, so
rotating an otherwise identical table produces the same observation.
"""

from __future__ import annotations

import hashlib
import math
import struct
from typing import Any, Final, Iterable, Sequence

from .actions import (
    ACTION_COUNT,
    BID_NAMES,
    CARD_CODES,
    SUIT_NAMES,
    legal_action_ids,
)


ENCODER_VERSION: Final = "bideuchre-cfr-observation-v2"

PHASES: Final[tuple[str, ...]] = (
    "NotStarted",
    "Bidding",
    "ChoosingContract",
    "ExchangingBidderCard",
    "ExchangingPartnerCard",
    "Playing",
    "HandComplete",
    "GameComplete",
)
CONTRACT_MODES: Final[tuple[str, ...]] = ("High", "Low", "Trump")

MAX_AUCTION_ACTIONS: Final = 4
MAX_CURRENT_TRICK_PLAYS: Final = 4
MAX_COMPLETED_TRICKS: Final = 6
MAX_PLAYS_PER_TRICK: Final = 4

# Layout (all widths are stable model-format commitments):
#   phase 8, hand number 1, dealer 4, current seat/none 5,
#   canonical scores 2, relative card counts 4, sit-out flags 4,
#   private hand 24, private exchange memory 24, auction 4 * 12,
#   high bid/none 7, bidder/none 5,
#   contract 18, current trick 4 * 29, completed tricks 6 * 125,
#   canonical trick totals 2, winner/none 3, legal-action mask 61.
FEATURE_DIM: Final = 1086


def encode_position(position: dict[str, Any]) -> list[float]:
    """Encode one private BEUCI position as a canonical numeric vector."""

    if not isinstance(position, dict):
        raise TypeError("position must be an object")
    actor = _seat(position.get("seat"), "seat")
    game = _mapping(position.get("game"), "game")
    own_team = actor % 2

    features: list[float] = []
    features.extend(_one_hot(game.get("phase"), PHASES))
    features.append(_number(game.get("handNumber", 0), "handNumber") / 40.0)
    features.extend(_relative_seat_one_hot(game.get("dealer"), actor, False, "dealer"))
    features.extend(
        _relative_seat_one_hot(
            game.get("currentSeat"), actor, True, "currentSeat"
        )
    )

    scores = _team_values(game.get("scores", [0, 0]), "scores")
    features.extend((scores[own_team] / 40.0, scores[1 - own_team] / 40.0))

    players = _players_by_seat(game.get("players", []))
    for relative in range(4):
        player = players.get((actor + relative) % 4)
        count = 0.0 if player is None else _number(
            player.get("cardCount", 0), f"players[{relative}].cardCount"
        )
        features.append(count / 7.0)
    for relative in range(4):
        player = players.get((actor + relative) % 4)
        features.append(1.0 if player is not None and player.get("isSittingOut") is True else 0.0)

    # Enforce privacy at the feature boundary: only the actor's cards count.
    private_cards = players.get(actor, {}).get("cards")
    private_codes = set(_card_codes(private_cards, "private cards"))
    features.extend(1.0 if code in private_codes else 0.0 for code in CARD_CODES)
    private_memory = position.get("cfrPrivateMemory", {})
    remembered_exchange = (
        private_memory.get("partnersBestReceived")
        if isinstance(private_memory, dict)
        else None
    )
    features.extend(
        1.0 if code == remembered_exchange else 0.0 for code in CARD_CODES
    )

    auction = _bounded_sequence(
        game.get("auction", []), MAX_AUCTION_ACTIONS, "auction"
    )
    for index in range(MAX_AUCTION_ACTIONS):
        if index >= len(auction):
            features.extend([0.0] * 12)
            continue
        action = _mapping(auction[index], f"auction[{index}]")
        features.append(1.0)
        features.extend(
            _relative_seat_one_hot(
                action.get("seat"), actor, False, f"auction[{index}].seat"
            )
        )
        # None is pass. isPass is redundant and intentionally ignored.
        features.extend(_one_hot(action.get("bid"), (None, *BID_NAMES)))

    features.extend(_one_hot(game.get("highBid"), (None, *BID_NAMES)))
    features.extend(
        _relative_seat_one_hot(game.get("bidder"), actor, True, "bidder")
    )

    contract = game.get("contract")
    if contract is None:
        features.extend([0.0] * 18)
    else:
        contract = _mapping(contract, "contract")
        features.append(1.0)
        features.extend(_one_hot(contract.get("bid"), BID_NAMES))
        features.extend(_one_hot(contract.get("mode"), CONTRACT_MODES))
        features.extend(_one_hot(contract.get("trump"), (None, *SUIT_NAMES)))
        features.append(
            _number(contract.get("requiredTricks", 0), "contract.requiredTricks")
            / 6.0
        )
        features.append(1.0 if contract.get("isPartnersBest") is True else 0.0)
        features.append(1.0 if contract.get("isAlone") is True else 0.0)

    current_trick = _bounded_sequence(
        game.get("currentTrick", []),
        MAX_CURRENT_TRICK_PLAYS,
        "currentTrick",
    )
    _append_play_slots(
        features,
        current_trick,
        MAX_CURRENT_TRICK_PLAYS,
        actor,
        "currentTrick",
    )

    completed = _bounded_sequence(
        game.get("completedTricks", []),
        MAX_COMPLETED_TRICKS,
        "completedTricks",
    )
    for trick_index in range(MAX_COMPLETED_TRICKS):
        if trick_index >= len(completed):
            features.extend([0.0] * 125)
            continue
        trick = _mapping(completed[trick_index], f"completedTricks[{trick_index}]")
        features.append(1.0)
        features.extend(
            _relative_seat_one_hot(
                trick.get("leader"),
                actor,
                False,
                f"completedTricks[{trick_index}].leader",
            )
        )
        features.extend(
            _relative_seat_one_hot(
                trick.get("winner"),
                actor,
                False,
                f"completedTricks[{trick_index}].winner",
            )
        )
        plays = _bounded_sequence(
            trick.get("plays", []),
            MAX_PLAYS_PER_TRICK,
            f"completedTricks[{trick_index}].plays",
        )
        _append_play_slots(
            features,
            plays,
            MAX_PLAYS_PER_TRICK,
            actor,
            f"completedTricks[{trick_index}].plays",
        )

    trick_totals = _team_values(game.get("tricksByTeam", [0, 0]), "tricksByTeam")
    features.extend(
        (trick_totals[own_team] / 6.0, trick_totals[1 - own_team] / 6.0)
    )

    winner = game.get("gameWinner")
    if winner is None:
        canonical_winner: str | None = None
    else:
        winner_team = _team(winner, "gameWinner")
        canonical_winner = "own" if winner_team == own_team else "opponent"
    features.extend(_one_hot(canonical_winner, (None, "own", "opponent")))

    legal_ids = set(legal_action_ids(position))
    features.extend(1.0 if action_id in legal_ids else 0.0 for action_id in range(ACTION_COUNT))

    if len(features) != FEATURE_DIM:  # pragma: no cover - developer invariant
        raise RuntimeError(
            f"encoder produced {len(features)} features; expected {FEATURE_DIM}"
        )
    return features


def information_state_key(position: dict[str, Any]) -> str:
    """Return a stable versioned hash of the canonical private observation."""

    features = encode_position(position)
    packed = struct.pack(f"<{FEATURE_DIM}d", *features)
    digest = hashlib.sha256(
        ENCODER_VERSION.encode("ascii") + b"\0" + packed
    ).hexdigest()
    return f"{ENCODER_VERSION}:{digest}"


def _append_play_slots(
    output: list[float],
    plays: Sequence[Any],
    slot_count: int,
    actor: int,
    name: str,
) -> None:
    for index in range(slot_count):
        if index >= len(plays):
            output.extend([0.0] * 29)
            continue
        play = _mapping(plays[index], f"{name}[{index}]")
        output.append(1.0)
        output.extend(
            _relative_seat_one_hot(
                play.get("seat"), actor, False, f"{name}[{index}].seat"
            )
        )
        card = _mapping(play.get("card"), f"{name}[{index}].card")
        output.extend(_one_hot(_card_code(card), CARD_CODES))


def _players_by_seat(value: Any) -> dict[int, dict[str, Any]]:
    players = _bounded_sequence(value, 4, "players")
    result: dict[int, dict[str, Any]] = {}
    for index, value in enumerate(players):
        player = _mapping(value, f"players[{index}]")
        seat = _seat(player.get("seat"), f"players[{index}].seat")
        if seat in result:
            raise ValueError(f"players contains duplicate seat {seat}")
        result[seat] = player
    return result


def _card_codes(value: Any, name: str) -> Iterable[str]:
    if value is None:
        return ()
    cards = _bounded_sequence(value, len(CARD_CODES), name)
    return (
        code
        for card in cards
        if isinstance(card, dict) and (code := _card_code(card)) is not None
    )


def _card_code(card: dict[str, Any]) -> str | None:
    code = card.get("code")
    if not isinstance(code, str):
        return None
    normalized = code.upper()
    return normalized if normalized in CARD_CODES else None


def _relative_seat_one_hot(
    absolute: Any,
    actor: int,
    allow_none: bool,
    name: str,
) -> list[float]:
    if absolute is None and allow_none:
        relative = None
    else:
        relative = (_seat(absolute, name) - actor) % 4
    choices: tuple[int | None, ...] = (0, 1, 2, 3, None) if allow_none else (0, 1, 2, 3)
    return _one_hot(relative, choices)


def _one_hot(value: Any, choices: Sequence[Any]) -> list[float]:
    return [1.0 if value == choice else 0.0 for choice in choices]


def _mapping(value: Any, name: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ValueError(f"{name} must be an object")
    return value


def _bounded_sequence(value: Any, maximum: int, name: str) -> list[Any] | tuple[Any, ...]:
    if not isinstance(value, (list, tuple)):
        raise ValueError(f"{name} must be an array")
    if len(value) > maximum:
        raise ValueError(f"{name} cannot contain more than {maximum} items")
    return value


def _seat(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 3:
        raise ValueError(f"{name} must be a seat from 0 through 3")
    return value


def _team(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value not in (0, 1):
        raise ValueError(f"{name} must be team 0 or 1")
    return value


def _team_values(value: Any, name: str) -> tuple[float, float]:
    if not isinstance(value, (list, tuple)) or len(value) != 2:
        raise ValueError(f"{name} must contain exactly two values")
    return (_number(value[0], f"{name}[0]"), _number(value[1], f"{name}[1]"))


def _number(value: Any, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be numeric")
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"{name} must be finite")
    return number
