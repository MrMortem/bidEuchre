# Consumable targeting implementation plan

**Goal:** Limit stored consumables to live hand use and new shop purchases to six saved random cards.
**Architecture:** Validate persisted per-offer samples in CampaignShop; expose explicit eligible cards to desktop and web; preserve Core's in-hand rules and all existing artwork/Fortune Teller work.
**Spec:** `docs/superpowers/specs/2026-11-15-consumable-targeting-design.md`.
**Tech stack:** C# Core/App/Avalonia/ASP.NET; JavaScript browser client.

- [x] App/Core: add ShopOffer.TargetCards, six-card sampling/read helper, atomic sample validation, legacy save upgrade and save validation. Reject held use in Shop/Intro; leave discard intact. Restrict shop Gambler to sample. Test deterministic persistence, rejected requests and hand boundaries.
- [x] Desktop: replace full-deck purchase pickers with six-card samples; show a confirmable sample for Gambler; remove held-use controls in Shop/Intro; preserve live gameplay. Test and render.
- [x] Server/web: project sample and printed tickets; expose current Fortune Teller actions through existing mutation/CAS pattern; remove outside-match held use and full-deck target fallback. Test adversarial calls and browser presentation.
- [x] Verify complete related suites, persistence and interaction flows, inspect screenshots, independent integration review, rebuild Mac app and document final behavior.

Review focus: forged card IDs, direct Buy sigil path bypasses, Gambler's automatic selection, duplicated card faces, canceled/reloaded/reopened offers, legacy paid ticket state, capacity-full immediate use, stale actions, read-only deck inspection.

Continue in the active workspace to preserve preceding uncommitted work. The user explicitly authorized execution and selected new purchases only. No commit or publication requested.

## Verification completed

- New regressions first reproduced unrestricted stored use and direct off-sample Sigil application. App/Core now pass 250/250 tests, including persistent samples, legacy upgrades, malformed saves, bounded Gambler effects and hidden/played-card rejection during matches.
- Desktop full suite and rendered shop suite passed. Six-card selectors and explicit Gambler confirmation were visually inspected; screenshots are in `artifacts/consumable-targets/desktop`.
- Mac application rebuilt and signature verified. Restore required regenerating the osx-arm64 assets target; packages were available locally. NuGet vulnerability-feed access warnings and pre-existing compiler warnings do not prevent the successful package.
- Independent review caught two legacy paths: web Shop loads omitted sample upgrade, and unversioned saves omitted explicit CardId-shape validation. Both were reproduced in failing tests and fixed. Concurrent web reads now upgrade once, preserve metadata/payment/tickets and retain the sample across restart. Bare malformed samples are rejected; the complete core suite passes 250/250 after the final fix.
- Final .NET server suite passed. Web unit tests passed 37/37; real HTTP tests passed 6/6 and browser journeys passed 16/16. Real paid-Gambler tests confirm cancellation/reload persistence and exactly two changed cards within the six-card sample.
- Browser screenshot capture waits for card atlas decoding. Final desktop/compact Sigil and Gambler pickers were visually inspected in `artifacts/consumable-targets/web`.
- Final Mac app was rebuilt after the loader fix and its signature verified. Diff formatting checks passed. No commits or publication performed.
