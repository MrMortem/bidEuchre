# Local hybrid engine

This engine optimizes complete first-to-40 match victory. The local generate/train/export/play/evaluate workflow is implemented; the tiny verification candidates have **not established improved playing strength**. The initial champion remains the frozen C++ heuristic. Root search with learned continuations is the default. Information-set tree and double-dummy leaves remain opt-in experiments.

## Install and verify on the Mac

Prerequisites: Xcode command-line C++ tools, Python 3.10–3.13, and the .NET 10 SDK. Installation uses a dedicated `engines/hybrid/.venv`, pinned PyTorch/NumPy and CMake 4.4.4. It does not use or change the CFR environment. Install the SDK separately or supply an existing absolute SDK executable. The validation machine used `/tmp/bideuchre-dotnet/dotnet`; that SDK is **not globally installed**, and `/tmp` is not a permanent installation promise.

From the repository root:

```sh
export BIDEUCHRE_DOTNET=/absolute/path/to/dotnet-sdk/dotnet
export DOTNET_ROOT="$(dirname "$BIDEUCHRE_DOTNET")"
export DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
HYBRID_PYTHON=/absolute/path/to/python3.12 ./engines/hybrid/install.sh
./engines/hybrid/build.sh
./test.sh
./engines/hybrid/test.sh
./engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python
./engines/hybrid/scripts/smoke_cycle.sh
```

After installation, `install.sh` reuses the stable venv Python unless `HYBRID_PYTHON` is set. Build defaults to Release, two compiler jobs, and the dedicated Python/CMake; `HYBRID_CMAKE` and `HYBRID_PYTHON` are explicit overrides. A CMake build-type argument can override Release. The SDK scripts resolve `BIDEUCHRE_DOTNET`, then `DOTNET_ROOT`, a repository `.dotnet`, then `dotnet` on PATH. A shared Bash3-compatible helper resolves relative/absolute executable symlink chains and physical parent directories before setting `DOTNET_ROOT`; this supports conventional PATH SDK shims and paths with spaces. The scripts suppress first-run banners. The hybrid suite also tests four discovery modes against a real private-SDK apphost.

The ordinary root suite explicitly skips unavailable optional CFR PyTorch, Prolog, Node or C++ components. Hybrid completion requires **all** separate hybrid checks above; a skip is not a hybrid pass. The smoke expects the preceding builds, creates a unique temporary directory containing spaces, prints its evidence path and retains it for inspection. It never touches the selected user's registry. It exports fresh C# truth, replays it natively, completes a fixed-seeded self-play match, compares a one-update checkpoint with four further actual optimizer steps, checks exported heads against Python, drives four production BEUCI clients through auction/normal/Partners Best/Alone hands, and runs four authoritative screening matches. Its fresh screening registry keeps the C++ champion. Both engines receive the same **200 ms decision allowance** (250 ms stop/1,000 ms process/5,000 ms startup); the baseline also freezes samples16/depth1/nodes1000 and the candidate maxsim7. This short workflow probe is not a production 3,000 ms strength evaluation.

## Train locally in bounded bursts

Use the installed entry point, or `.venv/bin/python -m hybrid_training.cli`. Paths below are examples; choose separate output/registry directories per experiment.

```sh
TRAIN="$PWD/engines/hybrid/.venv/bin/bideuchre-hybrid-train"
"$TRAIN" benchmark-device --device auto --batch-size 64
"$TRAIN" generate --output "$PWD/engines/hybrid/state/run-1" --device cpu --seed 1 --duration-seconds 1800
"$TRAIN" train --output "$PWD/engines/hybrid/state/run-1" --device cpu --seed 1 --duration-seconds 1800
"$TRAIN" cycle --output "$PWD/engines/hybrid/state/run-2" --device auto --seed 2 --duration-seconds 1800
"$TRAIN" resume --output "$PWD/engines/hybrid/state/run-2" --duration-seconds 1800
```

`generate` collects only complete matches; `train` consumes committed replay; `cycle` starts the next generation from the frozen population; `resume` continues the saved stage. The next generation of an uninformed model can take much longer than the fixed-seeded smoke. An interrupted or capacity-limited job is not a completed training result. `--execution-mode fixed_seeded` uses scripted/fixed-count generation for reproducibility checks, not evidence of strategic improvement. Timed baseline/search trajectories need not be bitwise repeatable.

Defaults: 30 minutes, at most two native generation workers and one trainer, 2 GiB measured memory, 5 GiB disk, checkpoints every 300 seconds and on clean interruption. The current scheduler stages one actual worker and one trainer sequentially. Explicit controls are `--workers`, `--max-memory-bytes`, `--max-disk-bytes`, `--duration-seconds`, `--checkpoint-seconds`, `--device`, `--seed`, `--batch-size`, `--generation-matches`, and `--max-updates`. Limits are sampled cooperative bounds, not OS hard allocation limits; the supervisor kills/reaps over-budget children. Ctrl-C/SIGTERM preserves the last complete checkpoint/update; a machine crash can lose work since the last checkpoint. Resume restarts an unfinished native match from its saved pre-job seed and never rewards it before completion. Checkpoints are trusted-local Torch files; live exports are validated JSON plus float32 bytes.

