# Collector's Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Arcade's single random Sigil prize with a Collector's Pack. The pack opens to 3 cards, and the player must pick one and apply it to one of six offered cards. A rare Legendary card fills the free Joker slots instead.

**Architecture:** Extend the pending `ArcadeResult` with three saved pack cards drawn in the same transaction as the play. A new App command, `CampaignArcade.PickPackCard`, either hands a Sigil to the existing committed shop-use session (hidden zero-price Arcade offer + `ShopUseSession`) or grants Jokers. Desktop (Avalonia) and browser each add a pack-reveal step on the Arcade result. The new card art comes in through the curated-art pipeline as a `collector` category.

**Tech Stack:** .NET 10 / C# (Core, App, Avalonia Desktop, ASP.NET Server), vanilla JS web client, SkiaSharp art builder, custom test runners (`tests/*/Program.cs`), Node `node --test` web tests.

**Spec:** `docs/superpowers/specs/2026-11-16-collector-pack-design.md`

## Global Constraints

- Pack size: exactly 3 distinct cards. Legendary: 1 in 50 per card slot, at most one per pack, rolled only when `Inventory.Jokers.Length < 5` at play time.
- Pack card keys: `Bear`, `Eagle`, `Fish`, `Spider`, `Frog`, `Duck`, `Vulture`, `Chameleon` (the `SigilId` names, matching `FeatureId` today) and `Legendary`.
- Card titles: Bear=Strength, Fish=Abundance, Frog=Echo, Duck=Lucky, Chameleon=Deception, Eagle=Resilient, Spider=Affliction, Vulture=Resourceful. Display as "<Title> — <Sigil> Sigil". Sigil keys, names and effects are unchanged.
- Tarot-tier prizes (Plinko 3–4, roulette colour match) are unchanged, including Keep / Use Now.
- A pack is never stored: Keep is rejected for pack results. Picking a Sigil commits the use session, with no Back or Cancel.
- Legendary grants `5 − Jokers.Length` Jokers, drawn uniformly without replacement from the shop Joker pool (`FeatureCatalog` Jokers not already owned, the same filter as `CampaignShop.cs:235`).
- Old pending Sigil results (`FeatureId` set, `PackCards` null) keep the old Keep / Use Now behaviour.
- All randomness uses `CampaignRandom` from `run.Resources.RandomState` and is saved before any reveal. Animation never decides results.
- Art: `collector` category, 256×384 output. Resources are `curated-collector-<bear|eagle|fish|spider|frog|duck|vulture|chameleon|legendary|pack>`. Originals are kept byte-exact with sha256 in `assets/pixel/source/collector/`.
- Every new state change bumps `Resources.Revision` by 1 and honours `expectedRevision`.
- Rejected commands throw `GameRuleException` and leave the run unchanged (tests use the `Reject` helper pattern).

## Review Focus

1. **Replayed or double pick** (double click, retried web request): the second pick must be rejected with the run unchanged. Pinned in Task 3 (`PickRejectsReplayAndStale`).
2. **Joker count changing between play and pick:** selling a Joker or buying one while a pack is pending must be rejected, so a Legendary can never overfill slots. Pinned in Task 3 (`PendingPackBlocksJokerChanges`).
3. **Restart after picking a Sigil but before applying it:** the use session resumes with the same Sigil and six cards, and Done stays blocked. Pinned in Task 3 (`PickedSigilSurvivesRestart`).
4. **Old save with a pending single-Sigil prize:** it still loads, validates, and offers Keep / Use Now. Pinned in Task 2 (`LegacySigilPrizeStillKeeps`).
5. **Browser poll during or after the reveal:** an unchanged state must not replay the pack flip or reset the player's highlighted card. Pinned in Task 6 (`pack reveal does not replay on unchanged poll`).

---

### Task 1: Collector art and card titles

