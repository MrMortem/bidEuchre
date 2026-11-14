# Task 16 part A — campaign server report

Implemented the separate cookie-scoped campaign service and HTTP API for issue #30. Browser UI remains part B; the complete issue still requires integrated UI review.

## Files and interfaces

- `Server/WebCampaigns.cs`: bounded leased registry, request semaphore + session gate + short state lock ordering, private hashed profile paths, process-lifetime root lock, persistent saves, dirty retention, eviction/shutdown, fresh restore and single terminal settlement.
- `CampaignWire.cs`: closed twelve-command union, physical IDs, exact enum strings, strict member/type/bounds validation. `CampaignProjection.cs`: explicit seat0 public state, known shared deck, inventory/target counts/shop eligibility, actual gumball copy changes, structured result summary, distinct profile/engine revisions.
- `CampaignEndpoints.cs` and narrow `Program.cs` registration/error handling: routes in `task-16-wire-contract.md`, body cap including streaming bodies, no query overrides, no-store responses, strict persistent scoped cookie. Existing LocalRequestPolicy is unchanged.
- Uses Task12's committed App APIs: CampaignCheckpoint, CampaignRun.WithCheckpoint, gated operation preconditions, coherent ReadCampaignAsync, CampaignStore.LoadSave and Save(CampaignSave,path), metadata/result summary in the same v3 envelope. No sidecar or second save format.
- Root-approved small shared rule accessor: `CampaignShop.JokerSellValue = 2`, used by SellJoker and public inventory sale preview. No economy change.
- README documents data root, cookie identity, caps/expiry, close vs forfeit, save errors, local scope, and corrected desktop exact-resume behavior at Task12's request.
- Existing security/browser/published test launchers now use separate temporary campaign roots. The new root lock is acquired on startup even for a freeplay-only server, so concurrent test processes otherwise collide with each other or user data.

## Validation

- Initial new HTTP tests failed because GET /api/campaign returned HTML before implementation; both passed after endpoint work.
- Malformed union test caught JSON null reaching property access; corrected required-member validation to reject nonobjects. Lifecycle test caught accepting data roots under app output; corrected descendant check. Both failed first and passed after fixes.
- Clean Server.Tests build: zero warnings/errors.
- `BidEuchre.Server.Tests` apphost: all 10 printed scenarios pass (2 existing policy/reaper + 8 campaign groups). Coverage includes twelve union variants/all gumball names; null/extra/invalid enum/ID/target payloads; coherent private state; exact restart/manual order; old profile version with unchanged engine revision; stale deal serial; duplicate concurrent pulls and arrangement; result/shop/delete; all four pending-choice families (Telepath actor is the partner bot); Partners Best exchange; dirty-save capacity retention/recovery; root ownership; live expiry and concurrent reads; a real Core-played terminal match settled once with persistent public scoring history.
- Node `campaign-api.test.cjs`, existing `security.test.cjs`, existing `browser.test.cjs`: 24/24 pass, zero skipped. Campaign HTTP five tests cover isolated cookie jars, no profile/RNG/wallet/live-snapshot JSON, all mutator origin/content-type protection, malformed input, exact HTTP restart and persisted result, cross-process root locking. Existing real Chromium freeplay navigation/close regressions remain green.
- `published.test.cjs`: 1/1 pass with private data root outside publish output.
- Full Core/App executable: 240/240 pass. Full Desktop verification belongs to Task12/Task11; no Desktop files edited here.

## Browser fixture support

Only the test executable accepts `--campaign-fixture <private-root> <43-character-base64url-token> <shop|human-turn|match-end>`. This is not a production endpoint. The browser test process (not page JS) creates a random token, writes a fixture before the first profile GET, and installs its cookie. Shop has $100, Alpha, Chameleon + DevilishDeal Tarot, Bear Sigil and all nine unlocks. Human-turn is a genuine initial seat0 auction. Match-end searches deterministic actual Core play to find a winning match one human card play from completion; verified generation succeeds. Fixtures use ordinary CampaignStore v3 files and metadata.

## Self-review and limits

Reviewed lock acquisition and callback paths: bot callbacks only enter the state lock; no operation holds that lock while awaiting a session; live profile/version/deal checks run inside the session mutation gate; reads project metadata with the seat0 view under that gate. Leases prevent eviction of queued/in-flight requests; shutdown rejects queued work before it can start replacement sessions. Paused replacement/forfeit invalidates old callbacks before disposal. Dirty eviction retries persistence and retains failed entries within the cap.

Result settlement uses the terminal checkpoint exactly once while Run.Phase is InMatch, clears LiveMatch via App, and stores public history/outcome/version atomically. Reads never open or reroll a shop; result acknowledgement opens once. Closing/expiry never forfeits. Corrupt files remain intact until explicit New/Delete. Cookie token, filesystem path, RNG, private live snapshot and other players' hidden cards/wallets are absent from projections.

The full frontend/browser task is ongoing. An independent combined review is still required by the parent. No claim is made here that every Tarot/Sigil has its own server-level scenario: their shared Core/App rules retain existing complete coverage; server tests cover representative stored-item targeting, live pending commands and strict structural validation. Save failure cannot promise restart durability; retained in-memory status and shutdown diagnostics document that limit.
