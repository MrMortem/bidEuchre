# Enhancement background integration

User approved connecting the generated face substrates to each card's enhancement in the C# GUI.

- [x] Add renderer and deck-picker regression checks for all ten enhancement resources, unenhanced white fallback and unchanged face art.
- [x] Pass enhancement state into the shared renderer from campaign hands, deck/shop targets, current/completed tricks and card flight visual factories. Resolve physical cards by seat in the current/completed campaign trick, preserving duplicate printed faces.
- [x] Run desktop and full regression suites, build and repackage the native Mac app. Keep ordinary Table cards white and hidden card backs unchanged.

Use the existing background resource names; no new graphics or game rule changes. Preserve concurrent project edits.


## Verification

- Observed the new renderer test fail on the missing enhancement overload, then deck/shop picker and campaign hand tests fail until their enhancement state was passed through.
- Headless GUI checks cover all ten backgrounds, unenhanced fallback, printed face preservation, deck/shop targeting, campaign hands, duplicate printed faces with distinct physical enhancements, current tricks and completed tricks.
- Flight visual factories capture the same enhancement as their destination card.
- Full test.sh passed:131/131 core/protocol/campaign tests,26 browser/asset tests,11 headless GUI checks and installed optional engine/package suites.
- Independent review found no concrete issues with physical-card lookup, flight capture or packaged resources.
- package.sh osx-arm64 succeeded and rebuilt dist/osx-arm64/Bid Euchre.app.
- No game rules, save formats, hidden reverse-side artwork or web enhancement rendering changed. Preserved unrelated user edits.