**Files:**
- Create: `assets/pixel/source/collector/{legendary,fish,bear,frog,duck,chameleon,eagle,spider,pack,vulture}.webp`, copied byte-exact from the session attachments `images/1.webp` … `images/10.webp` in that order (Vulture is `10.webp`, the pack is `9.webp`).
- Modify: `tools/CuratedCardArtBuilder/Program.cs` (size switch near line 57: add `"collector" => (256, 384)`)
- Modify: `assets/pixel/curated-art.json` (10 new entries, `category: "collector"`, with `sha256` of each source)
- Create (generated): `assets/pixel/curated-collector-*.png`
- Modify: `src/BidEuchre.Core/FeatureCatalog.cs` (add `CollectorTitle`)
- Modify: `src/BidEuchre.Desktop/PixelArt.cs` (add `CollectorImage`)
- Modify: `src/BidEuchre.Server/wwwroot/campaign-cards.js` (add `collectorArt`)
- Test: `tests/BidEuchre.Desktop.Tests/CuratedArtTests.cs`, `tests/web/curated-campaign-art.test.cjs`

**Interfaces:**
- Produces: `FeatureCatalog.CollectorTitle(SigilId id) -> string`, e.g. `"Strength"`.
- Produces: `PixelArt.CollectorImage(string packKey) -> Bitmap`, where `packKey` is a Sigil name, `"Legendary"` or `"Pack"`.
- Produces (JS): `CampaignCards.collectorArt(key) -> '/pixel/curated-collector-<lowercase key>.png'`.

- [ ] **Step 1: Write failing tests.** `CuratedArtTests.CollectorArtIsComplete` asserts that for each of the 10 resource names the manifest entry exists, has `category == "collector"`, its PNG is 256×384, and its source sha256 matches the file in `assets/pixel/source/collector/`. A web test asserts `collectorArt('Vulture') === '/pixel/curated-collector-vulture.png'`. A Core test (in `tests/BidEuchre.Tests/SettledCatalogueTests.cs`) asserts `CollectorTitle(SigilId.Vulture) == "Resourceful"` and checks all eight pairs from Global Constraints.
- [ ] **Step 2: Run and confirm they fail.** Run `./test.sh` with filter `Collector` (`dotnet run --project tests/BidEuchre.Desktop.Tests -- --filter Collector`, the same for `tests/BidEuchre.Tests`, then `node --test tests/web/curated-campaign-art.test.cjs`). Expected: FAIL, missing entries and members.
- [ ] **Step 3: Copy sources, add the builder size, add manifest entries, and run the builder** (`dotnet run --project tools/CuratedCardArtBuilder`) to generate the PNGs. Make sure the server serves `/pixel/curated-collector-*.png` and the desktop embeds them, following how `curated-sigil-*` are included (check the `.csproj` globs).
- [ ] **Step 4: Implement `CollectorTitle`, `PixelArt.CollectorImage` and `collectorArt`.**
- [ ] **Step 5: Run the tests. Expected: PASS.**
- [ ] **Step 6: Commit** `feat: import collector card art and titles`.

### Task 2: Pack drawn with the Arcade play

**Files:**
- Modify: `src/BidEuchre.App/CampaignSave.cs:33` (`ArcadeResult`)
- Modify: `src/BidEuchre.App/CampaignArcade.cs` (`CompletePlay`, `KeepArcadePrize`, `BeginArcadePrizeUse`, `IsValid`)
- Test: `tests/BidEuchre.Tests/CampaignArcadeTests.cs`, registered in `tests/BidEuchre.Tests/Program.cs`

**Interfaces:**
- Produces: `ArcadeResult.PackCards { get; init; } : string[]?`, an init property rather than positional so older JSON loads with `null`.
- Produces: `CampaignArcade.PackSize = 3`, `CampaignArcade.LegendaryOdds = 50`, `CampaignArcade.LegendaryKey = "Legendary"`.
- Produces: `CampaignArcade.IsPack(ArcadeResult r) -> bool` (`r.PrizeCategory == Sigil && r.PackCards is not null`).

