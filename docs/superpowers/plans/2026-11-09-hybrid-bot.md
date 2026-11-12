# Hybrid Bid Euchre Bot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and measure a progressively stronger hybrid Bid Euchre engine using only the user's existing Mac.

**Architecture:** A headless C# arena remains the rules and evaluation authority. A complete native simulator supplies private observations for compact learned policies, constrained hidden-deal sampling, and bounded search. Offline Python training exports immutable models; full-match evidence controls promotion.

**Tech Stack:** Existing .NET 10 SDK configuration, C++20/CMake 3.20+, Python 3.10+, PyTorch, NumPy, standard-library test runners, and BEUCI version 1. CPU is required; MPS is optional after measurement. Resolve compatible local Python packages during execution and record exact installed versions; do not install anything while reviewing this plan.

**Spec:** `docs/superpowers/specs/2026-11-09-hybrid-bot-design.md`, approved November 9, 2026.

## Global Constraints

- Full-game victory under the project's first-to-40 rules is the primary objective.
- Cloud services, paid compute, and external training data are outside the design.
- Keep BEUCI version 1 and the action grammar; older readers ignore the added field.
- Shared trained weights are allowed; shared live private information is not.
- Begin with compact feed-forward networks: two hidden layers of 256 and 128 units.
- The initial mixture is 90 percent learned and 10 percent uniform, adjustable through validation.
- Target a 3,000 ms decision budget in every phase, with a 3,500 ms internal stop deadline covering sampling, inference, and formatting.
- Start with at most two native worker processes and one training process.
- Provide explicit worker, memory, disk, device, seed, and run-duration settings; default each command to a bounded 30-minute run.
- Begin with a 2 GiB memory budget and 5 GiB replay-disk budget, reducing batch sizes or stopping cleanly if necessary.
- Checkpoint at least every five minutes and on clean interruption.
- The initial promotion design uses 1,000 paired full-match blocks against the incumbent.
- Require the lower bound of a paired-block 95 percent bootstrap interval for candidate win rate to exceed 50 percent.
- Use at least 500 paired replacement blocks, balanced across the declared conditions.
- Require the lower confidence bound on the pooled candidate-minus-incumbent win-rate difference to exceed minus two percentage points, and report each condition separately.
- Live engines load a frozen champion at startup and do not train or replace weights during a match.
- Do not start an unattended overnight run as part of initial verification.

## Review Focus

1. Stalled or noisy engine processes must not hang a tournament, survive a timeout with stale output, or count as successful play: Tasks 2 and 9.
2. Skipped/duplicate snapshots, redeals, restart, and same-card exchanges must preserve only justified private recall: Tasks 1 and 6.
3. Truncated, incompatible, nonfinite, or partially promoted models must never become the active champion: Tasks 7 and 12.
4. Interrupted training/evaluation, exhausted storage, and repeated resume must neither duplicate evidence nor fabricate terminal rewards: Tasks 3, 10, and 11.
5. Hidden cards, next deals, and another seat's private memory must not affect a fixed observer's policy inputs or opponent decision keys: Tasks 5, 6, 8, 9, and 13.

## Milestones and file map

This is one coordinated plan because the simulator, observations, search, and training share a versioned contract. The milestones are independently runnable: A measures existing engines; B replays the real rules natively; C completes the first generate/train/export/play/evaluate loop; D evaluates stronger search variants.

| Milestone | Tasks | Main files and responsibilities |
| --- | --- | --- |
| A: authoritative measurement | 1–3 | `src/BidEuchre.Arena/`: process lifecycle, full matches, fixtures, paired results and statistics; narrow Core fixture/deal identity additions |
| B: native correctness | 4–6 | `engines/hybrid/schema/`, `native/`: stable contracts, complete state machine, private observation and recall |
| C: working hybrid and training | 7–12 | `native/`: model, sampler, search, BEUCI and generation workers; `python/hybrid_training/`: replay, training, export and registry |
| D: measured refinements | 13–14 | Optional information-set tree/DD evaluator, ablations, complete Mac workflow and final evidence |

Each task lists exact owned files. Add new focused test files to the existing C# console runner rather than reorganizing its large `Program.cs`. Native tests use a small local runner and CTest; Python uses `unittest`. Keep generated data, weights, build products, and environments out of Git. Only small, deterministic test fixtures belong in source control.

Commands run from the repository root unless a task says otherwise. For direct `dotnet` commands, use the same `DOTNET_CLI_HOME="$PWD/.dotnet-home"`, `DOTNET_NOLOGO=1`, and `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` settings as `test.sh`; prefer the repository's `.dotnet/dotnet` if present. File lists below group exact filenames beneath an explicit directory; they are not permission to create similarly named files elsewhere.

Execution starts in an isolated worktree using the worktree skill. Keep the existing C++ strategic implementation unchanged as the baseline. Capture its source hash and explicit configuration before comparing candidates. `BIDEUCHRE_CPP_STRENGTH_ENGINE` is opt-in today; the existing 24-hand strength corpus is not automatically run by `test.sh`.

## Shared interface decisions

These names and formats are contracts between tasks. Implement their declarations in the owning task; later tasks consume them without renaming them.

