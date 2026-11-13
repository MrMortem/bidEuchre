# Campaign Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved 77-feature campaign with a persistent shared deck, five-offer shop, permanent bidding unlocks, consumable Tarot, and deterministic card/Joker effects.

**Architecture:** Extend the existing authoritative `GameEngine` behind an optional campaign context; retain ordinary Table actions, views, and BEUCI payloads. Use stable physical card identities, a typed effect ledger, explicit scoring buckets, and versioned campaign checkpoints. Integrate the new behavior with the existing paced session and native Avalonia interface.

**Tech Stack:** C#/.NET 10, existing Avalonia desktop project, System.Text.Json, existing executable test runner; no new runtime package dependency.

**Spec:** [Approved campaign feature design](../specs/2026-11-12-campaign-features-design.md).

## Global Constraints

- All four players share one 24-card deck, initially 9 through Ace in each suit.
- Each card has exactly one enhancement slot and one Sigil slot; changing suit preserves both.
- Permanent modifications persist for the run; Sticky Fingers copies expire at match end.
- Only tricks won determine whether a bid succeeds. Bonus points count even on a failed bid.
- Both teams start every match at zero; thresholds are `±(24 + 4 * (Ante - 1))`, including endless play.
- Eight hands normally end a match; tied scores continue until a fully scored additional hand breaks the tie.
- Panic Button costs 8 team points, grants opponents 0, and consumes a hand.
- Start with $0. Win base rewards are $5 Small, $6 Medium, $8 Boss; margin bonuses are $2 for 1–4, $4 for 5–9, $6 for 10+.
- Losses grant neither base reward nor victory bonus. Keep card/Joker money; apply interest after match income: `min(5, floor(money / 5))`.
- Shop: two $4 Jokers, one $3 Planet, one $3 Tarot, one $3 Sigil; no paid rerolls or replacement stock.
- Five active Jokers, no duplicate owned Jokers, $2 Joker sale value; two Tarot slots; full inventories block purchases.
- All eight Planets permanently unlock human bidding options; bots retain their baseline Partners Best/Alone bids.
- Tarot is usable after the deal and before the human bidding action; Sigil purchases immediately target any shared-deck card.
- Last Laugh applies to the sixth/final trick of every hand. Opponents receive tracked card rewards but do not buy/use items initially.
- Preserve the campaign's three-match Ante structure, lives, one-time victory, and abandoned-match forfeit policy.
- Free-play Table behavior, private information, protocol fixtures, and legacy external-engine behavior remain unchanged by default.
- No web campaign feature parity, new boss modifiers, mid-hand resume, rarity system, or external-engine campaign support in this delivery.

## Review Focus

1. Two cards transformed into the same face must stay independently playable, selectable, and animatable (Tasks 1, 4, 20).
2. A stale/double-clicked action must not spend twice, reroll randomness, target a later hand, or become an illegal-play penalty (Tasks 8, 9, 19).
3. Interrupted writes and crash recovery must retain a coherent last successful checkpoint without replaying rewards or consumed items (Tasks 3, 19).
4. Public deck inspection must not correlate opaque opponent hand IDs with known faces; partial reveals must expose exactly their authorized cards (Tasks 4, 18, 20).
5. An exhausted choice set—full inventories, all Planets unlocked, forced bid under Vow, or too many Stinky cards—must yield a clear legal outcome rather than a stuck bot loop (Tasks 8, 9, 12, 17).

---

## Execution organization and baseline

The subsystems are coupled through card identity and effect events. Keep one integration plan, divided into six independently verifiable stages rather than giving separate implementers incompatible card/scoring designs. Work in dependency order; do not expose an unimplemented catalogue item as purchasable. During intermediate stages use an internal implemented-feature allowlist; remove it when all 77 entries pass coverage.

Before execution, use `superpowers:using-git-worktrees` if isolation is needed. No worktree, dependency installation, or game implementation is part of writing this plan. Inspect existing changes and preserve them. Do not delete or edit the user's actual campaign save during tests.

The test runner already supports `--filter`; use test names prefixed `Campaign`. Register every new test method in `tests/BidEuchre.Tests/Program.cs` using the existing `Run(...)` wrapper for synchronous tests. There is no xUnit/NUnit package.

Commands, from the repository root:

```bash
dotnet run --project tests/BidEuchre.Tests/BidEuchre.Tests.csproj -- --filter Campaign
dotnet build BidEuchre.slnx --nologo -m:1
./test.sh
```

If `dotnet` is not on PATH, use the existing discovery in `engines/hybrid/scripts/dotnet.sh` (also used by `test.sh`) and the resolved executable; do not download another SDK. A filtered run succeeds only with a nonzero test count, `N/N tests passed`, and exit 0. Compilation failure is acceptable for the initial red test of a new API; follow-up tests should fail on a behavioral assertion. Run the unfiltered runner at stage boundaries and `./test.sh` for the final regression gate. Record optional prerequisite skips accurately.

Each task ends with a scoped commit during execution. Stage only that task's paths; never use `git add .`. Suggested commit messages are specified below. Do not commit product changes while merely preparing this plan.

## File and interface map

Existing anchors verified while planning:

- `Core/Domain.cs`: `Card` is a face value; `MatchOptions` has only target/starting scores.
- `Core/GameEngine.cs`: stores `List<Card>[]`; has `PlaceBid`, `ExchangeCard`, `PlayCard`, `ScoreCompletedHand`, `DetermineGameWinner`, and `CreateView`.
- `Core/GameEngine.Fixtures.cs`: ordinary fixture validation requires 24 distinct faces. Keep that requirement for ordinary fixtures.
- `App/CampaignRun.cs`: immutable run state, first-to-12 payout, lives and transitions.
- `App/CampaignStore.cs`: temp-file-then-move JSON persistence.
- `App/GameSession.cs`: `_gate`, deal completion barrier, bot pacing, terminal observation, and legacy action dispatch.
- `Desktop/CampaignFlow.cs`: saves before matches, records results once, handles abandoned matches and delayed result presentation.
- `Desktop/MainWindow.cs`: hand/trick animation keys currently use `card.Code`; controls send face-only actions.

All paths below are repository-relative under `src/BidEuchre.*` or `tests/BidEuchre.Tests`. Proposed files are new; existing files are modified only at the named methods/types. Keep new files focused:

