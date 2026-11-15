# Final branch review — settled Bid Euchre issues

Base: `174e987eeeaaff79997ce27b98be20bdddb823ee`
Reviewed head: `93263a1`
Checkout: `/Users/wtcolflesh/.codex/worktrees/settled-spec-issues/Bid Euchre`
Review date: 2026-11-14

## Strengths

- **One authoritative continuation path.** `CampaignRun.WithCheckpoint` (`src/BidEuchre.App/CampaignRun.cs:43`), the session action gate, strict v3 envelope validation, and explicit Core snapshot hydration carry the same resources and exact live state into desktop and browser saves. Physical IDs, order, historical faces, RNG, new duplicate counts, Duelist use and Double Down counters remain represented. Restore does not replay the deal or effects. The separate arrangement metadata preserves Sticky Fingers behavior after desktop Continue.
- **Scoring and explanation use the same arithmetic.** `src/BidEuchre.Core/CampaignEffects.cs:137` produces the breakdowns that `GameEngine.cs:494` applies. Fixed opponent and matching duplicate rewards remain after the whole-hand multiplier. Both cumulative totals are checked before publishing either score. The desktop summary, browser projection and persisted public result reuse the structured history rather than reconstructing scores from log text.
- **Presentation lifecycle is separated from game state.** `GameSession.cs:148` quiesces mutation for settings; `:367` uses a distinct contract presentation acknowledgment. Return, cancellation, hidden tables, restored games and disposal release presentation waits without resuming an unrelated settings pause. `MainWindow.cs:1388` records automatic arrangement only after the session accepts it. Hotseat handoff (`MainWindow.RematchHotseat.cs:32`) clears private presentation and invalidates retained controls before revealing the next human's hand.
- **Browser isolation is explicit.** Opaque cookie profiles have independent save paths, bounded resident runtimes, serialized requests and in-gate live preconditions (`WebCampaigns.cs:247`). The projection exposes seat-zero views rather than private snapshots. Terminal checkpoint settlement (`:110`) records the result once and clears the live match. Mutation timeouts reconcile with GET without replaying the POST; departed generations cannot replace a newer view.
- **Scope is controlled.** Nine gumballs remain intact, surviving persisted feature identities are retained, and Double Down is appended. Campaign opponent policy was not tuned. Free-play bot improvements use Core suit/trick helpers and the bot's visible information. Engine discovery (`EngineCatalog.cs:120`) is bounded to the configured bundle folder and does not launch discovered commands until use.

## Issues

### Critical — must fix

None found.

### Important — should fix

None found.

### Minor — nice to have

1. **[P3] Update the current README's Joker count.**
   - File: `README.md:264`.
   - The campaign description still says **31 Jokers**, while `src/BidEuchre.Core/CampaignInventory.cs:5` and the active catalogue now contain **32**, including Double Down. Issue #87 explicitly says the enumerated 32 entries take precedence over the stale heading.
   - Impact: the current guide contradicts the implemented collection; gameplay and acquisition are unaffected.
   - Fix: change this current README count to 32. Historical issue/spec snapshots should retain their original wording. A documentation check is sufficient; no new behavioral test or full-suite rerun is needed.

## Requirements and integration assessment

All 16 planned tasks and the 37 eligible issues are accounted for. No missing required feature or unapproved rule invention was found in the broad review.

| Area | Integration assessment |
| --- | --- |
| Tasks 1, 4–7: roster, effects, bids, antes, duplicates | Active catalogue and migration agree; all-seat unlock gates and Alpha's eligible partner match share validation; Duelist activates once per match; first-human-card replay is bounded; round counters reset; targets and successful opponent bonuses use the approved five-ante table; duplicates use the shared deterministic grant and settlement paths. |
| Tasks 2, 9–10: cards, live settings, caller announcement | Shared card content keeps the sigil clear of face indices; scale/spacing are changed together; session pause and presentation acknowledgment remain independent; auto-arrange retry, hidden-page recovery and selected-contract restore are handled. |
| Tasks 3, 16: web tables and campaign | Semantic Open/Close controls preserve deletion failure state and current-table navigation. Campaign has isolated saves, all command kinds, shared shop/deck/item logic, result/continue screens, stale-state recovery and explicit destructive-action confirmations. Existing free-play routes remain separate. |
| Tasks 8, 14–15: free-play bot, setup, rematch/privacy | Built-in strategy uses visible-hand and legal-action inputs; campaign policy is unchanged. Stable engine IDs persist across repair/removal and setup restore; rematch uses the actual prior table configuration and disposes its old session; hotseat clears private choices before Reveal. |
| Tasks 11–12: explanation and exact resume | Structured scoring survives subsequent hands and result transitions; public history contains private-exchange descriptions without hidden card values. Checkpoints include the new scoring/effect/arrangement state and are validated before hydration. Legacy restart is explicit and costs no life; explicit Forfeit remains a loss. |

The recorded engineering rulings are consistent with the final implementation: no reward for Panic Button duplicates, fixed duplicate points after multipliers, one contribution per matching type, Duelist consumption on auction activation, shop replenishment preserving the gumball offer/sold state, and unchanged endless policy beyond the specified ante table.