- [ ] **Step 1: Write failing tests.**
  - `SigilWinOpensPack`: for Plinko 6 and roulette 100 (use the existing `Outcome` helper), `PackCards.Length == 3`, the cards are distinct, all are Sigil names or `"Legendary"`, `FeatureId is null`, and `TargetCards.Length == 6`. A round trip through `RoundTrip` gives an identical snapshot.
  - `LegendaryRateAndLimit`: across seeds 1..3000 of Plinko-6 outcomes, no pack has more than one `"Legendary"`. The share of packs containing one is between 3% and 9%. With an inventory of 5 Jokers, no pack contains `"Legendary"`.
  - `PackCannotBeKeptOrUsedDirectly`: `Reject(run, () => KeepArcadePrize(...))` and `Reject(run, () => BeginArcadePrizeUse(...))` on a pack result.
  - `LegacySigilPrizeStillKeeps`: build a pending result with `PrizeCategory = Sigil`, `FeatureId = "Fish"`, `PackCards = null` and six targets. It passes `RoundTrip`, and `KeepArcadePrize` adds `SigilId.Fish` to inventory.
  - `StrictPersistence` (extend the existing test): a saved pack with 2 cards, duplicate cards, two Legendaries, an unknown key, or both `FeatureId` and `PackCards` set fails to load.
  - Update the existing `PrizesKeepOrUseWithoutExtraPayment` so its Sigil-tier cases expect a pack. Its Tarot cases stay the same.
- [ ] **Step 2: Run** `dotnet run --project tests/BidEuchre.Tests -- --filter Arcade`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.** In `CompletePlay`, for a Sigil prize, draw the pack before the targets. For each of the 3 slots: if no Legendary has been drawn yet and `run.Resources.Inventory.Jokers.Length < 5` and `random.NextInt(LegendaryOdds) == 0`, pick Legendary. Otherwise pick uniformly among Sigils not yet in the pack. Set `FeatureId = null` and `PackCards`. `KeepArcadePrize` / `BeginArcadePrizeUse` throw `"Open your Collector's Pack and choose a card."` when `IsPack`. `IsValid` accepts either the legacy shape or the pack shape and nothing else.
- [ ] **Step 4: Run the Arcade filter. Expected: PASS** (including all pre-existing Arcade tests).
- [ ] **Step 5: Commit** `feat: arcade sigil wins draw a three-card collector pack`.

### Task 3: Picking a pack card

**Files:**
- Modify: `src/BidEuchre.App/CampaignArcade.cs`
- Modify: `src/BidEuchre.App/CampaignShop.cs` (Joker buy/sell guards, if not already blocked while a result is pending)
- Modify: `bid-euchre-final-rules.md` (Section 11 Arcade paragraph) and `src/BidEuchre.Desktop/RulesPage.cs` if it carries its own copy
- Test: `tests/BidEuchre.Tests/CampaignArcadeTests.cs`

**Interfaces:**
- Consumes: `ArcadeResult.PackCards`, `CampaignArcade.IsPack`, `LegendaryKey` (Task 2).
- Produces: `CampaignArcade.PickPackCard(CampaignRun run, string playId, int index, long? expectedRevision = null) -> CampaignRun`.
  - Sigil: equivalent to today's `BeginArcadePrizeUse` with `FeatureId` set to the picked Sigil. It creates the `IsArcadePrize` offer `"<playId>:prize"` with the result's six targets, sets `UseSession(…, Sigil, key, false)`, and clears `Pending`.
  - Legendary: appends `5 − Jokers.Length` new `JokerState`s to the inventory and clears `Pending`, with no offer and no use session.

- [ ] **Step 1: Write failing tests.**
  - `PickSigilForcesUse`: picking index *i* gives `UseSession.FeatureId == PackCards[i]` and `Pending is null`, and `CampaignShop.FinishUse` throws before `UsePurchased`. After `UsePurchased(run, [six[0]])`, deck card `six[0]` has that Sigil and `Inventory.Sigils` is still empty.
  - `PickLegendaryFillsJokers`: from a seed whose pack contains Legendary, starting with 2 Jokers, picking it leaves 5 distinct Jokers that include the original 2, with `UseSession is null` and money unchanged. Starting with 4, exactly 1 is added.
  - `PickRejectsReplayAndStale`: `Reject` for index −1 and 3, a wrong `playId`, a stale `expectedRevision`, a second pick after the first, and a pick on a Tarot result.
  - `PendingPackBlocksJokerChanges`: with a pack pending, Joker sell and Joker buy are rejected (`Reject`).
  - `PickedSigilSurvivesRestart`: pick a Sigil, `RoundTrip`, and the same `UseSession` and targets are present. `FinishUse` is still rejected.