| Files | Responsibility |
| --- | --- |
| `Core/CampaignCards.cs`, `CampaignInventory.cs`, `FeatureCatalog.cs` | Stable IDs, card state, inventory and 77 static definitions |
| `Core/CampaignRandom.cs`, `CampaignContext.cs` | Explicit RNG state and authoritative campaign resources |
| `Core/CampaignCommands.cs`, `CampaignGameView.cs` | Typed commands and privacy-filtered campaign views |
| `Core/GameEngine.Campaign.cs` | Campaign entry points and lifecycle hooks in the existing partial engine |
| `Core/CampaignMatchRules.cs`, `CampaignScoring.cs` | End conditions and contract/bonus settlement |
| `Core/CampaignEffects.cs`, `EnhancementEffects.cs`, `SigilEffects.cs` | Ordered events, bounded replay, card effect handlers |
| `Core/CampaignCardRules.cs`, `CampaignDeal.cs` | Effective suits/ranks, legal plays, constrained deals |
| `Core/CampaignBidding.cs`, `PokerEvaluator.cs` | Unlocked auctions and special contracts |
| `Core/JokerScoringEffects.cs`, `JokerEconomyEffects.cs`, `JokerStateEffects.cs` | Joker handlers grouped by event responsibility |
| `Core/TarotEffects.cs` | Consumable validation and mutations |
| `App/CampaignSave.cs`, `CampaignShop.cs`, `CampaignCoordinator.cs` | Versioned save, pure shop transactions, checkpoints |
| `App/CampaignBotPolicy.cs` | Campaign-only built-in legal decisions |
| `Desktop/CampaignShopScreen.cs`, `CampaignDeckPicker.cs`, `CampaignTableControls.cs` | Native shop, deck inspection/targeting, bidding and inventory UI |
| `tests/.../Campaign*Tests.cs`, `CampaignTestDriver.cs` | Deterministic behavior and integration tests |

### Shared contract decisions

Use these names across tasks; define the types in their owning task before callers use them:

```csharp
// Core, Task 1. Card remains the existing face-only value type.
public readonly record struct CardId(int Value); // fixed run identities 0..23
public sealed record CardState(CardId Id, Card Face, EnhancementId? Enhancement, SigilId? Sigil);
public sealed record CardOverlay(CardId Id, Card Face, EnhancementId? Enhancement, SigilId? Sigil);
public sealed record JokerState(JokerId Id, int Value = 0);
public sealed record CampaignInventory(
    IReadOnlyList<JokerState> Jokers, IReadOnlyList<TarotId> Tarot,
    IReadOnlySet<PlanetId> Unlocks);
// Use stable explicit string IDs in JSON/catalogue; enum names are not save IDs.

// Core, Tasks 1/4. Snapshot contains persistent campaign resources, not live hands.
public sealed record CampaignSnapshot(
    long Revision, IReadOnlyList<CardState> Deck, CampaignInventory Inventory,
    int[] Wallets, ulong RandomState, ContractKey? PreviousContract);
public sealed record ContractKey(ContractMode Mode, Suit? Trump);
public sealed class CampaignContext {
    public CampaignContext(CampaignSnapshot snapshot);
    public CampaignSnapshot Snapshot();
}

// Core, Task 4. A hidden opponent hand has counts, not physical card IDs.
public sealed record CampaignCardView(CardId Id, Card Face,
    EnhancementId? Enhancement, SigilId? Sigil);
public sealed record CampaignTrickPlay(int Seat, CampaignCardView Card, Suit EffectiveSuit);
public sealed record CampaignPlayerView(int Seat, int CardCount,
    IReadOnlyList<CampaignCardView> VisibleCards, bool IsSittingOut);

// Core, Task 4. Base class owns concrete records introduced by later tasks.
public abstract record CampaignCommand;
// Add optional trailing constructor argument to existing GameEngine:
// GameEngine(names, randomSeed = null, options = null, campaign = null)
// campaign has type CampaignContext?. Null selects the existing standard rules.
// Public engine members added:
// void ExecuteCampaignAction(int seat, CampaignCommand command);
// CampaignGameView CreateCampaignView(int? viewerSeat);
// CampaignSnapshot ExportCampaignSnapshot();

// App, Task 3. Keep CampaignStore.Load/Save accepting CampaignRun.
public sealed record CampaignSave(int Version, CampaignRun Run);
// Extend CampaignRun with init properties, not a reordered positional constructor:
// CampaignSnapshot Resources; ShopState? Shop; long LastAppliedRevision;
// Set Resources from a fresh default factory, never a shared mutable singleton.
```

Use immutable/deep-copied resource snapshots at boundaries. Opaque facedown placeholders must not carry permanent `CardId`s: the human knows the full shared deck. Stable IDs belong in the human's own hand and explicitly revealed opponent card views only. For legacy views/protocol, keep `Card`, `CardPlay`, and action notation as they are; omit campaign-only view data when absent.

After Task 3, `CampaignRun.Money` mirrors `Resources.Wallets[0]`: the resource wallet is authoritative for purchases and game income, and every run-level transaction synchronizes the mirror. Migration initializes wallet 0 from legacy Money. Persist collection interfaces through concrete array/list DTOs and explicit stable-ID converters; do not assume JSON can directly construct `IReadOnlySet`. No save or UI may add an entire wallet balance as though it were an income delta.

## Stage A — identities, match rules, and state

### Task 1: Stable cards, inventory, and deterministic randomness

**Files:** Create `src/BidEuchre.Core/{CampaignCards,CampaignInventory,CampaignContext,CampaignRandom,FeatureCatalog}.cs`; create `tests/BidEuchre.Tests/CampaignModelTests.cs`; register tests in `Program.cs`.

**Interfaces:** Produces the shared Task 1 contracts above; `CampaignCards.CreateDeck() -> IReadOnlyList<CardState>`; `CampaignCards.ChangeSuit(CardState card, Suit suit) -> CardState`; `CampaignRandom(ulong state)`, `NextInt(int exclusiveMax) -> int`, `State -> ulong`. Catalogue exposes `All -> IReadOnlyList<FeatureDefinition>` with category, stable ID, name, description and price; all five ID enums have explicit mappings.

- [x] **Step 1:** Add `CampaignPhysicalIdentity`, `CampaignSlotReplacement`, `CampaignRandomRoundTrip`, and `CampaignCatalogueCounts`. Include these exact assertions:
  ```csharp
  Assert.Equal(24, CampaignCards.CreateDeck().Select(c => c.Id).Distinct().Count());
  Assert.Equal(77, FeatureCatalog.All.Count);
  Assert.Equal(10, Enum.GetValues<EnhancementId>().Length);
  Assert.Equal(12, Enum.GetValues<SigilId>().Length);
  ```
  Transform two cards to the same face and assert IDs remain different; change suit on an enhanced/Sigil card and assert both slots unchanged. Resume RNG from saved state and assert its next 20 bounded outcomes match uninterrupted generation.
- [x] **Step 2:** Run the filtered campaign command; expect the missing new model/API to fail compilation.
- [x] **Step 3:** Implement definitions, cloning, validation and a documented SplitMix64 stream with rejection sampling for bounded integers. Reject bounds <=0. Catalogue must include Glassblower and Spring Loaded, and Last Laugh's approved every-hand wording. Resource snapshots validate four nonnegative wallets, unique IDs 0..23, 24 valid faces, valid inventory IDs/capacities, and <=18 Stinky cards.
- [x] **Step 4:** Run campaign tests; expect nonzero `N/N` passing. Verify category counts 10/31/8/12/16 and deterministic IDs independent of face changes.
- [x] **Step 5:** Commit only these files: `feat: add persistent campaign card identities and catalogue`.

### Task 2: Match endings and revised campaign rewards

