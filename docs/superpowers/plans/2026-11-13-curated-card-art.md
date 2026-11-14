# Curated card art integration

Import the user's nineteen supplied images using their correctly labeled Downloads filenames: eight enhancement backgrounds, eight Sigils, three Gumballs. Preserve originals and existing fallback art. Do not regenerate or reinterpret the curated artwork.

- [x] Preserve originals and stable feature mappings; export native sprites with their alpha intact and a labeled contact sheet.
- [x] Add GUI regressions for curated enhancement selection, Sigil badges, matching shop icons, Gumballs and unprovided-art fallback.
- [x] Wire curated images into the shared C# renderer, including hand/shop/trick/flight Sigil state. Keep rank and suit markings readable on the darker backgrounds.
- [x] Run GUI/full checks, review the composite, and rebuild the Mac app.

Runtime resources use a curated- prefix, retaining all previously generated assets. Card Sigils sit below the upper-left rank and suit, at 63x63 on the 124x176 reference card. Existing game rules and feature IDs stay unchanged.


## Verification ledger

- Downloads filenames resolved all uncertain mappings, including three Gumballs rather than Sigils.
- Preserved all19 original images; SHA256 equality against the named Downloads originals passed. Export/manifest IDs match all19 entries.
- Observed GUI tests fail on missing Sigil rendering overload before implementation.
- Headless GUI suite passed, including all curated backgrounds, shop Sigils, card badges, three Gumballs, original-art fallback, and duplicate physical-card enhancements/Sigils in current/completed tricks.
- Full test.sh passed:145/145 core/protocol/campaign tests, browser/asset tests,12 GUI checks, installed optional engine suites and packaging checks.
- Independent review found no concrete bugs in mappings, image alpha, fallback, badge layout, physical identity or packaging.
- Mac app rebuilt successfully in dist/osx-arm64/Bid Euchre.app.
- Launched a distinct preview bundle with BIDEUCHRE_DATA_DIRECTORY pointing to a disposable generated campaign save. Visually verified the shop and shared deck, all enhancements and curated Sigil badges. Captured docs/previews/curated-art-native.png. Closed the preview; no user campaign save was modified.
- Preserved all previous artwork and unrelated project changes.

Final visual adjustment: Sigils are 63x63 at x=6,y=52 on the reference card, following the requested 25% reduction from84x84. Latest preview: docs/previews/curated-art-native-sigils-63.png. GUI regression checks and Mac packaging passed after this adjustment.
