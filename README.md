# Bid Euchre

A complete, cross-platform Bid Euchre game written in C#. It includes a native
desktop executable, a local HTTP API, an authoritative rules
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
- A desktop environment supported by Avalonia.
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

Graphics preferences are saved between launches. UI scale adapts to the current
window size, restoring the requested scale when it fits; window presets are
clamped to the display’s working area. The native table fills the
window: Jokers, stored Tarots and Sigils, and the score sit in a strip along the top, and bids
and other choices appear in a scrollable column beside the board. Wild cards
open their legal suit choices when pressed. The campaign strip keeps status
text readable and scrolls inventory horizontally when needed; ordinary tables
hide the campaign shelves. Engines and Rules open
from the main menu. **Collection** browses every Joker, Tarot card, Gumball,
playing card, card enhancement, and Sigil, with artwork and descriptions in
scrollable categories. All entries are visible without starting a campaign.

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

## Local HTTP API

Start the API with `dotnet run --project src/BidEuchre.Server`. The default
launch profile listens at `http://127.0.0.1:5050`. Check `/api/health` for
server status. Local callers can list tables and view any human seat.

Requests require a loopback peer and host, and browser requests must use the
server's own origin. These checks remain in force if `ASPNETCORE_URLS` or
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

The script runs artwork and HTTP API regressions when Node.js is available,
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
| `BidEuchre.Server` | Local HTTP API |
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
the other. The campaign includes 8 enhancements, 32 Jokers, 8 Gumball bidding
unlocks, 8 Sigils, and 9 Tarot consumables. The active Sigils are Bear, Eagle,
Fish, Spider, Frog, Duck, Vulture, and Chameleon. The nine Tarot Cards are
Festival (Wild), Stone mason (Stone), Prospector (Golden), Devilish Deal,
Gambler, Traitor (Silver), Astronomer, Taxes, and Ectoplasm (Bouncy).
Safety Net unlocks bidding 2. The Chameleon Sigil keeps its separate identity.
Emerald and Stinky enhancements; Coin, Snake, Owl, and Peacock Sigils; and
Merchant, Beggar, Love Potion, Nightshade, Clover Tonic, Crystal Elixir, and
Glassblower Tarots are retired. Older runs load with retired card modifiers
cleared and retired held items, generated Tarots, and shop offers removed,
preserving the rest of the run and surviving item identities.

Losing or forfeiting a match costs one life (two for the boss) in Antes 1 to 3,
and two lives (three for the boss) from Ante 4 onward. The run moves on to the
next match while any lives remain.

Both teams start each match at zero. Antes 1–5 use targets of ±12, ±16, ±24,
±32, and ±42 points. Matches end after a fully scored hand reaches a target
or after eight full rounds; a tie continues for additional full rounds until
broken. Successful opponent contracts add +0/+1/+2/+3/+4 fixed points in
Antes 1–5, after hand multipliers. Contract success counts tricks only; effect
bonuses remain even after a failed bid.

Poker is a standalone showdown. Win the auction with an exact bid of 4 to reveal
all four hands. Each player's best five of six cards is compared; the winning
five cards are highlighted and each winning team earns +4 points once. Opposing
teams tied for best both receive +4. The result displays briefly, then the next
round is dealt automatically, without choosing trump or playing tricks. A Poker
showdown counts as one round and may end the match. Extra Poker copies pay only
when your team wins the showdown. Current card faces count, including copied
faces; Wild and Bear abilities do not change poker hands, and five of a kind
outranks a straight flush.

Between matches the shop offers two $4 Jokers, a Fortune Teller, and a
gumball machine with one $3 pull. Hold up to five distinct Jokers. Tarots and Sigils share one inventory of
four slots; discard one to free space. Sell Jokers for $2. Each pull is blind: a
gumball rolls out and permanently unlocks a random bidding option you do not
have yet. After all eight unique bids are unlocked, pulls grant random duplicates.
Astronomer uses the same rule and grants its gumball immediately, without a held
Planet card. Each extra copy awards +1 fixed point after hand multipliers when
you successfully make its matching bid; matching types such as Alone and High/Low
stack once each. Failed, unrelated and partner-made bids earn no duplicate bonus.
Unique unlocks and duplicate counts persist for the run. A shop reroll
costs $2, then $1 more per reroll that visit, and refills the three regular offers,
including sold slots. It preserves the gumball offer and whether its one paid
pull has been used. Partners Best,
Alone, and the High and Low contracts start locked for every seat.
The Fortune Teller prints one blind ticket for $3 or three for $6; choose one
to keep or use and the other printed tickets are lost.

Each shop has a soda vending machine. Buy one random new can per visit for $8. Its effect lasts for the run, and owning a base flavor makes its upgrade eligible. Hover over an owned can to read its effect.

Each shop starts with one $6 Joker Booster. Its wrapper is drawn at random
from five equally likely themes (Arcade, Comic, Horror, Retro-Synth and Tech).
Rerolls keep the same booster, including its sold status. Buy the booster, then
click its wrapper to tear it open and reveal four different Jokers you do not
own. If all five slots are full, sell an owned Joker for $2 before taking a card. Choose one Joker or skip all
four. Skipping spends the $6. Reopening the shop restores the same paid choices.