- **C# arena records, Task 2:** `EngineSpec(string Id, string Executable, string[] Arguments, string WorkingDirectory, string ExecutableSha256, string? ModelSha256)`; `BudgetSpec(int DecisionMs=3000, int StopMs=3500, int ProcessMs=5000, int StartupMs=5000)`; `MatchSpec(string BlockId, int LegId, int Seed, int Dealer, EngineSpec[] Seats, BudgetSpec Budget)`; `MatchResult(MatchSpec Spec, MatchStatus Status, int? Winner, int[] Scores, int Hands, string? Error, string TracePath)`. `MatchStatus` is `Complete`, `Interrupted`, or `Failed`.
- **Native aliases, Task 4:** namespace `hybrid`; `ActionId=std::uint8_t`, `Json=beu::json::Value`, `Rng=std::mt19937_64`, `Deadline=std::chrono::steady_clock::time_point`, `ActionMask=std::array<bool,61>`, `Features=std::array<float,1188>`, `BoundaryFeatures=std::array<float,38>`. Card codes/order stay `CDHS` and `9TJQKA`.
- **Actions:** pass 0; bids 1–6; High/Low 7/8; trump suits 9–12; exchanges 13–36; plays 37–60. Every boundary validates this mapping and the legal mask.
- **Observation v1, Tasks 4/6:** ordered feature blocks are phase 8; relative dealer 4; current seat/none 5; scores 2; card counts 4; sitting flags 4; current hand 24; initial hand plus known flag 25; sent exchange plus known flag 25; received exchange plus known flag 25; auction 48; high bid/none 7; bidder/none 5; contract 18; current trick 116; completed tricks 750; team tricks 2; winner/none 3; legal mask 61; own effective-suit counts 4; public voids 16; public session summaries 32. Total 1,188. Preserve known-empty versus unknown recall.
- **Normalization:** scores divide by 40 without clipping negatives; hand/suit counts divide by 7; trick counts divide by 6. Other card/seat/action fields are one-hot with explicit presence flags. Each seat's eight summary features are capped bid-observation count/100, smoothed pass rate, smoothed solo-bid rate, average numeric bid/6 (zero with no numeric bids), capped contract-observation count/100, and smoothed High/Low/Trump rates. Use Beta(1,1) for binary rates and Dirichlet(1,1,1) for mode rates. Do not include hand number, deal serial, names, seed, or model ID in network inputs.
- **Boundary v1:** scores 2, next dealer relative to observer 4, public summaries 32. Construct these 38 features before dealing; the API cannot accept cards, recall, RNG, or a complete simulator state.
- **Model output, Task 7:** `NetworkOutput` has `policy_logits[61]`, `win_logit`, `trick_logits[7]`, and `ownership_logits[96]`, with ownership indexed `[card][relative seat]` for currently remaining cards. Terminal match values bypass neural inference with exact 0/1.
- **Artifact v1:** JSON manifest plus little-endian float32 binary tensors, row-major `[out,in]`; explicit tensor names, offsets, shapes, activations, exact byte length, schema/rule/source/model hashes and provenance. Main MLP uses ReLU 256/128; independent boundary MLP uses ReLU 64/32 and one logit. Softmax/sigmoid are applied outside stored layers.
- **Hashes:** model SHA-256 covers the weight-file bytes; a separate candidate/registry manifest hash covers the complete canonical manifest, avoiding a self-referential hash. Schema hash covers the committed schema bytes. Rule fingerprint covers ordered path-and-content pairs for `bid-euchre-final-rules.md`, `src/BidEuchre.Core/Domain.cs`, `GameRules.cs`, and `GameEngine.cs`; source provenance additionally records the Git revision and any source-diff digest. Generate the expected rule/schema fingerprints alongside constants in Task 4.
- **Replay v1, Task 10:** typed decision arrays use keys `features` (float32, N×1188), `legal` (bool, N×61), `action` (uint8, N), `behavior_prob` (float32, N), `policy_target` (float32, N×61), `policy_weight` (float32, N), `ownership_target` (int8, N×24), `ownership_mask` (bool, N×24), `trick_target` (uint8, N), and `win_target` (float32, N). Zero-weight rows have zero policy targets. Ownership classes are relative seats 0–3 with -1 for masked cards. Boundary arrays use `boundary_features` (float32, M×38) and `boundary_win_target` (float32, M). Metadata stores behavior/teacher kind, policy versions, optional search statistics, and match/deal/decision identifiers. Full hands appear only in separate label/fixture structures, never concatenated into features.

## Task 1: Give deals stable identity and add narrow rules fixtures

**Files:** Modify `src/BidEuchre.Core/GameEngine.cs`, `src/BidEuchre.Core/GameView.cs`, `docs/BEUCI.md`, `tests/BidEuchre.Tests/Program.cs`. Create `src/BidEuchre.Core/GameEngine.Fixtures.cs`, `src/BidEuchre.Core/Properties/AssemblyInfo.cs`, `tests/BidEuchre.Tests/HybridFixtureTests.cs`.

**Interfaces:** Produce `GameEngine.DealSerial: long` and additive `GameView.DealSerial: long? { get; init; }`. Produce internal `FixtureStart(string[] Names, Card[][] Hands, int Dealer, int[] Scores, int HandNumber=1, long DealSerial=1)` and `GameEngine.CreateFixture(FixtureStart): GameEngine`. Friend access is limited to `BidEuchre.Arena` and `BidEuchre.Tests`; normal actions still go through production methods.

- [ ] **Write `DealSerialSurvivesRedeal`, `LegacyViewHasUnknownSerial`, and `FixtureRejectsDuplicateCards`.** Pin the observable assertions:
  ```csharp
  var first = game.CreateView(0).DealSerial;
  game.Redeal();
  Assert.Equal(first!.Value + 1, game.CreateView(0).DealSerial!.Value);
  Assert.Equal(1, game.HandNumber);
  Assert.True(PositionCodec.Decode(legacyPayload).Game.DealSerial is null);
  Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(duplicateCardStart));
  ```
  Add fixture checks for copied inputs, six cards per seat, 24 unique valid cards, legal dealer, negative scores, and a hand beginning near 40.
- [ ] **Run the C# tests and observe the expected missing-interface failures:** `dotnet run --project tests/BidEuchre.Tests`. Register all new test methods in its existing runner.
- [ ] **Implement identity and fixture construction.** Make `GameEngine` partial, increment serial on every real `DealHand`, and initialize the nullable view property without changing its positional constructor. The internal factory validates/copies the explicit deal and initializes a legal bidding boundary without exposing public arbitrary-state mutation.
- [ ] **Verify:** rerun the C# command; all old/new tests pass. Document camelCase `dealSerial` as optional for legacy payloads, retaining BEUCI 1.
- [ ] **Commit only these files:** `feat: add stable deal identity and internal rules fixtures`.

## Task 2: Run authoritative full matches with bounded engine processes

**Files:** In `src/BidEuchre.Arena/`, create `BidEuchre.Arena.csproj`, `Program.cs`, `Contracts.cs`, `ActionApplier.cs`, `MatchRunner.cs`, `ArenaProcessDriver.cs`, `FixtureExporter.cs`. In `tests/BidEuchre.Tests/`, create `ArenaTests.cs`, `ArenaProcessTests.cs`, `ArenaTestChild.cs` and modify `BidEuchre.Tests.csproj`, `Program.cs`. Modify `BidEuchre.slnx`.

**Interfaces:** Consume Task 1 and `IBotDriver`/`BuiltInBotDriver` from App. Produce `ActionApplier.Apply(GameEngine, int seat, BotAction): void`; `MatchRunner.RunAsync(MatchSpec, Func<EngineSpec,IBotDriver>, string tracePath, CancellationToken): Task<MatchResult>`; `ArenaProcessDriver(EngineSpec, BudgetSpec): IBotDriver`; `FixtureExporter.ExportAsync(string path, CancellationToken): Task`. Use `fixture-v1` JSONL with explicit starting hands/scores/dealer and expected state/private legal views after each action.

