# Bid Euchre

A complete, cross-platform Bid Euchre game written in C#. It includes a native
desktop executable, an optional local web interface, an authoritative rules
engine, session and bot management, and a text protocol for external engines
inspired by chess UCI.

## What is included

- Four-seat games with opposite partnerships and random rotating dealers.
- High, Low, trump, Partners Best, and Alone contracts, with the bidder's
  partner sitting out during trick play in both solo contracts.
- Correct Right/Left Bower effective-suit behavior and follow-suit validation.
- Full scoring, first-to-40 game completion, and illegal-play penalties.
- Private player views: a human or engine receives its own cards, not every hand.
- Hot-seat human play, built-in bots, all-bot spectator games, and mixed tables.
- In-memory sessions that can be created, started, played, and removed.
- External bot loading through the Bid Euchre Universal Command Interface
  (BEUCI).
- A reusable command router, process client, engine host, sample C# bot, and
  basic SWI-Prolog bot.
- A PyTorch CFR bot that performs bounded counterfactual training and atomically
  autosaves its model and experience after every played decision.
- A high-strength C++ bot with Monte Carlo auction evaluation, hard void-suit
  inference, and bounded team minimax card-play search.
- Deliberately paced bot turns, a public card-by-card table log, and completed
  tricks that remain visible before the next lead.

The authoritative game rules are in
[`bid-euchre-final-rules.md`](bid-euchre-final-rules.md). The engine protocol is
documented in [`docs/BEUCI.md`](docs/BEUCI.md).

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) or newer
  compatible SDK.
- A desktop environment supported by Avalonia. A browser is only needed for the
  optional web client.
