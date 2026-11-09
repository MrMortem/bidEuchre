# Animation Refinement Implementation Plan

> Execute inline using superpowers:executing-plans. This is a focused refinement of the approved pixel-art design.

Goal: Give both clients brisk, tactile motion without changing rules or bot pacing.

Design: Short staggered deals; newly played cards arrive from their seat with a small settling overshoot; a single turn nudge, winner accent, and score bump. Preserve card identity and layout. Never replay motion on unchanged polling, reconnects, viewer switches, or unrelated legal-action updates. Keep existing trick DOM nodes so another player's move cannot restart an in-flight card. Respect browser reduced-motion and offer a motion toggle in both clients. Use local animation primitives; no new packages or art.

Alternatives: Ambient looping motion would distract from cards; a hover-only polish would miss actual game events. Use event-driven, one-shot motion as a direct extension of the user's approved visual direction.

- [x] Test public-state motion decisions, including repeated polls, viewer switches, completed tricks, new deals, and score changes.
- [x] Implement a small motion tracker in JavaScript and C# with no private-hand dependency.
- [x] Integrate keyed trick rendering and web animations; preserve hover/focus/fan transforms by animating wrapper elements.
- [x] Add native arrival/settle, turn, winner and score animations; cancel on detach or motion disable.
- [x] Verify both builds, motion tests, existing .NET tests, and live UI. Review and create a draft PR; do not merge without instruction.

Review focus: No replay on polls; no private hand reveal; quick bot sequences; no animation transforms overriding fan or hover; motion-off immediately cancels active effects; reduced-motion skips imperative animations too.


Verification:
- Motion tests were run failing before implementation, then passing (7 browser tests, including real openSession/selectViewerSeat lifecycle coverage; 2 C# motion cases inside the 31-test suite).
- Whole solution build: zero warnings/errors. .NET suite: 31/31. Browser motion tests: 7/7. JavaScript syntax, shell syntax, and diff whitespace checked.
- Live web spectator game at Quick pace completed all six tricks without browser console errors; Motion: Off works. Native spectator game completed all six tricks and motion-off was checked in the UI.
- Native/browser animations use separate wrappers/individual transform properties so card hover and fan transforms remain intact. Reduced-motion blocks imperative browser effects, and both clients allow cancellation via the motion toggle.
- Review findings fixed: reconnect now resets the event baseline; changing viewer/session or leaving the table cancels ongoing browser effects before fetching. Both have RED→GREEN regression coverage. Native toggle label now reflects its current setting on new tables.
- Full test.sh still encounters the existing Python 3.9/zip(strict=True) incompatibility in the unchanged CFR bot tests; C++/PyTorch/Prolog prerequisites remain unavailable, as recorded for PR #1.