- [ ] **Write `MatchRunsToForty`, `OnlyActorReceivesPrivateHand`, and process failure tests.** Assert `Complete` has an actual authority winner, every delivered view hides three hands, all four seats receive each hand's terminal observation, and a sitting partner receives no play request. A test-child mode emits unlimited `info`, fills stderr, exits midresponse, returns an illegal card, or answers late; assert each bounded failure is `Failed`, never a substituted move or reused stream.
  ```csharp
  Assert.Equal(MatchStatus.Complete, result.Status);
  Assert.True(result.Winner is 0 or 1);
  Assert.Equal(MatchStatus.Failed, timedOutResult.Status);
  ```
- [ ] **Run:** `dotnet run --project tests/BidEuchre.Tests`; observe new tests fail before implementing the runner/driver.
- [ ] **Implement match/process lifecycle.** Reference Core, Protocol and headless App. Use `ProcessStartInfo.ArgumentList`, explicit cwd, asynchronous stderr draining with a 64 KiB retained tail, bounded stdout lines (1 MiB), whole-operation cancellation, and kill/dispose on timeout. Existing `EngineProcessClient` lacks these guarantees; do not reuse its timed-out stream or the UI's fallback behavior. Use deterministic explicit starting dealer and `StartNextHand()` until actual `GameComplete`.
- [ ] **Implement `export-fixtures --output PATH`.** Generate legal normal/High/Low/PB/Alone traces, same-card exchange, bower voids, near-40/tied-threshold/negative-score cases, redeals and explicit illegal-play penalty cases through the authority. Record private observation delivery cadence separately from privileged parity snapshots. Normalize card representations and legal sets; compare machine fields and private card visibility, excluding names, free-text events and human-readable reasons. This normalized projection is the `fixture-v1` contract shared by the native replay tool.
- [ ] **Verify:** C# tests pass; `dotnet run --project src/BidEuchre.Arena -- export-fixtures --output /tmp/bideuchre-fixtures.jsonl` writes a versioned, replayable corpus. Test the actual auction-to-exchange sequence; existing external-engine helpers skip that observation history.
- [ ] **Commit:** `feat: add headless authoritative bot arena`.

## Task 3: Pair matches, resume evaluation, and calculate promotion evidence

**Files:** In `src/BidEuchre.Arena/`, create `ArenaManifest.cs`, `RunStore.cs`, `TournamentStatistics.cs`, `TournamentRunner.cs` and modify `Program.cs`. In `tests/BidEuchre.Tests/`, create `ArenaStatisticsTests.cs`, `ArenaResumeTests.cs` and modify `Program.cs`.

**Interfaces:** Produce `ArenaManifest` containing schema/source/rule fingerprints, engine definitions, budget, conditions, all `MatchSpec` legs, gate type and bootstrap seed. `RunStore.Open(path, manifest)`, `TryReadCompleted(blockId, legId, out MatchResult)`, `Commit(MatchResult)`, and `RecordAttempt(...)` maintain immutable results. Produce `TournamentStatistics.Summarize(ArenaManifest, IReadOnlyList<MatchResult>): TournamentReport` and `TournamentRunner.RunAsync(ArenaManifest, string directory, TimeSpan maxDuration, CancellationToken): Task<TournamentReport>`.

- [ ] **Write `PairedLegsShareDeals`, `ResumeDoesNotDuplicateLegs`, `ChangedManifestIsRejected`, and `IncompleteEvidenceCannotPromote`.** Assert swapped partnerships share seed/dealer and overlapping deals; repeat resume leaves one committed result per leg; changed hashes/budgets fail; interrupted/failed/unfinished blocks cannot enter a passing gate. For synthetic complete blocks assert `allWins.PrimaryLower95 == 1`, `allHalfWins.PrimaryLower95 == 0.5`, and `allHalfWins.CanPromote == false`.
- [ ] **Run C# tests and confirm failures for the absent store/statistics implementation.** Synthetic records are named helper fixtures in `ArenaStatisticsTests.cs`, not fake live results.
- [ ] **Implement frozen batches and atomic persistence.** Materialize paired legs before execution, rotate dealer evenly, run one match at a time, record every failed attempt, and restart only interrupted unfinished legs from the original seed. A process/protocol failure taints the gate instead of being erased by retries. A 30-minute run boundary saves the schedule and returns resumable/incomplete status.
- [ ] **Implement paired-block statistics.** Use 10,000 deterministic bootstrap resamples and percentile 2.5/97.5 bounds. Primary units average the two candidate win indicators in a swapped block. Replacement units compare candidate versus incumbent with fixed partner/opponents on the same deal sequence; resample within conditions and combine with predeclared equal condition weights. Require exactly the registered 1,000 primary blocks and at least 500 replacement blocks, primary lower bound >0.50, replacement lower difference >-0.02, and zero gate errors. Screening is explicitly non-promoting.
- [ ] **Verify:** C# tests pass; a tiny manifest produces resumable JSON/Markdown reports with denominators, intervals, per-condition breakdowns and timings. Do not run a full promotion gate here.
- [ ] **Commit:** `feat: add reproducible paired tournaments and promotion statistics`.

## Task 4: Establish native builds and one cross-language schema

**Files:** Under `engines/hybrid/`, create `CMakeLists.txt`, `build.sh`, `test.sh`, `schema/observation-v1.json`, `scripts/generate_schema.py`, `native/include/hybrid/schema.hpp`, `native/include/hybrid/contracts.hpp`, `native/include/hybrid/actions.hpp`, `native/include/hybrid/json_io.hpp`, `native/src/actions.cpp`, `native/src/json_io.cpp`, `native/tests/test_main.cpp`, `native/tests/test_support.hpp`, `native/tests/test_schema.cpp`, `python/hybrid_training/__init__.py`, `python/hybrid_training/schema.py`, `tests/python/test_schema.py`. Modify root `.gitignore`.

**Interfaces:** Produce all shared aliases/feature contracts above, `format_action(ActionId): std::string`, `parse_action(std::string_view): ActionId`, `legal_mask(const Json& privatePosition): ActionMask`, and `json_stringify(const Json&): std::string`. Generate C++/Python schema constants from the committed JSON; `generate_schema.py --check` must detect stale generated files. Native test support exposes `CHECK(name, condition)` and `load_case(name): Json` once Task 5 supplies fixtures.

