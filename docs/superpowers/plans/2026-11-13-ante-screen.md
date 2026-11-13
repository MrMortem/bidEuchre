# Ante Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-match campaign intro with a three-panel Ante screen shown between the shop and the next match.

**Architecture:** The reward and lives-at-stake numbers move into two static functions on `CampaignRun`, used by both `Finish` and the screen. A new `CampaignAnteScreen` builds the screen from a run; `CampaignFlow.ShowIntro` shows it instead of `CampaignScreens.Intro`, which is removed.

**Tech Stack:** C# / .NET 10, Avalonia 12, the repo's own console test runners.

**Spec:** `docs/superpowers/specs/2026-11-13-ante-screen-design.md`

## Global Constraints

- No change to campaign phases, the save format, or any rule's outcome.
- No Skip button. No boss rule text.
- Earlier panels read `PLAYED`, later ones `UPCOMING`.
- The Start button keeps the name `CampaignStartMatch`; Main Menu keeps `CampaignMainMenu`.

## Review Focus

- Endless play (Ante 6+): header and thresholds keep counting (`ANTE 6`, `±44`).
- A run with no Jokers: the Joker row shows five empty slots, not a gap.
- A note ("The match could not start…", "Could not save progress.") still appears, named `CampaignNote`.
- Keyboard: the Start button is focused on arrival; Up/Down reaches Main Menu.
- A window narrower than the three panels scrolls instead of clipping.

---

### Task 1: Stakes on the run

**Files:**
- Modify: `src/BidEuchre.App/CampaignRun.cs`
- Test: `tests/BidEuchre.Tests/CampaignRunTests.cs`, registered in `tests/BidEuchre.Tests/Program.cs`

**Interfaces:**
- Produces: `public static int CampaignRun.BaseReward(int matchInAnte)` → 5, 6, 8 for 1, 2, 3;
  `public static int CampaignRun.LivesAtStake(int matchInAnte)` → 1, 1, 2.

- [ ] Add `CampaignRunTests.Stakes` asserting those six values, register it, run and see it fail to compile.
- [ ] Add the two functions and make `Finish` use them. Run `tests/BidEuchre.Tests`: all pass.

### Task 2: The screen

**Files:**
- Create: `src/BidEuchre.Desktop/CampaignAnteScreen.cs`
- Modify: `src/BidEuchre.Desktop/CampaignFlow.cs` (`ShowIntro`), `src/BidEuchre.Desktop/CampaignScreens.cs` (remove `Intro`), `src/BidEuchre.Desktop/CampaignShopScreen.cs` (`Small` and `EmptySlot` become `internal`)
- Test: `tests/BidEuchre.Desktop.Tests/Program.cs`

**Interfaces:**
- Consumes: Task 1's functions.
- Produces: `internal static Control CampaignAnteScreen.Build(CampaignRun run, Action start, Action back, string? note, PixelMotion motion)`.
  Named controls: panels `AntePanel1..3` (Border), status lines `AnteStatus1..3` (TextBlock), `CampaignStartMatch`, `CampaignMainMenu`, `CampaignNote`.

- [ ] Add a desktop test: a run at Match 5 (Ante 2, match 2) saved in `Intro` phase, opened through `CampaignFlow`, Continue pressed. Assert `AnteStatus1` is `PLAYED`, `AnteStatus3` is `UPCOMING`, exactly one `CampaignStartMatch` exists and sits inside `AntePanel2`, the panel text includes `±28 POINTS`, `REWARD $6+` and `LOSE ♥2`, and pressing Start calls `startMatch`. A second case at Match 16 asserts the title text `ANTE 6` and `±44 POINTS`.
- [ ] Build the screen: header (title, lives, money), Joker row using the shop's small cards, three panels, Main Menu, note; inside a `ScrollViewer` via `MenuParts.Scene`.
- [ ] Point `ShowIntro` at it, delete `CampaignScreens.Intro`. Run both test projects.
- [ ] Launch the app and look at the screen at Ante 1 match 1 and at a boss match.
