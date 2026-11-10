# Hybrid bot: actual Mac validation — 2026-11-09

The complete local workflow ran successfully, but **improved first-to-40 match-winning strength is not established**. The incumbent remains the frozen C++ heuristic. The final five-update candidate lost its two primary legs; its one paired replacement block was worse by1. These tiny samples prove integration, not a reliable strength estimate. No full promotion, unattended training or overnight job was started. Root/policy remain defaults; tree/DD remain disabled experiments.

## Delivered and verified

Stable install/build/test scripts now select the dedicated hybrid `.venv` Python and pinned local CMake4.4.4, independent of the disposable SDD tooling environment. The venv Python executables are copied files; their base interpreter is the Codex runtime Python3.12.14, not the SDD venv. The historical `pyvenv.cfg` creation command mentions SDD but runtime `home`/`executable` point at the independent runtime. A fresh installation requires an explicit supported Python3.10–3.13.

SDK discovery uses `BIDEUCHRE_DOTNET`, `DOTNET_ROOT`, repository `.dotnet`, then PATH. Actual validation used `/tmp/bideuchre-dotnet/dotnet`, **not a global SDK installation**. Scripts set DOTNET_ROOT and first-run banner controls. Native play requires neither Python nor .NET; .NET is needed for the app/authority/tests. Baseline strategy, Core rules, CFR environment/data and existing training data were untouched. The earlier Bash3 repair `8d55ff1` is preserved.

`run.sh` supports explicit `--registry` and no-argument environment/default registry selection. Native startup forwards complete frozen executable, typed argv, cwd and hash; a running engine retains its loaded choice. Old pointers lacking descriptors require offline rollback to the same verified champion or a fresh registry. No migration service was added. Required smoke checks fail rather than skip missing dependencies.

## Exact final commands and outcomes

Working directory: `/Users/wtcolflesh/.codex/worktrees/hybrid-bot/Bid Euchre`, branch `codex/hybrid-bot`, implementation base `480237577b2170231230155636811fd9bfc41045`. These commands ran in order. The suite/smoke PATH explicitly excluded the scratch tooling venv:

```sh
BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet ./engines/hybrid/install.sh
PATH=/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet ./test.sh
PATH=/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet ./engines/hybrid/test.sh
PATH=/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet DOTNET_ROOT=/tmp/bideuchre-dotnet DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python
PATH=/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet ./engines/hybrid/scripts/smoke_cycle.sh
PATH=/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin engines/hybrid/.venv/bin/bideuchre-hybrid-train benchmark-device --device auto --batch-size 8
```

All exited 0. Root: **65/65 C#**, **17/17 browser tests**, **28 CFR standard-library tests**, unchanged C++ unit/transcript/recovery, and Mac bundle creation/rebuild passed. Root explicitly skipped optional CFR PyTorch/process integration and Prolog because their dependencies were absent; no required hybrid check was skipped. The dedicated hybrid matrix passed **17/17 CTest suites in39.76s**, regenerated/replayed **62 C# authority traces**, and passed production-host **2/2 learned-model/deadline tests plus1/1 missing-model play test**. Deliberate 10-second sampler/worker delays returned at2981/2983ms, inside the3500ms stop limit. All hybrid Python tests passed **71/71 in16.369s**; eight-phase parity covered886 observations with max raw absolute error 1.19e-7. No new compiler/test warnings appeared in the final hybrid log.

Logs: `/tmp/task14-install.log`, `/tmp/task14-root.log`, `/tmp/task14-hybrid.log`, `/tmp/task14-python.log`, `/tmp/task14-smoke-green.log`, `/tmp/task14-device-clean.json`. CMakeCache confirms Python3_EXECUTABLE and HYBRID_TRAINING_PYTHON are the hybrid venv and CMAKE_COMMAND is its local cmake package. The separate required suites were not needlessly rerun after documentation-only edits.

TDD: the new integration assertion was executed before launcher wiring and exited1 with `AssertionError: missing integrated smoke launcher` (`/tmp/task14-smoke-red.log`). The focused ablation-cap regression initially exited1 (`Ran2tests; FAILED(failures=1); ValueError not raised`), then passed2/2 after rejecting decision_ms>3000 (`/tmp/task14-ablation-{red,green}.log`). The smoke compares checkpoint after one update with four further updates, since the existing trainer correctly requires positive update limits; no zero-update semantics were added.

## Actual end-to-end cycle

Evidence directory (temporary, retained locally, not checked into Git): `/private/var/folders/jp/4f3n3mz53czdh3dbct2rszdh0000gn/T/bid euchre smoke 74tdk_su`. Its `verification.json` contains command arrays/timings, frozen registry/source/authority metadata, model manifest, replay checksums/footer, parity errors and complete screening report. Temporary artifacts may be removed by OS cleanup; this report preserves the meaningful measured results and identities. Total smoke time: **12.100870s**.

Fresh authority export/replay exercised62 traces. Generation used run seed1, training job seed4, fixed-seeded fixed/uniform population, dealer0, seats[1,0,1,0], one actual worker, one staged trainer. It completed a real first-to-40 match: **12hands,302decisions,48boundary rows,winner team1**. It did not use a score-delta surrogate or unfinished-match reward. A first bounded20s cycle performed one update; a bounded15s resume performed four further updates. **Five updates,40 sampled rows,batch8**, all recent because only one shard existed; default recent fraction remains.5. All12 main parameter tensors changed across the resumed updates. All raw/normalized heads and the separate boundary logit reopened in the native checker; maximum absolute discrepancy was **3.725290298461914e-8**, tolerance1e-5.

Training peak measured memory301,629,440bytes; native worker reported3,080,192bytes. CPU1thread, configured worker cap2, actual worker1, trainer1, staged=true; 2GiB memory/5GiB disk caps, checkpoint300s, learning rate.0003 and loss weights[1,.5,.25,.25]. Per-job complete-match safety limits were1,000hands/20,000decisions and19seconds remaining. The scripted probe has search fractions0; its stored3000/3500ms worker defaults do not make it a learned strategic self-play test.

Four separate production `EngineProcessClient` instances loaded the trained export and completed live auction, ordinary four-player play, both Partners Best exchanges/three-player play, and Alone/three-player play. That host check passed1/1 in1.487s. All subprocesses ran from the temporary directory, distinct from the repository; launcher, model and registry paths contained spaces. For host and registry launch probes, PATH contained only a directory with a `dirname` symlink—**no Python executable**. Explicit and environment-default registry probes both identified the unchanged C++ champion, preserving its full frozen descriptor.

