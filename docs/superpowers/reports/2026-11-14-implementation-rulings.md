# Implementation rulings

These rulings were made while implementing the settled issues. They supplement the eleven user decisions in the scope document; unresolved product choices remain excluded. Entries follow their order in the execution ledger.

| Ruling | Reason | Cost if wrong |
|---|---|---|
| Proceed with settled issue implementation without another plan approval. | The active goal explicitly authorizes that work. | The user may need to reject or revise the reviewable branch. |
| Run Tasks 1-3 concurrently with disjoint file ownership and coordinated builds; serialize shared Core changes. | Independent work can progress safely under the active delegation instruction. | Missed interactions require integration fixes. |
| Leave #70 open for a concrete partner-passivity policy. | Its issue identifies the bid threshold/pass policy as a design choice that the eleven decisions do not settle. | Partner behavior remains unchanged until the user supplies that policy. |
| Duplicate gumball points require a matching successful human bid; partner contracts and Panic Button do not qualify. | The issue specifies successful human bids, and Panic Button ends a match without a successful contract. | Duplicate Panic Button provides no score benefit; a different desired reward needs a design decision. |
| Duelist consumes its once-per-match use only when the bid wins the auction and activates. | An overcalled bid never starts a duel; invalid commands consume nothing. | Players can attempt Duelist again after being overcalled; a stricter attempted-bid policy would need a rule change. |
| Apply duplicate points after multipliers and include each matching type once. | This preserves literal +1 per extra copy, including overlapping Alone/High-Low qualification. | Overlapping bonuses may affect balance and would require an explicit adjustment. |
| Implement shop rerolls before duplicate/resume work with independent metadata and later integration tests. | Reroll count and stock replacement do not depend on new live snapshot APIs. | Persistence interactions require later integration fixes. |
| A reroll replenishes all four regular slots, including sold slots, while preserving the gumball offer and its sold state. | Paid replenishment must remain useful after buying out regular stock. | A different intended shop economy would require stock and price retuning. |
| Split browser campaign work into server/API and browser parts with an explicit shared wire contract. | Ownership is disjoint and both parts remain required for issue completion. | Contract drift requires coordinated fixes before delivery. |
| Start browser implementation while the score-explanation review waits for a free reviewer slot. | Implementation can proceed independently; unreviewed work is not published as complete. | A late score-model finding could require browser rework. |

No finding has been silently waived by these scheduling rulings. Each task still requires an independent review, and the full branch requires integrated validation and a final review.

## Final review boundaries

The controller checked the final reviewer's declined-to-judge list against the issue snapshot and scope. These additional rulings preserve those boundaries explicitly.

- Keep #70 excluded until a concrete partner-passivity policy is supplied. The issue explicitly requires a design choice; partner behavior remains unchanged if a policy was expected now.
- Keep #94 aggression and statistical balance tuning deferred. The user explicitly deferred aggression; the new balance has behavioral tests but no target win-rate claim.
- Keep #23, #26, #56-60 and #65-66 excluded pending their listed product decisions. No missing design is invented; those features and payout changes remain unimplemented.
- Do not add optional shop locks/discounts/rarity or bot-generated-Tarot rules under #27. Locks/discounts/rarity are explicitly optional, and bot Tarot behavior needs a separate rule choice; those observations remain outside the delivered reroll/availability/sell-value behavior.
- Resolve #24 with the built-in heuristic alternative; do not invent difficulty tiers or claim measured superiority. The issue offers alternative improvement paths; the legal/partner-aware heuristic is delivered, but difficulty tiers and benchmarked playing strength remain future work.
- Keep arbitrary external-engine bundling and account/cross-device campaign sync outside this branch. The plan requires bounded discovery and per-browser saves; users still need their own external engines and a browser-specific save context.
