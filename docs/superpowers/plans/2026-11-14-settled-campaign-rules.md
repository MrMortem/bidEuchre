# Settled campaign rules and UI plan
Goal: implement and verify all settled issues in reviewable commits.
Spec: ../specs/2026-11-14-settled-issue-scope.md; detailed issue bodies in ../specs/2026-11-14-open-issues.json.
Architecture: deterministic Core rules, App save/orchestration, Avalonia desktop, ASP.NET/vanilla JS web. .NET10, custom console tests and Node tests.

## Global Constraints
- Work only in isolated worktree on codex/settled-spec-issues.
- Preserve surviving numeric enum and serialized IDs; remove retired items safely from old saves.
- Complete roster is nine gumballs; High/Low is one. Preserve eleven user decisions.
- Do not tune campaign opponent aggression or invent unresolved designs.
- Behavioral tests for logic/persistence; appropriate UI verification without mirroring cosmetic constants.
- Commit only owned paths. Implementers must not push, merge, or edit GitHub issues.
- Writes/build/git need require_escalated because native-managed worktree is outside writable roots.
- SDK /Users/wtcolflesh/.dotnet/dotnet, DOTNET_ROOT=/Users/wtcolflesh/.dotnet, DOTNET_CLI_HOME=/private/tmp/bideuchre-goal-dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, MSBUILDDISABLENODEREUSE=1. Build --disable-build-servers -m:1 -p:UseSharedCompilation=false. Test apphost directly, not dotnet test.dll (self-spawning tests).

## Review focus
Saved identities, exact roster, bounded retriggers, eligibility, bot parity, zero scores, noncampaign isolation, UI privacy and lifecycle.

## Task 1: Active catalogue, names and retirement migration
Issues #74,#76,#77,#78,#79,#80,#81. Read their complete bodies in docs/superpowers/specs/2026-11-14-open-issues.json.
Own Core CampaignInventory.cs, FeatureCatalog.cs, TarotEffects.cs, CampaignEffects.cs, GameEngine.Campaign.cs; App CampaignStore.cs/CampaignShop.cs; relevant core tests including Program.cs; artwork manifest names/current rules doc.
Retire Sigils Coin, Snake, Owl, Peacock. Active set Bear,Eagle,Fish,Spider,Frog,Duck,Vulture,Chameleon. Remove active handlers/visibility/catalog/acquisition/generation. Preserve surviving numeric IDs. Old saved deck sigils become null; remove retired held items/offers.
Retire Tarots LovePotion,Nightshade,CloverTonic,CrystalElixir,Glassblower. Active nine Festival,Stone mason,Prospector,Devilish Deal,Gambler,Traitor,Astronomer,Taxes,Ectoplasm. Migrate inventory/generated/shop offers and remove retired use/generation paths.
Display Lowballer as Safety Net, Chameleon Tarot as Festival, Grindstone as Stone mason, SpringLoaded as Ectoplasm, TaxCollector as Taxes. Preserve surviving internal keys/serialized IDs; don't rename Chameleon Sigil. Update visible logs/help/art metadata; preserve historical archives.
Tests first for exact active sets, legacy migration across all saved locations, unknown-item rejection, surviving IDs, round trips and random Tarot generation. Update obsolete fixtures to active equivalents.
No other rule changes. Build/run core tests, commit owned paths, report test commands/results/commit/concerns.

## Task 2: Readable hand cards and corner sigils
Issues #62,#63,#64. Read detailed issue bodies in docs/superpowers/specs/2026-11-14-open-issues.json.
Own Desktop HandFan.cs, PixelArt.cs and tightly necessary flight helpers; relevant Desktop tests. Do not edit MainWindow.cs without coordination, nor Core/App/Server.
Move sigil top-right with appropriate inset, keep rank/suit artwork readable in every shared CardContent surface. Increase hand-card dimensions 10-15% from .75 baseline (use .84, 12%). Increase spacing above horizontal56/side42 coordinated with scale; retain reachability, hit targets, drag slot math, deal/play flight positions.
Inspect existing layout/test infrastructure. Verify supported sizes/scales with headless render/screenshot if available, checking clipping/readability/geometry. Avoid tests simply asserting cosmetic constants.
Coordinate builds with controller. Run relevant tests, inspect representative render, commit owned paths; report evidence/unverified limits.

