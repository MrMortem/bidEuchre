# Import the approved Joker artwork

The user requested importing the 32 approved Joker exports into the game and replacing the old artwork. The sprites already have matching rounded corners, cream borders and dark outlines. Keep the existing gameplay IDs and shared asset filenames so desktop and browser shops, collections and inventories all resolve the replacement portraits.

- [x] Preserve the original 32 pre-frame runtime inputs and provenance separately from game textures.
- [x] Regenerate the sheet against those preserved inputs, avoiding repeated frame processing after import.
- [x] Add a curated import manifest mapping all 32 approved sprites by checksum. Copy their PNG bytes directly and retain the original Drive source metadata.
- [x] Confirm all 32 runtime PNGs match the approved sprites and all 43 other imported assets retain their previous hashes.
- [x] Verify sheet geometry, provenance, browser mappings and exact runtime matches: 12 checks passed.
- [x] Verify desktop embedded resources and rendered shop/collection, server output copies, and independent rebuild/review.
- [x] Rebuild and verify the macOS app and refresh the downloadable asset pack.

Files: `assets/curated-source/2026-11-16-joker-frame-inputs` preserves the pre-frame inputs; `assets/curated-source/2026-11-16-euchatro-joker-frames/manifest.json` is the active import manifest; `assets/sheets/jokers` contains approved sprites and the sheet; `assets/pixel/curated-joker-*.png` are the shared game textures. Original Drive artwork remains preserved. No gameplay logic, commits, or remote deployment are part of this import.

Initial verification: `/private/tmp/joker-import-web-checks.log` records 12/12 passing checks. Runtime import log: `/private/tmp/joker-import-runtime.log`. Before-import provenance for the 32/43 comparison: `/private/tmp/joker-import-runtime-before.json`.

Final verification:

- Desktop build and focused rendered suite passed. All 32 embedded Joker PNG hashes match the approved exports. Shared rounded corners, outline/rim colors and proportional display passed. Shop and collection screenshots reviewed in `artifacts/joker-import/desktop`. Logs: `/private/tmp/joker-import-desktop-build.log` and `/private/tmp/joker-import-desktop-render.log`.
- Server build passed with zero warnings/errors. All 32 copied `wwwroot/pixel` Joker PNG hashes match the approved exports. Log: `/private/tmp/joker-import-server-build.log`.
- Independent review found no issues. Rebuilding into temporary directories reproduced every sheet sprite, atlas, frame, preview and all 75 runtime PNGs byte-for-byte; exactly 32 Jokers changed and 43 other assets stayed unchanged. Logs: `/private/tmp/joker-import-review-9G5qtp/sheet.log` and `runtime.log`.
- Release macOS arm64 publication, bundle assembly and strict signature verification passed. Logs: `/private/tmp/joker-import-macos-publish.log` and `/private/tmp/joker-import-macos-bundle.log`. The existing Desktop `TextBox.Watermark` warning remains unrelated.
- Refreshed `assets/sheets/euchatro-jokers-asset-sheet.zip`; archive integrity passed. `git diff --check` passed.