**Files:** Create `Core/CampaignMatchRules.cs`; modify `Core/Domain.cs` (`MatchOptions`), `Core/GameEngine.cs` (`DetermineGameWinner`, completion), `App/CampaignRun.cs`, `Desktop/CampaignFlow.cs` (`MatchCompleted` caller); modify `CampaignRunTests.cs`; create `CampaignMatchTests.cs`.

**Interfaces:** `CampaignMatchRules.Winner(int team0, int team1, int completedHands, int threshold) -> int?`; append nullable `NegativeThreshold` and `HandLimit` init properties to `MatchOptions` so existing constructor calls remain valid. `CampaignRun.NextMatch()` returns target `24+4*(Ante-1)`, negative threshold its negation, hand limit 8, and zero starting scores. Replace `ApplyResult(bool,int)` with `ApplyResult(bool won, int playerScore, int opponentScore, int matchEndIncome = 0)`; update current `CampaignFlow.MatchCompleted` caller and tests in the same task. Outcome gains `BaseReward`, `MarginBonus`, `JokerIncome` init properties; retain `Payout` as base plus margin, avoiding broken result rendering.

- [x] **Step 1:** Add tests asserting `Winner(24,24,3,24)==null`, `Winner(-24,-20,2,24)==1`, `Winner(4,3,8,24)==0`, `Winner(4,4,8,24)==null`, and `Winner(5,4,9,24)==0`. Test both teams crossing thresholds and higher-score selection. Test Ante 6 threshold 44. Replace old first-to-12/handicap campaign expectations, retaining ordinary first-to-40 tests.
- [x] **Step 2:** Run campaign tests; expect old campaign rules to fail those assertions.
- [x] **Step 3:** Implement the pure end condition, option validation and normal-hand completion integration. Reward order: committed wallet -> match-end income -> win base/margin -> interest. For Small win 24–20 with $0 assert payout 7, interest 1, money 8; for Medium win 28–23 assert payout 10, interest 2, money 12; loss with $23 asserts payout 0, interest 4, total 27. Cover margins 1/4/5/9/10, all three bases, life losses, victory, and double result rejection. Forfeit keeps its old public method and supplies no match-end income.
- [x] **Step 4:** Run filtered tests and unfiltered test executable; all pass. Reject incoherent threshold/hand-limit options while defaults still reproduce ordinary play.
- [x] **Step 5:** Commit scoped paths: `feat: add campaign hand limits thresholds and victory payouts`.

### Task 3: Versioned saves and legacy migration

**Files:** Create `App/CampaignSave.cs`; modify `App/{CampaignRun,CampaignStore}.cs`; extend `CampaignStoreTests.cs` and `CampaignModelTests.cs`.

**Interfaces:** `CampaignRun` resource properties from the shared contract; `CampaignSave.Version = 2`; preserve `CampaignStore.Load(string) -> CampaignRun?`, `Save(CampaignRun,string) -> bool`, and `Delete(string) -> bool`. Define the `ShopOffer`/`ShopState` records specified in Task 8 here so the save schema compiles before shop behavior exists. Load accepts old bare run JSON and new version-2 envelope; unknown newer versions return null without overwriting the file.

- [x] **Step 1:** Add `CampaignLegacySaveMigration`, `CampaignResourceSaveRoundTrip`, and `CampaignInvalidResourceSave`. Assert a bare legacy run preserves money/lives/match/phase while acquiring 24 standard IDs, no Jokers/Tarot/unlocks; new saves retain modified cards, RNG, revision and inventory. Test duplicate IDs, malformed enums, >18 Stinky, wrong wallet count, truncated JSON and a future version.
- [x] **Step 2:** Run campaign tests; expect round-trip resource loss or missing envelope API to fail.
- [x] **Step 3:** Implement validated migration and temp-then-move persistence. Do not flatten equal faces into one card or use process-random hash codes as IDs. Keep missing/corrupt-save behavior, dispose streams, and preserve the prior valid file if replacement fails. Isolate test files in unique temp folders.
- [x] **Step 4:** Test failed replacement by using an unwritable/directory target and verify the original file remains readable. Run campaign tests; expect all pass, without touching the actual profile save.
- [x] **Step 5:** Commit: `feat: version campaign saves and migrate legacy runs`.

### Task 4: Identity-aware engine actions and private views

**Files:** Create `Core/{CampaignCommands,CampaignGameView,GameEngine.Campaign}.cs`, `tests/.../CampaignTestDriver.cs`, `CampaignEngineTests.cs`; modify `Core/{GameEngine,GameEngine.Fixtures,GameView}.cs`.

**Interfaces:** Add optional `CampaignContext? campaign = null` constructor argument and the three engine methods in the shared contract. Define `CampaignCommand.Play(CardId Id, Suit? EffectiveSuit = null)` and `CampaignCommand.Exchange(CardId Id)`. `CampaignGameView` contains `Revision`, `DealSerial`, `ViewerSeat`, privacy-filtered player/card views, legal command choices, pending choice, scores, completed hand count and public events. A visible card has physical ID, resolved face and slots; a hidden card is a count/opaque placeholder. Engine command dispatch never resolves a campaign card by face string.

- [x] **Step 1:** Add `CampaignDuplicateFacesPlayById` and `CampaignViewPrivacy`: two identical faces in one hand remain separate legal actions and playing one removes only its ID. Opponent unrevealed views contain no permanent IDs/faces. Ordinary `CreateView` and protocol fixture tests remain byte/semantically unchanged. Create deterministic test-driver helpers that apply fixture partitions and expose legal campaign commands; do not weaken legacy fixture uniqueness validation.
- [x] **Step 2:** Run campaign tests; expect missing engine command/view APIs to fail.
- [x] **Step 3:** Refactor shared engine card storage to IDs plus a resolver for both profiles, projecting ordinary `Card` values at legacy API boundaries. Default profile has a standard one-to-one face map. Reject legacy face-only play/exchange calls in campaign mode rather than selecting the wrong duplicate. Shared engine still owns turns, trick order, scores, and hand progression. Extend the existing friend-test fixture support with a separate campaign fixture partition validator that checks IDs, allowing duplicate faces.
- [x] **Step 4:** Run campaign and ordinary engine/fixture/protocol tests; expect all pass. Poll a view repeatedly and assert no state revision or RNG changes.
- [x] **Step 5:** Commit: `feat: add identity-aware campaign commands and private views`.

## Stage B — score ledger and persistent purchases

### Task 5: Contract versus effect score settlement

**Files:** Create `Core/CampaignScoring.cs`, `tests/.../CampaignScoringTests.cs`; modify `Core/GameEngine.Campaign.cs` and ordinary engine completion hooks.

**Interfaces:** `ScoreEntry(int Team, int Amount, ScoreKind Kind, string Source, int? Trick)` with kinds `BaseTrick`, `CardEffect`, `JokerEffect`, `ModifierBonus`, `ContractPenalty`; `CampaignScoring.Settle(Contract contract, int biddingTeam, int[] tricks, IReadOnlyList<ScoreEntry> entries, bool bubble, bool duelist, bool chaos) -> HandSettlement`; settlement exposes team deltas, success, entries and destroyed Joker IDs. Existing base `HandResult` stays valid for played contracts.

