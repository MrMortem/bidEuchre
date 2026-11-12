# Campaign loop (Play button)

Approved in chat on 2026-11-11. This is the first of three campaign specs. The boss rule system and shop items each get their own spec later.

## Goal

The Play button on the main menu starts a single-player campaign run: a sequence of short matches against bots with lives, rising difficulty, money and a shop stop between matches. Table remains free play to 40 and is unchanged.

## Scope

In scope: the run loop, its rules and maths, engine support for a target score and starting scores, saving between matches, and the native desktop screens.

Out of scope: a boss rule-modifier system, anything for sale in the shop, the web client, resuming a match mid-hand, and telling bots that the target is 12.

## Rules

### Structure

- A run is a sequence of matches numbered from 1.
- Three matches make one Ante. Ante = `(match - 1) / 3 + 1`.
- The third match of every Ante is a boss match.
- The player is seat 0 (team 0) with a bot partner in seat 2. Seats 1 and 3 are bot opponents. All three bots use `EngineCatalog.BuiltInEngineId`.

### Match

- A campaign match is played to a target of 12 instead of 40. The win rule is otherwise unchanged: a team wins when it has at least 12 and more than the other team; a tie at or above 12 continues.
- The player's team starts each match at `-(Ante - 1)`: 0, -1, -2, -3, -4 for Antes 1 to 5, continuing to drop by 1 per Ante in endless play.
- The opposing team starts at 0 in normal matches.
- In a boss match the opposing team starts at `Ante + 1` points (2, 3, 4, 5, 6, ...). This is the only boss rule in this spec and is a placeholder; it is one named constant/function so the boss spec can replace it.
- Starting scores apply to that match only.

### Lives

- A run starts with 3 lives.
- Winning costs nothing. Losing a normal match costs 1 life. Losing a boss match costs 2 lives.
- When lives reach 0 or below, the run is lost and ends.

### Money

Applied once, after each match, in this order:

1. Payout. On a win: `max(0, finalScore - 12)` where `finalScore` is the player's team score. On a loss: 0, whatever the score. Money is never removed.
2. Interest. `min(5, money / 5)` using integer division on the total after payout.

A run starts with $0. Examples: hold $12, win on 14 -> +$2 = $14, interest +$2 = $16. Hold $23, lose -> $23, interest +$4 = $27.

### Victory and endless

- Winning match 15 (the Ante 5 boss) shows a victory screen once, after the normal result screen.
- The victory screen offers Continue Endless or Main Menu. Either choice keeps the run saved; Continue goes to the shop as usual.
- After that, the run continues with the same rules until lives run out. No further victory screens.
- If the player loses match 15 but survives, the run simply continues into Ante 6 and there is no victory screen.

## Flow

```text
Menu -> Play -> [Continue | New Run] -> Match intro -> Table -> Result
     -> (Victory, once) -> Shop -> Match intro -> ...
Result with 0 lives -> Run over -> Menu
```

- **Play:** with no save, starts a new run and opens the match intro. With a save, shows Continue and New Run; New Run asks for confirmation before discarding the save.
- **Match intro:** Ante number, match 1-3 of 3, a boss marker when relevant, lives, money, and both teams' starting scores. Buttons: Start Match, Main Menu.
- **Table:** the existing table with the seat/engine setup panel hidden, the campaign seats fixed, and a compact strip showing lives, money and Ante/match. All "to 40" text reads the match target instead.
- **Result:** shown when the engine reaches `GameComplete`. Win or loss, final scores, lives lost and remaining, payout, interest, new total. Button: Continue.
- **Shop:** shows money and "Nothing for sale yet". Button: Next Match.
- **Run over:** matches won, Ante reached, final money. Button: Main Menu. The save is deleted.

## Saving

- One JSON file, `campaign.json`, in the same folder as `preferences.json` (`LocalApplicationData/BidEuchre`), written with the same write-temp-then-move pattern as `GamePreferences.Save`.
- The saved run records a phase: `Intro`, `InMatch`, `Shop`, or `Victory`.
- Saved when: a new run is created (`Intro`), Start Match is pressed (`InMatch`), the result is applied (`Shop`, or `Victory` after match 15 is won), and on Next Match (`Intro`).
- Continue on an `InMatch` save means the match was abandoned: it is applied as a loss with $0 payout (interest still applies), the result screen is shown with a "match forfeited" note, and the flow continues from there.
- Leaving the table for the menu during a campaign match asks for confirmation that it counts as a loss. Confirming leaves the `InMatch` save in place, so the forfeit is applied on the next Continue. Closing the window behaves the same without a prompt.
- A missing, unreadable or invalid save is treated as no save.
- A lost run deletes the file.

## Code shape

### Core

- `MatchOptions(int TargetScore = 40, int StartingScoreTeam0 = 0, int StartingScoreTeam1 = 0)`, passed optionally to the `GameEngine` constructor. Default options reproduce today's behaviour exactly.
- `DetermineGameWinner` uses `TargetScore`. Scores are initialised (and reset) from the starting scores.
- `GameView` gains `TargetScore`.

### App

- `GameSession` accepts optional `MatchOptions` and forwards them.
- `CampaignRun` — immutable record: `Lives`, `Match` (1-based), `Money`, `MatchesWon`, `Phase`, `VictoryShown`. Pure members:
  - `Ante`, `IsBoss`, `IsOver`
  - `NextMatch()` -> `MatchOptions` for the current match
  - `StartMatch()`, `ApplyResult(bool won, int playerScore)` -> `(CampaignRun Run, MatchOutcome Outcome)`, `LeaveShop()`, `Forfeit()`
  - `MatchOutcome(bool Won, bool Boss, int LivesLost, int Payout, int Interest, int Money, bool Forfeited)`
- `CampaignStore` — `Load(path)` returning `CampaignRun?`, `Save(run, path)`, `Delete(path)`.

### Desktop

- `CampaignScreens.cs` — static builders for Play choice, match intro, result, shop, victory and run over, in the style of `MainMenu.cs` and `SettingsPage.cs`.
- `CampaignFlow.cs` — owns the current `CampaignRun`, calls the store, and switches screens through callbacks supplied by `MainWindow`.
- `MainWindow.cs` — wiring only: a `ShowCampaign` entry for the Play button, starting a session from campaign seats and options, hiding setup and showing the campaign strip in campaign mode, reporting `GameComplete` to the flow, the leave-match confirmation, and replacing hard-coded "40" text with the view's target.
- `MainMenu.cs` — Play calls `openCampaign`; the "coming soon" note goes away.

## Errors

- Save failures do not interrupt play. The flow continues in memory and the screen shows a one-line "Could not save progress" note.
- An engine or session failure during a campaign match is shown the way the table shows it today; the run stays `InMatch`.

## Testing

Added to the existing test runner in `tests/BidEuchre.Tests`:

- Engine: default options match current behaviour; a game ends at the target of 12; starting scores are applied and shown in the view; a tie at or above target continues.
- `CampaignRun`: Ante and boss numbering; player penalty per Ante including beyond Ante 5; boss opponent head start; lives lost on normal and boss losses; run lost at 0 lives (including going from 1 life to -1 on a boss); payout on wins at 12, above 12; $0 on any loss; interest on the post-payout total and its $5 cap; victory phase only after winning match 15 and only once; endless continues past match 15.
- `CampaignStore`: round-trip of every phase; corrupt and missing files load as no save; delete; an `InMatch` save forfeits as a loss.

Desktop screens are checked by running the app: new run, win, loss, boss loss, run over, quit mid-match then Continue, and victory.
