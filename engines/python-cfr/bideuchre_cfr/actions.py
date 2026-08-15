"""Stable BEUCI action vocabulary and position decoding.

The learning code uses a fixed 61-element action space.  The host remains the
authority on legality: :func:`legal_action_ids` translates only values present
in ``game.legalActions`` and never attempts to reconstruct the game rules.
"""

from __future__ import annotations

import base64
import binascii
import json
from typing import Any, Final


BID_NAMES: Final[tuple[str, ...]] = (
    "Three",
    "Four",
    "Five",
    "Six",
    "PartnersBest",
    "Alone",
)
BID_TOKENS: Final[tuple[str, ...]] = (
    "3",
    "4",
    "5",
    "6",
    "partnersbest",
    "alone",
)
SUIT_NAMES: Final[tuple[str, ...]] = (
    "Clubs",
    "Diamonds",
    "Hearts",
    "Spades",
)
SUIT_TOKENS: Final[tuple[str, ...]] = (
    "clubs",
    "diamonds",
    "hearts",
    "spades",
)

# Suit-major ordering is part of the persisted model format.  Do not reorder
# these values without also changing the encoder/model version.
CARD_CODES: Final[tuple[str, ...]] = tuple(
    f"{rank}{suit}" for suit in "CDHS" for rank in "9TJQKA"
)

PASS_ACTION_ID: Final = 0
BID_ACTION_OFFSET: Final = 1
CONTRACT_HIGH_ACTION_ID: Final = 7
CONTRACT_LOW_ACTION_ID: Final = 8
CONTRACT_TRUMP_ACTION_OFFSET: Final = 9
EXCHANGE_ACTION_OFFSET: Final = 13
PLAY_ACTION_OFFSET: Final = 37
ACTION_COUNT: Final = 61

BID_ACTION_IDS: Final = {
    bid: BID_ACTION_OFFSET + index for index, bid in enumerate(BID_NAMES)
}
TRUMP_ACTION_IDS: Final = {
    suit: CONTRACT_TRUMP_ACTION_OFFSET + index
    for index, suit in enumerate(SUIT_NAMES)
}
EXCHANGE_ACTION_IDS: Final = {
    code: EXCHANGE_ACTION_OFFSET + index for index, code in enumerate(CARD_CODES)
}
PLAY_ACTION_IDS: Final = {
    code: PLAY_ACTION_OFFSET + index for index, code in enumerate(CARD_CODES)
}

ACTION_COMMANDS: Final[tuple[str, ...]] = (
    "bestaction pass",
    *(f"bestaction bid {token}" for token in BID_TOKENS),
    "bestaction contract high",
    "bestaction contract low",
    *(f"bestaction contract trump {suit}" for suit in SUIT_TOKENS),
    *(f"bestaction exchange {code}" for code in CARD_CODES),
    *(f"bestaction play {code}" for code in CARD_CODES),
)

if len(ACTION_COMMANDS) != ACTION_COUNT:  # pragma: no cover - import invariant
    raise RuntimeError("The BEUCI action vocabulary must contain 61 actions.")


def decode_position(payload: str) -> dict[str, Any]:
    """Decode an unpadded base64url BEUCI position payload.

    ``ValueError`` is used for every malformed-input case so the command loop
    can turn decoder failures into a single-line protocol error without
    depending on implementation-specific JSON/base64 exceptions.
    """

    if not isinstance(payload, str) or not payload:
        raise ValueError("position payload must be a non-empty string")

    try:
        encoded = payload.encode("ascii")
        encoded += b"=" * (-len(encoded) % 4)
        raw = base64.b64decode(encoded, altchars=b"-_", validate=True)
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeError, binascii.Error, json.JSONDecodeError, ValueError) as error:
        raise ValueError(f"invalid position payload: {error}") from error

    if not isinstance(value, dict):
        raise ValueError("invalid position payload: top-level JSON must be an object")
    return value


def legal_action_ids(position: dict[str, Any]) -> list[int]:
    """Return sorted action IDs explicitly authorized by ``legalActions``.

    The phase selects which fixed action namespace applies to card values, but
    all actual bids, contracts, suits, and cards come from the host-provided
    legal-action object.  Unknown future enum values are ignored safely.
    """

    game = _required_mapping(position, "game")
    legal = _required_mapping(game, "legalActions")
    phase = game.get("phase")
    action_ids: set[int] = set()

    if phase == "Bidding":
        if legal.get("canPass") is True:
            action_ids.add(PASS_ACTION_ID)
        for bid in _sequence(legal.get("bids", []), "legalActions.bids"):
            action_id = BID_ACTION_IDS.get(bid)
            if action_id is not None:
                action_ids.add(action_id)

    elif phase == "ChoosingContract":
        modes = set(
            value
            for value in _sequence(
                legal.get("contractModes", []),
                "legalActions.contractModes",
            )
            if isinstance(value, str)
        )
        if "High" in modes:
            action_ids.add(CONTRACT_HIGH_ACTION_ID)
        if "Low" in modes:
            action_ids.add(CONTRACT_LOW_ACTION_ID)
        if "Trump" in modes:
            for suit in _sequence(
                legal.get("trumpSuits", []),
                "legalActions.trumpSuits",
            ):
                action_id = TRUMP_ACTION_IDS.get(suit)
                if action_id is not None:
                    action_ids.add(action_id)

    elif phase in ("ExchangingBidderCard", "ExchangingPartnerCard"):
        _add_card_actions(action_ids, legal, EXCHANGE_ACTION_IDS)

    elif phase == "Playing":
        _add_card_actions(action_ids, legal, PLAY_ACTION_IDS)

    return sorted(action_ids)


def format_action(action_id: int) -> str:
    """Format one action ID as the exact BEUCI ``bestaction`` line."""

    if isinstance(action_id, bool) or not isinstance(action_id, int):
        raise TypeError("action_id must be an integer")
    if action_id < 0 or action_id >= ACTION_COUNT:
        raise ValueError(f"action_id must be between 0 and {ACTION_COUNT - 1}")
    return ACTION_COMMANDS[action_id]


def _add_card_actions(
    output: set[int],
    legal: dict[str, Any],
    vocabulary: dict[str, int],
) -> None:
    for card in _sequence(legal.get("cards", []), "legalActions.cards"):
        if not isinstance(card, dict):
            continue
        code = card.get("code")
        if not isinstance(code, str):
            continue
        action_id = vocabulary.get(code.upper())
        if action_id is not None:
            output.add(action_id)


def _required_mapping(value: Any, key: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise TypeError("position must be a mapping")
    result = value.get(key)
    if not isinstance(result, dict):
        raise ValueError(f"{key} must be an object")
    return result


def _sequence(value: Any, name: str) -> list[Any] | tuple[Any, ...]:
    if not isinstance(value, (list, tuple)):
        raise ValueError(f"{name} must be an array")
    return value

