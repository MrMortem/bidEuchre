# Shop Arcade Implementation Plan

> **For agentic workers:** Use coordinated subagents for the independently owned App, Desktop and Server/browser files. Root owns assets, documentation, integration and final review.

**Goal:** Replace direct shop Sigils with playable Plinko and roulette that award saved consumable prizes.

**Architecture:** A shared authoritative App transaction charges and records each result before clients animate it. Clients claim prizes through the existing inventory or committed six-card use flow. Persist per-game usage in the current shop, independent of rerolls.

**Tech Stack:** C#/.NET 10, Avalonia, existing ASP.NET server and browser JavaScript, existing curated PNG pipeline.

**Spec:** `docs/superpowers/specs/2026-11-16-shop-arcade-design.md`

## Global constraints

- $3 per play; one Plinko and one roulette play per shop visit.
- Preserve user artwork, current Joker import and existing uncommitted work.
- No new collectible category, new dependency, external purchase, commit or deployment.
- Prizes and payments are durable before animation. Preserve existing mandatory six-card use and held Tarot behavior.
- Root must obtain/verify actual Drive Plinko assets before claiming that artwork requirement is complete.

## Review focus

- Paid states must not be silently reset by old-save loading or malformed optional fields.
- Inventory full must preserve the prize and allow immediate use without inventory capacity.
- Retry, restart, double-click and stale revisions must not produce free plays or duplicate prizes.
- Browser polls and motion settings must not change the outcome or reset active choices.
- Legacy purchased Sigils and already-open six-card sessions must remain finishable.

## Tasks

- [x] **Domain:** Add `ArcadeGame`, `ArcadeResult`, `ArcadeState`, `ShopState.Arcade`, `ShopOffer.IsArcadePrize`, and `CampaignArcade`. Implement `PlayPlinko`, `PlayRoulette`, `KeepArcadePrize`, `BeginArcadePrizeUse`, and `DismissArcadeLoss`. Remove new direct Sigil stock, extend unresolved-state guards, validate and migrate saves. Add focused App transaction/persistence tests, then run the Core/App suite.
- [x] **Desktop:** Add an Arcade shop entry, submenu, Plinko path animation, twelve-pocket wheel and prize controls. Wire authoritative mutations/persistence through `CampaignFlow`; preserve reduced motion and committed-use routing. Add focused UI checks and rendered screenshots.
- [x] **Server/browser:** Expose strict arcade actions, projection and capabilities. Add an isolated browser Arcade renderer/controller with stable local navigation and poll-safe choices. Test API and browser prize flows, limits, reloads and mandatory use.
- [x] **Assets:** Import the two supplied Plinko attachments, preserve source provenance, wire the real sprites into both clients and verify their geometry. Use a native vector roulette wheel.
- [x] **Integration:** Review cross-client contracts and saved-state validation; run relevant tests, inspect screenshots, update player rules and rebuild/sign the Mac app.

## Shared interfaces

`ArcadeResult(string PlayId, ArcadeGame Game, int Choice, int Landing, int Score, int[] Path, FeatureCategory? PrizeCategory, string? FeatureId, CardId[] TargetCards)` and `ArcadeState(bool PlinkoPlayed=false, bool RoulettePlayed=false, ArcadeResult? Pending=null)`. A losing result has null prize fields and no target cards. Plinko Path has fifteen lattice positions (0–26), starting at `6 + Choice * 2`, with fourteen steps of ±1 constrained to the artwork's peg rows. Native X is `78 + 40 * position`; the last position maps to eleven bins with `clamp((x - 64) / 98, 0, 10)`. Roulette Path is empty. Prize feature IDs are existing catalogue keys.

All `CampaignArcade` mutation methods return `CampaignRun` and accept the existing optional expected revision. Communicate any necessary contract changes across the three owners before implementation diverges.

## Review corrections

- Both clients stage Arcade mutations and publish them only after a successful save. A failed write leaves the prior wallet, RNG, result, inventory and use session intact; retrying applies the action once.
- Full inventories can discard a held Tarot or Sigil while retaining the pending prize, then keep it. Immediate use remains available without a free slot.
- Finishing a committed pre-Arcade purchase upgrades the browser shop before offering games, preserving the paid purchase and removing obsolete unsold Sigils.
- The ball follows the supplied peg rows and rests at native y=1082, above the printed slot number. The board and dropper retain their original pixels and aspect ratios.

## Verification completed

- Core/App suite: 267/267 passed. Server suite: 16 groups passed, including failed-write recovery and legacy paid-session migration.
- Desktop suite: 92 passing checks; rendered Arcade suite: eight passing groups. Logs and screenshots are under `artifacts/arcade/desktop/`.
- Static browser/artwork tests: 49/49 passed. Full HTTP/browser regression: 33 passed; eight focused Arcade journeys passed after the persistence fixes.
- Desktop and 390px browser layouts, in-motion frames, final Plinko bins and roulette pointer alignment were visually inspected. Browser screenshots are under `artifacts/arcade-browser/`.
- Both supplied PNGs retain their exact original bytes. All 75 existing runtime textures remain unchanged, including the 32 approved Joker frames.
- macOS arm64 Release publish succeeded; `dist/osx-arm64/Bid Euchre.app` was rebuilt and passed `codesign --verify --deep --strict`. Publishing reported an unavailable NuGet vulnerability-data lookup and the existing `TextBox.Watermark` deprecation warning.
- Independent review found no remaining actionable issues. `git diff --check` passes. No commit or remote deployment was made.