- [ ] **Write mapping/layout/round-trip tests:** `parse_action(format_action(i)) == i` for all 61 IDs; all block offsets cover exactly 1,188 main and 38 boundary inputs; schema output heads total 165 logits. Reject duplicate/missing actions and stale generated constants. Test JSON escaping and finite-number serialization.
  ```cpp
  CHECK("main features", Features{}.size() == 1188);
  CHECK("boundary features", BoundaryFeatures{}.size() == 38);
  CHECK("last action", parse_action(format_action(60)) == 60);
  ```
- [ ] **Run the new Python/schema and native test commands to observe failures:** `python3 -m unittest discover -s engines/hybrid/tests/python -p test_schema.py`; configure CMake with `-DBUILD_TESTING=ON` and build the failing native test target.
- [ ] **Implement the schema/build foundation.** Link `bideuchre_heuristic_lib` through CMake without modifying its strategy. Implement the missing JSON serializer separately. Use Bash-3-compatible launch/test scripts; ignore only hybrid build, state, data and venv directories.
- [ ] **Verify:** `./engines/hybrid/test.sh` builds and runs CTest; schema generation check and Python tests pass. These bounded commands are the native verification entry point for later tasks.
- [ ] **Commit:** `feat: define hybrid engine schema and native build`.

## Task 5: Implement the complete native rules simulator and parity replay

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/match.hpp`, `native/src/match.cpp`, `native/src/replay_main.cpp`, `native/tests/test_match.cpp`, `native/tests/test_parity.cpp`; modify `CMakeLists.txt`, `test.sh`. Create repository fixture `tests/fixtures/hybrid/authority-v1.jsonl`.

**Interfaces:** Produce `Deal { std::array<beu::CardMask,4> hands; }`; `HandResult { scoresBefore[2], deltas[2], tricks[2], scoresAfter[2], optional winner; }`; and `NativeMatch::start(const Deal&, int dealer, scores[2], int handNumber=1, uint64_t serial=1)`, `legal_actions(): ActionMask`, `apply(ActionId)`, `view(int seat): Json`, `full_state(): Json`, `hand_result(): HandResult`, `start_next_hand(const Deal&)`, `redeal(const Deal&)`, and `apply_illegal_play_penalty(int seat)`. Produce `score_hand(const beu::Contract&, int bidder, std::array<int,2> tricks, std::array<int,2> scoresBefore): HandResult` for ordinary completed hands and DD leaf conversion. `view` returns a private BEUCI position. `full_state` is privileged and never accepted by a policy interface.

- [ ] **Write authority parity and boundary tests.** Compare phase, actor, contract, legal sets, all four private views, trick totals, score deltas and winner after every recorded action. Assert `start_next_hand` is rejected while playing or after game completion, and `apply` rejects off-suit play before mutation. Include ±12/±24 outcomes, defender points, same-card return, sitting lead skip, both teams over 40, and tied continuation.
  ```cpp
  CHECK("authority parity", json_stringify(actual) == json_stringify(expected));
  CHECK("alone failure", score_hand({8,beu::Mode::Low,-1,true},0,{5,1},{0,0}).deltas[0] == -24);
  ```
- [ ] **Run:** `./engines/hybrid/test.sh`; the missing state machine/parity cases fail. Generate the committed small corpus with Task 2's exporter instead of hand-authoring expected rule outcomes.
- [ ] **Implement exact phase transitions.** Reuse card primitives, not `HeuristicBot`'s modeled auction or greedy play. Retain an explicit `HandComplete` boundary and both team score deltas. Deal injection is explicit; RNG belongs to generation/orchestration, not `NativeMatch`.
- [ ] **Implement `bideuchre-hybrid-replay --fixtures PATH`.** Read the C# fixture schema and report the first divergent transition/field with nonzero exit. Add bounded randomized legal authority traces with fixed seeds and a separate rare-contract corpus; retain both as tests.
- [ ] **Verify:** C# tests, native tests, and replay against freshly exported fixtures all pass. No shared numeric C#/C++ seed assumption is used for deal parity.
- [ ] **Commit:** `feat: add complete native simulator checked against C# rules`.

