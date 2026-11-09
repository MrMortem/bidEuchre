# Production Polish Pass

Continue the approved visual/animation direction on the unmerged animation PR #2. Implement inline; leave merging to the user.

The focused pass improves actual play and local release usability:
- Persist motion preferences in both clients. System reduced-motion takes priority in web; hidden tabs do not animate, and returning establishes a fresh baseline.
- Make web loading and connection loss visible, lock stale actions, clear private hand cards immediately when switching viewers, and scope asynchronous completions to their originating view. Retry reads only; never automatically resubmit a move.
- Size the native table against the available window height so action controls remain reachable without scrolling past a full-size table. Preserve readable labels and the existing logical board positions.
- Produce a host-native Mac .app with bundled runtime, art, font, rules, and license. Keep explicit runtime overrides, and document the unsigned local package's launch path.
- Make optional Python bot checks skip clearly when Python 3.10+ is unavailable, matching existing optional engine prerequisites, while still running the .NET and browser checks.

Avoid new game modes, network multiplayer, sound/assets, rule changes, distribution signing, or unrelated bot changes. Ambient visual effects and new screens are unnecessary for this pass.

Verification: regression tests first for preferences, hidden-tab behavior, stale hand input, lost/recovered connection, and late action completions; full available test script and Release builds; live desktop/web play; open the self-contained Mac app; check narrow web and native window layouts. Independent code review before updating PR #2. Record unavailable checks rather than claiming a fully production-certified release.


## Completed checks
- Full available `./test.sh`: 33 .NET tests, 15 browser tests, and Mac bundle construction/rebuild checks pass; solution build has zero warnings/errors. Optional C++/Python/PyTorch/Prolog engine suites clearly skip absent compatible prerequisites.
- Preference and input-guard tests observed failing before implementation. Review findings for uncertain POST responses and background-tab resume were reproduced with tests, then fixed and verified passing. No action is automatically retried.
- Browser live UI: two human seats switched correctly with exactly six face-up cards in the chosen hand and zero in all others; no console errors during normal play. Stopping the server showed the reconnect banner and disabled actions. Restarting it showed the missing-table recovery message and removed the retry button. Narrow 390px connection layout has no horizontal overflow.
- Published host-native self-contained osx-arm64 Release app; rules and font license confirmed beside the executable inside the bundle. Opened the .app through the OS and created/started a game without a dotnet launcher. First automated launch observation was unusually slow; subsequent UI interactions were responsive. Launch-time performance was not measured.
- Native default-size table/action layout visually checked, including visible bidding controls; motion setting write confirmed at the user preference path and restored to On afterward. Corrupt/missing preference behavior covered by automated tests. Native minimum-size drag verification was unavailable because the UI automation surface could not perform coordinate drags; do not claim that interaction was tested.
- Local app is unsigned; signing/notarization and distribution-platform QA remain outside this pass.
- Screenshots: docs/previews/production-desktop.png and docs/previews/production-connection.jpg. Existing user .DS_Store left untouched and ignored.