The separate fresh screening registry froze **both** engines at200ms decision allowance; stop250ms, process1000ms,startup5000ms. Hybrid maxsim 7/seed42/root-policy; baseline samples16/play200ms/depth1/nodes1000/seed42. These equal declared allowances are not equal internal algorithms or proof both spend the whole allowance; unchanged baseline auction/exchange are sample-bounded, and Arena records actual durations. One primary paired block plus one replacement paired block ran sequentially through the real authority. Screening run seed14000 gives seeds56002/56006; bootstrap271828. No promotion seeds or training rows were reused.

| Screening result | Actual value |
| --- | ---: |
| Complete matches / attempts | 4/4 |
| Decisions | 1,014 |
| Authority wall time | 6.0038452s |
| Supervised invocation wall time | 6.0727955s |
| Maximum measured decision | 41.6918ms |
| Primary win fraction | 0 |
| Replacement difference | -1 |
| Primary interval / replacement interval | [0,0] / [-1,-1] |
| Gate errors | 0 |
| CanPromote | false |
| Peak parent+arena-tree memory | 345,161,728bytes |
| Sampled registry disk | 5,620,272bytes |

The one-block bootstrap intervals are degenerate and uninformative; do not interpret them as calibrated certainty about strength. The tiny candidate was worse in the observed schedule. Evaluation caps were CPU, one arena job/no trainer,60seconds,2GiB memory/5GiB disk. The initial C++ champion pointer was **byte-identical before/after**, and the user's default/selected pointer was likewise unchanged (including absent-before/absent-after). No candidate was promoted.

## Frozen identities

| Identity | SHA-256 |
| --- | --- |
| model weights | `b5d4c475dbd5d0fb6b0aeda2aafa322b355d2f0419fe10cd700e9ae574fc278d` |
| candidate canonical manifest | `f5157aab51c510c41d7ba4810334b6b43b3d7d0c6bdf9c6b90fdc02a52c03472` |
| artifact file | `7ed226e484999d3559816c63daa5f9f3e92d2971231e22d87ba0f8467e77350f` |
| checkpoint | `99c1d43b198ea25ebc17db4556d9bc0c716c6f4aad212e67c1d2aef124babfda` |
| rule | `00e84be9a4c04bac443d0ff786b7eb10f33eb23dc4e957ef1a979ee39378d12d` |
| schema | `3bf505c6055429b583d5ada0c5916730c207b145c1ce65bd718cc2bb6a65ac7e` |
| hybrid source | `dce17fdefbb7bc17ddf378444de1402391a709ca238ebd081291bf6991d2a1ea` |
| source diff | `618d6d48b625c433a3bb3b0baeedd287f74a6d5e95a123ae33c433028a38a2e5` |
| authority fixtures | `3f1206f156667352037ab809e8a1c942bb711fca9502a1a378946c00480bc8f5` |
| screen manifest | `a3f49f371310054bbd70294cdca0d4b6fb6d34a660f6348cf3ecd68d5d50b9d6` |
| registry config | `40e440d2a3ca40c8a992aa0af4c8f5730e74461eaef0e64aa07e005b198edb07` |
| hybrid binary | `22ee89aa0f9ba17677848ec0268bd4b02cd21760c3bb0f92338e744d7f5ba63e` |
| baseline binary | `d474596fab34c83b953f4fadbba1040d3ec1853b93dc47667fd7324de9497ac4` |
| baseline ordered source map | `48b5b1f8e8ef6c64d9ff5d79bba566c637e1992c7d0da85e3330479138836180` |

Incumbent ID: `baseline-85a67cd2f41250a16e4a218160bc58b845e50b01a5f17a2c4626ca20134351f8`. Provenance records base revision4802375 plus the exact working-source diff hash above; the final commit includes subsequent documentation. The exporter hashes source bytes independently of Git tracked status. The artifact contains1,452,696bytes of float32 tensors plus its JSON manifest; canonical-manifest hash is separate from weight hash.

| Replay array | Rows / shape | SHA-256 |
| --- | --- | --- |
| action | `[302]` | `c016deee938eb71efd8d15c46561a64fc75ff8c562e3212571680712736b3d7e` |
| behavior_prob | `[302]` | `90dd8b73614ee3efc0d3911a28d57eb6167390a31e025b699221e66b85d1719a` |
| boundary_features | `[48, 38]` | `ac18a6c37d996468004563d828f766796ebae80efa433590043ab3364b75c882` |
| boundary_win_target | `[48]` | `45ed597cfe87bf70c536bbc676d2b30a1c18c082cba4727932e0fc040a4f5a0e` |
| features | `[302, 1188]` | `e36d0a2a6d85c963c2c325666922543509c43bc9aadbcd08b10b5e6f1907aa91` |
| legal | `[302, 61]` | `061ba5e646955310f28d4a616daca86b8888dec7955c55d0dbc8827a84ff28a6` |
| ownership_mask | `[302, 24]` | `5721181451fad9576ff28a56c95737cda2ecb55db1864809ea0d919425b7b675` |
| ownership_target | `[302, 24]` | `f44e06a6c57eb356cc6d2cbc84c58ec1e299ee618bc22cb15f3b043f23bb5e22` |
| policy_target | `[302, 61]` | `16dc8454a17f31ffad76d39399778e5112936e2cd5259f499f2556c83f3ab190` |
| policy_weight | `[302]` | `a715ea334686adc6fcfa88dd8f2901a62ce5976f186716424a53b6d1a45b6391` |
| trick_target | `[302]` | `dcb3730932454f12729e86219fdfb71326b223aadea2e1fe3fe60d5ae9b55519` |
| win_target | `[302]` | `b96573be67e4cc48b9a8aa5baff7980def62b70188cb0e13e82227e8c805a3fe` |

| Frozen authority component | SHA-256 |
| --- | --- |
| BidEuchre.App.dll | `b5992ef9ae97c5eb32e22c48d12df79ae64a4149c8c0d1d70c8930320b290d00` |
| BidEuchre.Arena | `c0d2775bfc03122fc9f79882685316dce0787ad5538a0fd8113dcc6a0f01ac02` |
| BidEuchre.Arena.dll | `afdc13900e6c141ef2d326945d733428a2df966ae46c3fa2cf5f7cc0bd21f8ba` |
| BidEuchre.Core.dll | `06e6bb2d1ca34ba5752a930ac9e5d278e27700cb1c329ec17b21b4a95e40e5f9` |
| BidEuchre.Protocol.dll | `801cd24251a2fc025774fc5e61cdc5cc41e16b6ad0203c4f4e1c6fa554733df4` |

## Device comparison, cost and limits

