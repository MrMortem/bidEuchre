# Ante Screen Design

Approved in chat on 2026-11-13.

## Intent

Between the shop and the next match the player should see the whole Ante at a glance, in the manner of a
"choose your next blind" screen: three panels, one per match, with the next match lit and startable. Today
that slot holds a plain text intro for the single upcoming match.

## Goals

- Replace `CampaignScreens.Intro` with an Ante screen, shown everywhere the intro is shown today (new run,
  Continue into `Intro` phase, leaving the shop, a match that failed to start).
- Three panels side by side: SMALL, MEDIUM, BOSS. Each shows the threshold (`±N POINTS`), `8 HANDS`,
  the base reward (`REWARD $5+`, `$6+`, `$8+`) and the lives at stake (`LOSE ♥1`, `♥1`, `♥2`).
- The current match's panel is lit and holds the Start Match button. Earlier panels read `PLAYED` and later
  panels read `UPCOMING`; both are dimmed.
- Header shows `ANTE n`, lives and money. The player's Jokers sit in a row above the panels.
- The boss panel reserves a rule line, empty until bosses get rules.
- Main Menu button below the panels. Any pending note still shows.

## Non-goals

- No Skip button or skip mechanic.
- No boss rules.
- No change to `CampaignRun` rules, phases, or the save format. Win/loss per match is not tracked, which is
  why earlier panels say `PLAYED` rather than `DEFEATED`.

## Approaches

1. **Replace the intro screen (chosen).** One screen in the slot, no extra click, no new phase.
2. Add a new screen before the existing intro. Two screens saying the same thing, and an extra click.
3. Add a new saved phase for the Ante screen. Changes the save format for no gain.

## Left to the builder

- The numbers each panel shows come from one place next to the run rules, so the screen and `Finish` cannot
  drift apart on rewards or lives.
- Layout, colours and motion follow the shop screen and `MenuParts`.
- Tests: a rules test for the three panel descriptions, and a desktop test for the panel states
  (played / current / upcoming) and that only the current panel has the Start button.
