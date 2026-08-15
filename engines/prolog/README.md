# Basic Prolog Bot

This directory contains a deliberately simple external Bid Euchre engine
written for [SWI-Prolog](https://www.swi-prolog.org/). It implements BEUCI
version 1 and chooses directly from the host's `legalActions` values:

- pass whenever passing is legal;
- otherwise use the first legal bid;
- use the first legal contract mode and trump suit, when needed;
- exchange the first legal card in either Partners Best exchange phase; and
- play the first legal card.

It is an integration example, not a competitive strategy. Because the host
does not request actions from a sitting partner, the bot naturally makes no
play after its team bids Partners Best or Alone. It participates only in the
two private Partners Best exchange turns when its seat is responsible for one.

## Requirements

Install SWI-Prolog 9 or newer and ensure `swipl` is on `PATH`. For example:

```bash
# Arch Linux
sudo pacman -S swi-prolog

# Debian or Ubuntu
sudo apt install swi-prolog-nox
```

## Run the tests

```bash
./engines/prolog/test.sh
```

The test suite covers base64url position decoding and every actionable game
phase, including both Partners Best exchange roles. It also checks the exact
handshake transcript and verifies that silent commands produce no output. The
repository-wide `./test.sh` additionally runs a real four-process integration
test whenever `swipl` is available.

## Load it in Bid Euchre

Open **Engines**, then enter:

- **Executable:** the absolute path to `engines/prolog/run.sh`
- **Arguments:** leave blank

The launcher resolves `basic_bot.pl` relative to itself, starts SWI-Prolog in
quiet mode without a user initialization file, and keeps standard output clean
for protocol messages.

On Windows, or when a shell launcher is inconvenient, configure it directly:

- **Executable:** the absolute path to `swipl` or `swipl.exe`
- **Arguments:** `-q -f none -s "C:/absolute/path/to/basic_bot.pl"`

You can also test the handshake manually:

```bash
printf 'beuci\nisready\nquit\n' | ./engines/prolog/run.sh
```

Expected output:

```text
id name "Basic Prolog Bot"
id author "Bid Euchre Project"
protocol bideuchre 1
beuciok
readyok
```

See [the full BEUCI specification](../../docs/BEUCI.md) when extending the
strategy or implementing another language binding.