After all Task14 suites and smoke finished, a separate short batch8 CPU/MPS probe measured **CPU48.13661154batches/s versus MPS27.02623581batches/s**, including transfers, both networks, backward, AdamW and synchronization. Numerical comparisons passed; auto selected CPU. This supports CPU for this small local workload; measure other batch/model sizes rather than generalizing. Probe kernels used one CPU thread; the post-probe version inventory reported the restored default of four. Environment: arm64,macOS26.6.2,Python3.12.14,PyTorch2.9.1,NumPy2.3.5,CMake4.4.4. Historical Task11 probe45.0692CPU/25.2669MPS remains historical, potentially affected by background contention.

A cleanup audit found the first sandbox-denied SDD helper still running as Bash PID30777 after a later successful helper invocation. The controller verified exact argv, sent scoped SIGTERM, confirmed it and all matching helpers gone, and found no unbounded task-owned file growth (scratch162,648KiB/build326,980KiB/venv748,872KiB at inspection). The first attempted pkill had not stopped it; the initial yielded session was not properly tracked. Earlier local timing measurements could therefore include contention. The clean device probe above ran only after current workloads completed; no benchmark was run concurrently with suites/smoke. This operational failure did not alter game outcomes/hashes or justify any strength claim.

The full gate requires at least3,000 matches:1,000 paired primary blocks plus500 paired replacement blocks balanced across conditions. Primary paired-bootstrap95% lower bound must exceed.50; replacement candidate-minus-incumbent lower bound must exceed-.02; all diagnostic/verification gates must also pass. Simple750× extrapolation of the current four-match invocation is **4,554.60seconds (~1.27hours)** at these short, maxsim 7/samples16/depth1 settings. This is not a production runtime forecast. At the observed253.5decisions/match, spending a full3seconds at every decision over3,000matches would total **633.75hours**, before overhead—an allowance illustration, not an expected runtime. Production3000ms strength and practical full-gate throughput remain unmeasured. Do not start such a run without an explicit bounded experiment choice.

Resource limits are sampled cooperative limits with32MiB reserve, not hard OS quotas. An in-flight operation can exceed a sample threshold. Exact CPU continuation is tested locally; timed trajectories and cross-device/platform bitwise reproduction are not promised. Model quality, belief calibration, mixed-partner generalization and full first-to-40 strength remain empirical questions. An untrained held-out belief probe from Task8 was worse than uniform (Brier.738981 vs.703487); trained improvement was not inferred.

## Earlier actual experiments and diagnostics

Task11's four-update/32-row artifact completed the loop in1.251763s with302decision/48boundary rows and peak286,162,944bytes. Its model SHA was `a59034e071fa723812de5c835b529e9d3efb4c2db36cd2ff661ebc023c8cc446`; canonical/file manifest SHA `13016cf44ccdae60efe9a37a9742057536f598db2cb1429357264aa599705987`; checkpoint `e6d084bb2e8cfdf820e043fda90b84edbd404f983cb83962f915bbd21d0403c0`; replay features `e36d0a2a6d85c963c2c325666922543509c43bc9aadbcd08b10b5e6f1907aa91`. A subsequent uninformed-model generation diagnostic exceeded its short bound, was interrupted after14.327s and received no reward; it was not escalated into a long run.

Task12's four-match50/100ms screen completed1,220decisions in8.333833s, zero errors, primary0/replacement0 and no promotion; no-play re-audit retained the same four attempts and completed in72.421ms. Its baseline play20ms differed from candidate50ms, so this was only workflow evidence, not equal-budget strength evidence. Peak140,492,800bytes, disk6,842,011bytes; historical reduced-setting estimate1.74hours for3,000matches was not a production forecast.

Task13 ran24 actual ablation matches, **not repeated in Task14**. All arms used the Task11 weights above, native binary `22ee89aa0f9ba17677848ec0268bd4b02cd21760c3bb0f92338e744d7f5ba63e`, seed42,maxsim 7,decision200/stop250ms (internal target180ms), process1000/startup5000ms, one arena job,30s per arm,2GiB/5GiB. All used screening run seed13000 (52002/52006), bootstrap271828. The counterpart's frozen baseline play20ms/samples16/depth1/nodes1000 was common to every arm; it was not equal to candidate200ms, so those results compare runtime toggles only, not equal-budget incumbent strength. Tree query cap2/slice100ms was frozen for every arm.

| Arm | Seconds | Complete legs | Hybrid / policy-only decisions | Policy-only fraction | Max decision ms |
|---|---:|---:|---:|---:|---:|
| baseline | 7.848 | 4/4 | 265 / 113 | 29.89% | 41.850 |
| uniform-beliefs | 8.504 | 4/4 | 282 / 107 | 27.51% | 42.538 |
| uniform-continuations | 3.672 | 4/4 | 223 / 97 | 30.31% | 16.409 |
| score-value | 23.355 | 4/4 | 782 / 390 | 33.28% | 42.587 |
| information-tree | 12.771 | 4/4 | 214 / 94 | 30.52% | 117.370 |
| double-dummy | 6.114 | 4/4 | 298 / 115 | 27.85% | 29.821 |


All six arms completed4/4, zero GateErrors, CanPromote=false, unchanged champions, primary win fraction0 and replacement difference0 on just one block each. Policy-only fractions include forced actions and incomplete balanced passes; they are not pure timeout rates. No baseline/immediate fallback occurred. A separate36-position authoritative High/Low/PartnersBest sweep produced36/36 balanced root passes. Tree:12queries,22visited edges,1,097availability observations,483policy continuation fallbacks (caps/forced/incomplete combined),215ms aggregate across6positions. DD:36completed exact perfect-information leaf estimates,0unknown,74ms across6positions. These runtime toggles reuse identical weights and do not measure removing a training loss; retraining ablations would require matched data/seeds/updates/compute and new artifacts. Tree allocation/reach bias and DD perfect-information strategy fusion remain explicit limits.

Task13's first attempt omitted DOTNET_ROOT, so Arena failed before any matches; the first diagnostic conversion also used noncanonical private-card order and caused sampler rejection. Final evidence came from a new registry after correcting both, rerunning all six arms and diagnostics. Task2's early all-PB scripted fixture drove negative scores without a winner; its identified test child was stopped and the fixture was corrected to force PB only in the first hand, then tests passed. None of these interrupted/failed diagnostics became completed-match training rewards or promotion evidence. Task14 itself had only the intentional TDD failures; its final smoke completed without retry.

Task13 frozen arm identities, retained here before scratch cleanup:

