# Panic Button retirement and Double Down artwork

The user requests removing Panic Button from the game and adding Double Down's sprite. Remove the action, unlock, catalogue entry and player-facing descriptions in both clients. Preserve the numeric identity of High/Low (8) and all other remaining items. Existing saves lose only the retired unlock/extra copies, including mirrored live-checkpoint resources. Retain the historical scores and hand state of any already-used Panic Button. A sold machine stays sold after migration.

Double Down's current effect remains unchanged. The November 15 source set contains no Double Down image, and a November 16 check of the Drive project still shows the same 75 sources. The user explicitly chose the existing unused Boomerang artwork for Double Down. The November 16 source manifest maps that preserved original to `joker:double-down` and the shared curated portrait pipeline exports `curated-joker-double-down.png` for desktop and browser surfaces. No image generation or source-pixel editing was performed.

- [x] Core/App retirement, selective save migration and regressions.
- [x] Desktop/server/browser action removal, eight-gumball presentation and protocol rejection tests.
- [x] Double Down source artwork, curated import, display mappings and rendered verification.
- [x] Current player documentation updated.
- [x] Full relevant test suites, focused independent review and rebuilt macOS app.

Work is split by file ownership: Core/App; client code; source artwork/docs/integration. Preserve the ongoing uncommitted work from previous requests. No commits or remote publication are part of this request.

## Verification

- Core/App: 262/262 tests passed, including five retirement groups covering active grants, legacy shops, live hand/history preservation, known-ID-only migration, and malformed retired data. Log: `/private/tmp/panic-core-full-final.log`.
- Desktop: all 80 checks passed. Curated render checks verified Double Down in the collection and shop, and the eight-gumball roster. Logs: `/private/tmp/retire-panic-desktop-final-tests.log` and `/private/tmp/retire-panic-desktop-curated.log`. Screenshots: `artifacts/panic-double-down/desktop/double-down-collection.png` and `curated-shop-machines.png`.
- Server: all 13 groups passed after rebuilding with the final App migration. Log: `/private/tmp/retire-panic-server-final-tests.log`.
- HTTP/browser integration: 28/28 passed with no skips. Web checks: 39/39 passed. Logs: `/private/tmp/retire-panic-http-browser-tests.log` and `/private/tmp/retire-panic-web-tests.log`.
- Independent review identified one migration validation gap, which was fixed before packaging. All 14 follow-up probes passed, confirming corrupt retired counts, duplicate aliases, resource mismatches and unknown IDs are rejected while valid historical aliases migrate. No remaining review findings.
- All 75 source/output asset hashes and the source-manifest hash validated. Double Down's export is byte-identical to the existing Boomerang runtime portrait, and all 32 active Jokers now resolve curated artwork.
- Release macOS arm64 publication and app assembly succeeded; `codesign --verify --deep --strict` passed for `dist/osx-arm64/Bid Euchre.app`. Logs: `/private/tmp/panic-double-down-package.log` and `/private/tmp/panic-double-down-bundle.log`.
- `git diff --check` passed. Build warnings were existing nullable/Watermark warnings and unavailable NuGet vulnerability-feed warnings; compilation and packaging succeeded.
