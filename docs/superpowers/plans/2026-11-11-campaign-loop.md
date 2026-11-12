# Campaign Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The main-menu Play button starts a saved, single-player campaign run of to-12 matches with lives, Antes, money, interest and an empty shop stop.

**Architecture:** `GameEngine` gains optional `MatchOptions` (target score, starting scores). All run rules live in a pure immutable `CampaignRun` record in `BidEuchre.App`, persisted by `CampaignStore`. The desktop app adds screen builders and a small flow object; `MainWindow` only gains wiring.

**Tech Stack:** C# / .NET, Avalonia desktop, `System.Text.Json`, the project's own console test runner (`tests/BidEuchre.Tests/Program.cs`).

**Spec:** `docs/superpowers/specs/2026-11-11-campaign-loop-design.md`

## Global Constraints

- Free-play Table behaviour must not change: default `MatchOptions` is target 40, both teams start at 0.
- Campaign target score is 12. Starting lives 3. Three matches per Ante; the third is the boss. Victory match is 15.
- Player team is team 0 (seats 0 and 2). Seat 0 is the human. Bots use `EngineCatalog.BuiltInEngineId`.
- Payout: win `max(0, finalScore - 12)`; loss 0. Interest: `min(5, money / 5)` on the total after payout. Money never decreases.
- Save file is `campaign.json` beside `preferences.json` in `LocalApplicationData/BidEuchre`, written temp-then-move like `GamePreferences.Save`.
- Desktop only. Do not touch `src/BidEuchre.Server` or `wwwroot`.
- No boss rule system and no shop items; the shop reads "Nothing for sale yet".
- Tests are registered by adding a `("name", () => Run(Class.Method))` line to the `tests` list in `tests/BidEuchre.Tests/Program.cs`. Use the existing `Assert` helpers there.
- Build and test command (referred to below as **RUN TESTS**):
  `dotnet build BidEuchre.slnx --nologo -m:1 && dotnet run --project tests/BidEuchre.Tests/BidEuchre.Tests.csproj --no-build`
  (use `./.dotnet/dotnet` instead of `dotnet` if that file exists). Expected final line: `N/N tests passed.`
- The working tree already holds unrelated uncommitted work, including in `MainWindow.cs` and an untracked `MainMenu.cs`. Stage only the files each task names; never `git add -A`.

## Review Focus

1. **Hand-edited or stale save with impossible values** (lives 0 or less, match below 1, negative money, unknown phase number): Play must behave as if there is no save, not open a broken run. Pinned in Task 3.
2. **A result reported twice** (double click, or the refresh timer seeing `GameComplete` on several ticks): lives and money must change once. Pinned in Task 2 (`ApplyResult` throws outside `InMatch`) and Task 5 (flow reports once).
3. **Leaving a finished match**: if the match has already reached `GameComplete`, pressing Menu must record the real result, not a forfeit. Pinned in Task 5 (result applied when `GameComplete` is first observed, not on a button).
4. **Losing with a score above 12** (for example 13 to 15): payout is $0. Pinned in Task 2.
5. **Table after a campaign match**: opening free-play Table afterwards must show the seat setup again, play to 40, and leave `campaign.json` untouched. Pinned in Task 1 (defaults) and Task 5 (manual check).

---

### Task 1: Match options in the engine

**Files:**
- Modify: `src/BidEuchre.Core/Domain.cs` (add record), `src/BidEuchre.Core/GameEngine.cs`, `src/BidEuchre.Core/GameView.cs`, `src/BidEuchre.App/GameSession.cs`
- Test: `tests/BidEuchre.Tests/Program.cs` (`GameEngineTests`)

**Interfaces:**
- Produces:
  - `public sealed record MatchOptions(int TargetScore = 40, int StartingScoreTeam0 = 0, int StartingScoreTeam1 = 0)` in namespace `BidEuchre.Core`
  - `GameEngine(IReadOnlyList<string> playerNames, int? randomSeed = null, MatchOptions? options = null)`
  - `GameView` gains a trailing positional member `int TargetScore = 40`
  - `GameSession(...)` gains a trailing optional parameter `MatchOptions? matchOptions = null`, forwarded to `new GameEngine(...)` at `GameSession.cs:75`

