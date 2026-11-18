# Euchatro artwork import

**Goal:** Import the supplied new and updated artwork and replace every active Tarot illustration with its matching arcade ticket.

**Approach:** Keep existing feature IDs, effects and saves. The nine current Tarot entries match all nine ticket images. Use the existing curated resource mechanism for item art, preserve ticket proportions, and use the five full arena images for campaign boss matches. Desktop and web use the same resources.

- [x] Rebuild all 38 runtime assets with source provenance. Map Festival to Chameleon, Ectoplasm to SpringLoaded, and Psychic artwork to Telepath. Keep the original downloads intact.
- [x] Display portrait tickets without stretching in the shop, collection and inventory; label the category Tickets. Preserve playing-card face readability over enhancement textures and Sigil artwork.
- [x] Select Club, Diamond, Heart, Spade and Cursed arenas for boss Antes 1–5, cycling for endless play. Normal matches and menus retain their existing scene. This is a presentation choice, not a new boss rule.
- [x] Verify embedded resources and routing, run desktop/web regressions and the project checks, and inspect rendered screenshots.

Primary files: `tools/CuratedCardArtBuilder/Program.cs`, source/runtime manifests, `PixelArt.cs`, `MainWindow.cs`, desktop category labels, web campaign presentation and regression tests.

Review focus: complete nine-ticket coverage; source alpha and proportions; alias mapping; boss-only selection/reset; card indices and Sigils remain legible; asset packaging.

Execution is authorized by the user's direct import request. Work stays in the active workspace with the previously downloaded sources; no publication or commit is required.

## Verification

- Builder and independent PNG/hash checks passed for all 38 outputs and unchanged sources. All nine active Tarot IDs have matching tickets. The legacy 19-item manifest also exports successfully.
- The new ticket test first failed on missing Festival artwork. Real Skia renders passed after import; the default headless backend substitutes 1x1 images, so the size check was corrected to decode embedded PNGs directly. The focused test and complete desktop suite then passed.
- Project checks passed: 247 core/protocol/app cases, 33 web regression cases, server tests, C++/Python/Prolog checks and macOS bundle checks. The full script initially stopped on the headless test assumption; the desktop suite and remaining Prolog checks passed separately after correction. The 24 optional Chromium browser cases were skipped because Playwright is not installed.
- Collection and shop screenshots were visually inspected and saved alongside the source package. Independent integration review found no actionable issues.
- Rebuilt `dist/osx-arm64/Bid Euchre.app`; its bundle signature verifies successfully. No gameplay or saved identifiers changed.