- [x] **Step 1:** Add `CampaignFailedBidKeepsBonus` asserting bid 4/tricks 3/+5 effects yields +1; `CampaignTrickMultiplierFailure` asserts the approved Vow examples -1 and -3; successful bid 4 with one doubled trick is +5; Alone sweep with all doubled base trick entries is +30. Add defender scoring assertions and independent negative Cursed entries.
- [x] **Step 2:** Run campaign tests; expect current aggregate scoring to fail those examples.
- [x] **Step 3:** Implement settlement: remove bidder's raw base trick count on failure, then contract penalty; replace solo base count with ±12/±24; retain effect/modifier entries. Bubble removes contract and Duelist failure penalty only; Chaos doubles both teams' final point deltas after replacements. Do not let points alter trick counts. Complete every hand through one settlement boundary before evaluating winner.
- [x] **Step 4:** Test two Glass multipliers produce x4 card-effect points, not base/contract/money; Bubble + failed Duelist + Chaos preserves other negative effects; no-Bonus default scoring matches ordinary results. Run campaign tests.
- [x] **Step 5:** Commit: `feat: separate campaign contract and effect scoring`.

### Task 6: Ordered effects and bounded replay

**Files:** Create `Core/CampaignEffects.cs`, `tests/.../CampaignTriggerTests.cs`; modify `GameEngine.Campaign.cs`.

**Interfaces:** `EffectEvent` holds event ID, lifecycle kind, actor, physical card, trick/hand/match identity and provenance; `CampaignEffects.Resolve(EffectEvent evt, CampaignContext context) -> EffectResolution`. Resolution contains score entries, wallet deltas, card/inventory mutations and public messages. Lifecycle kinds include hand setup, contract selected, card played, trick won, hand settled and match settled. Register handlers once; consumers do not rerun them when rendering.

- [x] **Step 1:** Add `CampaignReplayIsBounded`, `CampaignTriggersHaveStableOrder`, and `CampaignReplayDoesNotReplayPhysicalPlay`. With Frog, Bouncy, Lucky Duck and Deep Digger handlers registered, assert termination, one actual play/removal, stable event ordering and no second permanent mutation/return/copy. Scripted rolls verify independent Lucky point/money trials.
- [x] **Step 2:** Run tests; expect missing dispatcher/replay policy to fail.
- [x] **Step 3:** Resolve enhancement -> Sigil -> Jokers in slot order. Frog adds one eligible effect activation; Bouncy replays earlier on-play effects but suppresses replay generators inside replay. Lucky Duck/Deep Digger add matching activations without mutually invoking themselves. Schedule winner-dependent Deep Digger repeats after winner determination. Give every effect event and replay a unique provenance, separate from physical play identity.
- [x] **Step 4:** Assert repeated view creation and repeated terminal observation do not dispatch effects. Script a rejected command and assert no RNG advance. Run trigger/scoring tests.
- [x] **Step 5:** Commit: `feat: resolve deterministic campaign effects with bounded retriggers`.

### Task 7: Basic enhancement effects

**Files:** Create `Core/EnhancementEffects.cs`, `tests/.../CampaignEnhancementTests.cs`; modify event registration.

**Interfaces:** Enhancement handlers consume Task 6 events and return the same `EffectResolution`; no new parallel scoring API. This task implements Lucky, Golden, Cursed, Stone, Emerald, Silver and Glass. Bouncy remains the registered replay handler; Wild and Stinky complete in Task 12.

- [x] **Step 1:** Add table-driven `CampaignEnhancementRewards`: forced Lucky success +4/$3, Stone +1 to playing team, Cursed -1 to winning team, each Golden +$1 to winner, Silver $5 to losing side, Emerald $10 only for last play of trick six. Assert Golden stacks per card and Glass does not affect wallets.
- [x] **Step 2:** Run tests; expect absent handlers to yield wrong rewards.
- [x] **Step 3:** Implement per-card attribution. Allocate a losing-team Silver wallet award to the losing side's last active player in that trick as a deterministic initial policy; if it is the human's team, credit the human spendable wallet. Apply team monetary benefits consistently and document recipient in events. Use the scoring kind boundary for Glass, including negative card points.
- [x] **Step 4:** Run campaign tests; verify the same effect benefits opponents when they play/win the shared card. Last Laugh extra activations must not yet be claimed implemented.
- [x] **Step 5:** Commit: `feat: add campaign enhancement rewards and trick modifiers`.

### Task 8: Shop stock and transactional purchases

**Files:** Create `App/CampaignShop.cs`, `tests/.../CampaignShopTests.cs`; extend `CampaignRun.Resources` and save serialization.

**Interfaces:** `ShopOffer(string Id, FeatureCategory Category, string FeatureId, int Price, bool Sold)`; `ShopState(int MatchJustFinished, IReadOnlyList<ShopOffer> Offers)`; `CampaignShop.Open(CampaignRun run) -> CampaignRun`; `Buy(CampaignRun run, string offerId, CardId? sigilTarget = null) -> CampaignRun`; `SellJoker(CampaignRun run, JokerId id) -> CampaignRun`; `DiscardTarot(CampaignRun run, int slot) -> CampaignRun`. Invalid transactions throw `GameRuleException` with a user-readable reason and leave input unchanged.