- [ ] **Step 1: Write the failing tests** in `GameEngineTests`, and register them.

Add a helper `PlayHand(GameEngine game)`: the three seats after the dealer pass in order, the dealer bids `BidLevel.Three` and chooses `ContractMode.Trump`, `Suit.Clubs`, then `PlayOut(game)`.

```csharp
public static void DefaultMatchOptionsAreUnchanged()
{
    var view = NewGame().CreateView();
    Assert.Equal(40, view.TargetScore);
    Assert.Equal(0, view.Scores[0]);
    Assert.Equal(0, view.Scores[1]);
}

public static void StartingScoresApply()
{
    var game = new GameEngine(Names, 42, new MatchOptions(12, -4, 6));
    game.StartGame(dealer: 0);
    var view = game.CreateView();
    Assert.Equal(12, view.TargetScore);
    Assert.Equal(-4, view.Scores[0]);
    Assert.Equal(6, view.Scores[1]);
}

public static void MatchEndsAtTarget()
{
    var game = new GameEngine(Names, 42, new MatchOptions(12));
    game.StartGame(dealer: 0);
    for (var hand = 0; hand < 60 && game.Phase is not GamePhase.GameComplete; hand++)
    {
        if (game.Phase is GamePhase.HandComplete) game.StartNextHand();
        PlayHand(game);
        var s = game.Scores;
        var shouldEnd = Math.Max(s[0], s[1]) >= 12 && s[0] != s[1];
        Assert.Equal(shouldEnd, game.Phase is GamePhase.GameComplete);
    }
    Assert.Equal(GamePhase.GameComplete, game.Phase);
    Assert.True(game.Scores.Max() < 40, "A to-12 match must not run on toward 40");
}
```

- [ ] **Step 2:** RUN TESTS. Expected: build fails (`MatchOptions` not defined).
- [ ] **Step 3: Implement.** Store the options in the engine; initialise `_scores` from the starting scores wherever scores are set or reset (the constructor and the reset near `GameEngine.cs:346`); `DetermineGameWinner` compares against `TargetScore`; `CreateView` passes `TargetScore`. Reject `TargetScore < 1` with `ArgumentOutOfRangeException`. Forward the new `GameSession` parameter.
- [ ] **Step 4:** RUN TESTS. Expected: all pass, including every existing test.
- [ ] **Step 5: Commit**

```bash
git add src/BidEuchre.Core/Domain.cs src/BidEuchre.Core/GameEngine.cs src/BidEuchre.Core/GameView.cs src/BidEuchre.App/GameSession.cs tests/BidEuchre.Tests/Program.cs
git commit -m "feat: match options for target and starting scores"
```

---

### Task 2: CampaignRun rules

**Files:**
- Create: `src/BidEuchre.App/CampaignRun.cs`
- Create: `tests/BidEuchre.Tests/CampaignRunTests.cs`
- Modify: `tests/BidEuchre.Tests/Program.cs` (register tests)

**Interfaces:**
- Consumes: `MatchOptions` from Task 1.
- Produces (namespace `BidEuchre.App`):

```csharp
public enum CampaignPhase { Intro, InMatch, Shop, Victory }

public sealed record MatchOutcome(
    bool Won, bool Boss, int LivesLost, int Payout, int Interest, int Money, bool Forfeited);

public sealed record CampaignRun(
    int Lives = 3, int Match = 1, int Money = 0, int MatchesWon = 0,
    CampaignPhase Phase = CampaignPhase.Intro, bool VictoryShown = false)
{
    public const int TargetScore = 12;
    public const int VictoryMatch = 15;
    public int Ante { get; }                 // (Match - 1) / 3 + 1
    public int MatchInAnte { get; }          // 1..3
    public bool IsBoss { get; }              // MatchInAnte == 3
    public bool IsOver { get; }              // Lives <= 0
    public int PlayerStartingScore { get; }  // -(Ante - 1)
    public int OpponentStartingScore { get; }// IsBoss ? Ante + 1 : 0
    public MatchOptions NextMatch();         // (12, PlayerStartingScore, OpponentStartingScore)
    public CampaignRun StartMatch();         // Intro -> InMatch
    public (CampaignRun Run, MatchOutcome Outcome) ApplyResult(bool won, int playerScore); // InMatch -> Shop | Victory
    public (CampaignRun Run, MatchOutcome Outcome) Forfeit();  // ApplyResult(false, 0) with Forfeited = true
    public CampaignRun AcknowledgeVictory(); // Victory -> Shop
    public CampaignRun LeaveShop();          // Shop -> Intro
}
```