| Arm | Artifact file SHA-256 | Frozen config SHA-256 |
|---|---|---|
| baseline | `c54ccd5cc2d181aa6df7fe5f24e422f6849856976974017e3dfe5328be29c640` | `4038d95fee372d4b8fd772f9ca04bc538d0a85db89ac85c28754e6fa666acb3c` |
| double-dummy | `1e45557ec131c3bcf346db7e7a7a295262accf91bc859b1dccfbc058aa116a80` | `53a6792a2bf1019368374b91f4a7cc5e65692c51e94d65e2a0ca7bf824b8b686` |
| information-tree | `f326229926e307042085f913de8a7471705bfe9c91463e8529f0b044ed6bbbce` | `6c5970be942bba3de922bf228b4123222ddd3de5355587173b274c6ba0e627ef` |
| score-value | `c54ccd5cc2d181aa6df7fe5f24e422f6849856976974017e3dfe5328be29c640` | `2fec7cebfba47d9a58cb5cc737e9c3b92e2daf2ecdf9847285c2b94bacd8d42e` |
| uniform-beliefs | `c54ccd5cc2d181aa6df7fe5f24e422f6849856976974017e3dfe5328be29c640` | `e7dec2e6c061f7475bcdf0b149cd4737f3a812f86b42ef85c716bcca9a56fb10` |
| uniform-continuations | `c54ccd5cc2d181aa6df7fe5f24e422f6849856976974017e3dfe5328be29c640` | `595152ebdc6d10ac69f3224a20cf11412a49ad44d4fa4813979f129ced068c8e` |

The earlier raw experiment directory is `engines/hybrid/build/task13-screen-1794278764`; it contains immutable configurations, manifests, reports/traces, results.json and diagnostics.json. No raw replay data, weights, checkpoints or generated reports are committed.

Exact final installed dependency inventory (editable revision4802375, preceding the final documentation commit):

```text
-e git+https://github.com/MrMortem/bidEuchre.git@480237577b2170231230155636811fd9bfc41045#egg=bideuchre_hybrid&subdirectory=../../../../../.codex/worktrees/hybrid-bot/Bid Euchre/engines/hybrid
cmake==4.4.4
filelock==4.2.10
fsspec==2026.9.0
Jinja2==3.1.6
MarkupSafe==3.0.4
mpmath==1.3.0
networkx==3.7
numpy==2.3.5
pip==25.0.1
setuptools==84.2.1
sympy==1.14.0
torch==2.9.1
typing_extensions==4.16.0
```

## Durable per-task review record

Ranges use the exclusive base and inclusive final commit. All Tasks 1–13 and the Bash portability prerequisite received independent spec/quality review; every Critical/Important finding was resolved before the next task. Evidence is summarized here so deletion of the temporary SDD ledger does not delete the review record. Counts refer to each task's recorded gate, not an extra rerun.

| Task | Commit range | Review / repair | Recorded evidence |
| --- | --- | --- | --- |
| 1: authority fixtures / identity | `9265455..83b78c6` | Approved, no findings | C#45/45; serial/redeal/legacy/near-40 fixture assertions |
| Bash3 prerequisite | `83b78c6..8d55ff1` | Approved, no findings | Root suite; C#46/46; baseline strategy unchanged |
| 2: full-match arena | `8d55ff1..3af3175` | Approved; active-cancellation suggestion later covered by Task3 | C#55/55, real process integration, privacy/malformed/timeout paths |
| 3: paired tournaments / resume | `3af3175..a92e7e9` | Crash-window evidence retention repaired and reviewed | Full root gate; four focused crash/resume/deadline/no-duplicate checks after fix |
| 4: schema / native scaffold | `a92e7e9..e727768` | Approved | Four CTest suites; six Python schema cases; 118 native checks |
| 5: complete simulator | `e727768..1d4d0a6` | Approved | C#62/62; 62 authoritative rare/random traces; full native parity |
| 6: private observations / recall | `1d4d0a6..a368e54` | Approved | Six CTest suites; 17,968 native checks, duplicate/history/privacy/rotation |
| 7: compact model export | `a368e54..11d3d57` | Python3.10 dependency repair reviewed; strict boolean metadata closed in Task12 | Eight CTest suites; models4/export6; 886 observations/eight phases, max error8.94e-8 |
| 8: feasible beliefs | `11d3d57..8d3e76d` | Approved; tighter frequency/unequal-history fixture remains final-review triage | Eight Release CTest suites; 96,765 native checks, 1,707 observations, 90-world sampling |
| 9: playable native search | `8d3e76d..e395947` | Baseline exchange-memory restoration repaired and reviewed | Nine CTest suites, 62 fresh traces, host3/3; fix106 protocol checks and host2/2 |
| 10: generation / replay | `e395947..1ffe4cb` | Stdin-backpressure timeout and nonobject-record handling repaired and reviewed | Eleven CTest suites; 1,100,139 generation checks; worker6/replay6; complete493-row/17-hand match |
| 11: local trainer | `1ffe4cb..1ffe8f8` | Orphan status-temporary restart repaired and reviewed | Fourteen CTest suites; trainer11/checkpoint2/CLI2; cycle302 decisions, four updates, CPU/MPS measured |
| 12: promotion / registry | `1ffe8f8..8f701ab` | Artifact durability and synthetic-evidence rejection repaired and reviewed | Sixteen CTest suites; registry5/promotion12 after fix; real4-match screen and no-play audit |
| 13: gated experiments / launch descriptor | `8f701ab..4802375` | Approved; >3000ms manifest cap closed in Task14 | Seventeen CTest suites; 24 actual ablation matches, 36 balanced diagnostic positions; no promotion |
| 14: final Mac workflow and integration closeout | `4802375..18bcca2` | Approved: SDK-symlink fix and all six whole-branch Important findings addressed; final scoped review found no new Critical/Important issue | Controller freshly reran all four full gates on `18bcca2`: root 66 C#, hybrid 17 CTest, Python 78/78, isolated smoke 4/4 sealed matches; details below |

At the component-gate handoff, Task8's frequency tolerance/unequal-latent-history suggestion and Task10's compressed generation readability were deferred to whole-branch review; their final nonblocking outcomes are recorded below, not claimed fixed. Task7's ignored `nodiscard` warning and Task13's advertised decision-cap mismatch are repaired here. The existing baseline `all_bids` warning is outside strategic-source scope. Task2's active cancellation is exercised by the later real bounded tournament test: a 25ms active run records Interrupted, then resumes four complete legs without duplication. No redundant cancellation framework was added.

## Exhaustive decision appendix

This appendix preserves every line containing `Ruling:` in the SDD ledger at handoff, in order, with spacing restored and decision, reason and cost made readable. The initial capture contained 30 rulings; the final whole-branch fix wave adds rulings 31–40 below without changing the original decisions. These are controller scope/interpretation decisions, not invented user requirements.