## Review method and verification

- Read the approved scope, detailed plan, durable rulings, execution ledger, relevant original issue bodies, and the supplied final review package. Reviewed the production changes in bounded passes, with focused source reads for surrounding contracts and sampled behavioral tests. This was a whole-branch integration review, not a duplicate independent task review of every test/artwork line.
- Cross-checked the clean Task 12 and Task 16 rereviews and the server review. Their reported missing-value/variant validation and focus/stalled-mutation findings are fixed in the reviewed head; no interaction in those fixes exposed a new blocker.
- Examined the continuation matrix and score formula tests, session/boundary behavior, server restart/arrangement/precondition tests, and the implementation reports' real browser flow coverage. The tests exercise actual engines, persistence and HTTP/browser operations; unusual UI command states also use explicit DTO fixtures, which is appropriately supplemented by Core/server coverage.
- The controller completed `test.sh` from the clean committed archive `/private/tmp/bideuchre-final-93263a1`, **exit 0**. Inspected its log at `/private/tmp/bideuchre-final-93263a1-tests.log`: full solution build has 0 errors and 8 known baseline warnings; Core/App **246/246**; desktop **78 PASS, 0 FAIL**; server **10 scenario groups**; Node web **28/28** and server/browser/security/published **40/40**, zero skips in those Node suites. C++ and Prolog unit/transcript/recovery checks and Mac bundle/signature/tamper checks passed.
- Optional Python CFR checks were explicitly skipped because Python 3.10+/the Torch environment are unavailable. Those engines were not changed by this branch. This report does not claim those optional checks ran.
- No full suite was rerun by this reviewer. No concrete unanswered runtime doubt required an additional probe. This reviewer did not mutate the checkout, index, HEAD, branch state, or main checkout, and spawned no subagents. Only this report was written outside the checkout.
- Visual/accessibility confidence relies on the existing inspected render artifacts, headless desktop checks and actual Chromium keyboard/responsive tests, not a fresh manual native screen-reader or platform-wide accessibility audit.

## Recommendations

Correct the one README count before final delivery if convenient. Preserve the exact reviewed head's test evidence and the durable rulings with the PR; there is no reason to broaden implementation or rerun the complete suites for this one prose correction.

## Declined to judge

- Exact partner-passivity tuning (#70): explicitly unresolved in the approved scope; no threshold/pass policy has been authorized.
- Opponent aggression and statistical balance tuning (#94): explicitly deferred; this review verifies the policy was preserved, not whether the new economy achieves a target win rate.
- Boss mechanics, soundtrack/assets, rare packs, ticket/arcade economy, bundle cadence/content/overflow, six-card application rules, and additive-versus-alternative payouts (#23, #26, #56–60, #65–66): explicitly excluded pending product decisions; no implementation is being claimed for them.
- Optional shop locks, discounts/rarity and bot use of generated Tarots: the binding task requires rerolls/availability/sell-value changes; it does not authorize inventing these separate economy or bot rules. The original generated-Tarot observation is not silently treated as implemented.
- Additional free-play difficulty tiers and measured superiority over external engines: the approved task selects one reasonable built-in heuristic; behavior and legal completion are covered, but statistical playing strength is not claimed.
- Arbitrary external-engine packaging and cross-device/account campaign synchronization: the plan requires bounded engine discovery and per-browser server saves, and explicitly does not require packaging arbitrary bundles or introducing accounts.

## Assessment

**Ready to merge? Yes.**

No Critical or Important defect was found in the final integration/architecture review, and the clean committed archive passes the available required checks. The sole finding is a nonblocking current-documentation count; the eligible requirements, preserved user decisions and shared lifecycle/persistence/scoring seams are consistent.

## Final correction re-review

# Final fix scoped re-review

Reviewed range: `93263a1..96eba61fd20bc151a19b46438ef7d5009e20310e`.

## Finding verdict

- **[P3] README Joker count — ADDRESSED.** `README.md:264` now says **32 Jokers**, matching the active enum/catalogue including Double Down. The supplied complete fix package contains only this one-line README correction; historical snapshots and product code are unchanged.

## New breakage

None found in the fix. The neighboring counts remain nine gumballs, eight Sigils and nine Tarots, and no rule or persistence wording changed.

## Checks and scope

Read `/private/tmp/bideuchre-final-doc-fix.md` and the complete `final-fix-review.diff` package. The implementer records successful whitespace checks with no output. No tests were rerun: the correction is prose only, and the available full integration checks already passed on unchanged product code at `93263a1`. No checkout, index or HEAD mutations; no subagents. The root's unrelated uncommitted verification/ruling reports are outside this one-line fix review.

## Declined to judge

No new behaviors set aside in this fix. The six exclusions in the full review remain explicitly recorded; the root reports that each has been adjudicated and preserved as a ruling.

## Verdict

**All findings addressed. Ready to merge: Yes.** No outstanding Critical, Important or Minor finding remains from the final branch review.
