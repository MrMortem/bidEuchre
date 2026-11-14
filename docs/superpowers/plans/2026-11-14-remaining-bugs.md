# Remaining defect implementation plan

**Goal:** Recheck and resolve open defect reports #32, #33, #35–#45, then submit one PR with closing references.
**Architecture:** Fix each failure at its input or UI boundary. Preserve existing legal gameplay, safe checkpoint loading, campaign migration, and local-only server behavior.
**Tech stack:** C#/.NET 10, Avalonia headless/Skia, Node browser regressions, Python/PyTorch.
**Spec:** GitHub MrMortem/bidEuchre issues #32, #33, #35–#45; feature requests remain outside scope.

- [x] Rules/protocol: undefined contracts (#32), malformed card responses (#36), fail closed after timeout/cancellation (#37). Add domain, HTTP, and real-process regressions.
- [x] CFR: recover corrupt checkpoints without unsafe loading or journal loss (#38); allocate hidden cards under public void and private exchange constraints (#39). Add deterministic and process handshake coverage.
- [x] Persistence: reject nonnumeric save versions and invalid saved shop records (#41). Recheck retired Stinky/Beggar path (#40) with migrated saves, active Devilish Deal, and complete deals/hands.
- [x] Web: semantic keyboard-operable lobby navigation (#33); package rules for deployed execution and retry failed loads (#35). Verify a published server outside the source tree.
- [x] Desktop: recheck menu/shop at supported scales (#42–#43); visible recoverable settings-save errors (#44); independent keyboard focus and activation on campaign cards (#45).
- [x] Run complete suite, inspect rendered UI and actual browser keyboard behavior, review diff, and prepare the PR with issue-by-issue evidence.

Review focus: cancellation after sending go; stale replies across newgame; migrated blind Gumball offers; unknown/null schema fields; Left Bower void inference; focus distinct from selection; subsequent save recovery; retry after failed Rules load. Preserve unrelated packaging edits in the original checkout.
