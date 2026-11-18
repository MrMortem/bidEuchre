# Gambler two-card targeting

The user requests selecting two cards for Gambler. It now requires exactly two distinct physical IDs, makes those cards Lucky, and consumes no random draws. Match targets must be in the current hand; a newly printed shop ticket targets only its saved six-card opportunity. Fewer than two eligible cards prevents use. Existing save identities, prices and other consumables remain unchanged.

- [x] Core/App: require two targets, apply Lucky to exactly those IDs and preview both; validate bad cardinality, duplicate/out-of-scope IDs, unchanged RNG, atomic rejection and one-time use.
- [x] Desktop/web: use normal two-card pickers in hand/shop, remove random-preview exceptions, disable confirmation until exactly two are selected; preserve sample across cancel/reload.
- [x] Verify related full suites and real browser/API selection; inspect selected-card render, review and rebuild Mac app.

Initial tests failed as expected because the current implementation accepted an empty target list; the real browser also projected zero targets before the change.

## Verification

- Core/App regressions first failed because empty Gambler target lists were accepted. After the change, the complete core suite passed 250/250. Invalid target counts, duplicates, hidden/offered-set violations, unchanged RNG and exact chosen IDs are covered; both selected cards preview Lucky.
- Desktop full suite and shop rendering tests passed, including shop and held-hand selection, deselection, cancellation and reload. Screenshot: `artifacts/gambler-desktop/shop-gambler-two-selected.png`.
- Web unit tests passed 37/37; campaign HTTP/browser tests passed 24/24; .NET Server.Tests passed. Real server journeys verified shop and live-hand targeting. Desktop and compact browser screenshots were visually inspected in `artifacts/gambler-selection/web`.
- Independent focused review found no actionable defects. Diff checks passed.
- macOS arm64 app rebuilt and signature verified. Runtime restore was refreshed before publishing; existing compiler warning about TextBox.Watermark remains. No commits or publication performed.
