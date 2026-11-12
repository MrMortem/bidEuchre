# Card Sprites Implementation Plan

> Implement inline in the existing workspace, preserving pre-existing edits.

**Goal:** All 24 card faces render as transparent sprites over a replaceable white background in both clients.

**Architecture:** Shared PNG face atlas with six rank columns and four suit rows. Background is an independent PNG layer; desktop crops the atlas and web uses sprite positions. Individual PNG exports and a JSON manifest make assets reusable.

**Tech Stack:** Avalonia/.NET, browser JavaScript/CSS, generated transparent court artwork, deterministic asset assembly.

**Spec:** docs/superpowers/specs/2026-11-11-card-sprites-design.md

## Constraints and review focus

Preserve existing user edits, action labels, legal-card behavior, hover/focus visibility, backs and animations. Atlas coordinates must cover all 24 identities once. Transparent empty areas must expose a swapped background, including courts. Background must stay independent of hover highlighting. Assets must ship in native and web builds.

## Tasks

- [x] Add a browser regression for all 24 sprite coordinates, accessible labels and independent backgrounds; run and confirm missing sprite behavior.
- [x] Generate transparent court source art; assemble transparent faces, atlas, white background, mapping manifest and preview. Verify dimensions, alpha, all identities and readability.
- [x] Replace desktop live face construction and web live face markup with shared sprites; preserve card wrappers, labels and interactions.
- [x] Run web regressions, .NET build/rules tests where available, and visual review. Document asset use and any unavailable checks.

Ruling: execute approved work inline without a new approval round; avoid moving pre-existing in-progress card animation changes into a different checkout.


## Verification ledger

- Added identity/manifest regressions and observed both fail before implementation.
- All 21 Node tests pass, including byte-for-byte atlas crop comparisons, actual alpha coverage in every face, and fully opaque pure white background pixels.
- Full `test.sh` passed, including 39/39 .NET tests, browser regressions, native packaging checks and installed optional engine tests. Set `AVALONIA_TELEMETRY_OPTOUT=1` because the build telemetry log directory is outside the allowed workspace.
- Solution built with zero warnings/errors using `--no-restore -p:UsedAvaloniaProducts=`. Normal packaging completed with NuGet vulnerability-feed warnings due unavailable network access.
- `package.sh osx-arm64` rebuilt the self-contained executable and Mac app in dist/osx-arm64.
- Browser UI: created a temporary local table and started a hand; confirmed crisp sprite faces, independent white background URLs and correct computed atlas positions. Browser error logs empty. Screenshot: docs/previews/card-sprites-web.png.
- Independent review found no important bugs. Its alpha-test suggestion is covered by tests/web/card-sprite-assets.test.cjs.
- Preserved pre-existing work, including concurrent card-flight changes. Existing running native game was not restarted; it loads the rebuilt artwork on its next launch.