## Task 3: Close web tables
Issue #34. Read body in docs/superpowers/specs/2026-11-14-open-issues.json.
Own Server wwwroot/app.js,index.html,styles.css and tests/server/browser.test.cjs or existing web tests; avoid other code.
Separate semantic Close and Open controls in lobby; active game confirmation includes table name/state. Use existing DELETE endpoint; refresh list, announce success/error. Failed deletion preserves card; deleting current viewed table returns to lobby. Keyboard/screen-reader usable; preserve JSON mutation header and server resource removal.
Behavior tests first: successful/failed DELETE, active confirmation, current-table return, accessibility where harness supports. Existing endpoint frees bot resources/capacity. Run web/relevant server tests, commit owned paths, report exact evidence.

## Task 4: Effect corrections and Double Down
Issues #75,#83,#84,#86,#87,#88,#89,#90. Depends Task1. Read issue snapshot bodies.
Own Core effects/engine/inventory/catalog and tests, metadata/current rules as needed.
Duck1in3 +4. SnakeEyes+4. RollerCoaster zero each round. Cure-All player/partner wins only. LoverBoy/Clubhouse/RoyalCouncil remain player-team-only and descriptions explicit; LoverBoy requires human calledHearts.
DoubleDown retriggers first human card each round once, bounded independent interactions with Frog/LuckyDuck/Bouncy/Glass/LastLaugh; read #87 criteria. LastLaugh final trick each round unchanged.
Tests first for team scope, round resets, deterministic chance, first-card triggers, bounded combination effects. Commit/report.

## Task 5: Campaign bidding eligibility and Duelist
Issues #69-73,#91. Depends Task4. Read issue snapshot.
Own Core bidding, App CampaignBotPolicy, core tests/catalog.
All seats share campaign unlock restrictions incl High/Low; freeplay unaffected. Partner prefers pass until humanpassed, preserves forceddealer/VowOfSilence. Opponent evaluation unchanged.
Duelist4-5, failureextra2, oncepermatch (not round), rejectedcommands don't consume. Alpha any eligible unlocked partnerbid inclSafetyNet2,PartnersBest,Alone, neveropponent/locked.
Tests across seats/unlocks/direct rejection; partner before/after human pass; forceddealer; Alpha special; Duelist round/match reset. Commit/report.

## Task 6: Ante targets and successful opponent bid bonus
Issues #67,#93. Depends Task5. Read issue snapshot.
Core options/settlement/context, App CampaignRun, tests/current rules.
Targets +/-12,16,24,32,42 Antes1-5; 8 fullrounds plus fullround overtime until tiebroken. Bothteamsstart0 normal/boss.
Only successful opponentcontract adds +0,1,2,3,4 points once. No failed/playerteam/pertrick/cash bonus. Fixed bonus after contract multipliers so exact amount preserved, document/test ordering. BeyondAnte5 preserve prior progression unless invalid.
Tests allspecifiedantes/boundaries/success/failure/team/boss/overtime. Commit/report.

## Task 7: Duplicate gumballs and immediate Astronomer
Issues #68,#82. Depends Task6. Read issue snapshot.
Core inventory/snapshot/effects, App Shop/Store, count/payout UI and tests.
Keep pull after9 unlocked. Locked-first then random duplicate; +1 per extra acquisition on matching successful human bid. Persist extra-copy counts while keeping unique Unlocks semantic; oldsave extras0.
Astronomer grants immediately both shop/match through same helper; never heldconsumable, usable after allunlocked, fullinventory doesn't block immediate use.
Tests RNG, roundtrip, success/failure/matching/stacking, bothgrantpaths and invaliddata. Document engineering rulings. Commit/report.

## Required follow-on scope
After Tasks1-7 create detailed tasks for #17 score summary/log, #22 exactlivecampaignresume, #24 freeplaybot, #25 engine/setup persistence, #27 reroll/badges, #29 rematch/hotseat, #30 webcampaign, #46 livematchsettings, #61 trumpannouncement. Triage /private/tmp/bideuchre-legacy-ready.md says all implementationready. Do not drop broad issues.

## Task 8: Stronger built-in free-play bot
Issue #24. Independent of campaign rules, but serialize test Program.cs edits with Core tasks. Read detailed issue snapshot.
Own App/BotDrivers.cs, new FreePlayBotPolicy.cs, new focused bot tests, test runner registration. Use Core GameRules helpers for effective suit/bower/trick comparison.
Port a deterministic reasonable heuristic, using engines/cpp-heuristic as reference only. Evaluate each legal Trump/High/Low contract from the seat's actual visible hand; sensible raises and special bids only when justified, fallback always legal including forced dealer. In play, don't overtake a winning partner needlessly; play lowest winning card against opponents, discard cheaply when unwinnable; trump outranks off-suit ranks and Low reverses rank correctly. Partner's Best exchange takes contract strength into account. Preserve public information boundary. Keep campaign policy/aggression untouched. Difficulty tiers optional, choose one stronger built-in policy to satisfy proposed port.
Behavioral tests: unrelated-color Jack is not trump; 9 trump beats off-suit Ace; left bower follows trump; partner winning saves high card; weakest winner; forced bid; safe raise; High/Low strength; legal exchange. Simulate full bot table completion. No new external dependency. Commit/report.

