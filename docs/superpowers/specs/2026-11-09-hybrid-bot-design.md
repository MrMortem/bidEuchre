# Bid Euchre hybrid bot design

Status: hybrid approach and detailed design approved by the user on November 9, 2026.

Builder review: completed November 9, 2026, with no material planning questions remaining.

## Intent and agreed constraints

Build the strongest practical Bid Euchre bot that can be trained and played on the user's existing Mac. The user prioritized playing strength, approved combining learning with C++ search, and selected existing Mac hardware only. Full-game victory under the project's first-to-40 rules is the primary objective.

This is a substantial engine and training project. It includes an authoritative tournament runner, a fast training simulator, learned inference and strategy, bounded native search, and reproducible model evaluation. Training can continue in resumable runs after the software is delivered. No particular training duration or playing strength is assumed in advance.

The implementation choices below are proposed engineering decisions, not additional preferences attributed to the user. CPU operation is required; the Mac's integrated GPU is an optional training accelerator. Cloud services, paid compute, and external training data are outside the design.

## Approaches and selection

| Approach | Strengths | Tradeoffs |
| --- | --- | --- |
| Extend the existing C++ heuristic engine | Reuses fast search, constrained sampling, private exchange memory, and protocol integration | Fixed player models and omniscient hypothetical continuations limit what additional search alone can achieve |
| Train an end-to-end neural policy | Can discover strategies and cooperation through self-play | Expensive data generation; tactical reliability and generalization must be demonstrated |
| Hybrid learning and search | Learns hidden-card beliefs and strategy while spending search on consequential decisions | Requires simulator parity, model export, and disciplined evaluation |

The user selected the hybrid. Reuse the C++ primitives and Python training concepts, while keeping the existing engines available as opponents and baselines. Do not assume the existing Python engine constitutes a full-game CFR solver: its current alternative-action targets use fixed heuristic continuations, and its policy/value heads do not guide those continuations.

## Success and failure criteria

The primary metric is paired full-match win rate against a frozen incumbent at equal decision budgets. Secondary evaluation covers a fixed opponent pool and mixed partnerships. The initial incumbent is the existing C++ engine at a recorded configuration, not TableBot. TableBot remains a sanity check.

The bot must handle bidding, contract selection, both exchange decisions, normal four-player play, and three-player solo play. It must retain useful private recall, respect legal actions, remain within BEUCI deadlines, and play without Python running in the live engine.

Failures include hidden-information leakage; a simulator that learns different rules; a model that only works with its own clone; unmeasured claims of strength; and a weaker checkpoint silently replacing the champion. Winning more individual tricks or producing more training updates does not establish progress toward the main objective.

A deployable system requires one complete generate/train/export/evaluate cycle on the Mac. A claim of improved strength additionally requires the promotion evidence described below. If a candidate does not pass, preserve the incumbent and report the unsuccessful result; completion of software plumbing is not completion of the strength objective.

## Existing foundation and component boundaries

The authoritative rules remain in `bid-euchre-final-rules.md` and `src/BidEuchre.Core`. `GameEngine` already runs without a UI, accepts a seed and dealer, produces private observations, and advances through complete matches. The current strength regression covers only 24 fixed first hands against TableBot.

The C++ CMake project already exposes `bideuchre_heuristic_lib`, including card rules, hidden-deal sampling, JSON/protocol helpers, and a double-dummy solver. Its `PlayState` covers trick play, not an entire auction/exchange/match environment. Add the missing complete state machine rather than training through the heuristic bot's approximations of other players' bids.

| Component | Responsibility |
| --- | --- |
| New C# headless arena | Authoritative full matches, deterministic fixtures, baseline comparisons, private observation delivery, and result reports |
| New native hybrid engine | BEUCI executable, observation memory, model inference, constrained sampling, and decision search |
| Native complete simulator | Exact auction, contract, exchange, play, scoring, dealer progression, and terminal rules for fast self-play |
| Offline Python trainer | Batched PyTorch optimization, replay sampling, checkpointing, export, and experiment orchestration |
| Artifact registry | Immutable candidates, configuration and source hashes, validation reports, and an atomic champion pointer |

Use a new `engines/hybrid` area and a separately named engine, provisionally `Hybrid Champion`. Link reusable C++ primitives without modifying the baseline's strategic behavior. Put the arena in a separate headless .NET project. Internal native modules may share memory for throughput, but every agent-facing interface accepts only a seat's observation.

## Rules and simulator parity

The simulator must reproduce the exact custom rules, especially:

- One bidding turn per seat; forced dealer bid after three passes; contract selected after the auction; bid three permits only trump.
- Left Bower effective suit; reversed ranking in Low; following effective suit.
- Partners Best transfers one card out and one back, including a legal return of the same card; the partner then sits out. Alone also removes the partner.
- The first active player left of dealer leads; the bidder does not automatically lead.
- Numeric success scores actual tricks, failure loses the bid; Partners Best is plus/minus 12 and Alone plus/minus 24; defenders score their tricks.
- Both teams reaching 40, equal-score continuation, negative scores, and dealer rotation.

Build differential replay fixtures from the C# authority: explicit starting hands/dealer, every action, legal action sets, phase/turn, private views, and final scores. Matching numeric random seeds across C# and C++ is insufficient because their random generators and shuffles differ. Native replay starts from the explicit fixture deal; authority replay uses its recorded seed or a narrow internal test fixture interface. Full-card fixture data is available only to simulator tests and training-label generation.

Add an additive public `dealSerial` field to `GameView`, incremented on every deal including redeals. It contains no card information and prevents private memory from surviving a same-hand-number redeal. Keep BEUCI version 1 and the action grammar; older readers ignore the added field. New engines accept a missing field from legacy hosts and conservatively clear uncertain recall.

## Information available to the bot

The observation contract includes the acting seat's current and remembered initial hand, its own prior actions and exchanged cards, public auction and plays, dealer/turn, contract, scores, card counts, and sitting-out flags. Seat and team coordinates are relative to the acting seat. Legal actions use the existing 61-action vocabulary. Define a new versioned encoder instead of reinterpreting existing CFR checkpoints.

The live host sends snapshots before decisions and after completed hands, rather than every intermediate action. Reconstruct missed public actions from the full current-hand auction and trick arrays. At its bidding turn every seat can remember its initial six cards. During exchange the partner recognizes the seventh card by comparison; the bidder recognizes the return from its next hand and recorded discard. Treat same-card returns correctly. A fresh process without earlier observations marks private recall unknown instead of inventing it.

Opponent modeling may use only public actions observed during the current match, with conservative priors when observations are sparse. Seat-local memory resets on `newgame`; deal memory resets on `dealSerial`. Player names, free-text events, simulator seeds, checkpoint identities of other players, and other seats' memory are excluded from policy inputs. Shared trained weights are allowed; shared live private information is not.

Training may use full hands as supervised labels for inference, never as policy or value input. Tests must change inaccessible cards while holding a seat's observation fixed and verify that its policy outputs are unchanged.

## Learned models and hidden-card beliefs

Begin with compact feed-forward networks: two hidden layers of 256 and 128 units, with separate outputs for legal-action policy, eventual match-win probability, final team-trick distribution over zero through six, and hidden-card ownership logits. These widths are starting values for measurement, not a claimed optimum. Include ordered auction/trick history, known cards, effective suits, original-hand/exchange recall with explicit unknown masks, score information, and simple public behavioral summaries.

At a hand boundary, a small continuation value model predicts match victory from both scores, next dealer relative to the team, and public session summaries. It has no access to the undealt next hand. This lets current-hand search value risk by its effect on winning the match. Terminal victories and defeats have exact values of one and zero. Auxiliary trick prediction aids calibration but does not replace the match objective.

Construct complete feasible hidden deals, not independent ownership guesses. Enforce all visible cards, hand capacities, played cards, effective-suit voids, and privately known transfers. Use ownership logits to bias a constrained sequential assignment sampler with backtracking; mix in a small uniform feasible component to retain support. The initial mixture is 90 percent learned and 10 percent uniform, adjustable through validation. Log feasibility failures, unique samples, and belief calibration.

Sample latent exchange histories when another player's private recall matters; do not assume its current hand was its original auction hand. Never relax a known void or duplicate a card to finish a sample. If learned sampling fails, use the validated uniform constrained sampler; if time is exhausted, use the legal policy fallback.

The belief network is trained directly on the complete observation history. Do not multiply in likelihoods for those same observations again unless a separate sequential filtering formulation explicitly accounts for the conditioning. Calibration tests use held-out deals and include rare solo contracts.

## Decision search

The first complete hybrid uses root-action search with learned, information-limited continuations. Evaluate each legal root action on common feasible hidden deals. Roll forward through the rest of the auction, contract choice, exchange, and play as needed. Every simulated seat chooses through its own private observation and policy, with its own permitted recall. It never chooses by inspecting the simulator's other hands. Score completed hands exactly, then use the boundary continuation model for matches that continue.