Rules the signatures do not determine:
- Every transition method throws `InvalidOperationException` when called in any other phase.
- `ApplyResult`: lives lost is 0 on a win, 1 on a normal loss, 2 on a boss loss. Payout then interest per Global Constraints. `MatchesWon` increments on a win.
- `Match` increments by 1 unless the run is now over (`Lives <= 0`), so a lost run still reports the match and Ante it ended on.
- Resulting phase is `Victory` only when the match just won was number 15 and `VictoryShown` was false; `VictoryShown` becomes true at that point. Otherwise `Shop`.

- [ ] **Step 1: Write the failing tests** in `CampaignRunTests` (internal static class, methods below), and register each.

```csharp
static CampaignRun AtMatch(int match, int lives = 3, int money = 0) =>
    new(lives, match, money, 0, CampaignPhase.InMatch);

public static void Structure()
{
    Assert.Equal(1, new CampaignRun(Match: 1).Ante);
    Assert.Equal(1, new CampaignRun(Match: 3).Ante);
    Assert.Equal(2, new CampaignRun(Match: 4).Ante);
    Assert.Equal(6, new CampaignRun(Match: 16).Ante);
    Assert.False(new CampaignRun(Match: 2).IsBoss);
    Assert.True(new CampaignRun(Match: 3).IsBoss);
    Assert.True(new CampaignRun(Match: 15).IsBoss);
    Assert.Equal(new MatchOptions(12, 0, 0), new CampaignRun(Match: 1).NextMatch());
    Assert.Equal(new MatchOptions(12, 0, 2), new CampaignRun(Match: 3).NextMatch());
    Assert.Equal(new MatchOptions(12, -4, 0), new CampaignRun(Match: 13).NextMatch());
    Assert.Equal(new MatchOptions(12, -4, 6), new CampaignRun(Match: 15).NextMatch());
    Assert.Equal(new MatchOptions(12, -5, 0), new CampaignRun(Match: 16).NextMatch());
}

public static void Lives()
{
    var (win, _) = AtMatch(1).ApplyResult(true, 12);
    Assert.Equal(3, win.Lives);
    var (loss, lossOutcome) = AtMatch(1).ApplyResult(false, 5);
    Assert.Equal(2, loss.Lives); Assert.Equal(1, lossOutcome.LivesLost);
    var (boss, bossOutcome) = AtMatch(3).ApplyResult(false, 5);
    Assert.Equal(1, boss.Lives); Assert.Equal(2, bossOutcome.LivesLost); Assert.True(bossOutcome.Boss);
    var (dead, _) = AtMatch(3, lives: 1).ApplyResult(false, 5);
    Assert.Equal(-1, dead.Lives); Assert.True(dead.IsOver); Assert.Equal(3, dead.Match);
    Assert.Equal(2, win.Match); Assert.Equal(1, win.MatchesWon);
}

public static void Money()
{
    Assert.Equal(0, AtMatch(1).ApplyResult(true, 12).Outcome.Money);          // win on 12 pays 0
    var (_, o) = AtMatch(1, money: 12).ApplyResult(true, 14);                 // 12 + 2 = 14, interest 2
    Assert.Equal(2, o.Payout); Assert.Equal(2, o.Interest); Assert.Equal(16, o.Money);
    var (_, lost) = AtMatch(1, money: 23).ApplyResult(false, 13);             // loss above 12 pays 0
    Assert.Equal(0, lost.Payout); Assert.Equal(4, lost.Interest); Assert.Equal(27, lost.Money);
    Assert.Equal(0, AtMatch(1, money: 3).ApplyResult(false, -9).Outcome.Payout); // never removes money
    Assert.Equal(5, AtMatch(1, money: 100).ApplyResult(true, 36).Outcome.Interest); // cap
    var (_, f) = AtMatch(1, money: 10).Forfeit();
    Assert.True(f.Forfeited); Assert.False(f.Won); Assert.Equal(12, f.Money);
}

public static void PhasesAndVictory()
{
    var run = new CampaignRun().StartMatch();
    Assert.Equal(CampaignPhase.InMatch, run.Phase);
    var (shop, _) = run.ApplyResult(true, 12);
    Assert.Equal(CampaignPhase.Shop, shop.Phase);
    Assert.Equal(CampaignPhase.Intro, shop.LeaveShop().Phase);
    Assert.Throws<InvalidOperationException>(() => shop.ApplyResult(true, 12)); // result reported twice
    Assert.Throws<InvalidOperationException>(() => shop.StartMatch());
    Assert.Throws<InvalidOperationException>(() => new CampaignRun().LeaveShop());

    var (victory, _) = AtMatch(15).ApplyResult(true, 12);
    Assert.Equal(CampaignPhase.Victory, victory.Phase);
    Assert.True(victory.VictoryShown); Assert.Equal(16, victory.Match);
    Assert.Equal(CampaignPhase.Shop, victory.AcknowledgeVictory().Phase);

    var (lostBoss, _) = AtMatch(15).ApplyResult(false, 0);      // survives with 1 life
    Assert.Equal(CampaignPhase.Shop, lostBoss.Phase); Assert.False(lostBoss.VictoryShown);

    var later = new CampaignRun(3, 30, 0, 0, CampaignPhase.InMatch, VictoryShown: true);
    Assert.Equal(CampaignPhase.Shop, later.ApplyResult(true, 12).Run.Phase);
}
```