- [x] **Step 1:** Add `CampaignShopFiveOffers`, `CampaignShopPurchases`, `CampaignShopExhaustedPools`. Assert two distinct $4 Jokers and $3 Planet/Tarot/Sigil; exclusions for owned Jokers/unlocked Planets; all-unlocked Planet slot sold out; no replacement after purchase; reopening round-tripped save preserves offers/RNG. Test exact funds and $1 short, fifth/sixth Joker, second/third Tarot, $2 sale, discard $0.
- [x] **Step 2:** Run tests; expect missing shop API or current empty-shop behavior to fail.
- [x] **Step 3:** Implement pure copy-on-write transactions and uniform rolls over eligible implemented features. Buy Sigil only with a validated target; cancel selects no transaction. Apply replacement to permanent card state only. Planet unlocks immediately. Repeated purchase of a sold offer throws before debit or RNG advance. Do not overwrite newly acquired state from an old screen snapshot.
- [x] **Step 4:** Assert a full inventory and canceled Sigil choice leave wallet, deck, revision and RNG unchanged. Save/reload offers and repeat purchase to prove no double debit. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add persistent five-offer campaign shop transactions`.

### Task 9: Tarot consumables and valid targets

**Files:** Create `Core/TarotEffects.cs`, `tests/.../CampaignTarotTests.cs`; extend commands, legal views and event mutation validation.

**Interfaces:** `CampaignCommand.UseTarot(int Slot, IReadOnlyList<CardId> Targets)` and `CampaignCommand.EndTarotWindow()`; `TarotEffects.LegalTargets(TarotId id, CampaignGameView view) -> IReadOnlyList<CardId>`. The engine validates phase/seat/slot/targets before consuming or rolling. Tarot use does not end the human bidding turn.

- [x] **Step 1:** Add `CampaignTarotBeforeBidOnly`, `CampaignTarotTransformations`, `CampaignTarotCreationAndMoney`. Assert all four suit changes preserve slots; the nine enhancement Tarot mappings match the spec; Devilish Deal uses three distinct held IDs, Gambler two distinct random held IDs; Astronomer unlocks only locked Planets; Tax Collector gains `min(money,20)`.
- [x] **Step 2:** Run tests; expect missing commands/effects to fail.
- [x] **Step 3:** Implement all 16 handlers. Tarot is available once the human's deal is complete and before that human submits their bid/pass/Panic action; human may use both stored Tarot in that window. One Up/Cheater choices precede it. Use current hand IDs, not the full deck, for targeted Tarot. Disable Astronomer if every Planet is unlocked, retaining the card. Generated Tarot at full capacity is skipped with a log message. Reject a mutation exceeding eighteen Stinky cards before consuming anything.
- [x] **Step 4:** Test out-of-turn, after-bid, reused slot, absent target, duplicate Devilish targets and stale deal commands: no consumption, mutation or RNG advance. Run campaign tests.
- [x] **Step 5:** Commit: `feat: implement stored pre-bid Tarot consumables`.

## Stage C — card legality and Sigils

### Task 10: Sigil play and trick rewards

**Files:** Create `Core/SigilEffects.cs`, `tests/.../CampaignSigilTests.cs`; extend registration and campaign view handling.

**Interfaces:** Task 6 handlers for Coin, Fish, Snake, Duck, Owl, Vulture, Peacock and Spider; Frog uses Task 6 replay policy. Bear/Eagle/Chameleon finish in Tasks 11–12.

- [x] **Step 1:** Add `CampaignSigilRewards`, `CampaignSpiderMutation`, `CampaignGeneratedTarotCapacity`. Assert Coin $3 to player, Fish +2 to player's team, Snake +1/-1 opposing teams, Duck scripted 1-in-2 +4, Peacock +1 per Sigil in trick, Vulture creates one Tarot per activation for actual trick taker.
- [x] **Step 2:** Run tests; expect missing effect results to fail.
- [x] **Step 3:** Implement handlers. Spider selects one other previously played physical card, replaces enhancement with Cursed permanently, retains Sigil, and does nothing if led. Future trick resolution reads the updated enhancement; past on-play effects are not undone. Track generated rewards for bot seats but do not enable bot item use. Owl reveal is public while held, including after exchange.
- [x] **Step 4:** Test Vulture full inventory (no replacement/no cash), Spider on opponent card, preserved slot, and one physical play with Frog. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add Sigil rewards mutations and public-card effects`.

### Task 11: Bear, Eagle and identity-safe card movement

**Files:** Modify `Core/{GameEngine.Campaign,CampaignCardRules,SigilEffects}.cs` (create card rules if absent); create `CampaignCardMovementTests.cs`.

**Interfaces:** `CampaignCardRules.EffectiveRank(CardState card) -> Rank`; campaign move/removal APIs operate on `CardId`. Eagle handler moves the losing card back exactly once after trick scoring.

- [x] **Step 1:** Add `CampaignBearRankCycle` with 9->10, 10->J, Q->K, K->A, A->9 and unchanged underlying face; assert Bear 10 of trump becomes a bower for trick play, while Bear Jack no longer ranks as Jack. Add `CampaignEagleSixTricks`: losing Eagle returns, may be played again, but trick six still ends the hand with leftover cards unused.
- [x] **Step 2:** Run tests; expect incorrect effective rank or six-trick completion to fail.
- [x] **Step 3:** Implement rank lookup before bower suit/rank evaluation. Keep source face for rank-based feature predicates per approved rules and Poker. Never duplicate the physical ID when returning a card; preserve remaining-hand order. Eagle replays do not create additional returns.
- [x] **Step 4:** Test solo/duel inactive hands, repeated Eagle losses, and duplicate faces. Ensure no code assumes every active hand becomes empty on trick six. Run campaign tests.
- [x] **Step 5:** Commit: `feat: support Bear ranking and Eagle return-to-hand rules`.

### Task 12: Wild, Chameleon and constrained Stinky dealing

**Files:** Create `Core/CampaignDeal.cs`; complete `Core/CampaignCardRules.cs`; modify engine dealing/legal-play hooks; create `CampaignLegalityTests.cs`.

**Interfaces:** `CampaignDeal.Deal(IReadOnlyList<CardState> deck, CampaignRandom random, int humanSeat) -> CardId[][]`; `CampaignCardRules.LegalPlays(IReadOnlyList<CampaignCardView> hand, IReadOnlyList<CampaignTrickPlay> trick, Contract contract, bool chameleonLead) -> IReadOnlyList<CampaignCommand.Play>`. The card/trick types come from Task 4 and legal commands expose explicit Wild suit options. Equal-strength, equal-effective-suit physical cards retain the earlier play as winner, matching ordinary first-best behavior.

- [x] **Step 1:** Add `CampaignWildSuitChoices`, `CampaignChameleonLead`, `CampaignStinkyDeal`. Test Wild following led suit, choosing lead suit, four reward-suit matches but one match per effect, normal bower rules, and underlying face color for Twin Flame. Chameleon requires off-suit when possible and follows only when unavoidable. With 18 Stinky, human gets all six eligible cards and every other ID is dealt exactly once.
- [x] **Step 2:** Run tests; expect ordinary follow-suit/deal logic to fail these cases.
- [x] **Step 3:** Implement authoritative effective-suit choice validation; Wild always qualifies as able to follow, so under Chameleon it is excluded if an actual off-suit card is available. Split reward suit predicates from legality suits. Choose six non-Stinky uniformly, then uniformly distribute remaining cards to others. Zero Stinky still yields a uniform ordinary full-deck deal. Draw restrictions do not prohibit exchanges or making held cards Stinky.
- [x] **Step 4:** Test nineteen-Stinky invalid snapshot/mutation rejection, exactly eighteen feasible dealing, Low/High/trump trick winners, and no hidden data used in legal public views. Run all campaign tests and unfiltered runner.
- [x] **Step 5:** Commit: `feat: implement Wild Chameleon and Stinky legality`.

## Stage D — permanent auction options

### Task 13: Unlock-aware bidding and Panic Button

**Files:** Create `Core/CampaignBidding.cs`; extend `Core/{CampaignCommands,GameEngine.Campaign,Domain}.cs`; create `CampaignBiddingTests.cs`.

**Interfaces:** `CampaignCommand.Bid(BidLevel Level, PlanetId? Variant = null)`, `CampaignCommand.Pass()`, `CampaignCommand.PanicButton()`; `CampaignBidding.LegalBids(int seat, int humanSeat, BidLevel? highBid, int? highBidder, CampaignInventory inventory) -> IReadOnlyList<CampaignCommand.Bid>`. Add explicit `BidLevel.Two = 2` while retaining existing numeric values; keep ordinary `Contract.Create`/raise enumeration rejecting Two. Use a campaign factory to construct allowed bid-2 contracts; update shared `RequiredTricks` numeric range to include Two without allowing free-play bid 2. A canceled hand has a campaign settlement record with nullable contract, not a fabricated ordinary contract.