This provides a concrete, testable baseline for the more expensive search experiment: a multi-observer information-set tree. Tree identities and action statistics must follow the acting observer's information, with correct legal-action availability handling. Root-private information must not enter opponent decision keys. Evaluate this mode against the root-action baseline under the same wall-clock budget before making it the default. The exact tree-selection formula and exploration constants are builder choices; information boundaries and comparison requirements are mandatory.

Neither mode is presented as a proven equilibrium algorithm for this partnership game. Consistent observation inputs reduce omniscient decision making, but sampling and tree construction can still introduce bias. Small controlled games and replay examples must exercise choices that would change improperly if hidden cards leaked into a continuation.

Reuse double-dummy solving as a separately measured late-hand evaluator and diagnostic. An exact answer for one fully specified deal is not an exact solution of the hidden-information position. Do not silently substitute perfect-information continuations throughout the new search. Any such leaf-evaluation mode remains explicit in the experiment manifest and must demonstrate a benefit.

All legal bids, modes, suits, and exchanges remain eligible. Allocate additional samples to close decisions after an initial balanced pass; compare alternatives on common randomness. Use the model fallback if a deadline interrupts the first pass, rather than letting candidate iteration order choose the result.

The search evaluates normal raises as auction-control decisions: the same final trick count earns the same successful numeric score, while a higher target can increase failure cost. It also models High/Low access at four. Partners Best and Alone require the probability of all six tricks and their effects on eventual match victory, including defender points and first leader.

## Local training and deployment