1. **Bash3 transcript repair before baseline verification.** Use the already-authorized `while IFS= read -r` repair because the baseline suite cannot run under Mac Bash3 with `mapfile`. Preserve strategy and assertions. Cost if wrong: one reversible shell change.
2. **Scoped worktree writes.** Use escalated mutations only inside the authorized native-app worktree because it is outside inherited writable roots. Cost if wrong: automatic review rejects the operation rather than silently writing elsewhere.
3. **Existing SDK and dedicated dependencies.** Use `/tmp/bideuchre-dotnet` and install missing build/Python dependencies locally in the hybrid environment, consistent with no cloud/paid compute and isolation from CFR. Cost if wrong: reversible tool artifacts.
4. **Legal fixture score starts.** Reject already-won unequal scores at/above40 but accept ties at that threshold, because production winner semantics govern fixtures. Cost if wrong: narrow fixture validation rework.
5. **Arena budget meanings.** `ProcessMs` bounds a whole operation; `StartupMs` bounds startup; decision/stop values freeze engine settings and elapsed time feeds later deadline gates. Keep BEUCI1 grammar; hybrid enforces its internal stop. Cost if wrong: stricter gates can reject a run, never silently promote it. Tasks3/12 own elapsed-versus-stop rejection.
6. **Random authority export scope.** Task5 may minimally extend FixtureExporter/CLI for bounded fixed-seed randomized C# truth; the plan omitted the exporter file. Preserve Core/baseline strategy. Cost if wrong: reversible export-only change, reviewed in Task5.
7. **Task5 test registration.** Permit minimal native runner helpers and the Arena randomized-export assertion because named tests require registration and exporter coverage. Cost if wrong: reversible tests; omission was plan bookkeeping.
8. **Generated-corpus review.** Review the full source diff and generated corpus provenance/hash; retain the raw8.8MB package for targeted cases rather than repeatedly flooding review context. Cost if wrong: reviewer requests exact cases; source review is never skipped.
9. **Editable package ignore.** Task7 may ignore only `engines/hybrid/python/*.egg-info/`, since editable packaging emits runtime metadata. Cost if wrong: reversible ignore entry.
10. **Exact terminal values.** Tasks9/10 callers own exact terminal0/1; model checker raw/normalized logits stay parity-consistent with any terminal diagnostic separate. Cost if wrong: caller integration changes require later review.
11. **Private sampler restore helper.** Task8 may add sanitized `SeatMemory::from_observation` and a card-free prior-public-summary helper; ordinary replay would invent unknown recall and cannot seed prior-deal counters. Preserve exact known/unknown recall, summaries and event deduplication, with no privileged inputs; test roundtrip and other-seat replay. Cost if wrong: reversible, reviewed API helper.
12. **Release performance verification.** Use ignored Release build configuration because O0 correctness tests took over2minutes versus8.48seconds Release. Tasks9/14 must make the default reproducibly optimized. Cost if wrong: no source effect.
13. **Release build default.** Task9 may minimally default build.sh to Release with an explicit override; actual budget evidence must not rely on incidental cache state. Cost if wrong: reversible build configuration, no strategy semantics.
14. **Baseline recall restoration failure.** A lazy/restarted baseline unable to restore actual own-exchange facts must return a diagnosed immediate legal fallback for that deal, reset on new deal/game, and never invent sent cards. The unchanged baseline lacks a public own-action setter. Preserve healthy worker state; replay necessary historical choices and compare with recorded facts; add mismatch/recovery regression. Cost if wrong: weaker rare-deal fallback, never mislabeled hybrid/baseline; promotion rejects fallback.
15. **Conditional behavior propensity.** Task10 `behavior_prob` means exact action probability conditional on the executed teacher branch, including epsilon. Record branch kind, branch-selection probability and joint branch×action probability. Computing unconditional counterfactual mixtures would search the deliberately skipped75/95% and defeat stratified compute. Cost if wrong: future cross-branch importance weighting needs marginal recomputation; trainer must not treat conditional likelihood as unconditional.
16. **Determinism scope.** Exact traces apply to completed scripted/fixed-count jobs. Seeded timed baseline/search freezes configuration but cannot promise bitwise traces under changing scheduling. Headers must say so. Cost if wrong: another legitimate timed trajectory, never fabricated completion/reward; baseline strategy unchanged.
17. **Seed namespaces and resume.** No legacy hybrid import compatibility is required. Use residues0/1/2/3 for train/tune/screen/promote and reject mismatched old shards, preserving CFR data. Resume continues checkpoint stage; explicit cycle advances generation. Cost if wrong: manual reimport requires declared new split/seeds.
18. **Trainer suite registration.** Task11 may add the minimal CMake trainer/checkpoint/CLI suite registration because the plan omitted required test wiring. Cost if wrong: reversible build/test configuration.
19. **Strict boolean tensor metadata.** Task12 may close Task7's deferred validator/test issue while hardening artifacts; Python must not certify a manifest native code rejects. Preserve format/parity. Cost if wrong: reversible validator/test changes.
20. **Arena backend/hash tests.** Task12 may add targeted existing-file Arena regressions and runner registration for the new promotion boundary, plus Task11's structured CLI error repair. Cost if wrong: reversible tests/integration; file list omission does not remove required verification.
21. **No-play authority audit.** Task12 may add narrow ArenaProgram/TournamentRunner audit and trace backend/hash validation so registry can independently recompute evidence without playing missing legs. Audit cannot start engines; describe any report/recovery writes honestly; completed immutable results remain unchanged. Cost if wrong: reversible audit/test extension.
22. **Loaded-weight identity.** Task12 may add validated-weight SHA storage/accessor to model.hpp, because reopening a changed manifest can mislabel bytes already loaded. Cost if wrong: reversible metadata helper, no policy/format change.
23. **Registry/promotion suite wiring.** Task12 may minimally register these CMake suites and structured CLI error tests. Any synthetic passing authority seam must be explicit in tests, with no production bypass or acceptance of test provenance. Cost if wrong: reversible test configuration.
24. **Worker cap scope and evaluation resources.** At most two native workers refers to generation/training, not the four explicitly required live-seat clients. Run one Arena job at a time, no trainer; allow Task12's scoped RSS/disk supervisor, process-group cleanup and capacity test. Cost if wrong: no relaxation of training cap; evaluation resources are separately bounded and sampled.
25. **Complete native registry launch.** Task13 may add `--registry` and atomic pointer descriptors binding exact executable/argv/cwd/hash, no Python and no policy override. Deploy evaluated maxsim/seed/modes, test loaded-A retention, arguments, mismatches and fallback. Task14 wires run.sh. Cost if wrong: reversible launcher/pointer extension.
26. **Explicit experiment modes.** Task13 may minimally extend export/model/registry manifests and tests for immutable `search_mode`/`leaf_mode`, as the plan requires these fields but omits owning files. Preserve weight bytes and legacy root/policy defaults. Cost if wrong: reversible versioned metadata.
27. **Stable final tooling.** Task14 may minimally change build/CMake/installer discovery/bootstrap so final commands do not depend on disposable SDD tooling. Use dedicated hybrid Python/CMake and an explicit SDK prerequisite. Cost if wrong: reversible dependency/build configuration only; no strategy/rule change.
28. **Scoped final minor repairs.** Task14 may fix the Task13 >3000ms budget validator/test and Task7 ignored-nodiscard test warning for accurate metadata and clean new-test output. Other deferred minors belong to final-review triage. Cost if wrong: reversible validation/test-only changes.
29. **Keep the branch/worktree.** Preserve `codex/hybrid-bot` and the native-app worktree without merge/push or another integration-choice question, because the user's prior instruction already selected keep-as-is. Cost if wrong: integration remains a later explicit reversible choice; no shared side effect.