- [ ] **Step 2:** RUN TESTS. Expected: build fails (`CampaignRun` not defined).
- [ ] **Step 3: Implement `CampaignRun.cs`** per the Interfaces block.
- [ ] **Step 4:** RUN TESTS. Expected: all pass.
- [ ] **Step 5: Commit**

```bash
git add src/BidEuchre.App/CampaignRun.cs tests/BidEuchre.Tests/CampaignRunTests.cs tests/BidEuchre.Tests/Program.cs
git commit -m "feat: campaign run rules"
```

---

### Task 3: CampaignStore

**Files:**
- Create: `src/BidEuchre.App/CampaignStore.cs`
- Create: `tests/BidEuchre.Tests/CampaignStoreTests.cs`
- Modify: `tests/BidEuchre.Tests/Program.cs` (register tests)

**Interfaces:**
- Consumes: `CampaignRun`, `CampaignPhase` from Task 2.
- Produces:

```csharp
public static class CampaignStore
{
    public static CampaignRun? Load(string path);        // null when missing, unreadable, invalid JSON, or invalid values
    public static bool Save(CampaignRun run, string path); // false on IO failure; temp-then-move
    public static bool Delete(string path);              // true when the file is gone afterwards
}
```

`Load` returns null when `Lives <= 0`, `Match < 1`, `Money < 0`, `MatchesWon < 0`, or `Phase` is not a defined `CampaignPhase`. Follow the exception handling in `GamePreferences.Load`/`Save`.

- [ ] **Step 1: Write the failing tests**, following the temp-folder pattern in `GamePreferencesTests.PersistMotion`, and register each.