## Task 9: Graphics settings within live tables
Issue #46. Depends Task2; owns App/GameSession.cs, Desktop/MainWindow.cs or new partial, SettingsPage.cs and focused App/Desktop tests. Read issue snapshot.
Add table Settings action for freeplay and campaign; await safe session pause under existing semaphore, display "Match paused" in settings, return button labeled Back to table. Do not detach/rebuild/dispose session or forfeit campaign. Apply and save graphics preferences through existing code. Bot operations cannot advance after pause returns; resume safely with pending deal/action delays and teardown cancellation. A hidden table cannot hold a deal-animation gate forever. Invalidate render state on return to apply graphics without replaying stale input/animations. Handle match completion while navigation begins.
Tests first: pause/resume ordinary/campaign with in-flight bot delay, same game identity/scores/cards/auction/current actor/revision after panel; resume and disposal complete; UI entry/return preserve campaign lives; options still saved. Retain menu-forfeit behavior.
Ruling: bots pause while settings is open, explicitly communicated, engineering lifecycle choice within issue.

## Task 10: Trump and caller announcement
Issue #61. Depends Task9 (pause gate), Task2. Own TableMotionTracker, new announcement helper, Desktop MainWindow/BidAnnouncement and tests.
On contract selection announce caller name and suit (or High/Low mode) visibly. Hold ~1.5 seconds and gate trick play while presentation pending; hold static text when Motion off. Trigger once per deal/selected contract, not each refresh; human and bot callers, Partner's Best exchange, Duelist/Telepath/Chaos supported. Chaos initial random trump announced, subsequent changes compact refreshed indicator at trick boundary, not full start hold every refresh. Preserve chosen pace and lifecycle/disposal/hidden page recovery; never deadlock non-desktop clients.
Tests human/bot contract cues and duplicate suppression, static motion-off text, no play before gate release, safe session change while cue pending. Visual inspect at supported small size. Commit/report.

## Task 11: Explain scoring and show match history
Issue #17. Depends Tasks4-7; coordinate with Task10 MainWindow. Own Core result/view additive records, scoring/result storage, Desktop summary/log helper and MainWindow, serialization/test callers as needed.
Expose structured hand result breakdown rather than parse event prose: contract contribution, card effects, Joker points, trick modifiers, whole-hand factor, new opponent/duplicate bonuses. Exact displayed parts must sum to final delta. Show contract/made-set/tricks and both teams after each hand. Scrollable public log includes effect/money events during play; avoid hidden-card leakage. Freeplay match end shows winner names, final scores and chronological hand history. Campaign last result remains readable through final transition rather than disappearing before user can inspect.
Tests plain/campaign, failed/successful bids, penalties and multipliers, sum invariants, history survives next hand, public log contains effects but no private exchanges. Preserve protocol compatibility with additive fields. Commit/report.

## Task 12: Exact live campaign resume
Issue #22. Depends Tasks4-7,9,11 to snapshot new state. Own Core new CampaignMatchSnapshot and GameEngine snapshot partial, App GameSession/CampaignRun/Store, Desktop CampaignFlow/MainWindow; behavioral tests.
Export/import versioned complete engine state: player names/options, phase/hand/deal/dealer/current actor/bid/contract/winner, scores/tricks/auction/history/events, each physical hand/current and completed tricks/exchange, resources/RNG, overlays/wild suits/reveals/pending choices/variants, duel usage, retrigger/reset counters, card/Joker/modifier/trick points, match overtricks, last/first card, wallet starts, effect sequence. No reflection/private-field serialization. Campaign RNG controls future campaign deals/effects; ordinary System.Random is only needed for campaign initial dealer already snapshotted.
Validate structural and phase/card/seat/identity invariants before constructing restored engine; invalid files fail cleanly, old saves without live state migrate safely (restart that match with preserved resources/lives, report legacy limitation instead of silent forfeit). Preserve existing retired migration and extra-gumball defaults.
Checkpoint engine+resources atomically after successful actions including hand reorder when it affects Sticky Fingers. Restore GameSession engine with fresh bot instances rather than StartGame/deal; revision/dealserial intact. Continue returns same match without life loss; explicit Forfeit remains. Clear live snapshot after result/forfeit. Failed writes visible and retain in-memory state.
Tests save/load at bidding, selected contract/exchange, incomplete trick, pending joker/telepath/duel choice, special variants/effects and next-deal determinism. Compare all views/legal actions and identical subsequent decisions/outcomes to uninterrupted engine. Corrupt snapshot tests and old-save migration. Desktop Continue smoke preserving lives and current hand. Commit/report.

