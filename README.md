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
native client.

Sessions intentionally live in memory and are cleared when the server stops.

## Load an external bot

### Prolog example

With `swipl` installed, open **Engines** in either GUI and enter:

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

Open **Engines** in either GUI and enter:

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