30. **Resolve SDK symlinks once.** Task14 fix may add one pure-Bash shared SDK-resolution helper and focused test to avoid duplicating link resolution across three scripts. Conventional PATH/private-SDK symlinks made the old derived runtime path point to the shim directory and broke direct apphosts. Cost if wrong: reversible build/test discovery helper, no live-engine or policy change.

## Task14 review fix1: SDK symlinks

Independent review of `d4ca506` found that root `test.sh`, hybrid `test.sh` and `smoke_cycle.sh` derived DOTNET_ROOT from the selected executable's containing directory without following executable symlinks. The CLI could run while Arena's native apphost failed to locate .NET. All three now source `engines/hybrid/scripts/dotnet.sh`; it resolves relative/absolute link chains and canonical physical directories, preserving explicit executable, DOTNET_ROOT, repository-local and PATH discovery precedence. It uses Bash3 plus standard macOS utilities, no Python or `readlink -f`.

A focused regression creates a two-link private-SDK shim path containing spaces, changes cwd, runs the real SDK CLI and **direct Arena apphost** under each of the four discovery modes, then checks the canonical root and62 exported authority traces. It is registered in hybrid/test.sh after authority build/export. The test first ran with the extracted old logic unchanged and failed exit131: `.NET location: Not found`, `DOTNET_ROOT = .../bid euchre sdk.../shim bin`. Thus this is an actual runtime-discovery failure, not a mocked path comparison.

```sh
/bin/bash engines/hybrid/tests/integration/dotnet_discovery.sh /tmp/bideuchre-dotnet/dotnet > /tmp/task14-fix1-red.log 2>&1
# After implementing link resolution:
/bin/bash engines/hybrid/tests/integration/dotnet_discovery.sh /tmp/bideuchre-dotnet/dotnet > /tmp/task14-fix1-green.log 2>&1
/bin/bash -n test.sh engines/hybrid/test.sh engines/hybrid/scripts/{dotnet,smoke_cycle}.sh engines/hybrid/tests/integration/dotnet_discovery.sh
git diff --check
```

GREEN exited0 under actual Mac Bash3.2.57: **explicit/PATH/ROOT/repository4/4** each exported62 traces from `/private/tmp/bideuchre-dotnet`; CLI and native apphost both succeeded. Shell syntax/diff checks passed. Only affected discovery/runtime checks were repeated; gameplay/model/training behavior did not change. The earlier full-suite/smoke evidence remains as recorded. Minor production-client backend identity assertion remains whole-branch-review triage, not claimed fixed here. Fix commit is recorded in the final task handoff; no self-referential hash is required in this appendix.


## Final whole-branch review: six Important integration fixes

The independent whole-branch review of `9265455..4cca637` found six Important issues, no Critical issues, and three nonblocking Minor follow-ups. The single consolidated fix wave starts at `4cca637`. It changes no Core rules, baseline strategic sources, observation/action schema, BEUCI1 grammar, or tensor encoding. It does not run a full promotion gate, install a challenger, or establish improved match strength or human-partner quality.

| Finding | Fix and regression evidence |
| --- | --- |
| Native registry accepted valid replacement weights or changed metadata | The descriptor binds canonical candidate identity and the raw immutable manifest SHA, and the target validates the exact manifest and weight bytes used by `FrozenModel::load`. Registry startup fails on either replacement; no Python or float reserialization is involved. RED: the actual native launch accepted metadata-only replacement (return code 0). GREEN: both replacement cases fail; existing typed argv/cwd, pointer durability and loaded-A-after-B tests pass. `ManifestSha256` is an additive EngineSpec property, preserving constructor compatibility and Python/C# frozen schedule serialization. |
| Truncated complete traces were recovered as completed matches | A terminal end row is durably flushed and the closed file is sealed in MatchResult/RunAttempt with SHA, action count, spec SHA and terminal-state SHA. Recovery and no-play audit verify the seal, header and end/result agreement before counting or committing a completed leg; timing/backend checks remain active for interrupted evidence. RED: four real completed matches truncated to 18/4/12/10 rows still recovered complete legs. GREEN: truncation, malformed rows, wrong header/end, wrong count and wrong terminal seal each yield 0 complete legs and errors; restored evidence recovers all four with no new engine starts. Header/end tests refresh only the outer file hash to exercise independent structural bindings. Legacy unsealed completed evidence is ineligible. |
| Private-hand wire order disabled root search | Observation sanitization sorts hands and legal card collections in numeric `CDHS`/`9TJQKA` order. Auction/trick/play history remains chronological. RED: reversed own cards changed the selected action/search counts. GREEN: across the Partners Best fixture's phases, fixed-count action, means, counts, targets and balanced-pass flag are exactly equal under reversed private-hand/legal-card arrays. |
| Resume retained stale attempt resource limits | The checkpoint's pending job contains immutable semantic controls; each attempt separately records current duration/memory/disk allowances. A checkpoint interval no longer limits match duration, and periodic writes preserve the durable pre-job restart point. The checkpoint writer is joined before the final write. RED: a 60-second, doubled-cap retry still received the old 1-second duration. GREEN: an actual native retry receives larger allowances, retains semantic job/RNG identity, completes one shard and advances once. Existing completed-shard and interruption tests remain passing. |
| Only candidate production allowances were validated | Promotion schedule creation and re-audit validate every participating known adapter: candidate, incumbent, historical partners and opponents. Hybrid effective allowances are 3000/3500 ms; C++ play allowance is 3000 ms with Arena enforcing the stop. Ambiguous, missing and unknown options fail; baseline's 700-ms default is insufficient, so the production example explicitly supplies `--play-ms 3000`. RED: eight mismatched/ambiguous configurations were accepted. GREEN: all reject promotion while explicit screening remains nonpromoting and permitted. Per ruling36, frozen fixed-count caps remain allowed. |
| Normal generation ignored the registry champion/history | Registry discovery follows explicit `--registry`, `HYBRID_REGISTRY`, then the same default as `run.sh`. Each generation freezes current incumbent, up to four recent distinct verified previous champions, baseline, fixed/reverse/uniform variety and a separately identified local learner. Checkpoints and candidate metadata retain the lineage. Native population models bind manifest and weight SHA. RED: a registry with a seven-champion history still selected literal baseline as incumbent. GREEN: bounded history, learner separation, explicit/default/missing discovery, pending-job freeze across pointer change, next-generation refresh, actual native lineup delivery, and metadata-replacement rejection. With no registry, bootstrap retains baseline/variety; fixed_seeded remains the documented scripted debug profile and rejects a selected timed-baseline registry. |