## Task 13: Deterministic shop reroll and offer availability
Issue #27. Depends Tasks1,7,12 persistence. Own CampaignSave/Store/Shop, Desktop ShopScreen/CampaignFlow, tests.
Reroll unsold regular stock costs $2 then +$1 each reroll this shop; reset per visit, persisted counter validated; use campaign RNG and increment revision atomically. Preserve one gumball pull per visit, sold state and retained ability to pull duplicates. Do not invent lock/rare/discount feature (explicitly optional). Visible badges/reasons for full Joker/item slots and insufficient funds, retain Buy&Use where valid, sell value visible before selection.
Tests deterministic reroll/reload, price escalation/reset, insufficient-funds rejection no mutation/RNG advance, gumball sold state retained, stale commands guarded; UI blocked/full/inadequate money and Buy&Use affordance. Commit/report.

## Task 14: Persist engine catalogue and last table setup
Issue #25. Independent App EngineCatalog/registry helper, Desktop engine/setup integration; serialize MainWindow with other tasks.
Persist stable descriptors (ID/path/args/name/author) beside preferences; load without eagerly launching all processes, validate/lazily handshake on use, mark missing/failing engines visibly and allow repair/removal. Discover known launchers/manifests only under explicit engines directory beside app; don't scan arbitrary directories or autoexecute untrusted files at discovery. Deduplicate discovered/loaded same descriptor. Builtin remains available.
Persist last names/seat kinds/engine IDs/pace/table name; restore controls using stable IDs, gracefully mark missing selections, no invalid array indexing. Keep server EngineCatalog transient by default unless explicitly configured.
Tests registry roundtrip/malformed/missing/stable IDs/removal/discovery dedup; lazy handshake failure unavailable state; last setup roundtrip and UI restore. No packaging arbitrary external bundles required. Commit/report.

## Task 15: Freeplay rematch and automatic hotseat privacy
Issue #29. Depends Tasks9,14; own MainWindow/new partial, optional preferences/handoff helper and tests.
GameComplete gives Rematch same seats/engines/pace fresh game and New table setup; show winning player names. Dispose prior bot resources safely, campaign lifecycle unaffected.
Opt-in automatic hotseat mode: when actor moves to different human, hide all private hands/choices, show Pass device to name and explicit Reveal; only then switch view. Respect Partner's Best exchange, pending turn, next deal and existing input generation guards. No stale click acts as next player; spectator/onehuman stays normal. Remember preference if appropriate.
Tests rematch seats/options/fresh state, disposal, crosshuman hidden-until-reveal and no flash/stale inputs, exchange and singlehuman exclusions. Commit/report.

## Task 16: Campaign in browser with isolated saves
Issue #30. Depends Tasks7,12,13 and web Task3. Own Server campaign service/endpoints, wwwroot campaign UI module and app integration, server/browser tests and README.
Reuse Core/App rules/shop/policy and exact resume. Persistent opaque per-browser identifier scoped via server cookie and isolated save path under configured campaign data directory; never desktop campaign.json. Preserve LocalRequestPolicy protections, no accounts implied. Serialize per-profile mutations; validate revision on shop/run mutations, expected revision+dealserial on live commands.
API: get/new/continue run, start/forfeit match, next hand, shop buy/buy-use/sell/pull/discard/reroll, deck/target selection, ordinary campaign actions. Return only viewer-appropriate campaign state (human seat0); private full snapshots never exposed. Lifecycle bounded/dispose resources on deletion/expiry; resume persisted state.
Browser: Play campaign, ante/lives/money/target/opponentbonus, shop badges/reroll/gumball feedback, deck picker, shared card enhancements/sigils, complete campaign bidding/contract/pending choices/items and scores/results/continue. Existing freeplay lobby still works. Clear errors, loading state and stale revision refresh, accessible controls.
Tests two-browser isolation, persistence/restart midmatch, stale revision and dealserial rejection, end-to-end new run -> match actions -> result/shop -> buy/use/pull/reroll -> next match, complete pending commands; browser accessible flow and freeplay regressions. Document data location/runtime behavior. Commit/report.