## Task 6: Encode seat-private recall and public behavior consistently

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/observation.hpp`, `native/src/observation.cpp`, `native/tests/test_observation.cpp`; modify `CMakeLists.txt`, `native/tests/test_main.cpp`.

**Interfaces:** Produce `SeatRecall` with optional initial hand, sent/received cards, and own action history; `PublicSummary` with four seats' public counters; `SeatObservation { Json position; SeatRecall recall; PublicSummary summary; ActionMask legal; Features features; }`; `SeatMemory::observe(const Json& privatePosition): SeatObservation`, `record_own_action(ActionId)`, `new_game()`, and `from_private_history(const std::vector<Json>& positions, const std::vector<ActionId>& ownActions): SeatMemory`. Produce `encode_boundary(scores[2], int nextDealer, int observer, const PublicSummary&): BoundaryFeatures`.

- [ ] **Write recall/encoding tests.** Assert duplicate snapshots do not double counts; skipped snapshots yield the same summary as sequential delivery; changed `dealSerial` clears deal recall; legacy/midhand restart leaves unjustified recall unknown. Assert same-card return, bidder return reconstructed after intervening public own plays, seat-rotation invariance, and ignored injected opponent cards/names/events. For a fixed observation assert `featuresA == featuresB` despite different inaccessible hands. Two different next deals must produce identical boundary features.
  ```cpp
  CHECK("private inputs invariant", observationA.features == observationB.features);
  CHECK("next deal invariant", boundaryBefore == boundaryAfter);
  ```
- [ ] **Run native tests and observe the expected absent-interface failures.** Fixtures come from Task 5's real auction-to-exchange traces.
- [ ] **Implement observation/recall.** Parse the new serial separately from baseline `beu::Position`. Deduplicate public events by deal/action location, not text. Reconstruct bidder return from current cards plus its played cards minus original hand after discard. With missing legacy serial, clear recall when a transition cannot be justified. Use the exact schema block sizes and smoothed summary definitions above.
- [ ] **Verify:** `./engines/hybrid/test.sh` passes, including observer invariance and 1,188/38 feature dimensions. Policy-facing objects contain no complete hands or next-deal fields.
- [ ] **Commit:** `feat: add private recall and canonical hybrid observations`.

## Task 7: Trainable model definitions and verified native artifact loading

**Files:** Under `engines/hybrid/`, create `pyproject.toml`, `install.sh`, `python/hybrid_training/models.py`, `python/hybrid_training/export.py`, `native/include/hybrid/model.hpp`, `native/include/hybrid/integrity.hpp`, `native/src/model.cpp`, `native/src/integrity.cpp`, `native/src/model_check_main.cpp`, `native/tests/test_model.cpp`, `tests/python/test_models.py`, `tests/python/test_export.py`; modify `CMakeLists.txt`.

**Interfaces:** Produce Python `HybridNet.forward(features) -> dict[str,Tensor]`, `BoundaryNet.forward(features) -> Tensor`, and `export_model(main, boundary, metadata: dict, destination: Path) -> Path`. Produce native `FrozenModel::load(const std::filesystem::path& manifest): FrozenModel`, `evaluate(const Features&): NetworkOutput`, `boundary_logit(const BoundaryFeatures&): float`, and `sha256(std::span<const std::byte>): std::string`. The artifact contract is defined above; hash SHA-256 bytes rather than `std::hash`.

- [ ] **Write output-shape, normalization, corruption and parity tests.** Assert shapes 61/1/7/96; impossible actions get zero probability; 64/32 boundary model accepts only 38 inputs. Reject bad hashes/versions/shapes, truncated tensors, NaN/Inf weights and wrong byte order. Golden Python/native raw heads and normalized outputs must agree within `atol=1e-5, rtol=1e-5`; nonfinite inference results trigger fallback, never an action from invalid scores.
  ```python
  np.testing.assert_allclose(native_logits, python_logits, atol=1e-5, rtol=1e-5)
  self.assertEqual(0.0, masked_probabilities[illegal_action])
  ```
- [ ] **Run Python/native tests to see missing implementations fail.** Set up a dedicated hybrid venv through the new installer during execution, with compatible PyTorch/NumPy versions recorded; keep the CFR environment/checkpoints untouched.
- [ ] **Implement models, export and native evaluation.** Use the specified ReLU dense networks and explicit row-major float32 layout. Write candidates to a temporary directory, validate the complete artifact, then rename. Store schema/rule/source fingerprints and use known SHA-256 test vectors to verify integrity code. Native linear algebra optimization must preserve golden tolerances.
- [ ] **Verify from repository root:** `./engines/hybrid/test.sh`; `engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python -p test_models.py`; repeat with `-p test_export.py`. `bideuchre-hybrid-model-check --manifest PATH --inputs PATH` must agree with Python on held-out observations from every phase.
- [ ] **Commit:** `feat: export compact models with verified native inference`.

## Task 8: Sample strict feasible worlds and compatible private histories

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/sampler.hpp`, `native/src/sampler.cpp`, `native/tests/test_sampler.cpp`; modify `CMakeLists.txt`, `native/tests/test_main.cpp`.

**Interfaces:** Consume Tasks 5–7. Produce `SampledWorld { NativeMatch match; std::array<SeatMemory,4> memories; }`, `enum class SampleFailure { Inconsistent, Exhausted, Deadline }`, `SampleResult=std::variant<SampledWorld,SampleFailure>`, and `BeliefSampler::sample(const SeatObservation&, const std::optional<std::array<float,96>>& logits, Rng&, Deadline): SampleResult`. Produce `BeliefDiagnostics` with attempts, feasible/unique counts, constraint failures, and uniform/learned sample counts.

- [ ] **Write strict-constraint and sampling tests.** Assert 24-card conservation, exact capacities, hard bower-aware voids, all remembered transfers, and same-card-return ownership. An inconsistent forced ownership must return `Inconsistent`, never be ignored. Tiny enumerated constrained cases must match uniform frequencies within a predeclared statistical tolerance; fixed seeds reproduce samples. An expired deadline must interrupt the internal counting recursion. Replayed latent exchanges must reproduce the observer's entire known history.
  ```cpp
  CHECK("constraint retained", std::get<SampleFailure>(inconsistentResult) == SampleFailure::Inconsistent);
  CHECK("deadline honored", std::get<SampleFailure>(expiredResult) == SampleFailure::Deadline);
  ```
- [ ] **Run native tests and confirm the missing strict sampler fails.** Do not copy the baseline sampler's silently ignored forced constraint or its uninterruptible counting recursion.
- [ ] **Implement bounded constrained assignment.** Use completion-count dynamic programming for the uniform component and weighted completion counts for ownership-logit assignments, checking deadlines inside recursion. The weighted distribution is proportional to products of card/seat weights subject to hard constraints. Choose the learned/uniform component with probability .90/.10; learned failure may retry uniform without relaxing facts. This DP implements constrained sequential assignment without rejection bias toward easier branches.
- [ ] **Reconstruct compatible initial hands and private exchange histories.** Add public played cards back to remaining hands, reverse a feasible exchange, then replay the public auction/contract/plays and sampled private transfers through `NativeMatch`. Generate each other seat's permitted memories from its simulated observation cadence. Preserve the root seat's real unknown/known recall instead of replacing unknown facts with guesses. Reject histories inconsistent with any root observation. Do not reapply public-history likelihoods already represented by the network.
- [ ] **Verify:** native tests pass; log uniform versus learned calibration on a small held-out fixture corpus, without claiming a strength gain.
- [ ] **Commit:** `feat: add constrained beliefs and compatible private histories`.