- [x] **Step 1:** Add `CampaignUnlockGates`, `CampaignPanicInterruptsAuction`, `CampaignPanicHandLimit`. Locked human PB/Alone rejected; unlocked accepted; bots retain baseline choices; Low-baller 2 must be Trump. Panic after an existing bid deducts exactly 8/0, advances completed-hand count once, and cannot be repeated by duplicate request.
- [x] **Step 2:** Run tests; expect ungated bids/no Panic command to fail.
- [x] **Step 3:** Implement variants as declared numeric bid + variant, no combination of two Planets, strict raises unless Alpha. Panic finishes a hand at bidding without card/trick effects, checks thresholds/hand limit, and waits for normal next-hand/deal flow rather than recursively dealing inside command dispatch. Normal dealer rotation applies. Tied scores continue even when a threshold is reached.
- [x] **Step 4:** Assert Panic on hand eight ends if unequal, continues if tied; test overtime Panic and negative-threshold crossing. It must not invoke a fake contract failure, Bubble, or match-end card effect with no last card. Run campaign and ordinary auction tests.
- [x] **Step 5:** Commit: `feat: add permanent bidding unlocks and Panic Button`.

### Task 14: Chaos, Telepath and Duelist setup

**Files:** Extend `Core/{CampaignBidding,GameEngine.Campaign,CampaignCommands,CampaignGameView}.cs`; create `CampaignContractTests.cs`.

**Interfaces:** `CampaignCommand.SelectOpponent(int Seat)`, `CampaignCommand.RevealPartnerCard(CardId Id)`; pending choices expose actor and legal targets. Engine stores active seats and per-trick trump in campaign match-local state; ordinary contract APIs still work for baseline modes.

- [x] **Step 1:** Add `CampaignChaosTrumpPerTrick`, `CampaignTelepathBeforeTrump`, `CampaignDuelistActiveSeats`. Chaos legal only target >=4, rolls one suit before each lead (six recorded draws), and holds it through that trick; Telepath >=5 reveals partner-selected card privately before trump choice; Duelist permits 3–5, selects one opponent, skips both inactive seats for all six tricks, and adds 1 failure penalty.
- [x] **Step 2:** Run tests; expect missing setup phases or wrong active seats to fail.
- [x] **Step 3:** Implement pending choices and transition into the shared contract/exchange/trick engine. Recompute bower suit using current Chaos trump. Telepath partner bot deterministically selects its highest effective-rank legal card. Duelist lead skips inactive seats clockwise from dealer. Chaos/Telepath use Trump contracts for their special variant; baseline numeric High/Low remains available separately.
- [x] **Step 4:** Test choice by wrong seat/invalid opponent, hidden reveal to unrelated seats, Chaos double penalty/bonuses but no money doubling, and no extra exchange or inactive plays. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add Chaos Telepath and Duelist contracts`.

### Task 15: Poker and six-card comparison

**Files:** Create `Core/PokerEvaluator.cs`, `tests/.../CampaignPokerTests.cs`; extend contract-selection event handler.

**Interfaces:** `PokerEvaluator.Best(IReadOnlyList<Card> sixCards) -> PokerValue`; `PokerValue` implements total comparison by category and rank kickers. Evaluate five-card subsets of current face-only cards (no Bear or Wild ability substitution). Allow duplicate faces and five-of-a-kind above straight flush.

- [x] **Step 1:** Add `CampaignPokerCategories`, `CampaignPokerMutatedDeck`, `CampaignPokerTiedTeams`. Verify every category, kicker ordering, best five of six, and five-of-a-kind > straight flush. Same-team best tie earns 4 once; opposing-team best tie awards both teams 4. Bear/Wild do not affect poker comparison; actual transformed suits do.
- [x] **Step 2:** Run tests; expect missing evaluator to fail.
- [x] **Step 3:** Implement bounded subset enumeration, full reveal after winning exact-4 Poker bid, and +4 award entries. Continue normal six-trick bid-4 scoring. Poker's normal play mode is selected using existing bid-4 High/Low/Trump options after the reveal; the reveal award never satisfies its trick target.
- [x] **Step 4:** Test illegal Poker bids 3/5, effective copied faces, tied best kickers, and all four viewers' full-reveal state. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add Poker contract and modified-deck evaluator`.

## Stage E — the complete Joker catalogue

### Task 16: Additive and multiplying scoring Jokers

**Files:** Create `Core/JokerScoringEffects.cs`, `tests/.../CampaignJokerScoringTests.cs`; register handlers.

**Interfaces:** Task 6 events/score entries; handlers cover Three's Company, High Roller, Lover Boy, Snake Eyes, Twin Flame, Clubhouse, The Undertaker, Last Laugh, Vow of Silence's scoring half, Cure-All, Trend Setter, Contrarian, Consolation Prize, Royal Council, and Protective Bubble. Remaining Vow auction behavior is Task 17.