```csharp
public static void RoundTrip()
{
    // path = <temp folder>/campaign.json
    Assert.True(CampaignStore.Load(path) is null, "Missing file is no save");
    foreach (var phase in Enum.GetValues<CampaignPhase>())
    {
        var run = new CampaignRun(2, 7, 31, 4, phase, VictoryShown: phase is CampaignPhase.Victory);
        Assert.True(CampaignStore.Save(run, path));
        Assert.Equal(run, CampaignStore.Load(path)!);
    }
    Assert.True(CampaignStore.Delete(path));
    Assert.True(CampaignStore.Load(path) is null);
    Assert.True(CampaignStore.Delete(path), "Deleting a missing save succeeds");
    Assert.False(CampaignStore.Save(new CampaignRun(), folder), "A directory path fails gracefully");
}

public static void RejectsInvalidSaves()
{
    foreach (var text in new[]
    {
        "not json", "null", "{\"Lives\":0}", "{\"Lives\":3,\"Match\":0}",
        "{\"Lives\":3,\"Match\":1,\"Money\":-5}", "{\"Lives\":3,\"Match\":1,\"MatchesWon\":-1}",
        "{\"Lives\":3,\"Match\":1,\"Phase\":99}"
    })
    {
        File.WriteAllText(path, text);
        Assert.True(CampaignStore.Load(path) is null, text);
    }
}

public static void AbandonedMatchForfeits()
{
    CampaignStore.Save(new CampaignRun(3, 3, 10).StartMatch(), path);
    var (run, outcome) = CampaignStore.Load(path)!.Forfeit();
    Assert.True(outcome.Forfeited); Assert.Equal(2, outcome.LivesLost);
    Assert.Equal(1, run.Lives); Assert.Equal(12, run.Money); Assert.Equal(CampaignPhase.Shop, run.Phase);
}
```

- [ ] **Step 2:** RUN TESTS. Expected: build fails (`CampaignStore` not defined).
- [ ] **Step 3: Implement `CampaignStore.cs`.**
- [ ] **Step 4:** RUN TESTS. Expected: all pass.
- [ ] **Step 5: Commit**

```bash
git add src/BidEuchre.App/CampaignStore.cs tests/BidEuchre.Tests/CampaignStoreTests.cs tests/BidEuchre.Tests/Program.cs
git commit -m "feat: campaign save file"
```

---

### Task 4: Campaign screens and flow

**Files:**
- Create: `src/BidEuchre.Desktop/CampaignScreens.cs`
- Create: `src/BidEuchre.Desktop/CampaignFlow.cs`

**Interfaces:**
- Consumes: `CampaignRun`, `MatchOutcome`, `CampaignStore`, `MatchOptions`; `MainWindow` statics used by `MainMenu.cs` (`PrimaryButton`, `QuietButton`, `Paper`, `Gold`, `Green`, `Muted`), `PixelArt.TableScene`, `PixelMotion`.
- Produces:

```csharp
internal static class CampaignScreens   // each returns a full-screen Control framed like MainMenu.Build
{
    public static Control PlayChoice(CampaignRun saved, Action resume, Action newRun, Action back, PixelMotion motion);
    public static Control Intro(CampaignRun run, Action start, Action back, string? note, PixelMotion motion);
    public static Control Result(CampaignRun run, MatchOutcome outcome, int[]? scores, Action next, string? note, PixelMotion motion);
    public static Control Shop(CampaignRun run, Action next, PixelMotion motion);
    public static Control Victory(CampaignRun run, Action continueEndless, Action menu, PixelMotion motion);
    public static Control RunOver(CampaignRun run, Action menu, PixelMotion motion);
}

internal sealed class CampaignFlow(
    string savePath, Action<Control> show, Func<MatchOptions, Task> startMatch,
    Action showMenu, PixelMotion motion)
{
    public CampaignRun? Run { get; }
    public bool InMatch { get; }                       // Run?.Phase is InMatch
    public string Strip { get; }                       // "♥ 3 · $12 · ANTE 2 · MATCH 1/3" (+ " · BOSS")
    public void Open();                                // Play pressed
    public void MatchCompleted(bool won, int[] scores);// called once by MainWindow at GameComplete
}
```