## Task 9: Make the first hybrid playable with bounded root search

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/search.hpp`, `native/include/hybrid/policy.hpp`, `native/include/hybrid/baseline_process.hpp`, `native/src/search.cpp`, `native/src/policy.cpp`, `native/src/baseline_process.cpp`, `native/src/main.cpp`, `native/tests/test_search.cpp`, `native/tests/test_protocol.cpp`, `run.sh`; modify `CMakeLists.txt`, `test.sh`. Modify `tests/BidEuchre.Tests/Program.cs` for optional hybrid process integration.

**Interfaces:** Produce `FrozenPolicy(const FrozenModel&)` with `distribution(const SeatObservation&): std::array<float,61>`; `SearchLimits { int decisionMs=3000; int stopMs=3500; std::optional<int> maxSimulations; uint64_t seed; }`; `RootStats { means[61], counts[61], completedBalancedPass, optional target[61] }`; `SearchResult { ActionId action; RootStats stats; }`; `RootSearch::choose(const SeatObservation&, const SeatMemory&, const FrozenModel&, SearchLimits): SearchResult`. Produce `BaselineProcess::choose(const Json& privatePosition, Deadline): std::optional<ActionId>` and lifecycle/reset methods.

- [ ] **Write search and protocol tests.** Check every phase returns a masked legal action, equivalent candidate order gives identical fixed-count results, forced actions bypass search, and an interrupted first balanced pass returns the precomputed policy fallback with no target. Spy policies assert every continuation receives only its acting-seat observation. Hand-complete evaluation must receive 38 boundary features and no next deal. Test `beuci/isready/newgame/position/go/stop/quit`, malformed positions, CRLF/case handling, one answer per `go`, four independent instances, and startup readiness.
  ```cpp
  CHECK("no partial target", !partial.stats.completedBalancedPass && !partial.stats.target.has_value());
  CHECK("legal fallback", observation.legal[partial.action]);
  ```
- [ ] **Run native and optional .NET process tests before implementing these entry points.** Use already-expired and fixed-count budgets for deterministic unit cases, plus a bounded real-process integration test for the wall-clock requirement.
- [ ] **Implement common-world root evaluation.** Sort legal IDs, construct a legal fallback first, and apply every candidate to common sampled worlds/policy randomness. Continue all acting seats through `FrozenPolicy` using only their private observation; score the hand exactly and apply terminal 0/1 or the boundary model. Complete an initial balanced pass before selecting from search. Allocate later paired samples to close alternatives. A completed comparison emits `softmax(meanWinValue / 0.10)` over legal actions; record temperature, sample counts and seed. Never train from an incomplete first comparison.
- [ ] **Implement BEUCI and model fallback.** Read a frozen model at startup. Missing/corrupt models delegate to the unchanged C++ `HeuristicBot` in a separately supervised `--baseline-worker` child of the same native executable; terminate that child at deadline and use the immediate legal fallback if it stalls. This isolates uninterruptible baseline work without changing its policy. Use typed process arguments on macOS, forward only this seat's observations, and never detach a search thread that can print a late answer. Successful model loads need no baseline subprocess until fallback is required.
- [ ] **Verify:** native and real-host integration pass with test weights, missing model, delayed baseline, and slow sampler. Record backend/model hash in bounded `info` diagnostics; the arena must distinguish actual hybrid play from fallback. Budgeted computation stops by 3,500 ms in test conditions; startup remains below the host's timeout.
- [ ] **Commit:** `feat: add playable hybrid engine with bounded private rollouts`.

## Task 10: Generate complete self-play matches and durable replay shards

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/generation.hpp`, `native/src/generation.cpp`, `native/src/worker_main.cpp`, `native/tests/test_generation.cpp`, `python/hybrid_training/worker.py`, `python/hybrid_training/replay.py`, `tests/python/test_replay.py`, `tests/python/test_worker.py`; modify `CMakeLists.txt`.

**Interfaces:** Produce native `GenerationJob` with job ID, split, frozen population manifest, explicit match seed, resource limits, search fractions, and exploration settings; `generate(const GenerationJob&, RowSink&, Deadline): GenerationStatus`. `GenerationStatus` is `Complete`, `Interrupted`, `Failed`, or `Capacity`. `RowSink::emit(const Json&)` emits versioned header, decision/boundary rows, and complete/interrupted match footer. Produce Python `JobResult(status: str, job_id: str, spool_path: Path, reported_memory_bytes: int)`, `run_job(job: dict, worker: Path, spool: Path) -> JobResult`, `ReplayStore(root: Path, max_bytes: int)`, `commit_match(spool: Path) -> str`, and `iter_batches(split: str, batch_size: int) -> Iterator[dict[str,np.ndarray]]`.

- [ ] **Write generation/replay tests.** Assert same frozen CPU job seed produces the same trace, all actor features equal Task 6's private encoding, ownership labels are separate, and boundary rows precede the next deal. Incomplete matches have no invented win/loss labels and cannot commit as completed training shards. Repeated job IDs do not duplicate rows; interrupted temporary files are ignored; disk exhaustion stops cleanly before replacing a good shard.
  ```python
  self.assertEqual("interrupted", interrupted_job.status)
  with self.assertRaises(ValueError):
      store.commit_match(interrupted_job.spool_path)
  ```
- [ ] **Run native/Python tests and observe missing-worker/store failures.** Use tiny full matches and deterministic scripted policies, not a long training run.
- [ ] **Implement full-match generation.** Bootstrap from unchanged C++ policies, fixed varied legal policies and labeled exploration; use actual simulator actions, never the baseline's modeled auction as truth. Freeze a population per generation. Initially allocate half opponents to incumbent and the remainder across other members; half games use matching teammate policies and half varied partners. After a model exists, search 25 percent of bidding/contract/exchange decisions and 5 percent of non-forced plays by default, recorded/configurable per generation. Record the action's actual behavior probability.
- [ ] **Implement streaming and atomic replay.** Send bounded JSONL batches from native workers to Python spools, then pack completed matches into little-endian typed `.npy` arrays with a JSON manifest for mmap training. Version each array and enforce shapes/dtypes. On terminal completion attach each seat's team win, final hand tricks, and masked current-ownership labels. Store teacher/search-target kind separately; simple/exploratory opponent actions have zero policy-imitation weight but can train belief/value heads.
- [ ] **Verify:** generator/store tests pass; one local full match produces a readable, checksummed shard. Native worker failures and pending spools remain explicitly incomplete. No state or data artifacts enter Git.
- [ ] **Commit:** `feat: generate private full-match replay with durable shards`.

## Task 11: Close the bounded training and approximate policy iteration loop

**Files:** Under `engines/hybrid/`, create `python/hybrid_training/trainer.py`, `python/hybrid_training/checkpoint.py`, `python/hybrid_training/device.py`, `python/hybrid_training/cli.py`, `tests/python/test_trainer.py`, `tests/python/test_checkpoint.py`, `tests/python/test_cli.py`; modify `pyproject.toml`, `install.sh`.

**Interfaces:** Produce `TrainConfig(device="auto", seed=1, workers=2, max_memory_bytes=2147483648, max_disk_bytes=5368709120, duration_seconds=1800, checkpoint_seconds=300, learning_rate=0.0003, loss_weights=(1.0,0.5,0.25,0.25))`; `CycleReport(status, export_manifest, rows_seen, updates, device_report, duration_seconds)`; `DeviceReport(device, available, cpu_batches_per_second, mps_batches_per_second, reason)` (unavailable MPS rate is `None`); `train_cycle(config: TrainConfig, replay: ReplayStore, output: Path) -> CycleReport`; `save_checkpoint(state: dict, path: Path)`, `load_checkpoint(path: Path) -> dict`; and `choose_device(requested: str, benchmark_batch: dict) -> DeviceReport`. Register `bideuchre-hybrid-train` with `generate`, `train`, `cycle`, `benchmark-device`, and `resume` subcommands.

