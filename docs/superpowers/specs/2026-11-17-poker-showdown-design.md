# Poker showdown

The user explicitly replaced Poker's old six-trick continuation with: reveal all cards, highlight the winning poker hand, award the points, and redeal. No contract-mode selection or trick-taking follows a winning Poker bid.

Keep the unlocked, human-only, exact-four bid and current-face best-five-of-six comparison. Each team tied for the best hand earns four points once. Show the five physical cards making each tied winning hand, the winning player(s), category, and award. The award is a Poker score component, not a Joker or made/set contract. Poker duplicate copies pay when the human team wins Poker; High/Low and trick-only bonuses do not apply.

Use a dedicated persisted PokerReveal phase. At auction completion, reveal and award once, preserving a result with winning physical card IDs. Both clients show the result for three seconds and then acknowledge it through the existing guarded campaign command path. Acknowledgment advances the dealer and deal, or finishes the match if the points or normal round limit end it. Reopening a saved reveal shows the result again without re-awarding points. A Poker showdown counts as one round. These presentation-duration and round-count choices use the existing campaign progression model.

The alternative of immediately dealing and presenting only a transient client animation would lose authoritative reveal state on reload. Reusing trick-play or a synthetic High contract would retain the rule error. A dedicated reveal phase keeps the state explicit and recoverable.

Retain older in-progress Poker trick-hand checkpoints for compatibility; every newly won Poker auction uses the corrected showdown. Protect all pre-existing uncommitted work, add no dependencies, and do not publish or commit unrelated changes.
