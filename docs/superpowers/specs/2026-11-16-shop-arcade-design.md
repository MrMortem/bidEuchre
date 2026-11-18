# Shop Arcade

The user requested replacing direct shop Sigils with an Arcade submenu containing Plinko and roulette. Strong results earn a Sigil, lesser results earn a Tarot ticket, and misses earn nothing. The user explicitly selected a $3 charge and one play of each game per shop visit.

## Gameplay

Implementation choices: Plinko is one ball per paid play, with eight selectable drop lanes matching the supplied board's top row. Fourteen peg bounces move the ball left or right through the artwork's staggered rows, reflecting at each row's edges. The eleven printed landing scores are `6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6`: 5–6 earns a random Sigil, 3–4 earns a random Tarot, and 1–2 earns nothing. Show these thresholds before payment.

Roulette is a compact twelve-pocket arcade variant. Choose a number from 1 to 12; odd pockets are red and even pockets black. Matching the number earns 100 points and a Sigil; matching only its color earns 50 and a Tarot; the opposite color earns nothing. The pointer and animated final pocket must agree with the saved result.

These are virtual game-money transactions. Use the existing campaign economy and item catalogue. The Fortune Teller, Jokers and gumball machine remain available.

## Transaction and prize rules

The shared App layer validates choices, price, per-visit usage and expected revision. Payment, RNG consumption, path/landing, score, prize identity and six eligible cards are one saved transaction before presentation. Animation never determines a result. A restart preserves the same paid result and consumed play.

A winning result offers Keep when the shared inventory has room, or Use Now on its saved six-card opportunity. Use Now enters the existing committed shop-use flow: the prize must be applied, and held Tarots can also be used on those same six cards. A full inventory must not lose a prize or prevent Use Now. Inventory discard remains available while claiming a prize.

A losing result can be acknowledged. Pending results block other paid plays, shop departure and rerolls until handled. Rerolls never replenish arcade plays. New shop visits replenish both games. Legacy committed Sigil purchases remain finishable; unbought direct Sigil stock is removed when an old visit is upgraded.

## Data and clients

Use separate `ShopState.Arcade` state, not a new collectible category. Persist played flags and one pending result. A prize committed for immediate use can be represented by a hidden, sold, zero-price offer so the existing use-session rules continue to apply.

Both desktop and browser have a dedicated Arcade screen, accessible controls and saved-result animations. Reduced motion shows the same result immediately. Browser polling preserves local choices on unchanged state and never repeats payment or animations. Strict server requests accept only legal player choices, never client-provided scores or rewards.

The user supplied `Plinko Body.png` (1199×1312, opaque) and `Plinko Dropper.png` (1312×1199, alpha) as local attachments from the Euchatro project. Import their exact bytes as `arcade-plinko-board` and `arcade-plinko-dropper`, retaining original files and checksums. The dropper moves along the top rail; a native ball follows the saved peg path into the printed slot. The roulette wheel is a native vector/control graphic that can spin and render accurately at different sizes.

## Validation

Cover all prize tiers, insufficient funds, duplicate requests/claims, stale revisions, invalid choices, per-visit limits, rerolls, restart during pending results and committed use, full inventory, held multi-target Tarot use, old paid Sigil sessions, and malformed saves. Render both games and verify that ball paths and wheel pointers agree with authoritative results. Preserve unrelated workspace changes. No commits or remote deployment are requested.
