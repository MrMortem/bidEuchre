# BEUCI Bot Command-Line Interface

BEUCI—the Bid Euchre Universal Command Interface—is the line-oriented command
protocol used between Bid Euchre and an external bot process. It is inspired by
chess UCI, but its commands, position model, and actions are specific to this
game.

This document is both a comprehensive implementation outline and the protocol
reference for BEUCI version `1`.

## Contents

1. [Scope and design](#1-scope-and-design)
2. [Protocol at a glance](#2-protocol-at-a-glance)
3. [Transport and process contract](#3-transport-and-process-contract)
4. [Command and response index](#4-command-and-response-index)
5. [Host commands in detail](#5-host-commands-in-detail)
6. [Engine responses in detail](#6-engine-responses-in-detail)
7. [Action grammar and phase mapping](#7-action-grammar-and-phase-mapping)
8. [Position payload encoding](#8-position-payload-encoding)
9. [Position JSON schema](#9-position-json-schema)
10. [Position invariants by phase](#10-position-invariants-by-phase)
11. [Validation, failures, and fallbacks](#11-validation-failures-and-fallback-behavior)
12. [Example transcripts](#12-example-transcripts)
13. [Implementing a C# engine](#13-implementing-a-c-engine)
14. [Implementing an engine in another language](#14-implementing-an-engine-in-another-language)
15. [Loading and testing an engine](#15-loading-and-testing-an-engine)
16. [Troubleshooting](#16-troubleshooting)
17. [Operational security](#17-operational-security)
18. [Compatibility and extension rules](#18-compatibility-and-extension-rules)
19. [Source-of-truth implementation files](#19-source-of-truth-implementation-files)

## 1. Scope and design

- The Bid Euchre application is the **host**.
- The external program is the **engine** or **bot**.
- The host owns the authoritative game state, validates every action, resolves
  tricks, and scores the hand.
- The engine receives a complete private snapshot immediately before its turn.
- The engine chooses exactly one action from the supplied legal actions.
- One engine process controls one seat. If the same engine is assigned to
  multiple seats, the host starts a separate process for each seat.
- The protocol does not expose HTTP, sockets, shared files, or callbacks. Normal
  communication uses only the child process's standard input and standard
  output.

The words **MUST**, **SHOULD**, and **MAY** describe required, recommended, and
optional behavior for a compatible engine.

## 2. Protocol at a glance

The normal lifecycle is:

```text
Engine registration
  host starts engine
    -> beuci
    <- identity, protocol, beuciok
    -> isready
    <- readyok
  host closes the registration process

Game session
  host starts one engine process for each bot-controlled seat
    -> beuci
    <- identity, protocol, beuciok
    -> isready
    <- readyok
    -> newgame

  for each turn assigned to that seat
    -> position <base64url-json>
    -> go
    <- zero or more info lines
    <- exactly one bestaction line

  session shutdown
    -> quit
    engine exits
```

After a hand reaches `HandComplete` or `GameComplete`, the application sends
each bot one additional seat-private `position <payload>` without a following
`go`. This silent terminal observation lets learning engines record the final
score while remaining backward-compatible with version 1 engines: `position`
already means “replace the current snapshot” and requires no response.

The application performs a temporary handshake when an engine is added to the
catalog, sends `quit`, and disposes that probe process. It starts a fresh
process for each occupied bot seat when a table begins. Engines therefore MUST
NOT depend on state from the registration process. They MUST also tolerate
multiple independent instances when the same engine is assigned to multiple
seats; a global singleton lock, fixed private port, or exclusive state file can
prevent a table from starting.

## 3. Transport and process contract

### 3.1 Process launch

- The host launches the configured executable directly; it does not invoke a
  command shell.
- The configured argument string is passed to the executable.
- Standard input, standard output, and standard error are redirected.
- The engine inherits the host process environment and working-directory
  context. Engines SHOULD use absolute paths for external resources.
- Engines run with the same operating-system privileges as the game host. Users
  should load only trusted executables.
- The engine MUST remain alive until `quit`, end-of-input, or an unrecoverable
  failure.

### 3.2 Streams and direction

| Stream | Direction | Purpose |
| --- | --- | --- |
| Standard input | Host → engine | BEUCI commands |
| Standard output | Engine → host | BEUCI responses only |
| Standard error | Engine → redirected pipe | Optional human-oriented diagnostics |

An engine MUST NOT write banners, ordinary logs, prompts, progress bars, or
other non-protocol text to standard output. Use standard error for bounded
diagnostics or `info <text>` while answering `go`.

Commands whose response is listed as **None** are silent on success. In
particular, the engine MUST NOT print `newgameok`, `positionok`, option
acknowledgements, or similar lines. Such output remains queued and can be
mistaken for the response to the next `go`.

The current client redirects but does not continuously consume standard error.
An engine SHOULD keep standard-error output small so the operating-system pipe
cannot fill and block the process.

### 3.3 Encoding and framing

- Text is UTF-8.
- Each command or response occupies one physical line.
- A line ends with the platform newline accepted by the process APIs, normally
  LF (`\n`) or CRLF (`\r\n`).
- A command name is the first whitespace-delimited token.
- Command names and defined keyword values are case-insensitive.
- Examples use the canonical lowercase wire spelling.
- Engines MUST flush standard output after every response line.
- There is no binary framing and no explicit line-length prefix.
- Blank input lines are ignored by the reference command router. Engines SHOULD
  not emit unnecessary blank response lines.

### 3.4 Tokenization and quoting

Outside double quotes, spaces and other whitespace separate tokens. Double
quotes allow an argument to contain whitespace:

```text
id name "Example Strategy Engine"
setoption name "Risk Level" value "Quite aggressive"
```

The reference tokenizer recognizes these backslash escapes inside or outside
quotes:

| Source text | Token character |
| --- | --- |
| `\n` | newline |
| `\r` | carriage return |
| `\t` | tab |
| `\"` | double quote |
| `\\` | backslash |
| `\x` for another `x` | `x` |

Single quotes have no special meaning. An unfinished quote or trailing
backslash is a protocol error. For maximum interoperability, quote identity,
option-name, and option-value text when it contains whitespace, quotes, or
backslashes.

The reference tokenizer concatenates adjacent quoted and unquoted fragments,
so `one" two"three` is the single token `one twothree`. It discards an empty
quoted fragment (`""`) instead of producing an empty token. Engines SHOULD omit
empty optional values instead of attempting to encode them as empty quotes.

### 3.5 Current application time limits

The limits below describe the current Bid Euchre application, not an immutable
part of protocol version `1`:

- The process client allows approximately **5 seconds per expected output
  line** during handshake, readiness, and action selection.
- The session layer allows approximately **8 seconds for the complete action
  request**, including all `info` lines and the final `bestaction`.
- On shutdown, the client sends `quit`, waits approximately **1 second**, then
  may terminate the process tree.

An engine SHOULD return actions well below these limits. Repeated `info` lines
restart the per-line timer but do not extend the whole-turn limit. Handshake and
readiness have no separate whole-operation deadline or maximum ignored-line
count, so engines must still produce the required sentinel promptly.

The current client does not send `stop`, restart the engine, or drain late
output after an action timeout. A late `bestaction` can therefore be consumed as
the next turn's answer. Engines SHOULD cancel timed-out work internally and
never emit a delayed response after abandoning a request.

## 4. Command and response index

### 4.1 Host-to-engine commands

| Command | Required | Expected direct response | Purpose |
| --- | --- | --- | --- |
| `beuci` | Yes | Identity lines, `protocol`, then `beuciok` | Start handshake |
| `isready` | Yes | `readyok` | Synchronization barrier |
| `newgame` | Yes | None | Reset engine state for a game |
| `setoption name ...` | Optional | None | Set an engine-specific option |
| `position <payload>` | Yes per turn | None | Replace the private position |
| `go` | Yes per turn | Optional `info`, then `bestaction` | Request one action |
| `stop` | Reserved | None defined | Request search cancellation |
| `quit` | Yes at shutdown | None | Request process exit |

### 4.2 Engine-to-host responses

| Response | Context | Purpose |
| --- | --- | --- |
| `id name <text>` | `beuci` handshake | Engine display name |
| `id author <text>` | `beuci` handshake | Engine author |
| `protocol bideuchre 1` | `beuci` handshake | Game and protocol version |
| `beuciok` | End of `beuci` handshake | Handshake complete |
| `readyok` | `isready` | Earlier input fully processed |
| `info <text>` | While processing `go` | Optional progress/diagnostic text |
| `bestaction <action>` | End of `go` | The engine's selected action |
| `error <message>` | Command failure | Single-line protocol error |

## 5. Host commands in detail

The syntax below is the compatible wire contract. The provided `EngineHost` is
permissive about trailing arguments on `beuci`, `isready`, `newgame`, `go`,
`stop`, and `quit`, and does not enforce the handshake order as a state machine.
Engines MUST NOT rely on those implementation leniencies.

### 5.1 `beuci`

**Syntax**

```text
beuci
```

**Purpose**

- Starts the identity and version handshake.
- May be sent immediately after the child process starts.
- The engine MUST finish the response with `beuciok`.

**Canonical response**

```text
id name "Example Engine"
id author "Example Author"
protocol bideuchre 1
beuciok
```

**Response fields**

- `id name` SHOULD be a short user-facing engine name.
- `id author` SHOULD identify the author or project.
- `protocol bideuchre 1` MUST be emitted exactly as shown for BEUCI version 1.
- Identity text may be quoted and may contain spaces.
- The reference client tolerates a missing name by using the executable file
  name, and a missing author by using `Unknown`. Compatible engines should still
  send both fields.
- The current client also defaults a missing protocol version to `1` and does
  not reject a different protocol family or version. This is host leniency, not
  a negotiated extension: compatible engines MUST emit `protocol bideuchre 1`.

### 5.2 `isready`

**Syntax**

```text
isready
```

**Required response**

```text
readyok
```

The engine MUST respond only after it has processed all preceding commands.
This is a synchronization barrier, not a request to choose a move. The engine
must remain ready to accept later `newgame`, `position`, and `go` commands.

### 5.3 `newgame`

**Syntax**

```text
newgame
```

**Purpose and behavior**

- Starts a new game from the engine's perspective.
- Clears the provided `EngineHost` instance's stored position.
- The engine SHOULD reset game-level caches, opponent models, and accumulated
  search state.
- It does not contain a position; a later `position` command supplies the
  authoritative state.
- It has no required response.
- It is sent once when a game session starts, not before every hand.

### 5.4 `setoption`

**Canonical syntax**

```text
setoption name <option-name> [value <option-value>]
```

**Examples**

```text
setoption name "Risk Level" value "Aggressive"
setoption name Ponder value false
setoption name ClearCache
```

**Rules**

- `name` and its following text are required.
- `value` and its following text are optional.
- Multiword names and values SHOULD be quoted.
- The reference engine host joins all tokens between `name` and `value` into
  the name, and all tokens after `value` into the value.
- Unknown options MAY be ignored.
- Version 1 defines no mandatory option names or discovery mechanism.
- The stock Bid Euchre process client currently does not send `setoption`; the
  command is available for compatible hosts and future use.
- There is no required response. A malformed command may produce `error ...`.

### 5.5 `position`

**Syntax**

```text
position <base64url-payload>
```

**Rules**

- Exactly one payload token is required.
- The payload has no whitespace.
- It is unpadded base64url containing UTF-8 JSON.
- Each `position` command completely replaces the previous position.
- The position is a snapshot, not a sequence of incremental actions.
- The engine SHOULD discard or reconcile analysis tied to the prior snapshot.
- There is no required response.
- A malformed payload may produce `error invalid position payload: ...`.

The payload and schema are defined in Sections 8 and 9.

### 5.6 `go`

**Syntax**

```text
go
```

**Purpose and behavior**

- Requests exactly one action for the most recently supplied position.
- A valid `position` MUST precede `go` after process start or `newgame`.
- The engine MUST use the position's `game.phase` and `legalActions` fields.
- The engine MAY write zero or more `info <text>` lines.
- The engine MUST finish with exactly one `bestaction <action>` line.
- The engine MUST NOT send multiple candidate actions.
- The host does not request an evaluation score, principal variation, or search
  depth in version 1.
- The provided engine host retains the latest position after a response, so a
  repeated `go` can reuse it. Compatible hosts send a fresh `position` before
  every turn, and engines SHOULD require their decision logic to use the latest
  snapshot.

### 5.7 `stop`

**Syntax**

```text
stop
```

`stop` is reserved for cancellation of background analysis. The stock process
client does not currently send it. The provided `EngineHost` processes `go`
synchronously, so its no-op `stop` handler cannot interrupt an in-flight
`ChooseActionAsync` call. Custom asynchronous engines MAY implement prompt
cancellation, but version 1 defines no required response to `stop`.

Engines must still budget and cancel their own decision work so it completes
within the action time limit.

### 5.8 `quit`

**Syntax**

```text
quit
```

**Rules**

- The engine SHOULD stop work, release resources, and exit promptly.
- No response is required.
- End-of-input may also be treated as a shutdown request.
- The current client may kill the process tree if it has not exited after about
  one second.

## 6. Engine responses in detail

### 6.1 Identity and handshake completion

```text
id name "Engine Name"
id author "Author Name"
protocol bideuchre 1
beuciok
```

These lines belong to the `beuci` handshake. `beuciok` MUST be last. The engine
SHOULD not perform lengthy initialization before emitting the identity lines;
use `isready` as the barrier for remaining setup. The current client recognizes
`beuciok` and `readyok` only when the raw line has no leading or trailing
whitespace, although letter case is ignored.

### 6.2 `readyok`

```text
readyok
```

This is the only required response to `isready`.

### 6.3 `info`

```text
info considering three legal cards
info selected AS with score 42
```

- `info` is optional and unstructured in version 1.
- The current client ignores lines beginning with `info ` while waiting for
  `bestaction`.
- A bare `info` without a following space is not a valid progress line for the
  current client.
- Do not expose secrets in `info`; a host may display or log it in future.
- Frequent output is discouraged and does not extend the turn deadline.

### 6.4 `error`

```text
error <single-line-message>
```

The reference command router emits `error` for malformed input, invalid state,
and unknown commands. Newline characters in messages are replaced with spaces.
An unknown command has this form:

```text
error unknown-command <command-name>
```

While waiting for readiness or an action, the host treats an engine `error`
line as a failed engine operation. Engines normally should return a legal
`bestaction` instead of reporting an avoidable decision error.

### 6.5 `bestaction`

The full action grammar is:

```text
bestaction pass
bestaction bid <bid>
bestaction contract high
bestaction contract low
bestaction contract trump <suit>
bestaction exchange <card>
bestaction play <card>
```

Extra or missing tokens make the action malformed.

## 7. Action grammar and phase mapping

### 7.1 Canonical grammar

```text
action          = "bestaction" SP action-body

action-body     = "pass"
                / "bid" SP bid-token
                / "contract" SP "high"
                / "contract" SP "low"
                / "contract" SP "trump" SP suit-token
                / "exchange" SP card-code
                / "play" SP card-code

bid-token       = "3" / "4" / "5" / "6"
                / "partnersbest" / "alone"

suit-token      = "clubs" / "diamonds" / "hearts" / "spades"

card-code       = rank-code suit-code
rank-code       = "9" / "T" / "J" / "Q" / "K" / "A"
suit-code       = "C" / "D" / "H" / "S"
SP              = one or more whitespace characters
```

Keywords and card codes are case-insensitive. The formatter in the C# library
emits lowercase action words and lowercase card codes. Examples in this
document use uppercase card codes for readability.

The parser also accepts `partners-best` and `pb` as aliases for
`partnersbest`. Engines SHOULD emit the canonical `partnersbest` token.

The current parser's enum conversion may accidentally accept numeric strings
for contract modes or suits. Numeric spellings and undefined enum values are
not part of BEUCI and MUST NOT be emitted.

`10` is written as `T` in a card code: Ten of Hearts is `TH`, not `10H`.
There is no `N` rank or placeholder card in the protocol.

### 7.2 Phase-to-action table

| `game.phase` | Legal response form | Authoritative fields |
| --- | --- | --- |
| `Bidding` | `pass` or `bid <bid>` | `canPass`, `bids` |
| `ChoosingContract` | `contract high`, `contract low`, or `contract trump <suit>` | `contractModes`, `trumpSuits` |
| `ExchangingBidderCard` | `exchange <card>` | `cards` |
| `ExchangingPartnerCard` | `exchange <card>` | `cards` |
| `Playing` | `play <card>` | `cards` |
| `NotStarted` | No `go` expected | None |
| `HandComplete` | No `go` expected | None |
| `GameComplete` | No `go` expected | None |

### 7.3 Bidding actions

Examples:

```text
bestaction pass
bestaction bid 3
bestaction bid 6
bestaction bid partnersbest
bestaction bid alone
```

Rules:

- Send `pass` only when `legalActions.canPass` is `true`.
- Send a bid only if its JSON enum value appears in `legalActions.bids`.
- JSON bid values map to wire tokens as follows:

| JSON value | Wire token |
| --- | --- |
| `Three` | `3` |
| `Four` | `4` |
| `Five` | `5` |
| `Six` | `6` |
| `PartnersBest` | `partnersbest` |
| `Alone` | `alone` |

### 7.4 Contract-selection actions

Examples:

```text
bestaction contract high
bestaction contract low
bestaction contract trump clubs
bestaction contract trump diamonds
bestaction contract trump hearts
bestaction contract trump spades
```

Rules:

- `high` is legal only when `High` appears in `contractModes`.
- `low` is legal only when `Low` appears in `contractModes`.
- A trump response is legal only when `Trump` appears in `contractModes`, and
  its suit MUST appear in `trumpSuits`.
- Bid `3` and Partners Best require a trump contract.
- High and Low never include a suit token.

### 7.5 Partners Best exchange actions

Examples:

```text
bestaction exchange AS
bestaction exchange 9D
```

Rules:

- In `ExchangingBidderCard`, the bidder selects one card to give to the partner.
- In `ExchangingPartnerCard`, the partner selects one card to return.
- The card code MUST occur in `legalActions.cards`.
- The exchanged cards are private to the bidder and partner.
- After the return card, the partner sits out. The partner receives no `go`
  command during trick play and retains six unused cards.

### 7.6 Card-play actions

Examples:

```text
bestaction play TD
bestaction play JC
```

Rules:

- The card code MUST occur in `legalActions.cards`.
- The list already enforces possession, follow-suit rules, and Left Bower
  effective-suit behavior.
- In ordinary contracts, four players contribute to each trick.
- In Partners Best and Alone, the bidder's partner sits out and three players
  contribute to each trick.
- An engine SHOULD treat `legalActions.cards` as authoritative instead of
  independently guessing whether a card is legal.

## 8. Position payload encoding

### 8.1 Encoding algorithm

The host encodes `position` as follows:

1. Serialize a `BotPosition` object as camelCase JSON.
2. Serialize enum values as their case-sensitive names, such as `Bidding`,
   `PartnersBest`, `Hearts`, and `Nine`.
3. Encode the JSON bytes as UTF-8.
4. Encode those bytes with standard base64.
5. Remove trailing `=` padding.
6. Replace `+` with `-` and `/` with `_`.

The result is an unpadded base64url token.

### 8.2 Decoding examples

Python:

```python
import base64
import json

def decode_position(payload: str) -> dict:
    padded = payload + "=" * (-len(payload) % 4)
    raw = base64.urlsafe_b64decode(padded)
    return json.loads(raw.decode("utf-8"))
```

JavaScript:

```javascript
function decodePosition(payload) {
  const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(base64.length + ((4 - base64.length % 4) % 4), "=");
  const bytes = Uint8Array.from(atob(padded), character => character.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}
```

C# engines referencing `BidEuchre.Protocol` do not need to decode manually;
`EngineHost` supplies a typed `BotPosition` to `ChooseActionAsync`.

## 9. Position JSON schema

### 9.1 Representative decoded payload

The exact values vary by turn. This complete structural bidding example shows
all four players and the production privacy model; the illustrative card values
are not tied to a particular random seed:

```json
{
  "seat": 1,
  "game": {
    "phase": "Bidding",
    "handNumber": 1,
    "dealer": 0,
    "currentSeat": 1,
    "scores": [0, 0],
    "players": [
      {
        "seat": 0,
        "name": "South",
        "team": 0,
        "cardCount": 6,
        "cards": null,
        "isSittingOut": false
      },
      {
        "seat": 1,
        "name": "West",
        "team": 1,
        "cardCount": 6,
        "cards": [
          { "suit": "Clubs", "rank": "Nine", "code": "9C" },
          { "suit": "Spades", "rank": "Ace", "code": "AS" },
          { "suit": "Diamonds", "rank": "Jack", "code": "JD" },
          { "suit": "Clubs", "rank": "King", "code": "KC" },
          { "suit": "Hearts", "rank": "Queen", "code": "QH" },
          { "suit": "Spades", "rank": "Ten", "code": "TS" }
        ],
        "isSittingOut": false
      },
      {
        "seat": 2,
        "name": "North",
        "team": 0,
        "cardCount": 6,
        "cards": null,
        "isSittingOut": false
      },
      {
        "seat": 3,
        "name": "East",
        "team": 1,
        "cardCount": 6,
        "cards": null,
        "isSittingOut": false
      }
    ],
    "auction": [],
    "highBid": null,
    "bidder": null,
    "contract": null,
    "currentTrick": [],
    "completedTricks": [],
    "tricksByTeam": [0, 0],
    "gameWinner": null,
    "legalActions": {
      "canPass": true,
      "bids": ["Three", "Four", "Five", "Six", "PartnersBest", "Alone"],
      "contractModes": [],
      "trumpSuits": [],
      "cards": []
    },
    "events": ["Hand 1 began. South is the dealer."]
  }
}
```

The serializer includes `null` values. All collection fields in normal host
output are non-null arrays; `players[].cards` is the only nullable collection.
JSON property order should not be treated as meaningful.

The reference decoder is intentionally permissive: property names are matched
case-insensitively, unknown fields are ignored, and no post-deserialization
check proves seat ranges, array sizes, privacy, or cross-field consistency.
Engines should validate the fields they require, while still accepting
well-formed additive fields.

### 9.2 Top-level `BotPosition`

| Field | Type | Meaning |
| --- | --- | --- |
| `seat` | integer `0..3` | Seat controlled by this engine process |
| `game` | `GameView` | Complete private snapshot for the turn |

### 9.3 `GameView`

| Field | Type | Meaning |
| --- | --- | --- |
| `phase` | `GamePhase` string | Current state-machine phase |
| `handNumber` | integer | `0` before the game starts; otherwise one-based hand number |
| `dealer` | integer `0..3` | Dealer seat for this hand |
| `currentSeat` | integer `0..3` or `null` | Seat expected to act; normally equals top-level `seat` when `go` is sent |
| `scores` | two integers | Team scores as `[team0, team1]`; scores may be negative and the game target is 40 |
| `players` | four `PlayerView` objects | Seat, team, hand visibility, and sit-out state |
| `auction` | array of `AuctionAction` | Bids and passes in chronological order |
| `highBid` | `BidLevel` string or `null` | Current winning bid |
| `bidder` | integer `0..3` or `null` | Current auction winner/final bidder |
| `contract` | `Contract` or `null` | Chosen mode and optional trump suit |
| `currentTrick` | array of `CardPlay` | Cards played in the unfinished trick |
| `completedTricks` | array of `CompletedTrick` | Completed tricks for the current hand |
| `tricksByTeam` | two integers | Tricks won as `[team0, team1]` |
| `gameWinner` | integer `0..1` or `null` | Winning team after game completion |
| `legalActions` | `LegalActionView` | Authoritative actions for this engine |
| `events` | array of strings | Human-readable current-hand event log |

Seat and team layout is fixed:

| Seat | Table position | Team | Partner |
| --- | --- | --- | --- |
| `0` | South | `0` | Seat `2` |
| `1` | West | `1` | Seat `3` |
| `2` | North | `0` | Seat `0` |
| `3` | East | `1` | Seat `1` |

Wire seat and team identifiers are zero-based. Human-facing screens may label
the same values as Seats 1–4 and Teams 1–2. `gameWinner` is a team identifier,
not a seat.

Do not parse `events` to make decisions. Its wording is for people and is not a
stable machine interface.

### 9.4 `PlayerView`

| Field | Type | Meaning |
| --- | --- | --- |
| `seat` | integer `0..3` | Player seat |
| `name` | string | Display name |
| `team` | integer `0..1` | Partnership index |
| `cardCount` | integer | Number of cards currently held |
| `cards` | array of `Card` or `null` | Full hand only for the receiving engine's seat; otherwise `null` |
| `isSittingOut` | boolean | `true` when the player is inactive during Partners Best or Alone trick play |

Privacy rules in the normal application session path:

- The object for `BotPosition.seat` contains that engine's cards.
- Other players expose only `cardCount`; their `cards` value is `null`.
- During the private Partners Best exchange, each participating process sees
  only its own current hand.
- `isSittingOut` becomes true after the Partners Best exchange enters trick
  play. The partner is still active while returning the exchange card.

Redaction is performed when the application creates the `GameView`, not by
`PositionCodec`. The codec serializes whichever view its caller supplies and
does not verify that top-level `seat` equals `game.currentSeat`. A custom host
must create a seat-private view before encoding it.

### 9.5 `Card`

| Field | Type | Values |
| --- | --- | --- |
| `suit` | string | `Clubs`, `Diamonds`, `Hearts`, `Spades` |
| `rank` | string | `Nine`, `Ten`, `Jack`, `Queen`, `King`, `Ace` |
| `code` | string | Two-character code such as `9H`, `TD`, `JC`, `AS` |

The `suit` field is the printed suit. For the Left Bower, its effective suit
during a trump contract may differ. Engines may use the rules library to
compute effective suit, but the supplied legal-card list already accounts for
it.

### 9.6 `AuctionAction`

| Field | Type | Meaning |
| --- | --- | --- |
| `seat` | integer `0..3` | Acting seat |
| `bid` | `BidLevel` string or `null` | Bid, or `null` for a pass |
| `isPass` | boolean | Convenience value equivalent to `bid == null` |

### 9.7 `Contract`

| Field | Type | Meaning |
| --- | --- | --- |
| `bid` | `BidLevel` string | Winning bid |
| `mode` | `ContractMode` string | `High`, `Low`, or `Trump` |
| `trump` | suit string or `null` | Required for Trump, `null` for High/Low |
| `requiredTricks` | integer | `3..6`, or `6` for Partners Best/Alone |
| `isPartnersBest` | boolean | Convenience flag |
| `isAlone` | boolean | Convenience flag |

`contract` is `null` during bidding and remains `null` until the auction winner
chooses the mode. `highBid` and `bidder` are available before that choice.
There is no serialized `partnerSitsOut` contract field; use `isPartnersBest`,
`isAlone`, and each player's `isSittingOut` state.

### 9.8 `CardPlay`

| Field | Type | Meaning |
| --- | --- | --- |
| `seat` | integer `0..3` | Seat that played the card |
| `card` | `Card` | Played card |

### 9.9 `CompletedTrick`

| Field | Type | Meaning |
| --- | --- | --- |
| `number` | integer `1..6` | Trick number within the hand |
| `leader` | integer `0..3` | Seat that led |
| `winner` | integer `0..3` | Winning seat |
| `plays` | array of `CardPlay` | Chronological cards in the trick |

`plays` has four entries in ordinary contracts and three in Partners Best or
Alone.

### 9.10 `LegalActionView`

| Field | Type | Meaning |
| --- | --- | --- |
| `canPass` | boolean | Whether `bestaction pass` is currently legal |
| `bids` | array of `BidLevel` strings | Legal raises for `bestaction bid` |
| `contractModes` | array of `ContractMode` strings | Legal contract modes |
| `trumpSuits` | array of suit strings | Candidate suits when Trump is legal |
| `cards` | array of `Card` | Legal exchange or play cards for the current phase |

Only the fields relevant to the current phase are populated. Other lists are
empty. The engine SHOULD choose directly from these values.

### 9.11 Enum values

| Enum | JSON values |
| --- | --- |
| `GamePhase` | `NotStarted`, `Bidding`, `ChoosingContract`, `ExchangingBidderCard`, `ExchangingPartnerCard`, `Playing`, `HandComplete`, `GameComplete` |
| `BidLevel` | `Three`, `Four`, `Five`, `Six`, `PartnersBest`, `Alone` |
| `ContractMode` | `High`, `Low`, `Trump` |
| `Suit` | `Clubs`, `Diamonds`, `Hearts`, `Spades` |
| `Rank` | `Nine`, `Ten`, `Jack`, `Queen`, `King`, `Ace` |

Engines SHOULD tolerate additional JSON fields for forward compatibility.
They MUST preserve index semantics for `scores` and `tricksByTeam`, whose
entries mean `[team0, team1]`. `auction`, `currentTrick`, `completedTricks`,
`events`, and trick `plays` are chronological. Consumers SHOULD key `players`
by its explicit `seat` field. Legal-value and card-list order is not a strategic
ranking and SHOULD otherwise be treated as set-like.

## 10. Position invariants by phase

In production, a bot position is sent only for an actionable phase, top-level
`seat` equals `game.currentSeat`, and `legalActions` is populated only for that
seat. The schema can represent non-actionable phases for inspection, but the
stock session does not call `go` in them.

### `Bidding`

- `currentSeat` is the bidding player.
- `contract` is `null`.
- `legalActions.bids` contains every legal raise.
- `legalActions.canPass` is false only when the first three players passed and
  the dealer is forced to bid with no standing high bid.
- `highBid` and `bidder` are either both `null` or both non-null.
- `auction` contains zero through three chronological actions while another bid
  is expected.
- A bot returns `pass` or one listed bid.

### `ChoosingContract`

- `currentSeat` and `bidder` identify the auction winner.
- `highBid` is non-null; `contract` is still `null`.
- `auction` contains exactly four actions.
- `legalActions.contractModes` contains the legal modes.
- `legalActions.trumpSuits` contains all four suits. This list is present even
  if the engine ultimately chooses High or Low.
- A bid of `3` or Partners Best exposes only `Trump` as a legal mode.

### `ExchangingBidderCard`

- The contract is Partners Best.
- `currentSeat` is the bidder.
- `legalActions.cards` contains the bidder's current six cards.
- After the action, the bidder temporarily holds five cards and the partner
  holds seven.
- The partner is not marked as sitting out yet.

### `ExchangingPartnerCard`

- `currentSeat` is the bidder's partner.
- `legalActions.cards` contains the partner's seven cards, including the card
  just received.
- The partner may return any listed card, including the card just received.
- After the action, both bidder and partner again hold six cards.

### `Playing`

- `contract` is non-null.
- `currentSeat` is an active player.
- `legalActions.cards` contains only legal cards from that player's hand.
- `currentTrick` contains earlier plays in chronological order.
- The bidder's partner is marked `isSittingOut: true` and skipped for Partners
  Best and Alone. That seat receives no play-time position/`go` request.
- Ordinary tricks contain four plays. Partners Best and Alone tricks contain
  three; the sitting partner retains six unused cards.

### Complete phases

The stock host does not send `go` during `HandComplete`, `GameComplete`, or
`NotStarted`. `currentSeat` is normally `null` and legal-action fields are
empty. An illegal-play penalty can complete a hand while `currentTrick` is
partial, so consumers must not assume `currentTrick` is empty in every
completed-state snapshot.

## 11. Validation, failures, and fallback behavior

The host remains authoritative. Parsing a response does not make it legal; the
game engine validates its phase, seat, bid, contract, and card.

### 11.1 Before trick play

If a caught engine timeout, error, malformed response, or illegal action occurs
during bidding, contract selection, or exchange, the current application
records the engine error and applies a safe fallback:

| Phase | Current fallback |
| --- | --- |
| `Bidding` | Pass if legal; otherwise choose the lowest legal bid |
| `ChoosingContract` | Choose Clubs as trump |
| Partners Best exchange | Choose the first legal card |

Fallback behavior is a host safety feature, not a strategy API. Engines MUST
not rely on it.

### 11.2 During trick play

If a caught engine timeout, malformed response, protocol error, or illegal card
occurs during `Playing`, the hand ends and the illegal-play penalty is applied
according to the authoritative game rules.

This is intentionally stricter than pre-play fallback. Always select a card
from `legalActions.cards`.

Current limitation: a syntactically invalid card code such as `ZZ` throws a
`FormatException` below the action parser, while the session currently catches
only protocol, game-rule, and cancellation failures. That particular malformed
response can fault the bot loop instead of following the fallback/penalty path.
Engines MUST validate the two-character card grammar before responding.

### 11.3 Process failures

The operation fails if the engine exits, closes standard output, exceeds the
response timeout, or cannot be started. The table exposes a human-readable
error. Shutdown may forcibly terminate an unresponsive process tree. After a
timeout, the current client does not resynchronize its output stream; restarting
the table session is the safest recovery from late engine output.

## 12. Example transcripts

In these examples, `>` is host-to-engine and `<` is engine-to-host. The angle
markers explain direction and are not part of the wire text. Angle-bracketed
payload descriptions are placeholders, not literal payload tokens.

### 12.1 Registration probe and table startup

The temporary registration process completes the handshake and is then shut
down:

```text
> beuci
< id name "Example Engine"
< id author "Example Author"
< protocol bideuchre 1
< beuciok
> isready
< readyok
> quit
```

When a table later starts, a new process performs the same handshake and then
receives `newgame`:

```text
> beuci
< id name "Example Engine"
< id author "Example Author"
< protocol bideuchre 1
< beuciok
> isready
< readyok
> newgame
```

There is no response to `newgame`.

### 12.2 Bidding turn

```text
> position <complete-base64url-payload>
> go
< info estimated tricks 4
< bestaction bid 4
```

The placeholder represents a valid, complete encoded `BotPosition` such as the
decoded structure in Section 9.1.

### 12.3 Contract selection

```text
> position <complete-base64url-payload>
> go
< bestaction contract trump hearts
```

### 12.4 Partners Best exchange

Bidder process:

```text
> position <payload-with-phase-ExchangingBidderCard>
> go
< bestaction exchange 9C
```

Partner process:

```text
> position <payload-with-phase-ExchangingPartnerCard>
> go
< bestaction exchange AS
```

The partner process receives no `go` during the following trick play.

### 12.5 Alone contract seat routing

After an Alone bidder selects a legal contract, the bidder's partner receives
neither an exchange request nor a play request. Only the bidder and two
opponents receive `position`/`go` during the six tricks.

```text
> position <payload-with-phase-ChoosingContract-and-highBid-Alone>
> go
< bestaction contract low
```

### 12.6 Card play and shutdown

```text
> position <payload-with-phase-Playing>
> go
< bestaction play TD
> quit
```

## 13. Implementing a C# engine

Reference `BidEuchre.Protocol`, implement `IBidEuchreBot`, and run it through
`EngineHost`.

```csharp
using BidEuchre.Core;
using BidEuchre.Protocol;

public sealed class MyBot : IBidEuchreBot
{
    public string Name => "My Bot";
    public string Author => "Me";

    public ValueTask<BotAction> ChooseActionAsync(
        BotPosition position,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var view = position.Game;
        BotAction action = view.Phase switch
        {
            GamePhase.Bidding when view.LegalActions.CanPass => new BotAction.Pass(),
            GamePhase.Bidding => new BotAction.Bid(view.LegalActions.Bids[0]),
            GamePhase.ChoosingContract =>
                new BotAction.ChooseContract(ContractMode.Trump, view.LegalActions.TrumpSuits[0]),
            GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard =>
                new BotAction.Exchange(view.LegalActions.Cards[0]),
            GamePhase.Playing => new BotAction.Play(view.LegalActions.Cards[0]),
            _ => throw new ProtocolException($"No action is available during {view.Phase}.")
        };
        return ValueTask.FromResult(action);
    }
}
```

Executable entry point:

```csharp
var host = new EngineHost(new MyBot());
await host.RunAsync(Console.In, Console.Out);
```

Optional interface methods allow an engine to handle `newgame` and
`setoption`:

```csharp
public ValueTask NewGameAsync(CancellationToken cancellationToken = default)
{
    // Reset engine-owned state here.
    return ValueTask.CompletedTask;
}

public ValueTask SetOptionAsync(
    string name,
    string? value,
    CancellationToken cancellationToken = default)
{
    // Parse a recognized option here.
    return ValueTask.CompletedTask;
}
```

The included [`BidEuchre.SampleBot`](../src/BidEuchre.SampleBot/) is a complete
working example.

## 14. Implementing an engine in another language

A non-C# engine needs only standard process I/O, base64url decoding, JSON
parsing, and the action grammar.

The included [`Basic Prolog Bot`](../engines/prolog/) is a complete minimal
implementation of this loop using SWI-Prolog. It selects only values supplied
by `legalActions` and handles both Partners Best exchange phases. The
[`C++ Heuristic Bot`](../engines/cpp-heuristic/) is a native C++20 example with
a dependency-free JSON/base64url implementation, hidden-deal sampling, and
bounded card-play search.

Implementation loop:

1. Read one line from standard input.
2. Tokenize the command name case-insensitively.
3. On `beuci`, emit identity/version lines and `beuciok`, flushing each line.
4. On `isready`, finish pending initialization and emit `readyok`.
5. On `newgame`, clear engine-owned game state.
6. On `setoption`, apply or ignore the named optional setting and remain silent.
7. On `position`, decode and replace the stored private snapshot.
8. On `go`, inspect `game.phase`, choose from `legalActions`, and emit one
   `bestaction`.
9. On reserved `stop`, accept the command, cancel work if supported, and remain
   silent.
10. On `quit` or end-of-input, exit.
11. Keep all non-protocol logs off standard output.

Decision outline:

```text
if phase == Bidding:
    pass only if canPass, otherwise choose a value from bids
elif phase == ChoosingContract:
    choose a value from contractModes and, for Trump, trumpSuits
elif phase starts with Exchanging:
    choose a card code from legalActions.cards
elif phase == Playing:
    choose a card code from legalActions.cards
else:
    no action should have been requested
```

## 15. Loading and testing an engine

### 15.1 Build the included sample

```text
dotnet build src/BidEuchre.SampleBot/BidEuchre.SampleBot.csproj
```

For a framework-dependent C# engine, configure the game with:

- **Executable:** full path to `dotnet`
- **Arguments:** full path to the engine DLL, quoted if the path contains spaces

For a native or self-contained engine, configure its executable directly and
provide any required arguments. The arguments field is a raw process argument
string, not a shell command; shell operators and shell quoting rules should not
be assumed.

### 15.2 Manual handshake test

Linux/macOS example:

```bash
printf 'beuci\nisready\nquit\n' | \
  dotnet src/BidEuchre.SampleBot/bin/Debug/net10.0/BidEuchre.SampleBot.dll
```

Expected lines include:

```text
id name "TableBot"
id author "Bid Euchre Project"
protocol bideuchre 1
beuciok
readyok
```

The included external sample and built-in bot both currently identify as
`TableBot`; use the author and external-engine entry to distinguish them while
testing, or give a derived bot a unique name.

### 15.3 Integration checklist

- [ ] Process starts without a shell or interactive terminal.
- [ ] No banner or log text appears on standard output.
- [ ] `beuci` returns name, author, version, and `beuciok` within the timeout.
- [ ] `isready` returns `readyok`.
- [ ] `newgame`, `setoption`, and `position` are silent on success.
- [ ] `position` accepts unpadded base64url JSON.
- [ ] Other players' `cards: null` values are handled correctly.
- [ ] JSON enums are handled by name, not assumed numeric values.
- [ ] Every phase produces the correct `bestaction` form.
- [ ] Bids, modes, suits, and cards come from `legalActions`.
- [ ] Ten uses `T` in card codes.
- [ ] Partners Best exchange is handled in both exchange phases.
- [ ] A sitting Partners Best/Alone partner is not expected to play.
- [ ] Every response is flushed immediately.
- [ ] The engine responds well inside the time limits.
- [ ] `quit` exits promptly.
- [ ] Registration and table startup work in separate, repeated launches.
- [ ] Multiple independent engine instances can run concurrently.

## 16. Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Engine never appears in the catalog | Missing `beuciok`, output buffering, or startup timeout | Flush each handshake line and finish with `beuciok` |
| Engine loads but table startup fails | Registration worked but a fresh process cannot launch | Remove singleton locks and check repeatable startup, files, and fixed ports |
| Engine name is executable filename | Missing/malformed `id name` | Emit `id name "..."` before `beuciok` |
| Readiness timeout | Missing `readyok` or slow initialization | Treat `isready` as a barrier and flush `readyok` |
| `A position must be supplied before go` | `go` processed before valid `position`, or `newgame` cleared it | Store each `position` before accepting `go` |
| `Expected a bestaction response` | Logging or another response appeared where an action was expected | Keep logs on stderr or prefix progress with `info ` |
| Unknown/malformed action | Wrong token count or spelling | Follow the grammar in Section 7 exactly |
| Card parse error | Used `10H`, a long suit name, or invalid code | Use two characters such as `TH`, `9C`, or `AS` |
| Legal-looking card penalized | Card was not in `legalActions.cards`, often due to follow suit or Left Bower | Select directly from the supplied legal list |
| Partners Best bot stalls | Engine handles only one exchange phase | Handle both `ExchangingBidderCard` and `ExchangingPartnerCard` |
| Sitting partner never receives `go` | Partners Best or Alone is active | This is intentional; only three seats play each trick |
| Process hangs under heavy logging | Standard-error or standard-output pipe filled | Reduce diagnostics and never stream verbose logs continuously |
| Responses become wrong after timeout | A late action remained queued and desynchronized the stream | Stop delayed output and restart the table session |
| DLL path with spaces fails | Raw argument path was not quoted | Quote that individual argument in the configured argument string |
| Process is killed at shutdown | Engine did not exit promptly after `quit` | Cancel work and terminate within one second |

## 17. Operational security

- An external engine is arbitrary native code, not a sandboxed script. It runs
  as the host's operating-system identity and can access whatever that identity
  can access. Load trusted bots only or isolate them with a dedicated user,
  container, filesystem, network policy, and resource limits.
- Avoid secrets in configured command-line arguments; they may be visible in
  process listings and application engine descriptors.
- The current client imposes no maximum protocol line length or total ignored
  output count. Hardened deployments should bound output and resource use.
- The stock web server's `POST /api/engines/load` endpoint is unauthenticated
  and accepts an executable path plus arguments. Exposing it beyond trusted
  loopback users enables arbitrary code execution under the server identity.
  Add strong authentication, authorization, and an executable allowlist before
  any wider exposure.
- `GET /api/engines` exposes registered executable paths and argument strings;
  treat that metadata as sensitive too.

## 18. Compatibility and extension rules

- Protocol version is currently `1`.
- Engines MUST emit `protocol bideuchre 1`; hosts SHOULD verify both the game
  name and version even though the current client is permissive.
- Engines SHOULD ignore unknown JSON properties so compatible fields can be
  added without breaking strict decoders.
- Engines MUST NOT invent commands or structured `info` fields and assume the
  stock host understands them.
- Version 1 has no option-discovery command, pondering mode, persistent analysis
  thread contract, evaluation schema, or network transport.
- Human-readable event and error wording is not a stable machine API.
- The legal-action object and `bestaction` grammar are the stable decision
  boundary.
- The position payload has no independent schema-version field. Public C#
  property additions can add JSON fields, which is why tolerant readers are
  required.

## 19. Source-of-truth implementation files

- [`BotAction.cs`](../src/BidEuchre.Protocol/BotAction.cs): action grammar and
  parsing
- [`CommandFramework.cs`](../src/BidEuchre.Protocol/CommandFramework.cs): line
  tokenizer, quoting, routing, and errors
- [`EngineHost.cs`](../src/BidEuchre.Protocol/EngineHost.cs): reference engine
  command host
- [`EngineProcessClient.cs`](../src/BidEuchre.Protocol/EngineProcessClient.cs):
  game-side child-process client and timeouts
- [`PositionCodec.cs`](../src/BidEuchre.Protocol/PositionCodec.cs): base64url JSON
  payload encoding
- [`GameView.cs`](../src/BidEuchre.Core/GameView.cs): private position schema
- [`Domain.cs`](../src/BidEuchre.Core/Domain.cs): cards, enums, contracts, and
  trick records
- [`SimpleBot.cs`](../src/BidEuchre.SampleBot/SimpleBot.cs): complete sample
  strategy
- [`basic_bot.pl`](../engines/prolog/basic_bot.pl): minimal external SWI-Prolog
  strategy and BEUCI command loop
- [`bid-euchre-final-rules.md`](../bid-euchre-final-rules.md): authoritative game
  legality and scoring rules