- [ ] **Step 2: Run** `--filter Arcade`. Expected: FAIL.
- [ ] **Step 3: Implement `PickPackCard`.** Reuse `RequireResult` and share the offer/session construction with `BeginArcadePrizeUse` through a private helper. Draw Legendary Jokers with `CampaignRandom` and save the new `RandomState`. Add any missing pending-result guard to Joker buy/sell.
- [ ] **Step 4: Update the rules text.** Sigil-tier wins give a Collector's Pack of three cards. You must pick one, and a Sigil must be applied to one of six offered cards (it cannot be kept). A rare Legendary instead fills empty Joker slots with random Jokers. List the eight card titles.
- [ ] **Step 5: Run** `--filter Arcade`, plus the full `tests/BidEuchre.Tests` runner. Expected: PASS.
- [ ] **Step 6: Commit** `feat: pick a collector pack card`.

### Task 4: Server endpoint and projection

**Files:**
- Modify: `src/BidEuchre.Server/CampaignEndpoints.cs:29` (add `"shop/arcade/pick"`)
- Modify: `src/BidEuchre.Server/WebCampaigns.cs:176-177` (`"shop/arcade/pick" => ["playId", "index"]`) and the action dispatch
- Modify: `src/BidEuchre.Server/CampaignProjection.cs:76-100` (`Arcade(run)`)
- Test: `tests/BidEuchre.Server.Tests/CampaignServerTests.cs`, `tests/server/campaign-api.test.cjs`

**Interfaces:**
- Consumes: `CampaignArcade.PickPackCard` (Task 3).
- Produces (wire): `arcade.result.pack: null | [{ key, title, name, legendary: bool }]` (title from `CollectorTitle`, `"Legendary"` for the Legendary), plus `arcade.result.pick: Capability`. For packs, `keep`/`use` are disabled. `NeedsDurableArcadeCommit` already covers `shop/arcade/*`.

- [ ] **Step 1: Write failing tests.** The API test plays Plinko until a Sigil win and asserts that `result.pack.length === 3` and `result.keep.enabled === false`. POST `shop/arcade/pick {playId, index: 0}` gives `shop.useSession.featureId === pack[0].key`. A repeated POST returns a 4xx with unchanged state. A body with extra fields (`featureId`) is rejected by the strict-field check.
- [ ] **Step 2: Run** `dotnet run --project tests/BidEuchre.Server.Tests -- --filter Arcade` and `node --test tests/server/campaign-api.test.cjs`. Expected: FAIL.
- [ ] **Step 3: Implement the endpoint, field list and projection.**
- [ ] **Step 4: Run the tests. Expected: PASS.**
- [ ] **Step 5: Commit** `feat: serve collector pack pick`.

### Task 5: Desktop pack reveal

**Files:**
- Create: `src/BidEuchre.Desktop/CollectorPackView.cs`, a control showing the wrapper, then three cards flipping face-up
- Modify: `src/BidEuchre.Desktop/ArcadeScreen.cs:13` (`ArcadeActions` gains `Action<string, int> Pick`) and lines ~180–195 (the result panel)
- Modify: `src/BidEuchre.Desktop/CampaignFlow.cs:265` (wire `Pick` to `Resolve(() => CampaignArcade.PickPackCard(Run!, id, index, revision), true)` for a Sigil; for the Legendary, resolve without opening the use screen and show a note naming the granted Jokers, from the inventory difference)
- Modify: the inventory/collection views that show a Sigil as a card (`CollectionPage.cs`, the inventory strip in `CampaignTableControls.cs` / `CampaignShopScreen.cs`) to use `PixelArt.CollectorImage` and "<Title> — <Sigil> Sigil"
- Test: `tests/BidEuchre.Desktop.Tests/CampaignArcadeUiTests.cs`, registered in `Program.cs`

