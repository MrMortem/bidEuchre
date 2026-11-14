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