The Arcade appears in one third of shops, half with Midnight Grape, and every
shop with Twilight Tangerine. Entry costs $12 on the first play and includes
one play each of Plinko and roulette that visit. Plinko uses eight drop
positions. A score of 5–6 wins a Spectral Pack, 3–4 wins a Tarot
ticket and 1–2 wins nothing. On the twelve-pocket roulette wheel, an exact
number wins a Spectral Pack and a color match alone wins a Tarot ticket.
Rerolls preserve both play limits.

The Spectral Pack is an Arcade prize. Click its wrapper to tear it open, then
choose one of its three cards (four with Peach Plus) or skip the pack. Apply a chosen Sigil to one of the six offered
deck cards. With a free Joker slot, each
pack card has a 1 in 50 chance to be Legendary, with at most one Legendary per
pack. Choosing it fills every free Joker slot with a random unowned Joker.
You can keep a Tarot prize if you have room or use it immediately. A restart
restores the paid outcome. Resolve the Arcade result before playing again, rerolling or
leaving the shop.

An Arcade prize or newly printed ticket may be applied immediately without a free inventory slot.
These shop opportunities show six random cards from the shared deck, and only
those cards may be changed. Gambler lets you choose exactly two of those six to become Lucky.
Opening this six-card menu commits the chosen prize or printed ticket, and it must
be used before pressing Done. An unfinished Sigil purchase from an older save follows the same rule.
There is no Back or Cancel. You may also use any stored Tarot on the same six cards,
before or after applying the bought item. Closing and reopening the game restores
the unfinished menu, its six cards, and any effects already applied without charging again.
Outside that menu, stored Tarots and Sigils are usable only during a match, on cards
currently in your hand when it is your turn to bid, name the contract, exchange or play.
Stored consumables may still be discarded in the normal shop or pre-match screen.
Using one does not end your turn.

Small and Medium wins pay $5, and Boss wins pay $7. Add $1 for a winning margin
of 1–4 points, $2 for 5–9, or $3 for 10 or more. Losses keep card and Joker earnings. Interest is $1 per $5
held after rewards, capped at $5. Version 3 saves commit the hand, auction, pending
choices, effects, physical card order and arrangement policy atomically with campaign
resources after accepted actions. Closing the window preserves the match; Continue
restores it without charging a life. The menu's explicit Forfeit applies the loss
immediately. Older version 2 or bare saves restart the same match with a visible
legacy notice because they did not store the hand. Invalid saves are preserved until
explicit recovery. Failed saves are reported and accepted changes remain in memory.

Campaign bots use the campaign's legal actions. Opponents can benefit from shared
card effects; they do not buy or use items in this version. External engines remain
available for ordinary Table play and are not supported for the new campaign
profile. The catalogue's initial values have regression coverage but are not a
claim of competitive balance or improved bot strength.

For an isolated local profile, set `BIDEUCHRE_DATA_DIRECTORY` before launching.
The app creates its `BidEuchre` subfolder there. Without the variable it uses the
normal local application-data directory.

### Campaign API

Each campaign cookie identifies one private, persistent run. Freeplay tables
are available separately under `/api/sessions` and live in memory.

Campaign saves default to `BidEuchre/web-campaigns` beneath .NET's user `LocalApplicationData` directory. Each file is `<root>/<hashed-browser-identity>/campaign.json`, separate from the desktop `BidEuchre/campaign.json`. No browser input controls a filesystem path. The directory must be outside `wwwroot` and the published application output. Configure it using ASP.NET configuration, for example:

```sh
dotnet run --project src/BidEuchre.Server -- --WebCampaigns:DataDirectory=/absolute/private/campaign-data
```

`WebCampaigns:MaxSessions` defaults to 32 (range 1–128) and `WebCampaigns:IdleTimeoutSeconds` to 1800 (range 1–86400). Idle eviction stops resident bots without losing a life or deleting the save. GET displays a dormant saved match; Continue activates it. Each resident profile holds at most one session. The root is exclusively locked for the process lifetime: two servers cannot safely write the same directory. Automated server tests use separate temporary roots.

The persistent `bideuchre.campaign` cookie lasts one year and is HttpOnly, SameSite Strict, host-only, scoped to `/api/campaign`, and Secure on HTTPS. Plain loopback HTTP remains supported. `localhost` and `127.0.0.1` have distinct identities; cookies are shared across ports on the same host. Separate instances on one host should configure both separate roots and distinct `WebCampaigns:CookieName` values. Losing/clearing the cookie loses access to that run; there is no public profile listing or account recovery. Saved files are not automatically deleted on cookie or runtime expiry.

All existing loopback, Host, same-origin and JSON mutation protections remain active. If saving fails, the accepted action stays in memory and the API reports unsaved progress. Dirty sessions are retained at the capacity limit while saves retry; shutting down with persistent disk failure cannot guarantee that unsaved progress survives. After a network failure, refresh the current state before choosing another action; purchases and game moves are never automatically replayed.