**Interfaces:**
- Consumes: `ArcadeResult.PackCards`, `PickPackCard`, `CollectorTitle`, `PixelArt.CollectorImage`.
- Produces: named controls `CollectorPack`, `CollectorCard0..2` (buttons, `AutomationProperties.Name = "<Title> — <Sigil> Sigil"` or `"Legendary — 5 random Jokers"`), and `CollectorLegendaryNote`.

- [ ] **Step 1: Write failing UI tests.**
  - `PackShowsThreeCardsNoKeep`: on a pack result there is no `ArcadeKeep` control, and three `CollectorCard*` buttons have the expected names.
  - `PickingSigilOpensForcedUse`: clicking `CollectorCard0` shows the six-card use screen, and its Done button is disabled until a card is chosen.
  - `ReducedMotionShowsCardsFaceUp`: with motion off, cards are face-up and clickable immediately.
  - `LegendaryPickShowsJokers`: on a Legendary seed, clicking it returns to the shop with `CollectorLegendaryNote` listing the new Jokers' names.
  - `InventorySigilUsesCollectorArt`: a stored Sigil in inventory renders the collector image and title.
- [ ] **Step 2: Run** `dotnet run --project tests/BidEuchre.Desktop.Tests -- --filter Arcade`. Expected: FAIL.
- [ ] **Step 3: Implement `CollectorPackView`.** The wrapper shakes briefly, then the cards flip one by one with `PixelMotion`. The Legendary card gets a gold glow border. Keyboard: Tab between cards, Enter picks. Then implement the result-panel branch and the flow wiring.
- [ ] **Step 4: Run the tests. Expected: PASS.**
- [ ] **Step 5: Commit** `feat: desktop collector pack reveal`.

### Task 6: Browser pack reveal

**Files:**
- Modify: `src/BidEuchre.Server/wwwroot/campaign-arcade.js:48-56` (the result panel)
- Modify: `src/BidEuchre.Server/wwwroot/campaign.js` (the `arcade-pick` action posting `shop/arcade/pick`)
- Modify: `src/BidEuchre.Server/wwwroot/campaign.css` (`.collector-pack`, `.collector-card`, flip animation, `.collector-card.legendary` glow, `prefers-reduced-motion` override)
- Modify: `src/BidEuchre.Server/wwwroot/campaign-screens.js:24` (the inventory Sigil card uses `collectorArt` and the title)
- Test: `tests/web/campaign-arcade.test.cjs`, `tests/server/campaign-browser.test.cjs`

**Interfaces:**
- Consumes: wire `result.pack`, `result.pick` (Task 4), `CampaignCards.collectorArt` (Task 1).
- Produces: buttons `data-action="arcade-pick" data-play-id data-index`, with an `aria-label` matching the desktop names.

- [ ] **Step 1: Write failing tests.**
  - `pack result renders three pick buttons and no keep` (web unit).
  - `pack reveal does not replay on unchanged poll`: re-rendering with an identical state keeps the `revealed` class and the focused card. The flip animation class is applied only on the first render of a given `playId`, tracked in a module-level `Set`.
  - `pick sigil opens use screen; legendary returns to shop with joker note` (browser test in `campaign-browser.test.cjs`).
- [ ] **Step 2: Run** `node --test tests/web/campaign-arcade.test.cjs` and the browser test. Expected: FAIL.
- [ ] **Step 3: Implement the render, action and CSS.** After a Legendary pick, show the note listing Jokers present in the new state but not the previous one.
- [ ] **Step 4: Run the tests. Expected: PASS.**
- [ ] **Step 5: Commit** `feat: browser collector pack reveal`.

### Task 7: Full verification

- [ ] **Step 1: Run** `./test.sh`. Expected: all suites pass, with no new SKIPs beyond the existing optional engines.
- [ ] **Step 2: Launch the desktop app** (`./run.sh`) and the web app (`./run-web.sh`). In each, use the developer console to reach a shop with money, win a Sigil-tier Plinko or roulette result, open the pack, pick a Sigil, and apply it. Repeat with a Legendary (seed search, or temporarily force it through the developer console if one exists, without committing that change). Check reduced motion.
- [ ] **Step 3: Commit** any test fixture fixes. Report anything that could not be checked.