Behaviour:
- `Open`: no save -> new `CampaignRun`, save, show Intro. Save present -> `PlayChoice`. Resume dispatches on phase: `Intro` -> Intro; `InMatch` -> `Forfeit()`, save, Result with `scores: null` and the "Match forfeited" line; `Shop` -> Shop; `Victory` -> Victory.
- New Run from `PlayChoice` swaps the two buttons for an inline "Discard the current run?" Yes / No before replacing the save.
- Intro Start: `StartMatch()`, save, then `await startMatch(run.NextMatch())`.
- `MatchCompleted`: ignored unless `InMatch`; `ApplyResult(won, scores[0])`, save (or `CampaignStore.Delete` when `IsOver`), show Result.
- Result Continue: `IsOver` -> RunOver; phase `Victory` -> Victory; else Shop. Victory Continue Endless: `AcknowledgeVictory()`, save, Shop. Victory Main Menu: `showMenu()` with the save left in `Victory`. Shop Next Match: `LeaveShop()`, save, Intro. RunOver: `showMenu()`.
- Any `Save` returning false sets the note "Could not save progress." on the next Intro or Result screen; play continues.

Exact copy:
- PlayChoice: title `CAMPAIGN`, line `ANTE {Ante} · MATCH {MatchInAnte}/3 · ♥ {Lives} · ${Money}`, buttons `Continue`, `New Run`, `Back`.
- Intro: title `ANTE {Ante}`, line `MATCH {MatchInAnte} OF 3`, boss line `BOSS MATCH · LOSING COSTS 2 LIVES`, `♥ {Lives}   ${Money}`, `YOUR TEAM STARTS AT {PlayerStartingScore} · OPPONENTS AT {OpponentStartingScore} · FIRST TO 12`, buttons `Start Match`, `Main Menu`.
- Result: title `MATCH WON` or `MATCH LOST`; `{scores[0]} – {scores[1]}` (omitted when null); `Match forfeited` when forfeited; `LIVES −{LivesLost} · ♥ {Lives}` (omit the `−` part on a win); `PAYOUT +${Payout}`; `INTEREST +${Interest}`; `TOTAL ${Money}`; button `Continue`.
- Shop: title `SHOP`, `${Money}`, `Nothing for sale yet`, button `Next Match`.
- Victory: title `CAMPAIGN WON`, `You beat the Ante 5 boss.`, buttons `Continue Endless`, `Main Menu`.
- RunOver: title `RUN OVER`, `REACHED ANTE {Ante} · MATCH {MatchInAnte}`, `MATCHES WON {MatchesWon}`, `${Money}`, button `Main Menu`.
- Give every button a `Name` of `Campaign` + its label without spaces (for example `CampaignStartMatch`), matching the `Menu{text}` convention.

- [ ] **Step 1: Implement `CampaignScreens.cs`** with one private helper that builds the framed panel used by all six screens (title, lines, buttons, optional note), reusing the layout of `MainMenu.Build`.
- [ ] **Step 2: Implement `CampaignFlow.cs`** per the behaviour list.
- [ ] **Step 3:** `dotnet build BidEuchre.slnx --nologo -m:1`. Expected: build succeeds with no new warnings. (Nothing calls the flow yet; Task 5 exercises it.)
- [ ] **Step 4: Commit**

```bash
git add src/BidEuchre.Desktop/CampaignScreens.cs src/BidEuchre.Desktop/CampaignFlow.cs
git commit -m "feat: campaign screens and flow"
```

---

### Task 5: Wire Play into the window

**Files:**
- Modify: `src/BidEuchre.Desktop/MainMenu.cs`
- Modify: `src/BidEuchre.Desktop/MainWindow.cs` (around lines 80, 143-158, 325, 476-539, 604+, 880-883, 1197-1240)

**Interfaces:**
- Consumes: `CampaignFlow` from Task 4; `GameSession(..., matchOptions:)` and `GameView.TargetScore` from Task 1.
- Produces: `MainMenu.Build(Action openCampaign, Action openTable, Action openSettings, PixelMotion motion)`.