- [ ] **Write objective/resume/resource tests.** Use controlled examples to assert value labels are terminal team victories, not score deltas; ownership loss ignores known/played cards; policy loss ignores incomplete searches and non-teacher exploratory actions. Interrupt before/after atomic checkpoint replacement and ensure a prior complete checkpoint loads. Fixed-seed CPU resume must reproduce the next batch/job choices. A training or promotion seed cannot appear in another split. Tiny memory/disk limits must reduce work or return a clear capacity status without deleting the champion.
  ```python
  self.assertEqual(before["generation"], load_checkpoint(path)["generation"])
  self.assertEqual(uninterrupted_next_job, resumed_next_job)
  self.assertTrue(training_seeds.isdisjoint(promotion_seeds))
  ```
- [ ] **Run:** `engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python -p test_trainer.py`, followed by checkpoint/CLI tests. Confirm expected missing-function failures first.
- [ ] **Implement training objectives and cycles.** Initial AdamW learning rate is 0.0003. Use policy cross-entropy, match binary cross-entropy, trick cross-entropy and masked ownership cross-entropy with weights 1.0/0.5/0.25/0.25; train the separate boundary model with terminal-match BCE. Prefer completed search targets, otherwise eligible teacher imitation. Keep recent and historical replay in a recorded mixture. These are starting hyperparameters, not promotion evidence. Export through Task 7 and reuse frozen candidates for the next generation.
- [ ] **Implement bounded execution and checkpoints.** Default duration 1,800 seconds, at most two workers plus one trainer, total planned memory 2 GiB and replay 5 GiB, checkpoint interval at most 300 seconds. Track worker/trainer memory and MPS allocation where supported; reduce batches or stop safely at budget. Save model/optimizer, CPU/backend/native job RNG states, split/population manifests, generation and replay cursors. Discard/restart interrupted unfinished matches from their saved pre-job RNG/seed; never reward them. Run generation and training in separate bursts when necessary for memory.
- [ ] **Select CPU/MPS by measured end-to-end batch throughput and numerical checks.** `--device cpu` always works; `auto` uses availability plus measured performance; explicit unavailable MPS fails clearly. No claim of cross-device bitwise reproducibility. Record Python/PyTorch/NumPy/platform versions in reports and exact dependency lock output.
- [ ] **Verify:** all Python/native tests pass and a bounded tiny CPU cycle generates, trains, exports and reopens a model. A real MPS probe is optional only if available; record its result. This proves the loop, not increased strength.
- [ ] **Commit:** `feat: add resumable Mac-local policy iteration training`.

## Task 12: Gate champion promotion and retain reproducible experiments

**Files:** Under `engines/hybrid/`, create `python/hybrid_training/registry.py`, `python/hybrid_training/promotion.py`, `tests/python/test_registry.py`, `tests/python/test_promotion.py`, `config/screening.example.json`, `config/promotion.example.json`; modify `python/hybrid_training/cli.py`, `native/src/main.cpp`, `native/src/model.cpp`. In `src/BidEuchre.Arena/`, modify `ArenaProcessDriver.cs`, `Contracts.cs`, `MatchRunner.cs` to record actual backend diagnostics.

**Interfaces:** Produce `Registry(root: Path)`, `register_candidate(manifest: Path) -> str`, `current() -> dict`, `promote(candidate_id: str, gate_report: Path, verification_report: Path) -> None`, and `rollback(champion_id: str) -> None`. Produce `create_evaluation(candidate_id: str, incumbent_id: str, seed_manifest: Path, mode: str) -> Path` and `evaluate_and_report(manifest: Path, arena: Path, max_seconds: int) -> Path`. The initial champion record is `kind=baseline` with its source/config hashes; later records identify immutable hybrid artifacts.

Add nullable init property `EngineSpec.ExpectedBackend` and init property `MatchResult.EffectiveBackends: string[]` without breaking earlier constructors. The arena records the hybrid's reported backend and model hash; missing or mismatched expected diagnostics taint a promotion gate. Ordinary baseline engines need no new protocol command.

- [ ] **Write promotion tests.** Reject smoke reports, incomplete batches, wrong hashes, reused promotion seeds, failed privacy/parity checks, any gate error, primary lower bound exactly .50, and replacement lower difference exactly -.02. Passing synthetic reports permit a pointer update only when all hashes and prerequisites match. Simulated crash during rename leaves either the previous or new complete pointer. A live engine already loaded with model A keeps A after promotion to B.
  ```python
  with self.assertRaises(ValueError):
      registry.promote(candidate_id, incomplete_report, verification_report)
  self.assertEqual(old_id, registry.current()["id"])
  ```
- [ ] **Run registry/promotion tests to observe absent gates fail.** Mark synthetic reports as test-only; production registration rejects a test provenance flag.
- [ ] **Implement registry and evaluation wiring.** Independently freeze/validate model, binary, rules, source, budgets, seed allocation and partner/opponent conditions. Fetch authoritative Task 3 reports; carry simulator/model/privacy verification hashes. Promote only complete 1,000-block primary and at least 500-block replacement evidence. Record the actual backend used in every match; unexpected baseline fallback prevents hybrid promotion. Preserve all failed/inconclusive candidate reports and previous champions.
- [ ] **Implement rollback and scoped writes.** Atomically replace only the champion pointer after verification; never mutate immutable candidate directories. Resume evaluation by Task 3's manifest rather than reconstructing a new batch. Fresh candidate trials get fresh promotion seeds; promotion results never enter replay.
- [ ] **Verify:** tests pass and a tiny real screening run exercises the whole registry/arena path without changing the champion. Report duration/throughput and estimated gate time without starting an unattended long batch.
- [ ] **Commit:** `feat: promote champions only from verified full-match evidence`.

## Task 13: Evaluate information-set trees and explicit double-dummy leaves

**Files:** Under `engines/hybrid/`, create `native/include/hybrid/tree_search.hpp`, `native/include/hybrid/dd_leaf.hpp`, `native/src/tree_search.cpp`, `native/src/dd_leaf.cpp`, `native/tests/test_information_tree.cpp`, `native/tests/test_dd_leaf.cpp`, `python/hybrid_training/ablations.py`, `tests/python/test_ablations.py`; modify `native/src/search.cpp`, `native/src/main.cpp`, `python/hybrid_training/cli.py`, `CMakeLists.txt`.