- [SWI-Prolog](https://www.swi-prolog.org/) 9 or newer is optional and needed
  only for the included Prolog bot.
- Python 3.10+ and PyTorch are optional and needed only for the learning CFR
  bot; its installer creates an isolated virtual environment.
- A C++20 compiler and CMake 3.20+ are optional and needed only to build the
  high-strength C++ engine.

The native interface uses the open-source Avalonia desktop framework. The rules,
session, and protocol layers have no third-party package dependencies.

## Run the native game

On Linux or macOS:

```bash
./run.sh
```

On any platform with the .NET SDK:

```text
dotnet run --project src/BidEuchre.Desktop/BidEuchre.Desktop.csproj
```

This opens a normal desktop window. It does not start a web server or browser.

To create a self-contained executable that works without an installed .NET
runtime:

```bash
./package.sh linux-x64
```

Without an argument, `./package.sh` detects the current Mac or Linux runtime.
On an Apple Silicon Mac, it also creates `dist/osx-arm64/Bid Euchre.app`:
open that app in Finder to play without installing .NET. Rules, pixel artwork,
font, and font license travel with the package. This is a local unsigned build;
external macOS distribution still requires signing and notarization.

Motion preferences are saved between launches. The native table fills the
window: Jokers, stored Tarots and Sigils, and the score sit in a strip along the top, and bids
and other choices appear over the middle of the board. Engines and Rules open
from the main menu. The web client also honors the system reduced-motion setting.

The executable is written to `dist/linux-x64/BidEuchre`. The packaging script
also accepts .NET runtime identifiers such as `win-x64`, `osx-x64`, or
`osx-arm64`; packaging another platform may download its runtime pack.

From the Play screen:

1. Choose the Normal, Relaxed, or Quick bot pace.
2. Give each seat a name and choose Human or Bot.
3. Select an engine for every bot seat and create the table.
4. Select **Start session**.
5. For a hot-seat game, use **View hand** to switch between human seats.

Every bot action pauses before the next action. The final card of a completed
trick stays in the center during the longer between-trick pause, and every play
is permanently listed in the table log for the current hand.

## Optional web interface

The browser interface remains available:

```bash
./run-web.sh
```

Then open `http://127.0.0.1:5050`. It uses the same paced session layer as the
native client. This is a **local-only hot-seat interface**, with no user accounts
or separate authorization for human seats. Every local user of the interface
can list tables and switch between their human hands. It is not suitable for LAN
or Internet hosting, reverse proxying, or use on a shared untrusted computer.

Requests require a loopback peer and host, and browser requests must use the
interface's own origin. These checks remain in force if `ASPNETCORE_URLS` or
`AllowedHosts` is overridden. API mutations require `Content-Type: application/json`.
External process loading over HTTP is disabled; use the desktop **Engines** page.

Sessions live in memory and are cleared when the server stops. The web server
allows 32 tables and expires tables after 30 minutes without a request to that
table, releasing any bot resources. Lobby polling does not keep a table alive.
`WebSessions:MaxSessions` (1–128) and `WebSessions:IdleTimeoutSeconds` (1–86400)
configure these bounds in the server configuration.

## Load an external bot

### Prolog example

With `swipl` installed, open **Engines** in the desktop application and enter:

- Executable: the absolute path to `engines/prolog/run.sh`
- Arguments: leave blank

The basic Prolog bot passes when it can and otherwise selects the first legal
action supplied by the host. It supports bidding, contract selection, both
Partners Best exchange phases, and card play. See
[`engines/prolog/README.md`](engines/prolog/README.md) for setup and tests.

### C# example

Build the included example first:

```text
dotnet build src/BidEuchre.SampleBot/BidEuchre.SampleBot.csproj
```

Open **Engines** in the desktop application and enter:

- Executable: the full path to `dotnet`
- Arguments: the full path to
  `src/BidEuchre.SampleBot/bin/Debug/net10.0/BidEuchre.SampleBot.dll`

The application starts the process and performs a BEUCI handshake. A successfully
loaded engine becomes available in every bot-seat selector. Executables are
started with the privileges of the game server, so only load engines you trust.

The built-in TableBot requires no process and is always available.

### Strong C++ heuristic bot

Build the native engine once:

```bash
./engines/cpp-heuristic/build.sh
```

Then load the absolute path to `engines/cpp-heuristic/run.sh` on the **Engines**
screen with no arguments. It evaluates complete auction outcomes, contract
choices, and Partners Best exchanges over sampled hidden deals, then uses
void-aware determinization and bounded double-dummy search during trick play.
See [`engines/cpp-heuristic/README.md`](engines/cpp-heuristic/README.md) for
tuning and tests.

### Self-improving Python CFR bot

Install the Python engine once:

```bash
./engines/python-cfr/install.sh
```

Then load the absolute path to `engines/python-cfr/run.sh` on the **Engines**
screen with no arguments. The bot uses PyTorch, masks its 61-action policy to
the host's legal actions, estimates counterfactual regret with sampled
rollouts, learns the completed-hand reward, and atomically autosaves after each
move. Multiple seats safely share the same SQLite journal and model. See
[`engines/python-cfr/README.md`](engines/python-cfr/README.md) for state paths,
tuning, and tests.

## Tests

```bash
./test.sh
```

The script runs browser interaction/motion regressions when Node.js is available,
and Mac bundle checks on macOS. Optional engine tests are explicitly skipped
when their Python 3.10+, PyTorch, C++, or SWI-Prolog prerequisites are missing.

The test executable has no external test-framework dependency. It returns a
nonzero exit code on failure and covers deck/rank rules, bowers, trick winners,
auction behavior, Partners Best and Alone play, scoring, penalties, private views,
protocol payloads, command parsing, and engine-host interaction.

When `swipl` is available, `test.sh` also runs the Prolog unit/transcript suite
and drives four Prolog engine processes through ordinary, Partners Best, and
Alone hands using the real process client.

When the Python CFR virtual environment is installed, the same command runs its
PyTorch tests and drives four learning-engine processes through those scenarios.

When CMake and a C++20 compiler are available, `test.sh` builds the native
heuristic bot, runs its unit/transcript tests, and drives four processes through
the same live game scenarios.

## Project map

| Project | Responsibility |
| --- | --- |
| `BidEuchre.Core` | Cards, auction, contracts, play legality, trick resolution, scoring, and private game views |
| `BidEuchre.Protocol` | Command framework, BEUCI action/position codecs, engine host, and external-process client |
| `BidEuchre.App` | Session management and paced human/bot orchestration shared by both interfaces |
| `BidEuchre.Desktop` | Native Avalonia desktop executable |
| `BidEuchre.Server` | Optional HTTP API and browser interface |
| `BidEuchre.SampleBot` | Standalone BEUCI engine example |
| `engines/prolog` | Basic external BEUCI engine written in SWI-Prolog |
| `engines/python-cfr` | Autosaving PyTorch CFR learning engine |
| `engines/cpp-heuristic` | Native Monte Carlo and double-dummy heuristic engine |
| `BidEuchre.Tests` | Executable rules and protocol verification suite |

The game engine is independent from the UI and bot protocol. All players—human,
built-in bot, or external engine—ultimately submit actions to the same state
machine and therefore use the same legality and scoring implementation.

## Local hybrid bot

The separate [hybrid workflow](engines/hybrid/README.md) provides native BEUCI play, private full-match self-play, resumable Mac-local training, immutable exports and authoritative screening/promotion gates. Install with `./engines/hybrid/install.sh`, build with `./engines/hybrid/build.sh`, and run the explicit hybrid tests and bounded `./engines/hybrid/scripts/smoke_cycle.sh` described there. Set `BIDEUCHRE_DOTNET` to your .NET 10 SDK executable when it is not on PATH; the scripts derive `DOTNET_ROOT` from it. CMake and training Python come from the dedicated hybrid environment.

The initial champion remains the frozen C++ heuristic. Tiny training and screening runs prove the software workflow, **not improved first-to-40 match strength**. Tree and double-dummy experiments are disabled by default. [Actual results, hashes, review decisions and limits](docs/superpowers/reports/2026-11-09-hybrid-bot-validation.md) distinguish completion of the local workflow from the still-unproven strength objective.

## Campaign cards and shop

**Play** starts a run with three lives, $0, and one shared 24-card deck. Permanent
card changes affect whoever draws that physical card for the rest of the run.
Each card supports one enhancement and one Sigil; replacing either preserves
the other. The campaign includes 10 enhancements, 31 Jokers, 9 Gumball bidding
unlocks, 12 Sigils, and 16 Tarot consumables.

Losing or forfeiting a match costs one life (two for the boss) in Antes 1 to 3,
and two lives (three for the boss) from Ante 4 onward. The run moves on to the
next match while any lives remain.

Both teams start each match at zero. Matches end after a fully scored hand
reaches either ±24 points in Ante 1, increasing by four per Ante, or after eight
hands. Ties continue. Contract success counts tricks only; effect bonuses remain
even after a failed bid.

Between matches the shop offers two $4 Jokers, a $3 Tarot, a $3 Sigil, and a
gumball machine with one $3 pull. Hold up to five distinct Jokers. Tarots and Sigils share one inventory of
four slots; discard one to free space. Sell Jokers for $2. Each pull is blind: a
gumball rolls out and permanently unlocks a random bidding option you do not
have yet; Partners Best, Alone, and the High and Low contracts
start locked for you. For a Tarot or Sigil, Buy stores it; Buy & Use applies it immediately to the
shared deck and needs no free slot. A stored Tarot or Sigil can be used whenever
it is yours to act: in the shop and on the screen before a match it is aimed at
the shared deck, and during a match it is played on cards in your hand on any of
your turns, whether you are about to bid, name the contract, exchange or play a
card. Using one does not end your turn. Panic Button is offered when bidding; it
costs eight team points, consumes a hand, and forces a fresh deal if the match
continues.

Win rewards are $5/$6/$8 for Small/Medium/Boss matches, plus $2/$4/$6 for victory
margins of 1–4/5–9/10+. Losses keep card and Joker earnings. Interest is $1 per $5
held after rewards, capped at $5. Match-end earnings and shop purchases are saved;
quitting an unfinished match retains committed card/inventory/money changes and
counts as a loss on Continue. A failed save is reported, and play can continue
in memory.

Campaign bots use the campaign's legal actions. Opponents can benefit from shared
card effects; they do not buy or use items in this version. External engines remain
available for ordinary Table play and are not supported for the new campaign
profile. The catalogue's initial values have regression coverage but are not a
claim of competitive balance or improved bot strength.

For an isolated local profile, set `BIDEUCHRE_DATA_DIRECTORY` before launching.
The app creates its `BidEuchre` subfolder there. Without the variable it uses the
normal local application-data directory.
