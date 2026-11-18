# Euchatro asset refresh implementation plan

**Goal:** Import the November 15 source set and replace matching artwork in desktop and web.

**Architecture:** Extend the existing curated PNG pipeline, keep stable catalog identifiers, and share exported textures across both clients. Preserve original source bytes. Use the new Devilish Deal design for the existing ticket and retain its previous design as an alternate resource. Import three unimplemented item images as resources without adding game rules.

**Tech stack:** C# / SkiaSharp asset builder, Avalonia desktop, plain JavaScript/CSS web.

**Spec:** User request to import new/updated artwork; source inventory in `assets/curated-source/2026-11-15-euchatro/manifest.json`.

## Constraints and review focus

- Continue in the current workspace, preserving earlier uncommitted art integration.
- All 31 matching Jokers must use new art; Double Down uses the existing card back; the missing web placeholder was repaired to match desktop.
- Portrait artwork uses aspect fit. Machine art must not obscure prices/actions or change purchase semantics.
- Source checksums and provenance remain verifiable, including previous-directory references.
- Boomerang, Over Achiever and System Error remain art-only until their rules exist.

## Tasks

- [x] Extend `tools/CuratedCardArtBuilder/Program.cs` for Joker portraits and machine images, explicit null runtime mappings, full current source set and clear inactive-art metadata. Export all 75 assets with source hashes and preview.
- [x] Update desktop shared icons and shop machine presentation; preserve animation and actions. Verify asset routing and render representative shop/collection views.
- [x] Update web curated Joker routing and machine offer presentation. Run web/server presentation regressions.
- [x] Validate all output/source hashes and mappings, review integration, run desktop/project checks, rebuild the macOS app, and inspect rendered images.

The direct import request authorizes these reversible changes. No commit, publication, or gameplay expansion is planned.

## Verification completed

- All 75 original/labeled sources and exported PNG hashes/dimensions validated. All output identifiers are unique; 64 mappings resolve to current gameplay items. Exactly four previously active textures changed: Gambler, Taxes, Wild, Devilish Deal.
- Latest manifest exports 75 resources; legacy 19-asset manifest still exports successfully. Source image bytes are unchanged.
- Desktop suite passed 78 checks, including animated and motion-off gumball purchases. Actual Skia renders verified Joker collection, tickets, machine shop and arena. Globe-ball placement was refined after visual review.
- Core suite passed 244 tests. Server test program passed. Web/server JavaScript suite passed 51 checks; 24 optional Chromium journeys skipped because Playwright is unavailable. Final curated-art suite passed 7 checks after the Double Down fallback correction.
- Desktop and compact web layouts were visually inspected in the available browser; new resources all loaded and no horizontal overflow appeared. Independent integration review found no actionable issues.
- Apple Silicon Mac bundle rebuilt and codesign verification passed. One pre-existing obsolete TextBox.Watermark compiler warning remains. Web build passed with zero warnings/errors.
- Rendered evidence: `artifacts/nov15-desktop/`. Runtime provenance: `assets/pixel/curated-art.json`. No commits or publication performed.