**Interfaces:** Produce `InformationTreeSearch::choose(const SeatObservation&, const SeatMemory&, const FrozenModel&, SearchLimits): SearchResult`; `ObserverKey make_observer_key(const SeatObservation&)` where `ObserverKey` is a SHA-256 digest of role-relative factual observation/recall before lossy feature normalization, excluding names/events/deal IDs; `DoubleDummyLeaf::evaluate(const SampledWorld&, int perspectiveSeat, Deadline): std::optional<float>`; and `make_ablation_manifests(base_manifest: dict, output: Path) -> list[Path]`. Search mode and leaf mode are explicit immutable manifest fields.

- [ ] **Write controlled hidden-information tests.** Two states indistinguishable to a simulated opponent must produce equal observer keys and equal fixed-count opponent-query results, even if the root's private cards differ. Different legal-action availability must be handled without choosing an unavailable edge. Test a small enumerated trap where omniscient continuation falsely credits an action with incompatible future plans. A DD solve interrupted before completion must never be labeled exact.
  ```cpp
  CHECK("observer key invariant", make_observer_key(opponentA) == make_observer_key(opponentB));
  CHECK("interrupted DD is unknown", !interruptedLeaf.has_value());
  ```
- [ ] **Run native/ablation tests and observe missing-mode failures.** Keep root policy rollouts as the available baseline throughout this task.
- [ ] **Implement the experimental information-set mode.** Use per-observer keys, availability-aware selection, policy priors, and team-consistent value signs. For opponent-tree updates, estimate returns from that observer's own belief resamples, not hands conditioned on root-private facts; fall back to its frozen policy when that work cannot fit the shared deadline. Apply the selected legal action to the original root simulation separately. Start with bounded observer queries and policy leaves; do not build unbounded recursive searches. Record visit/availability diagnostics and describe remaining sampling bias rather than claiming equilibrium convergence.
- [ ] **Implement explicit DD leaf mode.** Default it off; initially allow it only with at most two tricks remaining. Reuse the unchanged solver, check its interruption flag, convert final trick totals through exact scoring and boundary value, and identify this as a perfect-information leaf estimate. It is not an exact hidden-information solution.
- [ ] **Verify and compare:** run tests, then fixed-seed screening ablations at equal budgets for learned beliefs, learned policy continuations, match value, tree mode and DD leaves. Freeze artifacts/configuration; distinguish runtime toggles from retraining ablations. A screened winner still needs Task 12's gate before becoming champion. If tree/DD is weaker, ship it disabled with the recorded result.
- [ ] **Commit:** `feat: add gated information-set search experiments`.

## Task 14: Verify the complete Mac workflow and document actual results

**Files:** Under `engines/hybrid/`, create `README.md`, `scripts/smoke_cycle.sh`, `tests/integration/smoke_cycle.py`; modify `install.sh`, `run.sh`, `test.sh`. Modify root `README.md`, `test.sh`; modify `engines/cpp-heuristic/test.sh` only for Bash-3-compatible transcript collection. Record an evidence summary in `docs/superpowers/reports/2026-11-09-hybrid-bot-validation.md` (use the actual execution date if later).

**Interfaces:** Document stable commands for install/build, arena fixtures/screening/promotion/resume, local generation/train/cycle, registry inspect/rollback, and BEUCI loading. `smoke_cycle.sh` runs in a unique temporary state directory with explicit small budgets and does not change the user's champion.

- [ ] **Write the end-to-end smoke assertion.** It must cover real authority fixture export, native replay parity, a complete self-play match, a training update, Python/native export parity, four BEUCI instances including solo/exchange play, and an authoritative screening report. Assert the report is non-promoting and the prior champion pointer is unchanged. Test launchers from paths containing spaces and with a different working directory.
  ```python
  assert report["canPromote"] is False
  assert champion_before == champion_after
  assert updated_parameters_differ and native_export_matches_python
  ```
- [ ] **Run the smoke before wiring the final scripts to establish the missing integration failure.** Optional dependencies are clearly skipped by the ordinary repository test suite, but required hybrid completion checks may not be called successful when skipped.
- [ ] **Integrate bounded tests and local documentation.** Use Bash 3 compatible `while IFS= read -r` array collection instead of the existing baseline test script's `mapfile`. Keep baseline test expectations/strategy unchanged. Do not add UI features, a daemon, paid services, or automatic overnight training. Document train-versus-play resources, fallback reporting, interruption, all model/seed hashes, and large-gate cost.
- [ ] **Verify the complete integration once:** `./test.sh`; `./engines/hybrid/test.sh`; all hybrid Python tests; then `./engines/hybrid/scripts/smoke_cycle.sh`. Ensure native Mac load/play is exercised through the production process client as well as the arena. Run a bounded same-budget C++-incumbent screening comparison and record results honestly, even if inconclusive or worse.
- [ ] **Review the whole branch and evidence.** Correct actionable findings, rerun only affected checks, and report remaining limits. The software can complete its local workflow before a full promotion batch finishes; keep the incumbent until a full passing gate establishes improved strength. Do not equate a software milestone with the user's strength objective.
- [ ] **Commit:** `docs: document and verify the local hybrid bot workflow`.

## Execution and evidence handoff

Tasks 1–3 establish measurement; Tasks 4–6 establish correctness; Tasks 7–12 close the learning/deployment loop; Tasks 13–14 refine and validate it. Respect this dependency order. After interfaces are committed, independent test/analysis work can run in parallel; do not let concurrent implementers edit shared schema or test-registration files without explicit ownership.

Before each task, read both this plan and the approved design. Run the named failing test before implementation, complete its passing checks, and commit only its files. Concrete fixture builders in tests may be added alongside assertions; they must use the named production interfaces and cannot substitute expected results from the native implementation for C# authority results.

The preferred execution method is subagent-driven because errors in rules, private-information boundaries, and statistical evaluation can make apparent strength meaningless. Each implementation task should receive independent review, followed by one review of the complete branch. The alternate native method performs implementation in this chat with a final independent review; it trades intermediate review coverage for lower coordination cost.

## Plan self-review

- Spec coverage: authority/parity 1–5; recall/privacy 6/8/9/13; models/beliefs/search 7–9/13; local training/recovery 10–11; promotion/evidence 3/12; runtime and final delivery 2/9/14.
- Interface checks: one schema owns action IDs and feature dimensions; native snapshots use the BEUCI position shape; policy APIs accept private observations; boundary APIs accept no cards; only complete matches receive outcome labels.
- Review Focus: each of the five conditions above has named assertions in its owning tasks.
- Scope: the approved design's compute location, rule authority, privacy boundary, and promotion thresholds are preserved. Screening remains distinct from strength evidence.
- Status: plan prepared for user review; implementation has not started.
