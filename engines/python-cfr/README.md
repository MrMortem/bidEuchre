# PyTorch CFR Bot

This is a learning BEUCI engine for Bid Euchre. It combines a compact PyTorch
network with sampled counterfactual rollouts (CFR+) and improves from every
decision it plays.

## What it learns

- A fixed 61-action policy covers passing, every bid, every contract, all four
  trump suits, all 24 exchanges, and all 24 card plays.
- The host-provided `legalActions` list is always applied as a hard mask, so the
  model cannot intentionally select an illegal action.
- Bounded hidden-card determinizations estimate the value of every legal
  alternative, providing counterfactual regret targets rather than treating the
  chosen move as its own reward.
- The game host sends a private terminal observation after each completed hand.
  The bot attaches the real team score delta to every decision from that hand.

Online neural training is stochastic, so no learner can honestly guarantee
that every single weight update makes it stronger. Here, “always improving”
means every completed `go` durably adds a counterfactual sample, performs a
bounded update, and saves the resulting learning state before the action is
returned.

## Install

Python 3.10 or newer is required. Run:

```bash
./engines/python-cfr/install.sh
```

The installer creates `engines/python-cfr/.venv` and installs PyTorch plus this
engine. PyTorch's wheel is large, so the first install can take a while.

The dependency range is recorded in `pyproject.toml`. For CPU-only or
CUDA-specific installations, install the appropriate PyTorch build in the
virtual environment first, then run:

```bash
./engines/python-cfr/.venv/bin/python -m pip install ./engines/python-cfr
```

## Load it in Bid Euchre

Open **Engines** and enter:

- **Executable:** the absolute path to `engines/python-cfr/run.sh`
- **Arguments:** leave blank

Then choose **PyTorch CFR Bot** for any bot seat. Four seats may use it at once;
SQLite WAL journaling and a cross-process training lock safely merge their
learning into the same model.

## Autosave files

By default, durable state lives in `engines/python-cfr/state/`:

- `experience.sqlite3` contains every decision, legal mask, strategy,
  counterfactual targets, terminal rewards, and training status.
- `cfr-model.pt` contains model weights, optimizer state, schema versions, and
  update counters.
- `training.lock` coordinates concurrent seat processes.

Checkpoints use a same-directory temporary file, `fsync`, and atomic replace.
If a checkpoint is missing, corrupt, or incompatible, the bot starts a fresh
network while retaining the SQLite experience journal.

Set `BIDEUCHRE_CFR_STATE_DIR` to use another state directory. For example:

```bash
BIDEUCHRE_CFR_STATE_DIR=/absolute/path/to/cfr-state \
  ./engines/python-cfr/run.sh
```

`BIDEUCHRE_CFR_PYTHON` can override the launcher's Python executable.

## Options and tuning

The launcher forwards arguments to the bot:

```bash
./engines/python-cfr/run.sh --rollouts 8 --train-batch 8
```

- `--rollouts N` controls counterfactual determinizations per legal action.
- `--train-batch N` limits samples trained during each live move.
- `--seed N` controls repeatable model initialization and simulations.
- `--deterministic` selects the highest-probability legal action instead of
  sampling the learned strategy.
- `--state-dir PATH` overrides the autosave directory for this process.

Keep the live update comfortably below the host's eight-second turn timeout.
The defaults prioritize responsiveness on a CPU with four simultaneous bots.

## Tests

Run all language-independent tests with the system Python:

```bash
PYTHONPATH=engines/python-cfr python3 -m unittest discover \
  -s engines/python-cfr/tests -v
```

After installation, run the PyTorch and end-to-end process tests through:

```bash
./test.sh
```

The repository suite drives four independent CFR processes through ordinary,
Partners Best, and Alone hands and confirms that a sitting partner is never
asked to play.