- [x] **Step 1:** Add one named scenario per listed Joker with successful/failed trigger conditions; exact values are the approved catalogue. Include: bid exactly 3 +2 only on success; bid >=5/solo +3 only on success; two Aces +5; different colors +1; each winning 9 +1; Cure-All replaces -1 with +2; Trend Setter requires four matching cards and cannot trigger in three-/two-player tricks.
- [x] **Step 2:** Run tests; expect missing Joker entries/changes to fail.
- [x] **Step 3:** Implement additive rewards then replacement/multiplier scopes. Clubhouse/Royal Council benefit human team wins only; personal-win wording remains personal. Contrarian uses resolved played suits for uniqueness. Last Laugh doubles eligible effects of trick six of every hand, including money and generation, using bounded effect activations; double point entries only once, not both via replay and a second multiplier. Do not double the contract settlement. Bubble destroys itself only after an actual failed bid.
- [x] **Step 4:** Test Wild suit predicates, human vs partner wins, card effects vs whole-trick modifiers, final trick of solo/duel hands, and Last Laugh + Glass + Bubble + Chaos. Use Task 5 worked scoring examples as fixed regression assertions. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add scoring Jokers and final-trick effect doubling`.

### Task 17: Stateful, mutation and auction Jokers

**Files:** Create `Core/JokerStateEffects.cs`, `tests/.../CampaignJokerStateTests.cs`; extend bidding/deal/match-end handlers and pending-choice commands.

**Interfaces:** `CampaignCommand.SwapPartnerCard(CardId Id)` and `CampaignCommand.ChooseRevealSeat(int Seat)`; persistent Joker values/previous contract use `CampaignSnapshot`; match-local overlays use `CardOverlay`. Cover Roller Coaster, Déjà Vu, Lucky Duck, Deep Digger, King Midas, Sticky Fingers, Medusa, Cheese Touch, One Up, Cheater, Alpha, Slick Willy, and Vow's bidding restriction.

- [x] **Step 1:** Add named tests for each. Roller Coaster win/loss/win gives value +1 and pays it in High/Low only, reset at match start. Déjà Vu keys H/H/S/H/H give values 0/1/1/1/2, including opponent calls and across matches; human failed bid resets before payout, Panic ignored. Sticky Fingers copies face and both slots to stable leftmost ID; expiration restores the underlying permanent state.
- [x] **Step 2:** Run tests; expect missing state transitions to fail.
- [x] **Step 3:** Implement once-per-lifecycle mutations, seeded Midas/partner exchange/reveal, player-private Cheater reveal, skip-human dealer selection and numeric Alpha matching partner only. Vow automatically passes; forced-dealer case assigns the forced minimum numeric bid to next eligible seat clockwise even if they passed earlier, resolving the auction once. Perform One Up/Cheater after deal completion before Tarot/bidding; pending choices block progression.
- [x] **Step 4:** Test Midas after original enhancement activation, permanent writes under Sticky overlay, Medusa no-card match, Cheese once per match, Slick dealer wrap, Alpha cannot match opponents/solo bid, Vow forced dealer without deadlock, and Deep Digger repeats without rank retroactivity. Run campaign tests.
- [x] **Step 5:** Commit: `feat: add stateful Jokers card mutations and auction exceptions`.

### Task 18: Economy Jokers and privacy audit

**Files:** Create `Core/JokerEconomyEffects.cs`, `tests/.../CampaignJokerEconomyTests.cs`, `CampaignVisibilityTests.cs`; complete public/private view projection.

**Interfaces:** Task 6 handlers for Penny Pincher, Bully, People Pleaser and Millionaire. `ExportCampaignSnapshot` carries already-committed wallets; match-end income returns deltas, never the entire wallet again.

- [x] **Step 1:** Add `CampaignPennyPincherTotalsOvertricks` (bid 3/win 5 + bid 4/win 5 -> $3), `CampaignBullyMarginSix` (margin 5 ->0, 6 ->3), `CampaignPeoplePleaserRemainingFaces` (two faces left ->$2), `CampaignMillionaireVirtualGolden` (Diamond Golden not doubled). Defending and solo hands add no Penny Pincher overtricks.
- [x] **Step 2:** Run tests; expect missing income attribution/handlers to fail.
- [x] **Step 3:** Implement match/hand accounting, independent virtual Golden, and once-only settlement ordering before interest. Audit all views: normal hands private, Owl only that card public, Cheater only human viewer, Telepath only bidder/partner, Poker everyone, partner exchange only participants. Partial revealed arrays must not include hidden IDs.
- [x] **Step 4:** Compare serialized public views against known deck IDs and forbidden opponent cards. Repeated refresh cannot re-award income. Check all 31 Joker IDs have registered tested handlers and all 77 entries have implementation coverage. Run campaign tests and unfiltered runner.
- [x] **Step 5:** Commit: `feat: complete Joker economy and audit campaign visibility`.

## Stage F — session orchestration, desktop and release verification

### Task 19: Session commands, checkpoints and restart recovery

**Files:** Create `App/CampaignCoordinator.cs`, `tests/.../CampaignSessionTests.cs`, `CampaignRecoveryTests.cs`; modify `App/{GameSession,Models}.cs`, `Desktop/CampaignFlow.cs`.

**Interfaces:** `CampaignActionRequest(int Seat, long ExpectedRevision, long ExpectedDealSerial, CampaignCommand Command)`; `GameSession.ExecuteCampaignAsync(CampaignActionRequest request, CancellationToken token = default) -> Task`; `GetCampaignViewAsync(int? viewerSeat, CancellationToken token = default) -> Task<CampaignGameView?>`. Optional constructor context and checkpoint callback are trailing optional parameters. `CampaignCoordinator.Checkpoint(CampaignSnapshot snapshot) -> bool` merges latest resources into `CampaignRun`, advances `LastAppliedRevision`, and saves. Flow starts engine from current run resources and settles from final snapshot before recording match outcome.

- [x] **Step 1:** Add `CampaignStaleActionNoPenalty`, `CampaignCheckpointRoundTrip`, `CampaignRecoveryForfeitsOnce`, `CampaignDelayedResultExactlyOnce`. Apply Tarot then earn money/mutate card, save InMatch, reload and forfeit: Tarot stays consumed, changes remain, no match-end Joker income, one life loss and one interest award. Double/stale commands reject without penalty or debit.
- [x] **Step 2:** Run tests; expect missing session route/checkpoint behavior to fail.
- [x] **Step 3:** Serialize every action under `_gate`. Validate revision/deal before mutation. After an accepted action, checkpoint deck/wallet/inventory/RNG atomically as one snapshot before advancing bots/presentation. Use the same hook for bot-generated permanent changes. Preserve in-memory play on save failure with a visible unsaved-progress flag; last successful file remains coherent. Never claim failed writes are durable. Startup failure before any accepted match action restores the pre-start snapshot; later interruption forfeits.
- [x] **Step 4:** Inject write failure, duplicate terminal observations, cancellation while bot acts, closing during action, and delayed UI completion from an old deal. Assert no reapplication, deadlock or invalid-play penalty from stale UI actions. Run campaign/session tests.
- [x] **Step 5:** Commit: `feat: checkpoint campaign actions and preserve mutations on forfeit`.

### Task 20: Native shop, deck picker and identity-safe presentation

**Files:** Create `Desktop/{CampaignShopScreen,CampaignDeckPicker}.cs`; modify `Desktop/{CampaignFlow,CampaignScreens,MainWindow,CardFlights,HandGlide,PixelArt}.cs` only as required by call sites; modify `App/TableMotionTracker.cs`; create `tests/.../CampaignPresentationTests.cs` and extend existing motion/input tests.

**Interfaces:** `CampaignShopScreen.Build(CampaignRun run, Action<string> buy, Action<JokerId> sell, Action<int> discard, Action next, PixelMotion motion) -> Control`; `CampaignDeckPicker.Build(IReadOnlyList<CardState> deck, Action<CardId> select, Action cancel) -> Control`. Preserve existing visual style; use card-face rendering plus text badges for enhancement/Sigil initially, without requiring new art. Add a pure `CampaignPresentation.CardKey(int seat, CardId id) -> string` shared by campaign card controls and motion tracking.

- [x] **Step 1:** Add tests that identical faces have different keys, slot labels reflect replacements, deck inspection returns 24 entries even for duplicate faces, and stale picker completion cannot buy in a different shop revision. Existing Table motion tests must retain old keys in standard mode.
- [x] **Step 2:** Run presentation/campaign tests; expect missing key/picker behavior to fail.
- [x] **Step 3:** Wire five offers, affordability/slot reasons, selling/discarding, deck inspection, Sigil replacement selection and cancellation into coordinator-backed transactions. Re-render from latest saved/in-memory state. Use physical IDs in campaign hand/trick flight/glide keys; do not mix replay events with new card flights. Show the save-failure note in shop as well as result/intro.
- [x] **Step 4:** Build solution; run tests. Manually verify with a disposable test save: buy each item type, cancel Sigil without charge, replace Sigil preserving enhancement, full slots, sale +$2, no reroll on reopening, and distinct animation for duplicate faces. Check reduced motion and keyboard access. Do not reset the user's profile save.
- [x] **Step 5:** Commit: `feat: add campaign shop and shared-deck editing screens`.

### Task 21: Native bidding, Tarot and pending choices

**Files:** Create `Desktop/CampaignTableControls.cs`; modify `Desktop/{MainWindow,CampaignScreens,CampaignFlow}.cs`; extend `CampaignPresentationTests.cs` and `CampaignSessionTests.cs`.

**Interfaces:** `CampaignTableControls.Build(CampaignGameView view, Func<CampaignCommand, Task> send) -> Control`. Controls capture current revision/deal in the request closure; server/session remains authority. Display available legal choices rather than reimplementing legality in UI.

- [x] **Step 1:** Add presentation/view-model tests for thresholds ±24/±40/±44, `HAND 8 OF 8`, overtime hand 9, only unlocked human bids, no Tarot after bidding, required Wild suit, Duelist opponent, Telepath reveal, One Up/Cheater choices and Vow auto-pass.
- [x] **Step 2:** Run tests; expect missing controls/view-model projection to fail.
- [x] **Step 3:** Render active Joker and Tarot slots, pre-bid targeting, bid variant/target controls, Panic immediate action, pending choices and score/effect explanations. Update intro/result copy from first-to-12/starting handicaps to thresholds/hand limit/zero starts. Include base reward, margin bonus, Joker income and interest separately. Wait for existing deal-completion barrier; no choice may race animations or bot loop. Preserve final-trick display then delayed result presentation.
- [x] **Step 4:** Build/run native app and verify complete flows for two-/three-/four-player contracts, Panic on hand eight, overtime, both Tarot uses, and returning to menu. Every visible effect derives from committed events; polling must not roll Lucky or replay flights. Run campaign tests.
- [x] **Step 5:** Commit: `feat: expose campaign inventory special bids and score explanations`.

### Task 22: Campaign bot policy and unsupported engine guard

**Files:** Create `App/CampaignBotPolicy.cs`, `tests/.../CampaignBotTests.cs`; modify `App/{GameSession,BotDrivers}.cs` and campaign session construction.

**Interfaces:** `CampaignBotPolicy.Choose(CampaignGameView view, int seat) -> CampaignCommand`. Session branches to this policy only for campaign built-in seats; existing `IBotDriver` and `BotAction` types remain the ordinary protocol route. Validate all configured engines before starting any process; non-built-in campaign engines fail with a capability explanation.

- [x] **Step 1:** Add `CampaignBotsUseLegalIds`, `CampaignBotsResolveSetupChoices`, `CampaignExternalEngineRejectedBeforeLaunch`. Duplicate faces, Bear/Wild/Chameleon, Eagle, Chaos, Duelist, Telepath and forced Vow auction must all produce a legal command; assert the policy gets only its filtered view. Track but never consume bot Tarot.
- [x] **Step 2:** Run tests; expect face-only bot choices/unsupported setup phases to fail.
- [x] **Step 3:** Implement legal-action-first policies using visible effective rank/trump, estimated bid strength and partner-friendly trick selection, with deterministic legal fallback. No omniscient access to deck allocation or opponent concealed IDs. Block the session while a human-owned pending choice exists even if `CurrentSeat` still names a bot. Do not change free-play built-in or process policies.
- [x] **Step 4:** Drive bounded seeded all-bot campaign-profile matches, including synthetic human-seat actions selected from legal options; guard steps and fail on loops. Assert zero illegal actions and complete outcomes, without claiming stronger play. Run ordinary process integration tests and campaign tests.
- [x] **Step 5:** Commit: `feat: support campaign-aware legal bots and engine capability checks`.

### Task 23: Catalogue integration and release checks

**Files:** Create `tests/.../CampaignIntegrationTests.cs`, `CampaignCatalogueCoverageTests.cs`; update `README.md`; create `docs/superpowers/reports/2026-11-12-campaign-features-validation.md` during execution, not planning.

**Interfaces:** Tests consume the public engine/session/shop APIs above. A coverage map relates each of the 77 stable catalogue IDs to its handler, lifecycle and at least one focused test. Do not create new production APIs only to mirror implementation internals in tests.

- [x] **Step 1:** Add `CampaignFullPurchasePlaySaveFlow`: seeded win -> payout/interest -> saved shop -> buy Joker/Planet/Tarot/Sigil across affordable visits -> apply Sigil -> next match -> Tarot -> unlocked special bid -> mutated deck -> result -> reload. Assert money/inventory/deck revisions at every boundary. Add counterexamples for failed bid + bonuses, Bubble + Chaos, Bouncy/Frog/Lucky/Deep Digger, Sticky + Spider/Midas, Vulture full slots, and Last Laugh final-trick money/generation.
- [x] **Step 2:** Run campaign tests and confirm each scenario first fails if its intended behavior is removed/disabled; avoid vacuous coverage that merely checks a catalogue row exists.
- [x] **Step 3:** Remove the interim implemented-feature filter only after 10 enhancements, 31 Jokers, 8 Planets, 12 Sigils and 16 Tarot have handlers and tests. Update README with actual implemented rules and unsupported campaign external engines. Record deterministic seed, outcomes and known limits in the validation report. No runtime feature code belongs in the report.
- [x] **Step 4:** Run solution build, unfiltered executable tests, and `./test.sh`. Manually complete the native purchase/play/restart/forfeit flow with a disposable save. Run bounded seeded campaigns and record reward distributions, hand counts, timeout/negative endings, purchase frequency, and runaway combinations. Balance findings are evidence, not permission to silently change approved values. Report optional-engine skips, any save-failure limits, and bot-strength limits.
- [x] **Step 5:** Commit: `test: verify complete campaign feature catalogue and native flow`. Request final integration review before merging; do not merge/publish merely because this plan exists.

## Completion criteria and coverage map

| Approved design area | Owning tasks |
| --- | --- |
| 24-card shared persistent deck; slot replacement; temporary copies | 1, 3, 4, 9, 17, 19 |
| Thresholds, eight hands, tie continuation, lives/victory/endless | 2, 13, 21, 23 |
| Contract and bonus separation; multiplier examples | 5, 6, 16 |
| Match rewards, loss retention, interest | 2, 18, 19 |
| Five-offer shop, fixed prices, sale/discard, exclusions and persistence | 8, 19, 20 |
| All enhancements | 6, 7, 12 |
| All Sigils | 6, 10, 11, 12 |
| All Tarot, targeting and pre-bid timing | 9, 19, 21 |
| All Planets, unlocks and special setup | 13, 14, 15, 21 |
| All Jokers | 6, 16, 17, 18 |
| Physical identity and animation; authorized information | 4, 18, 20 |
| Atomic state, legacy migration, interrupted-match recovery | 3, 19 |
| Built-in bots and default protocol compatibility | 4, 22, 23 |
| Manual desktop flow and balance evidence | 20, 21, 23 |

Plan self-review: each approved section maps to tasks above; future consumers use the shared contract names rather than alternate models. Review-focus scenarios have owning tests. Keep implementation-method choice for the execution handoff; this document is a plan, not a claim that any feature is implemented or verified in the running game.

Execution completed natively on 2026-11-12. See [validation and execution rulings](../reports/2026-11-12-campaign-features-validation.md) for interface/commit grouping deviations, review fixes, test evidence, and limits.