- [ ] **Step 1: `MainMenu.cs`** — add the `openCampaign` parameter, make Play call it, delete the "coming soon" handler and comment. Keep the `note` TextBlock out if nothing else uses it. Change the subtitle to `FOUR SEATS · TWO TEAMS`.
- [ ] **Step 2: `MainWindow.cs` fields and entry** — add `_campaignPath` (same folder as `_preferencesPath`, file `campaign.json`) and a `CampaignFlow _campaign` constructed with `show: control => _screenHost.Content = control`, `startMatch: StartCampaignMatchAsync`, `showMenu: ShowMenu`. `ShowMenu` passes `_campaign.Open` as `openCampaign`.
- [ ] **Step 3: Shared session start** — extract the session creation and viewer setup in `CreateTableAsync` (lines ~506-527) into `private async Task OpenSessionAsync(IReadOnlyList<SeatConfiguration> seats, string name, int paceMilliseconds, MatchOptions? options)`; `CreateTableAsync` calls it with `options: null`. Add `StartCampaignMatchAsync(MatchOptions options)`: disposes any previous session, calls `OpenSessionAsync` with seats `You` (seat 0, Human), `West` (1, Bot), `Partner` (2, Bot), `East` (3, Bot), bots on `EngineCatalog.BuiltInEngineId`, name `Campaign`, the currently selected pace, then starts the session the way `StartSessionAsync` does and calls `ShowTable()`.
- [ ] **Step 4: Campaign mode on the table** — while `_campaign.InMatch`: hide the game page's leave-table button (`back`, line ~272) and the viewer-seat picker; show `_campaign.Strip` in a new `TextBlock` named `CampaignStrip` above the score panels. Outside campaign mode these are exactly as today. Free-play `ShowTable` from the menu must show the setup page when no free-play session exists.
- [ ] **Step 5: Target text** — replace `Eyebrow("TEAMS · TO 40")` with a field updated on each render to `TEAMS · TO {view.TargetScore}`, and `"The race to 40 is complete."` with `$"The race to {view.TargetScore} is complete."`.
- [ ] **Step 6: Report the result once** — in the refresh/render path, when `_campaign.InMatch` and `view.Phase is GamePhase.GameComplete`, detach and dispose the session and call `_campaign.MatchCompleted(view.GameWinner == 0, view.Scores)`. Because the flow leaves `InMatch` immediately, later ticks do nothing.
- [ ] **Step 7: Leaving mid-match** — in `ReturnToMenuAsync`, when `_campaign.InMatch`: first press changes the button text to `Forfeit match?`; a second press within the same match leaves (dispose session, `ShowMenu()`), keeping the `InMatch` save. Reset the text to `← Menu` whenever a match starts or the menu is shown. Window close needs no code: the `InMatch` save already on disk is the forfeit.
- [ ] **Step 8:** RUN TESTS. Expected: all pass.
- [ ] **Step 9: Verify in the running app** (`./run.sh`). Delete `~/Library/Application Support/BidEuchre/campaign.json` first, if that is where `LocalApplicationData` resolves on this Mac (print `_campaignPath` once if unsure). Check each and note the result:
  1. Play -> Intro shows Ante 1, Match 1 of 3, ♥ 3, $0, starts 0 / 0, first to 12.
  2. Start Match -> table has no seat setup, strip visible, scoreboard reads TO 12; the match ends at 12 and the Result screen appears by itself.
  3. Result -> Shop -> Next Match -> Intro shows Match 2 of 3 with updated money.
  4. Quit the app mid-match, relaunch, Play -> Continue shows MATCH LOST with "Match forfeited" and one life fewer.
  5. Menu during a match asks `Forfeit match?`; pressing again returns to the menu.
  6. Edit the save to `"Match":3` (Phase 0): Intro shows the boss line and opponents at 2. Edit to `"Match":15`, win or edit onward: Victory appears once after a win; Continue Endless reaches the shop.
  7. Edit the save to `"Lives":1`, lose a match: RUN OVER appears and the save file is gone; Play then starts a new run without a Continue choice.
  8. Menu -> Table shows the seat setup as before, scoreboard reads TO 40, and `campaign.json` is unchanged.
  9. Menu -> Play with a save -> New Run asks to discard before replacing it.
- [ ] **Step 10: Commit**

```bash
git add src/BidEuchre.Desktop/MainMenu.cs src/BidEuchre.Desktop/MainWindow.cs
git commit -m "feat: Play starts a campaign run"
```
