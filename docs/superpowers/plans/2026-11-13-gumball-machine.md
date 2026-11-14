# Gumball Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Planet bid unlocks become gumballs, sold as a blind $3 pull from an animated gumball machine in the campaign shop.

**Architecture:** The rename is mechanical across Core, App and Desktop, with the save reader accepting the old `planet:` ids. The shop holds a Gumball offer with an empty feature id; `CampaignShop.Buy` draws the bid when paid and records it on the sold offer. The machine is a code-drawn Avalonia control in its own file whose `Dispense` task plays the animation with `Tween.Run`.

**Tech Stack:** .NET 10, Avalonia 12.1, the repo's own test executables (`./test.sh`).

**Spec:** `docs/superpowers/specs/2026-11-13-gumball-machine-design.md`

## Global Constraints

- One pull per shop visit, $3.
- No new art files; no sound.
- The bid must not exist anywhere before the player pays.
- The purchase is saved before the animation starts.
- With motion off the animation is skipped.
- Runs saved with `planet:<name>` ids still load.

## Review Focus

- Astronomer used before the pull in the same visit: the pull never returns that bid.
- All eight bids owned: no Gumball offer is rolled; a stale offer refuses with "All bidding options are unlocked."
- A save written before this change with a Planet offer still open in the shop: the offer becomes a blind pull.
- Not enough money: `Pull` is disabled and nothing is drawn from the random state.
- The shop is rebuilt or left while the animation is running: no exception, purchase kept.

---

### Task 1: Rename Planet to Gumball

**Files:**
- Modify: `src/BidEuchre.Core/{FeatureCatalog,CampaignInventory,CampaignCommands,GameEngine,GameEngine.Campaign,TarotEffects,CampaignEffects}.cs`
- Modify: `src/BidEuchre.App/{CampaignJson,CampaignShop}.cs`, `src/BidEuchre.Desktop/{CampaignFlow,CampaignShopScreen}.cs`, `src/BidEuchre.Desktop/BidEuchre.Desktop.csproj`
- Modify: `tests/BidEuchre.Tests/*.cs`, `README.md`
- Test: `tests/BidEuchre.Tests/CampaignSaveTests.cs`

**Interfaces:**
- Produces: `FeatureCategory.Gumball`, `enum GumballId` (same members, same order), catalogue ids `gumball:<name>`.

- [ ] **Step 1:** Add `CampaignSaveTests.LegacyPlanetIds`: a saved run whose `Unlocks` is `["planet:chaos"]` loads with `GumballId.Chaos`, and saving it writes `"gumball:chaos"`. Register it in `Program.cs`.
- [ ] **Step 2:** Run `./test.sh`; expect a compile failure on `GumballId`.
- [ ] **Step 3:** Rename the enum, the category and the ids. In `FeatureConverter.Read`, treat a `planet:` prefix as `gumball:`. Astronomer text: "Unlock a random Gumball bid." Remove the planet `AvaloniaResource` line. Update README wording.
- [ ] **Step 4:** Run `./test.sh`; expect all passing.

### Task 2: Blind pull

**Files:**
- Modify: `src/BidEuchre.App/CampaignShop.cs`
- Test: `tests/BidEuchre.Tests/CampaignShopTests.cs`

**Interfaces:**
- Produces: an unsold Gumball `ShopOffer` has `FeatureId == ""`; once sold, `FeatureId` is the `GumballId` name that came out.

- [ ] **Step 1:** Add `CampaignShopTests.GumballPull`:
  - a fresh shop's Gumball offer has `FeatureId == ""` and `Price == 3`;
  - buying it leaves one unlock, money down 3, and the sold offer's `FeatureId` equal to that unlock's name;
  - with seven bids owned the pull always yields the eighth;
  - with eight owned, `Open` rolls no Gumball offer and `Buy` on a hand-made one throws;
  - with $2, `Buy` throws and `RandomState` is unchanged;
  - an offer carrying an old non-empty `FeatureId` is still drawn blind.
- [ ] **Step 2:** Run `./test.sh`; expect `GumballPull` to fail.
- [ ] **Step 3:** In `Open`, add the Gumball offer directly (empty id, $3) when fewer than eight bids are owned. In `Buy`, draw from the unowned bids with `CampaignRandom`, store the new `RandomState`, and write the id onto the sold offer.
- [ ] **Step 4:** Run `./test.sh`; expect all passing.

### Task 3: Machine, animation and shop wiring

**Files:**
- Create: `src/BidEuchre.Desktop/GumballMachine.cs`
- Modify: `src/BidEuchre.Desktop/CampaignShopScreen.cs`, `src/BidEuchre.Desktop/CampaignFlow.cs`
- Test: `tests/BidEuchre.Desktop.Tests/Program.cs`

**Interfaces:**
- Produces:
  - `GumballMachine.Colour(GumballId) : IBrush`
  - `GumballMachine.Ball(GumballId, double size) : Control`
  - `new GumballMachine(IEnumerable<GumballId> inGlobe, string tag)` — a `Border` the size of a shop card
  - `Task GumballMachine.Dispense(GumballId result, string name)` — coin, crank, jostle, roll out, reveal
  - `ShopActions.Pull : Func<string, GumballId?>` — buys and saves; null when refused

- [ ] **Step 1:** Add a desktop test: open a shop with $30, click the machine then `Pull`, and expect one unlock, a `CampaignNote` containing "gumball unlocked", and the machine reading SOLD. Run with motion off so it is synchronous.
- [ ] **Step 2:** Run `./test.sh`; expect it to fail to find the machine.
- [ ] **Step 3:** Build `GumballMachine` and wire it into the shop: machine in the second row; detail panel "GUMBALL · Mystery bid" with `Pull $3`; on pull call `actions.Pull`, disable the screen, await `Dispense` when motion is on, then `actions.Refresh`. `CampaignFlow.Pull` persists the purchase and sets the note without redrawing. Replace the `PLANET BIDS` label with `GUMBALLS n/8` and owned balls.
- [ ] **Step 4:** Run `./test.sh`; expect all passing.
- [ ] **Step 5:** Run the app, pull a gumball, and check the animation and the SOLD and EMPTY states by eye.
