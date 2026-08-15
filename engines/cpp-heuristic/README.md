# C++ Heuristic Bot

This is the high-strength, non-learning BEUCI engine for Bid Euchre. It combines
rule-specific evaluation with information-set Monte Carlo sampling and bounded
double-dummy search. It reads only the acting seat's private cards and public
game history.

## Strategy

The engine does substantially more than assign a fixed value to each card:

- Every bid and pass is compared on common sampled deals. Later auction turns,
  the likely winning contract, exact Bid Euchre scoring, and the current
  first-to-40 score are included in the estimate.
- Contract selection compares High, Low, and every legal trump suit by simulated
  trick distributions rather than raw rank totals.
- Partners Best evaluates bidder discards over possible partner hands. The
  partner evaluates all seven possible return cards before sitting out.
- During play, the engine infers hard void suits from completed and current
  tricks, samples only hidden deals consistent with those voids and card counts,
  and applies the same candidate play to every common deal.
- A team minimax solver searches those determinizations. It understands High,
  Low, Right and Left Bowers, following effective suit, teammate cooperation,
  the three-player turn order for Partners Best and Alone, and exact contract
  scoring.
- Search is bounded and has a rule-aware rollout fallback, so an action is
  returned comfortably inside the host timeout.

This is an imperfect-information game, so “strong” is not a mathematical claim
of optimal play. Determinized search can occasionally overestimate tactics that
depend on knowing a sampled deal. Within that limitation, the bot uses all legal
information exposed by BEUCI and is intended to be much stronger than the
included single-card heuristic examples.

## Requirements and build

- A C++20 compiler: GCC 10+, Clang 12+, or current MSVC
- CMake 3.20+

No JSON package or other third-party runtime library is required.

```bash
./engines/cpp-heuristic/build.sh
```

The optimized executable is written to:

```text
engines/cpp-heuristic/build/bideuchre-cpp-heuristic
```

## Load it in Bid Euchre

Open **Engines** and enter:

- **Executable:** the absolute path to `engines/cpp-heuristic/run.sh`
- **Arguments:** leave blank

On Windows, configure the built `bideuchre-cpp-heuristic.exe` directly.

## Tuning

The defaults use 160 hidden deals for auction and contract evaluation and a
700 ms play-search budget. The launcher forwards command-line options:

```bash
./engines/cpp-heuristic/run.sh \
  --samples 256 \
  --play-ms 1200 \
  --search-depth 14 \
  --search-nodes 80000
```

- `--samples N`: common hidden deals per decision, `16..4096`.
- `--play-ms N`: total card-play search budget, `20..5000` milliseconds.
- `--search-depth N`: maximum cards searched before a rollout, `1..30`.
- `--search-nodes N`: node cap for each determinized solve.
- `--seed N`: deterministic sampling seed.

The same values can be changed silently through BEUCI `setoption` using names
`Samples`, `PlayTimeMs`, `SearchDepth`, `SearchNodes`, and `Seed`.

Higher settings can improve close decisions but must remain under the host's
approximately eight-second complete-turn limit. The defaults are suitable for
running four independent instances on an ordinary desktop CPU.

## Tests

```bash
./engines/cpp-heuristic/test.sh
```

The suite builds an optimized binary, runs native rule/search tests, and checks
the exact BEUCI handshake, CRLF/case handling, silent commands, malformed input
recovery, and a real action transcript. The repository-wide `./test.sh` also
drives four C++ processes through a live auction plus ordinary, Partners Best,
and Alone hands using the production C# process client.

See [the BEUCI specification](../../docs/BEUCI.md) and
[the authoritative rules](../../bid-euchre-final-rules.md) for the host contract.