`auto` measures end-to-end CPU/MPS work, including transfers and synchronization. On the clean final validation probe, batch8 CPU was faster than MPS (48.14 vs 27.03 batches/s); this is a local measurement, not a general ranking. Live play is native CPU only and has no Python, trainer or network-service dependency. Four independent live seats are evaluation clients, separate from the generation-worker cap.

## Freeze, screen, resume and promote

Copy `config/screening.example.json` to a new `REGISTRY/config.json` and replace **every** absolute path. List the baseline sources and freeze both engine arguments, budgets, arena, resource limits and partnership conditions. The first `current` call initializes the registry and freezes the unchanged C++ incumbent. Configuration is immutable after initialization; use a new registry to change budgets or engine settings. Explicitly set baseline `--play-ms` equal to hybrid `--decision-ms`; baseline sample/depth/node caps must also be recorded. Auction/exchange work in the unchanged baseline is sample-bounded; the arena records actual elapsed times and rejects deadline violations.

```sh
REGISTRY="$PWD/engines/hybrid/state/registry"
"$TRAIN" current --registry "$REGISTRY"
"$TRAIN" register --registry "$REGISTRY" --manifest /absolute/path/to/candidate/manifest.json
# Save returned candidate and incumbent IDs. The seed JSON includes registry_root,
# run_seed, start_index, primary_blocks, replacement_blocks and bootstrap_seed.
"$TRAIN" create-evaluation --candidate CANDIDATE_ID --incumbent INCUMBENT_ID --seed-manifest /absolute/path/to/screening-seeds.json --mode screening
"$TRAIN" evaluate --manifest /absolute/path/to/returned/manifest.json --arena /absolute/path/to/BidEuchre.Arena --duration-seconds 1800
# Resume the SAME immutable manifest with the SAME evaluate command.
```

Arena fixtures and direct audit commands:

```sh
"$BIDEUCHRE_DOTNET" run --project src/BidEuchre.Arena -- export-fixtures --output /tmp/authority.jsonl
./engines/hybrid/build/bideuchre-hybrid-replay --fixtures /tmp/authority.jsonl
/absolute/path/to/BidEuchre.Arena audit --manifest /absolute/path/to/manifest.json --output /absolute/path/to/run
```

Screening always reports `CanPromote=false`. Promotion requires a separately frozen `--mode promotion` schedule with **1,000 primary paired blocks plus at least 500 balanced replacement paired blocks** (at least 3,000 complete first-to-40 matches). Seeds are disjoint by residue: training0/tuning1/screening2/promotion3 modulo4; promotion seeds cannot be reused for a new trial or enter SGD. Supply a real `verification_report` before creating the promotion schedule; it must bind candidate/model/schema/rule/source/binary/arena hashes and passing privacy/simulator/export logs. Registry promotion independently re-audits the complete authority traces and refuses synthetic/unbound/changed evidence, fallback, deadline violations, incomplete legs, or missing conditions.

The paired-bootstrap 95% primary win-rate lower bound must be **strictly above 50%**, and pooled candidate-minus-incumbent replacement lower bound **strictly above -2 percentage points**, with each declared condition reported. Then, and only then:

```sh
"$TRAIN" promote --registry "$REGISTRY" --candidate CANDIDATE_ID --gate-report /absolute/path/to/run/report.json --verification-report /absolute/path/to/verification.json
"$TRAIN" current --registry "$REGISTRY"
"$TRAIN" rollback --registry "$REGISTRY" --candidate PREVIOUS_VERIFIED_ID
```

Rollback accepts a verified historical champion, not an arbitrary registered candidate. It is also the offline way to republish an older pointer missing the complete launch descriptor: roll back to the **same verified current ID**, or initialize a fresh registry. No live migration or automatic promotion occurs. Frozen artifacts, reports, manifests and failed attempts remain inspectable; do not edit frozen files in place.

## Load in BEUCI

Select `engines/hybrid/run.sh` as an external engine executable. With no arguments it uses `HYBRID_REGISTRY`, or `engines/hybrid/state/registry` when that pointer exists. An explicitly selected missing/broken registry fails closed. Before any default registry exists, the native development fallback is available and diagnosed as baseline; an ambient `HYBRID_MODEL` can select a development model. For explicit selection:

```sh
./engines/hybrid/run.sh --registry /absolute/path/to/registry
# Development-only candidate play; this does not promote it:
./engines/hybrid/run.sh --model /absolute/path/to/manifest.json --max-simulations 7
```

The native registry entry reads one atomic pointer, verifies the frozen executable hash, and execs its full typed arguments and working directory. It does not require Python on PATH. Do not append policy overrides to `--registry`; evaluated settings must be deployed settings. A running process keeps its loaded champion even if an offline operation later changes the pointer. Missing/invalid model inference reports `backend=baseline` or `backend=immediate`; a valid model reports `hybrid` or `hybrid-policy` with the actual loaded weight hash. Policy-only includes forced actions and decisions lacking a complete balanced pass, so it is not a pure timeout count. Baseline restoration that cannot reproduce actual private exchange facts fails closed to a diagnosed legal immediate action for that deal.

See [actual validation and evidence limits](../../docs/superpowers/reports/2026-11-09-hybrid-bot-validation.md). No overnight training or full promotion batch was started for delivery. Keep the incumbent until a full passing gate demonstrates progress toward match-winning strength.