Focused commands (from the authorized worktree; the listed regression commands were run RED before their respective production changes and GREEN afterward):

```sh
engines/hybrid/.venv/bin/cmake --build engines/hybrid/build --target hybrid-tests -j 2
engines/hybrid/build/hybrid-tests
PYTHONPATH=engines/hybrid/python:engines/hybrid/tests/python engines/hybrid/.venv/bin/python -m unittest test_registry.RegistryTests.test_native_registry_rejects_valid_model_and_metadata_replacements
DOTNET_ROOT=/tmp/bideuchre-dotnet DOTNET_CLI_HOME="$PWD/.dotnet-home" DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 /tmp/bideuchre-dotnet/dotnet run --project tests/BidEuchre.Tests -- --filter ClosedTraceCorruption
PYTHONPATH=engines/hybrid/python:engines/hybrid/tests/python engines/hybrid/.venv/bin/python -m unittest test_trainer.TrainerTests.test_resume_refreshes_attempt_limits_without_changing_pending_semantics test_promotion.PromotionTests.test_production_budget_validation_covers_every_participant
PYTHONPATH=engines/hybrid/python:engines/hybrid/tests/python engines/hybrid/.venv/bin/python -m unittest test_trainer.PopulationTests
```

The first focused native GREEN reported **1,210,653 checks**, and the focused registry suite **10/10**; the integrated trainer/registry/promotion/CLI checkpoint reported **44/44**, before the additional discovery/native-lineage assertions. The full final gate below supersedes these interim counts.

### Additional controller rulings (31–40)

31. **Honor the unproven-strength boundary.** Do not run a full gate or claim increased strength/human-partner quality merely to close the software plan; the approved design permits software delivery before promotion evidence. Cost if wrong: strength remains unproven and explicitly disclosed; the incumbent stays active.
32. **Keep existing-Mac scope.** Cloud/distributed/paid compute and non-Mac deployment remain outside the approved constraints. Cost if wrong: no validation or performance promise for those environments.
33. **Keep reproducibility claims precise.** Exact CPU/fixed-count continuation is supported; timed and cross-device bitwise equality is not promised. Cost if wrong: different scheduling/devices can produce different legitimate trajectories, requiring comparison evidence there.
34. **Use the trusted-local integrity model.** Do not add an administrator-resistant signing service. Accidental artifact replacement and trace truncation still violate frozen-evidence guarantees and must fail closed. Cost if wrong: a hostile local owner can forge all trusted files/binaries; ordinary corruption remains rejected.
35. **Preserve optional legacy skips.** CFR-Torch and Prolog may remain explicitly unavailable; all required hybrid paths must execute. Cost if wrong: those optional runtimes remain unvalidated, and no hybrid skip may be called passing.
36. **Do not ban frozen fixed-count or policy-only profiles from promotion.** Enforce equal maximum time allowances and exact deployment of the evaluated configuration; validate and record caps and actual activity. A faster profile can legitimately win within the same allowance, as C++ depth/node caps can finish early. Mandatory compute expenditure is not an approved gate. Cost if wrong: a future gate may restrict profiles explicitly; no untested challenger is installed, and any eventual strength claim applies only to its exact tested configuration.

37. **Explicit isolated bootstrap opt-out.** Add persisted `--no-registry` and use it in the unique-state smoke. Correct default discovery must not make fixed-seeded verification consume or mutate an existing user champion, or fail because of an invalid ambient registry. Cost if wrong: an explicitly opted-out debug run uses recorded bootstrap policies; ordinary generation still discovers and freezes registry champions by default. Registry selection and opt-out are mutually exclusive CLI options.

38. **Defer stronger sampler-distribution regression.** Task and whole-branch review found no established union-counting defect. Keep probability-sensitive distribution checks and the tiny unequal-compatible-history fixture as a nonblocking coverage follow-up rather than expand the final wave. Cost if wrong: a subtle future distribution regression may evade the loose fixture; hard feasibility checks and full promotion evidence still protect deployment, with no present strength claim.
39. **Defer generation readability extraction.** The compressed generation flow has substantial real-behavior coverage and no established remaining correctness defect. Refactoring is optional code organization, not a prerequisite for the verified workflow. Cost if wrong: future maintenance/review costs are higher, and later changes need focused tests.
40. **Defer extra production-client model telemetry assertion.** Separate Arena/model-identity evidence proves actual learned play, while the four-client production suite proves transport/phase integration. Reviewers graded the extra all-four backend/hash assertion Minor, and the current client exposes no such diagnostic API. Cost if wrong: a future client-specific fallback regression could pass the functional smoke; the missing assertion remains documented and is not claimed fixed.

### Explicit residual Minor follow-ups

- Sampler frequency coverage still uses the loose .05 absolute tolerance at approximately .011 world probability, without the tiny unequal-compatible-history-count regression. No multiplicity-bias bug was established; stronger statistical coverage is deferred.
- `generation.cpp` retains dense policy/probability/label/resource flow. Named-helper extraction is a readability follow-up; the six required integrations did not expand into an unrelated refactor.
- The four-client production-host smoke still does not independently assert every client's learned backend/model SHA and nonforced balanced-search activity. Separate authoritative Arena runs enforce expected learned backend/model identity. This remains a coverage limitation, not a strength claim.


### Full final-wave gate and isolation follow-up

The broad gate ran once after all six integration fixes. Environment: `BIDEUCHRE_DOTNET=/tmp/bideuchre-dotnet/dotnet`, `DOTNET_ROOT=/tmp/bideuchre-dotnet`, `DOTNET_CLI_HOME="$PWD/.dotnet-home"`, `DOTNET_NOLOGO=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`; dedicated hybrid Python 3.12.14/Torch 2.9.1/NumPy 2.3.5/CMake 4.4.4.

```sh
./test.sh > /tmp/hybrid-final-fix-root.log 2>&1
./engines/hybrid/test.sh > /tmp/hybrid-final-fix-hybrid.log 2>&1
engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python > /tmp/hybrid-final-fix-python.log 2>&1
./engines/hybrid/scripts/smoke_cycle.sh > /tmp/hybrid-final-fix-smoke.log 2>&1
```