Use Python/PyTorch for offline training and C++ for simulation and live inference. Detect whether PyTorch MPS is usable and benchmark it against CPU for this compact model before selecting the training backend. CPU remains supported. Apple GPU availability and speed are not assumed from the confirmed arm64 architecture alone. PyTorch documents the runtime availability check in its [MPS backend guidance](https://docs.pytorch.org/docs/stable/notes/mps.html).

Export the small dense networks in a versioned format of float32 weights and explicit dimensions/activations, with a matching native evaluator. The manifest includes observation and action versions, rule fingerprint, model hash, and training provenance. Compare native outputs against Python reference outputs on held-out observations before use. Keep inference independent of Python and of a network service.

Training proceeds through repeated bounded cycles:

1. Generate a diverse bootstrap corpus using existing C++ policies, alternate fixed policies, and legal exploration. Preserve model versions and sampling probabilities. Use baseline actions as an initialization aid, not permanent ground truth.
2. Train policy imitation, hidden-card inference, trick distributions, and match/boundary values from private observations and complete-game outcomes.
3. Generate fresh self-play with frozen policies. Use costly search on a stratified subset of decisions, especially auction/exchange choices and close play decisions. Search every legal action before creating an improvement target; incomplete comparisons do not become labels.
4. Distill improved search distributions into the next policy, train values on eventual match outcomes, and retrain beliefs on current population data. Use replay from previous populations to limit forgetting. This is approximate policy iteration, not a claim of CFR convergence.
5. Export a frozen candidate, screen it, and run the independent promotion tournament only if screening is promising.

Use a bounded population containing the incumbent, up to four previous champions, the fixed C++ baseline, and simple varied policies. Initial opponent sampling assigns half the games to the incumbent and splits the rest across the remaining pool. Half the training games use matching teammate policies; the remainder vary partners. Exact replay ratios, exploration schedules, and loss weights are tuning decisions recorded per run.

Start with at most two native worker processes and one training process. Provide explicit worker, memory, disk, device, seed, and run-duration settings; default each command to a bounded 30-minute run. Begin with a 2 GiB memory budget and 5 GiB replay-disk budget, reducing batch sizes or stopping cleanly if necessary. Checkpoint at least every five minutes and on clean interruption. Resume from saved model/optimizer, random generator state, replay cursor, and generation counter. Finished shards are committed atomically; incomplete shards are discarded on recovery.

Separate simulation and GPU training bursts if they compete for unified memory. Benchmark throughput before estimating how many games an overnight run can produce. Long training is an explicit local command, not a daemon or a background process started when the user opens a table. Do not start an unattended overnight run as part of initial verification.

Live engines load a frozen champion at startup and do not train or replace weights during a match. Retain previous champions for rollback. A missing or incompatible model selects the unchanged C++ baseline and reports that fallback through protocol-compatible diagnostics. Model promotion is an atomic pointer update; existing processes finish with their loaded model.

## Runtime budgets and reliability

The current host allows approximately five seconds for the next output line and eight seconds for the complete action. Target a 3,000 ms decision budget in every phase, with a 3,500 ms internal stop deadline covering sampling, inference, and formatting. Produce a legal fallback immediately before starting optional work. Keep model loading and warmup within startup/readiness timeouts as well.

Deadline checks must occur inside bounded sampling, search, and solver batches. Return one `bestaction` and no delayed second answer. Diagnostics use permitted `info` lines or bounded stderr, not arbitrary stdout. Do not use periodic output to evade the host's total action timeout. The UI's deliberate pacing remains independent of engine compute time.

Benchmark wall-clock strength at the actual play budget. For reproducible debugging also support fixed simulation/node counts and seeds; timed search is not promised to be bit-for-bit reproducible.

## Evaluation and champion promotion

The C# arena drives `GameEngine` directly, with no UI pacing. Persist seeds, explicit starting deals for replays, dealer schedules, engine/model hashes, settings, actions, scores, errors, and timing. Full-card records are isolated from running players. Terminate a match only under the actual win rules; a watchdog-stopped match is unresolved or failed, not an invented draw or a silently discarded loss.

Pair each match with the same seed and dealer schedule while swapping candidate and incumbent partnerships. Rotate dealers evenly across the corpus. The deal sequence is the same for overlapping hands even when matches finish at different lengths. Statistical resampling uses the paired seed block as its unit, not individual hands or the two correlated matches separately.

Maintain three disjoint seed sets: training, tuning/screening, and promotion. Promotion batches are fresh for each selected candidate and fixed before the run; they are never fed into training. Use local sequential evaluation so multiple concurrent engines do not distort the compute budget. Resume unfinished batches without changing their configuration.

The initial promotion design uses 1,000 paired full-match blocks against the incumbent. Require the lower bound of a paired-block 95 percent bootstrap interval for candidate win rate to exceed 50 percent. This is a per-comparison evidence threshold, not a guarantee across unlimited candidate trials. Failed or inconclusive candidates remain challengers; a larger new evaluation requires a predeclared new batch rather than repeatedly peeking until a threshold is crossed.

Also evaluate candidate and incumbent in matched replacements with the fixed C++ partner and sampled historical partners against a fixed opponent pool. Use at least 500 paired replacement blocks, balanced across the declared conditions. Require the lower confidence bound on the pooled candidate-minus-incumbent win-rate difference to exceed minus two percentage points, and report each condition separately. This protects broad play while keeping clone-team strength primary. Human-partner strength remains unverified until tested with humans.

A promotion additionally requires no illegal actions, protocol failures, or deadline violations in the gate runs, simulator parity, model-export parity, and privacy checks. Record raw rates and intervals, contract-specific make/set rates, final-score differentials, and latency distributions. The 1,000/500 block counts are proposed initial gates; they set evidence requirements, not a promise that the Mac completes them in one session.

Run ablations at equal budgets for learned beliefs, learned continuations, match value, the information-set tree, and double-dummy leaf evaluation. Complexity remains enabled only when it earns its place through measured results.

## Delivery boundaries and validation

Deliver the headless arena and reports, native simulator with authority parity, independently loadable hybrid BEUCI engine, bounded resumable trainer, model exporter, champion registry, and local run documentation. A later implementation plan should put the arena and simulator parity first, then close the full training/evaluation loop before adding search complexity.

Required verification covers every rule phase and score case; randomized authority/native replay parity; seat-private observation invariance; exchange recall including same-card returns and redeals; model-output parity; four simultaneous engine instances; deadline/fallback behavior; interruption/resume; and at least one end-to-end local training and evaluation cycle. Existing engine and application tests must continue to pass.

The builder chooses internal file layout, detailed fixtures, optimizer and loss schedules, data-shard format, native batching/linear algebra, bootstrap implementation, and tree exploration constants. Those choices must be recorded and tested within the boundaries above. Any change to the game rules, compute location, privacy boundary, or primary strength objective requires revisiting the design.

## Research informing the proposal

The hybrid is an engineering hypothesis to test in this game. [Bridge bidding work](https://www.ieee-jas.net/article/doi/10.1109/JAS.2024.124488) combines learned policies and beliefs with search. [Human-agent cooperation in Bridge](https://arxiv.org/abs/2011.14124) distinguishes partnerships with oneself, other bots, and humans. [Research on postponing perfect-information reasoning](https://arxiv.org/abs/2408.02380) examines strategy-fusion errors in sampled-deal search. These results motivate the design; they do not establish its playing strength in this Bid Euchre variant.
