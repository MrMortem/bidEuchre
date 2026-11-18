# Committed shop consumables

The user requests using inventory Tarots while a new purchase's random six-card menu is open, and requires the purchased item to be applied before leaving that menu. This replaces the earlier restriction to the newly purchased consumable alone.

Opening the application menu commits and saves the purchase. A Sigil is charged once; a printed ticket has already been paid for and is chosen without another charge. The offer is sold and an optional `ShopState.UseSession` references its saved six physical card IDs. No target redraw occurs on reopen or reload. The purchased item and any inventory Tarots can be used in either order. Applying the purchased item enables Done but keeps the opportunity open for remaining Tarots. Done clears the session. Ordinary buy-to-inventory and keep-ticket choices remain available before entering the menu.

The domain enforces the obligation independently of UI navigation. It rejects leaving, restocking, other purchases, sales and discards during an active menu, and rejects Done while the purchased item remains unused. Held Sigils remain match-only. Target counts and physical-card restrictions remain unchanged, including two distinct cards for Gambler and three ordered cards for Devilish Deal. Non-targeted Tarots are available through explicit Use actions. Saving records the commitment, purchase-used flag, current inventory and card effects atomically.

- [x] Domain and persistence: introduce the session and begin/use/finish operations, strict save validation, compatibility for absent sessions, immutable rejection and revision checks.
- [x] Desktop: dedicated six-card screen with purchased/held Tarot choices, current card effects, required-use status and gated Done; resume into it.
- [x] Server and browser: project the session, enforce authoritative actions and render an equivalent persistent screen.
- [x] Player documentation: explain the inventory Tarot exception and mandatory purchase use.
- [x] Verify payment, same cards after reload, held Tarot before/after purchase, no double use, no bypass, zero-target effects, Gambler/Devilish selection, malformed saves, desktop rendering and browser journey.
- [x] Review implementation, rebuild the macOS app and verify its signature.

The application, desktop and server/browser changes are assigned to separate agents with disjoint file ownership. Root coordinates documentation, integration verification, review and packaging. No commits or publication are part of this request.

## Verification

- Core/App: 258/258 tests passed, including eight new session regression groups covering every Tarot, immutable rejection, payment and exact-once use, all shop exit/mutation guards, malformed saves and current/legacy format handling.
- Desktop: full suite passed with 80 PASS results. Committed menus were rendered at 980×680 and visually inspected; selected Gambler previews, required purchase, optional tickets and disabled Done remain clear. Screenshots are in `artifacts/committed-shop/desktop`.
- Server/browser: 12 .NET server groups, nine HTTP tests and 38 web unit tests passed. The final complete browser rerun passed 19/19 after review, with no skipped tests. Real journeys cover reload/restart before and after purchase use, held Tarot effects, payment, Escape and authoritative rejection.
- Independent review found a browser polling reset: disappearing mutation feedback caused an unchanged-version repaint to discard the selected Tarot and targets. The fix preserves ordered selections through unchanged-version feedback/save-status updates, clears them on new versions, and passed both a new regression and the reviewer's independent reproduction. No other actionable findings remain.
- Desktop and compact browser screenshots were visually inspected in `artifacts/committed-shop/web`. Diff whitespace checks passed.
- macOS arm64 package rebuilt and `Bid Euchre.app` signature verified. Only the existing compiler warnings remain (nullable catalogue tests and obsolete `TextBox.Watermark`).
