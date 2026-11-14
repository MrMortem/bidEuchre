# Remaining defect verification

Base: `d85af99` (`origin/main` when this work began). Scope: all 13 remaining open defect reports from the GitHub audit; feature requests are excluded.

| Issue | Disposition and evidence |
| --- | --- |
| #32 | Fixed. Authoritative contract construction rejects undefined modes and suits before mutation. Protocol tests cover valid contracts and numeric invalid values; HTTP regression verifies 400 and unchanged full private game view. |
| #33 | Fixed. Lobby tables expose named semantic Open buttons. Real Chromium Tab/Enter/Space checks verify visible focus and navigation into the table. Refresh preserves the focused table action. |
| #35 | Fixed. Rules travel with server build/publish output and load relative to the assembly. A temporary published directory outside the checkout serves the exact authoritative Markdown. Real Chromium verifies failed requests show Retry and a subsequent request renders rules; deterministic tests reject stale responses in both completion orders. |
| #36 | Fixed. Invalid and quoted-whitespace card syntax becomes ProtocolException. Real external processes verify play penalties and both exchange fallbacks without bot-loop faults. |
| #37 | Fixed. Timeout or cancellation permanently fails the channel closed and attempts process-tree termination. Subsequent decisions, observations, hand boundaries and starts cannot consume a late response. |
| #38 | Fixed. Safe weights-only loads validate into disposable model/optimizer copies. A disposable training probe checks optimizer usability. Thirteen corrupt payload types verify fresh state, preserved SQLite journal, actual ready handshake/action and valid replacement checkpoint. |
| #39 | Fixed. Hidden-card allocation enforces public voids from completed/current tricks using effective suits, including Left Bower. Exact capacity matching preserves visible/private exchange knowledge and explicitly rejects impossible assignments. High/Low/trump regressions cover these invariants. |
| #40 | Already resolved by feature retirement. Legacy Stinky enhancements and Beggar cards migrate out. A migrated reproduction with Sticky Fingers, active Devilish Deal, three complete hands and six-card deals cannot reach the old constrained-deck deadlock. No retired mechanic was reintroduced. |
| #41 | Fixed. Invalid version kinds and malformed saved shops are rejected before Continue. Coverage includes null offers/elements, duplicate IDs, two machine offers with distinct IDs, unknown IDs/categories, negative prices, valid blind/sold gumballs and legacy migration. Actual campaign UI recovers bad version/shop files to a playable fresh run. |
| #42 | Already resolved by menu scrolling and adaptive scale from earlier merges. Rechecked all offered effective viewports and minimum size for pointer/keyboard Settings recovery; existing MainWindow tests verify actual requested 150% and resize restoration. |
| #43 | Fixed. Full shop still overflowed at 980px despite the scale cap. Horizontal scrolling makes complete purchase/owned-card actions reachable at every supported effective viewport. |
| #44 | Fixed. Failed preference saves report inside active Settings, retain current selections, and clear after successful retry. Disposable-profile tests and Skia image inspection verify the error fits within the viewport. |
| #45 | Fixed. Campaign shop, owned, deck-target and Ante inventory cards have an independent white keyboard-focus outline. Tab/Shift+Tab, Enter/Space, motion off, and focus scrolling are covered; Skia images confirm separation from selection colour. |

## Validation

- `./test.sh`: exit 0; 174/174 core/app/protocol tests, 40 desktop checks, 28 Node interaction/motion checks, 12 HTTP/published/Chromium checks, Python CFR standard-library/full Torch suites, C++ engine and Prolog unit/process integrations passed.
- Full Torch suite: 39 tests, including real-process corrupt checkpoint recovery. The installed Torch environment emits its existing missing-NumPy warning; tests pass.
- Full suite build: zero warnings/errors on the final incremental build. Clean baseline had existing nullable/obsolete API warnings.
- Additional `BIDEUCHRE_RENDER_TESTS=/tmp/remaining-bugs-rendered dotnet run --project tests/BidEuchre.Desktop.Tests`: exit 0 after adding the Settings error viewport assertion; Settings and focus PNGs inspected.
- Independent reviews found whitespace parser errors, corrupt optimizer state, duplicate machines, and stale Rules responses. Each received a failing regression and a verified fix. No outstanding review findings.

Browser tests require the optional test-only install documented in README. No CI workflows are added by this change. Unrelated packaging edits remain in the original checkout.
