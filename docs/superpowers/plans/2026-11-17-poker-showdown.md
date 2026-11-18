# Poker Showdown Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the independently owned Core, desktop, and browser changes. Shared interfaces are fixed below.

**Goal:** Reveal Poker hands, highlight the winning five cards, award points, then automatically redeal without contract selection or tricks.

**Architecture:** Core persists a PokerReveal phase and typed result. Both clients render that result and send a guarded CompletePoker command after a three-second display. Normal match completion remains authoritative in Core.

**Tech Stack:** .NET 10/C#, Avalonia desktop, existing JavaScript browser client.

**Spec:** ../specs/2026-11-17-poker-showdown-design.md

## Global Constraints

- Preserve all existing uncommitted work in the current workspace; no commits or external publishing.
- No new dependencies. Use current face ranks/suits, best five of six, and existing tie rules.
- No contract choice, tricks, contract reward/penalty, or Joker attribution for new Poker rounds.
- Stale/repeated acknowledgments and save/resume must never award twice or deal twice.

## Shared Interfaces

- Append `GamePhase.PokerReveal`.
- `PokerWinningHand(int Seat, string Category, IReadOnlyList<CardId> CardIds)`.
- `PokerRoundResult(IReadOnlyList<PokerWinningHand> Winners, IReadOnlyList<int> TeamPoints)`.
- Optional `CampaignGameView.PokerResult`, `CampaignMatchSnapshot.PokerResult`, and `HandResult.PokerResult`.
- Nullable `HandResult.Contract`; additive `TeamScoreBreakdown.PokerPoints`.
- `CampaignCommand.CompletePoker`: only seat zero during reveal, then next deal or match completion.

## Review Focus

- Physical duplicate cards and ties highlight exactly the evaluated five cards per winning seat.
- Reloading or polling a reveal neither duplicates points nor restarts an active timer indefinitely.
- Target-score and final-round outcomes show the reveal before match completion.
- Hidden, paused, or replaced views cannot submit stale continuation commands.
- Non-Poker hands and old checkpoints retain their current behavior.

### Task 1: Core showdown and persistence

Owner: poker_core_fix. Files: Core models/evaluator/engine/snapshot/catalog and Core tests.
- [x] Add failing tests for standalone Poker scoring, visibility, winning IDs, ties, boundaries, and persistence.
- [x] Implement auction-triggered reveal, dedicated award/history, guarded continuation, and strict snapshot copying/validation.
- [x] Update obsolete six-trick Poker expectations and run the Core tests.

### Task 2: Desktop presentation

Owner: poker_desktop_fix. Files: desktop table/cards/result controls and desktop tests.
- [x] Add a behavior test for reveal rendering and progression.
- [x] Highlight actual winning cards and display player/category/award, without contract cues.
- [x] Automatically acknowledge after three seconds with lifecycle guards; handle Poker score history.
- [x] Run desktop tests and inspect rendered result.

### Task 3: Browser and server

Owner: poker_browser_fix. Files: server projection/wire, browser table/cards/history/timers, relevant tests.
- [x] Add failing renderer/transition or API tests.
- [x] Expose the result and command, highlight winning IDs and show the award, and auto-acknowledge with polling/lifecycle guards.
- [x] Verify server/projection and browser behavior.

### Task 4: Session integration, documentation, review

Owner: root. Files: App session/save integration only as needed, integration tests, rules/docs.
- [x] Verify checkpointed reveal and continuation through GameSession and saved campaign flow.
- [x] Update current player-facing rules and mark superseded design behavior.
- [x] Run appropriate .NET and browser suites; review combined changes and correct issues.

## Execution decisions

- Work in the existing dirty workspace because the audited behavior includes extensive uncommitted changes; moving only committed HEAD to a worktree would omit the product being fixed.
- Keep progression at one completed round per Poker showdown; show the result before ending a match.
- Preserve the existing +4 tie rule and award duplicate Poker bonuses only to a winning human team.

## Verification

- Core/App executable: 291/291 tests passed, including Poker session and save-boundary regressions.
- Full Avalonia desktop executable and .NET server executable passed.
- Node web suite: 60/60 passed. Node server/browser suite: 58/58 passed.
- Solution build passed; Avalonia telemetry was disabled for this build because its default log path lies outside the filesystem sandbox.
- Native desktop and browser desktop/mobile screenshots inspected: all hands visible, actual winning physical cards highlighted.
- Independent final review found no actionable Poker defects; `git diff --check` passed.
- Test integration updated one stale reflection invocation from the earlier match-completion rename; production behavior outside Poker was preserved.