All exited 0. Root: **17 web, 28 CFR standard-library, 1 baseline CTest, 66 C# tests**, package check and solution build with 0 warnings/errors. Optional CFR PyTorch/process integration and Prolog remained explicitly skipped. Hybrid: **17/17 CTest**, freshly exported/replayed **62 authority traces**, **4/4** SDK-discovery real-apphost modes, **2/2** learned/slow-path host tests plus **1/1** missing-model fallback test. Slow sampler/delayed baseline wall times were 2981/2983 ms. Separate Python: **77/77**, golden parity 886 observations/eight phases with max raw absolute error 1.19e-7. Core and baseline strategic source diffs are empty; schema checks pass.

The first actual smoke evidence is `/private/var/folders/jp/4f3n3mz53czdh3dbct2rszdh0000gn/T/bid euchre smoke s5l98bf1/verification.json`: passed in 12.64 s, **302 decisions/12 hands**, **five updates** including four additional optimizer steps, twelve changed main-network tensors, export max head error 3.73e-8, **4/4** sealed authoritative screening legs/**1,014 decisions**, no gate errors, maximum 40.92 ms decision, and both temporary baseline and user's champion unchanged. Screening used 200/250 ms allowances and maxsim 7; its primary rate 0 and replacement difference -1 are tiny nonpromoting observations, not a strength estimate.

The controller's final smoke-isolation check identified a directly coupled issue: a selected external registry must not influence the fixed_seeded smoke. R37 adds explicit/persisted `--no-registry`. RED: the new CLI regression failed with unrecognized `--no-registry` (exit 2). GREEN: **6/6 focused CLI/population tests**, including a real cycle and resume with `HYBRID_REGISTRY=/missing/unit-user-registry`, passed; bootstrap lineage and the persisted disabled-registry setting remain intact. The only follow-up verification is the affected focused suite and an actual isolated smoke, not repeated unrelated broad gates:

```sh
PYTHONPATH=engines/hybrid/python:engines/hybrid/tests/python engines/hybrid/.venv/bin/python -m unittest test_cli test_trainer.PopulationTests > /tmp/hybrid-final-fix-isolation.log 2>&1
HYBRID_REGISTRY=/missing/final-fix-user-registry ./engines/hybrid/scripts/smoke_cycle.sh > /tmp/hybrid-final-fix-isolated-smoke.log 2>&1
```

The isolated smoke also exited 0 with the deliberately invalid ambient registry. Evidence: `/private/var/folders/jp/4f3n3mz53czdh3dbct2rszdh0000gn/T/bid euchre smoke 8ob2ojtc/verification.json`. It passed in **12.47 s**, retained `registry_enabled=false` and a null registry bootstrap lineage, completed the same **302-decision/12-hand** training match and **4/4** sealed screening legs/**1,014 decisions**, with **0 gate errors**, maximum **41.57 ms**, and both champion-unchanged checks true. No full promotion, overnight run, strength claim, user-registry mutation, push or merge occurred. Final `git diff --check` passed.


## Final approval and fresh controller verification

The independent final scoped review of **`4cca637..18bcca2`** approved **all six Important fixes**, with **no new Critical or Important issue**. The review checked native artifact identity, closed-trace/result seals and no-play recovery, exact card-permutation search behavior, per-attempt resource limits/checkpoint ordering, all-participant production allowances, and frozen registry population lineage. It also confirmed additive EngineSpec serialization and the mutually exclusive, persisted R37 bootstrap opt-out. The three prior Minor findings remain nonblocking, explicitly deferred under rulings 38–40 above.

After that approval, the controller freshly ran all four full verification commands against code commit **`18bcca273b71c825d7c7344975cec0190cc034da`**, with the stable SDK/environment documented above. **Every command exited 0.** These results supersede the earlier final-wave counts as the final code verification, while the historical RED/GREEN and intermediate evidence remain preserved above.

```sh
./test.sh > /tmp/bid-euchre-final-direct-root.log 2>&1
./engines/hybrid/test.sh > /tmp/bid-euchre-final-direct-hybrid.log 2>&1
engines/hybrid/.venv/bin/python -m unittest discover -s engines/hybrid/tests/python > /tmp/bid-euchre-final-direct-python.log 2>&1
# Smoke ran with HYBRID_REGISTRY set to a deliberately nonexistent path.
./engines/hybrid/scripts/smoke_cycle.sh > /tmp/bid-euchre-final-direct-smoke.log 2>&1
```

| Final direct gate | Result |
| --- | --- |
| Root | 66 C# tests, 17 web tests, 28 CFR standard-library tests, baseline tests and macOS packaging passed. Optional CFR PyTorch/process and Prolog skips remain as disclosed. |
| Hybrid | 17/17 CTest suites in 42.06 s; 62 fresh authority traces; four real SDK-discovery apphost modes; three live-host checks passed. |
| All hybrid Python | **78/78** in 21.789 s, including the R37 isolation regression. |
| Invalid-ambient-registry smoke | Passed in **12.176219 s**: 302 generated decisions across 12 hands; five optimizer updates/40 rows; 12 main-network tensors changed; maximum native parity error **3.725290298461914e-8**. Four of four sealed authoritative screening matches completed, with 1,014 decisions, zero gate errors and maximum decision time **40.1373 ms**. |

The final screening remained **NonPromoting**, with primary win rate 0, replacement difference -1 and `CanPromote=false`. The temporary baseline champion and the user's champion bytes were both unchanged. These tiny screening observations verify the workflow, not improved strength. No full promotion or unattended training run was performed.

Durable evidence bindings from the controller's final summary:

- Model SHA-256: `b5d4c475dbd5d0fb6b0aeda2aafa322b355d2f0419fe10cd700e9ae574fc278d`.
- Candidate identity: `ee780abfe1b973aa70bb4b61dbf67cbc9f611b44ff56b81e8f650590e11675a4`.
- Checkpoint SHA-256: `9cbb005fef66fe490bfa4ded838c47080b833e08d207202d1a60500c6cd1bd1e`.
- Authority manifest fingerprint: `28150a7357dcc5e9dd4e0da04859ea605f669c05a55c76a5d99db2762742fa7b`.

The source records at closeout were `/tmp/bid-euchre-final-direct-summary.json`, the four logs above, and `/private/var/folders/jp/4f3n3mz53czdh3dbct2rszdh0000gn/T/bid euchre smoke 5z7y51f3/verification.json`. Those temporary paths may be cleaned up; the review outcome, complete decision ledger and final verification results are preserved in this committed report. This closeout changes documentation only and reruns no tests. Retain branch `codex/hybrid-bot` and its worktree; no merge, push or archive is authorized by this closeout.
