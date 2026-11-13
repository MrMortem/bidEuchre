# Campaign cards, bidding unlocks, and shop

Date: 2026-11-12

Status: approved by the user in chat on 2026-11-12, including all proposed rules. Labels such as “Proposed,” “recommend,” and “needs approval” below record the draft's origin; all those recommendations were accepted together. In particular, Last Laugh now applies to the final trick of every hand. The user requested an implementation plan; product-code execution is not part of that planning request.

## Intent and scope

Add build-making choices to the existing single-player Bid Euchre campaign: a shared evolving deck, persistent Jokers, permanent bidding unlocks, consumable Tarot, and purchasable Sigils. The deck is a strategic shared resource: a modification may help the player, their partner, or an opponent on a later deal.

The complete catalogue contains 77 entries: 10 enhancements, 31 Jokers, 8 Planets, 12 Sigils, and 16 Tarot cards. Values are initial playtest values, not evidence of validated balance.

Keep the existing campaign structure of three matches per Ante (Small, Medium, Boss), three starting lives, one life lost on a normal loss and two on a boss loss, victory after winning the Ante 5 boss, and optional endless play. Boss-specific rule modifiers remain a separate project. This design replaces the earlier campaign's target, starting-score handicaps, payout formula, and empty shop.

Free-play Table rules remain governed by `bid-euchre-final-rules.md`. Campaign-specific restrictions and abilities must not change ordinary Table play. Native desktop is the first delivery target; web feature parity and upgraded external bot engines are out of scope initially.

Terminology: a **trick** is one card from each active player; a **hand** is one deal and normally six tricks; a **match** normally lasts at most eight hands; a **run** is the campaign across matches. References to “round” in the supplied catalogue mean hand.

## Approach

Recommendation: implement one shared effect-resolution system and persistent card model, then deliver the full catalogue in dependency order. Shared-deck mutations, multiple scoring sources, temporary copies, and retriggers need consistent semantics across every item.

Alternatives considered: implementing each item directly in the UI is initially shorter but duplicates legality and scoring rules; shipping all effects together postpones useful validation until too many combinations exist. A thin end-to-end shop and card-effect slice should validate the foundation before adding the remaining content. The implementation phases below are a proposed sequence, not an implementation task checklist.

## Agreed match and economy rules

### Match length and scores

Both teams start every match at zero. The positive and negative match thresholds are:

| Ante | Thresholds | Normal hand limit |
| --- | --- | --- |
| 1 | +24 / -24 | 8 |
| 2 | +28 / -28 | 8 |
| 3 | +32 / -32 | 8 |
| 4 | +36 / -36 | 8 |
| 5 | +40 / -40 | 8 |

Ties after eight hands continue until a fully scored additional hand breaks the tie. **Proposed:** evaluate scores after all hand effects resolve; reaching either threshold or completing hand eight ends the match unless tied, and the higher-scoring team wins. During tie-breaking hands, ignore the normal hand limit but retain full scoring. Continue thresholds by +4 per Ante during endless play. The threshold interpretation and endless formula need final approval.

Only tricks won determine whether a bid succeeds. Bonus points count even on a failed bid. Example: bid 4, win 3 tricks, gain 5 bonus points: -4 contract points + 5 bonus points = +1 team point. Protective Bubble removes the contract failure penalty while retaining bonuses and other independent effects.

### Money

Start with $0. Keep money earned from cards and Jokers even on a loss. Winning grants the base reward plus a margin-of-victory bonus:

| Match | Base | Win by 1–4 | Win by 5–9 | Win by 10+ |
| --- | --- | --- | --- | --- |
| Small | $5 | $7 total | $9 total | $11 total |
| Medium | $6 | $8 total | $10 total | $12 total |
| Boss | $8 | $10 total | $12 total | $14 total |

Losses grant neither base reward nor victory bonus. After match rewards, apply interest of `min(5, floor(money / 5))`. Card/Joker income is included in the balance used for interest. Keep the existing once-only result application behavior so reopening a result cannot award money twice.

**Proposed:** settle match-end Joker income before base reward, margin bonus, and interest. A forfeit grants no match reward or victory bonus and keeps only card/Joker earnings already committed to the run; unplayed match-end Joker effects do not fire.

## Agreed deck and ownership rules

- All four players share one 24-card deck, initially 9 through Ace in each suit.
- Each physical card needs a stable identity independent of its current rank and suit. Duplicated faces from transformations do not add cards or merge identities.
- Permanent modifications survive every subsequent hand and match in the run.
- Each card has exactly one enhancement slot and one Sigil slot. A new enhancement replaces the enhancement; a new Sigil replaces the Sigil. Changing suit preserves both.
- Suit transformations and Tarot-applied enhancements persist for the run.
- Temporary effects such as Sticky Fingers expire at their stated boundary and do not overwrite the underlying permanent card accidentally.

**Proposed ownership default:** the human owns the shop inventory and receives Joker benefits according to each Joker's wording. Other players do not acquire inventories in this version. Card-bound effects work for whichever player or team plays/wins the card as specified, including opponents. Track money and generated consumables per seat for consistent resolution, but only the human's wallet and inventory are spendable in the initial campaign. Opponent rewards must still appear in the event log. Whether opponents should actually use generated Tarot is a product decision; see review items.

## Agreed shop and inventory

Each visit offers two random Jokers, one random Planet, one random Tarot, and one random Sigil.

| Item | Price | Behavior |
| --- | --- | --- |
| Joker | $4 | Five active slots; no duplicate owned Jokers; sells for $2 |
| Planet | $3 | Permanently unlocks its bidding option for the human |
| Tarot | $3 | Stored single-use consumable; two slots |
| Sigil | $3 | Immediately apply to any selected card in the shared deck |

Already-unlocked Planets are excluded from rolls. Refresh stock between matches; no paid rerolls. A Sigil replaces the existing Sigil and preserves the enhancement.

**Proposed transaction rules:** all five offers are drawn without duplicate Jokers within the same shop; bought offers disappear and do not refill. A full Joker or Tarot inventory blocks the purchase until space is available. Permit discarding a Tarot for no money. Selecting a Sigil target is part of the purchase: canceling spends nothing. If every Planet is unlocked, show that slot as sold out. Save offers and completed purchases so reopening the shop does not reroll stock. Uniform selection and no rarity tiers initially.

## Agreed Planets and Tarot timing

All Planets permanently unlock bidding options for the human for the rest of the run. Partners Best and Alone begin locked for the human in campaign mode. Tarot cards are stored consumables usable after the deal and before the human's bidding action. Their card modifications persist for the run.

Panic Button is a reusable bidding action. On the human's bidding turn, choosing it immediately ends the auction even if another player has bid, costs the human's team 8 points, grants opponents 0, and consumes one of the eight hands. Check match-ending conditions before redealing.

**Proposed Planet rules:** buying a Planet activates its unlock immediately. An Astronomer-generated Planet activates immediately too, uses no Tarot slot, and selects only locked options; if none remain, Astronomer cannot be used. Baseline numeric bids remain available. Other bots retain their existing baseline auction options, including Partners Best and Alone; only the human's options are gated by unlocks. Special numeric bids declare both target and variant on the bidding turn, must beat the existing bid, and cannot combine two Planet variants. Alpha alone permits matching a partner's numeric bid. Low-baller's 2 uses Trump, like the existing bid of 3. Dealer rotation continues normally after Panic Button, subject to Slick Willy.

## Catalogue: enhancements (10)

| Enhancement | Current effect |
| --- | --- |
| Lucky | When played, independently roll 1 in 4 for +4 points and 1 in 3 for $3. |
| Golden | A won trick containing this card earns $1. |
| Wild | Counts as every suit. |
| Cursed | A trick containing this card earns -1 point. |
| Stone | When played, earn +1 point. |
| Emerald | If played last in the final trick of a hand, earn $10. |
| Silver | When a trick containing this card is won, the losing team earns $5. |
| Stinky | The human cannot be dealt this card; partner and opponents can. |
| Glass | Double all points earned and lost from card effects in this trick. |
| Bouncy | When played, replay the on-play effects of cards played before it in the trick. |

Camouflaged and Black enhancements are removed. Stone's old cancellation effect is removed. Glass affects card-effect points, not money or the contract settlement.

## Catalogue: Jokers (31)

| Joker | Current effect |
| --- | --- |
| Three's Company | Successful team bid of exactly 3: +2 points. |
| High Roller | Successful team bid of 5 or more: +3 points. |
| Lover Boy | When the human calls Hearts trump, each Heart played adds +1 point to its trick. |
| Snake Eyes | Team wins a trick containing at least two Aces: +5 points. |
| Twin Flame | Team wins a trick in which human and partner played different colors: +1 point. |
| Clubhouse | Tricks containing at least one Club are worth +1 point. |
| The Undertaker | +1 point per 9 in tricks the team wins. |
| Roller Coaster | Starts at +0; gains 1 when the team wins a High/Low trick and loses 1 when it loses one; awards its value each High/Low hand. Exact accumulator timing/lifetime is proposed below. |
| Déjà Vu | Starts at +0; increases by 1 for consecutive hands calling the same trump; resets when the team fails a bid; awards its value each hand. |
| Last Laugh | Double all points and effects from the final trick of the game. Boundary needs approval below. |
| Vow of Silence | Human cannot bid; double points earned from tricks the human wins. |
| Cure-All | Cursed cards in tricks the human wins give +2 rather than -1. |
| Trend Setter | Human wins a trick containing four cards of the same suit: +2 points. |
| Contrarian | Human wins using a suit no other player played: double the trick's points. |
| Consolation Prize | When the human's team did not call trump, +1 per trick the human wins. |
| Royal Council | Tricks containing Ace, King, Queen, and Jack are worth twice the points. |
| Deep Digger | Effects on Spades in tricks the human wins trigger twice. |
| Lucky Duck | Lucky cards trigger twice. |
| Protective Bubble | Remove the team's failed-contract point penalty; destroy this Joker after a failed bid. |
| Penny Pincher | At match end, earn $1 per trick the team won beyond its bid. |
| Bully | At match end, earn $3 for winning by at least 6 points. |
| People Pleaser | When the human wins a trick, earn $1 per face card remaining in hand. |
| Millionaire | Diamonds the human plays also count as Golden. |
| King Midas | Kings the human plays have a 1 in 3 chance to permanently become Golden. |
| Sticky Fingers | After the human's first trick win each hand, the leftmost remaining card copies the winning card for the rest of the match. |
| Medusa | The last card played in each match permanently becomes Stone. |
| Cheese Touch | The first card played in each match permanently becomes Cursed. |
| One Up | At each hand's start, choose a hand card to swap with a random partner card. |
| Cheater | At each hand's start, choose a player and reveal one random card from their hand to the human. |
| Alpha | Human can outbid the partner by matching the partner's bid. |
| Slick Willy | Human never deals. |

## Catalogue: Planets (8)

| Planet | Unlocked option |
| --- | --- |
| Low-baller | Bid 2. |
| Chaos | Minimum bid 4; trump changes randomly each trick; double all points earned/lost that hand. |
| Poker | Bid exactly 4; reveal all hands; best poker hand earns 4 points. |
| Duelist | Bid 3–5; play alone against one chosen opponent; failed contract costs 1 extra point. |
| Telepath | Minimum bid 5; partner reveals one card before human chooses trump. |
| Partner's Best | Existing card exchange and solo play; sweep six tricks for +12, otherwise -12. |
| Alone | Existing solo play; sweep six tricks for +24, otherwise -24. |
| Panic Button | Immediately pay 8 team points, end the bidding hand, count it, and redeal if match continues. |

**Proposed contract details:** Chaos chooses one of the four suits before each lead and holds it for that trick; bower effective suits follow the current trump. Poker uses the best five-card poker hand from each six-card hand, breaks ties by standard rank comparison, and splits no points: tied best players on opposing teams each award their team 4 points, while a same-team tie awards 4 once. Continue six tricks after the reveal; normal bid-4 contract scoring also applies. Duelist has two active players, with inactive hands untouched. Telepath's partner selects the reveal, with a deterministic legal bot fallback. High Roller includes successful solo contracts. These are proposed rules, not claims about the original list.

## Catalogue: Sigils (12)

| Sigil | Current effect |
| --- | --- |
| Coin | When played, earn $3. |
| Bear | Count as one printed rank higher; Ace wraps to 9. |
| Eagle | Return to the player's hand if played in a losing trick. |
| Fish | When played, earn +2 points. |
| Spider | When played, Curse another random card already in the trick. |
| Snake | When played, steal 1 point from the opposing team. |
| Frog | When played, retrigger this card's effects. |
| Owl | This card is visible to all players. |
| Duck | When played, 1 in 2 chance to earn +4 points. |
| Vulture | The trick taker creates a Tarot card. |
| Chameleon | When led, other players must avoid following its suit if possible. |
| Peacock | Award the trick taker +1 point per Sigil in the trick. |

## Catalogue: Tarot (16)

All are one-use consumables. Card targets are in the human's current hand, unless the effect does not target cards.

| Tarot | Current effect |
| --- | --- |
| Love Potion | Change one card to Hearts. |
| Nightshade | Change one card to Spades. |
| Clover Tonic | Change one card to Clubs. |
| Crystal Elixir | Change one card to Diamonds. |
| Chameleon | Make one card Wild. |
| Grindstone | Make one card Stone. |
| Prospector | Make one card Golden. |
| Devilish Deal | Random enhancement on each of two selected cards; a third selected card becomes Cursed. |
| Gambler | Two random cards become Lucky. |
| Merchant | Make one card Emerald. |
| Glassblower | Make one card Glass; replaces Masquerade. |
| Traitor | Make one card Silver. |
| Astronomer | Create a random Planet unlock. |
| Beggar | Make one card Stinky. |
| Spring Loaded | Make one card Bouncy; replaces The Blacksmith. |
| Tax Collector | Double money, capped at $20 gained. |

## Proposed effect rules for review

These fill gaps in the supplied effects and must be approved with the design. Numbers and explicit distinctions between “you” and “your team” above are preserved.

### Attribution and scoring

On-play rewards go to the playing player's team (points) or player (money). Trick-contained effects pay the winning team/player except Silver, which explicitly rewards the loser. Human-owned Jokers benefit the human's team, conditioned on personal versus team wins as written. Clubhouse and Royal Council activate only on the human team's wins; they do not improve opponents' tricks merely because owned.

Represent the one base point per trick as a provisional trick entry; apply eligible trick multipliers, then settle the contract at hand end. On a failed numeric bid, remove the bidder team's unmodified base trick count, apply the failure penalty, and retain modifier gains and other bonuses. On solo contracts, replace the six base trick points with +12/+24 on success or the failure penalty; retain effect bonuses. This prevents bonus points from satisfying a contract while still making “double trick points” meaningful. Examples must be checked explicitly during implementation.

Apply replacement effects first (Cure-All), additive effects second, then eligible multipliers. Independent multipliers multiply: two Glass cards give x4 card-effect points. Do not multiply money unless the effect explicitly doubles effects/rewards. Chaos doubles both teams' point deltas, including failure penalties and bonuses, but not money. Protective Bubble removes the failed-contract penalty, including Duelist's extra failed-contract point, before Chaos doubles the remaining delta; unrelated Cursed losses remain.

Proposed multiplier examples: with Vow of Silence, bid 4 and win three tricks personally, with no other effects: the three doubled trick entries total 6; remove 3 base trick points and apply -4, leaving -1. If only one of those three wins was personal, retain just its +1 multiplier bonus, leaving -3. A successful bid 4 with four team tricks, one personally doubled, scores +5. An Alone sweep with all six tricks doubled scores +24 contract points plus 6 multiplier bonus points = +30, not +48: the contract award itself is not a trick entry.

### Triggers and randomness

Resolve a played card's enhancement, then Sigil, then matching Joker triggers in stable inventory order. Record trigger provenance. Independent random rolls are independent for each activation. Replays repeat effects without physically playing a second card, changing turn order, or recursively replaying replay-generating abilities.

Frog repeats the card's enhancement and applicable non-retrigger on-play effects once; Frog never calls itself. Bouncy repeats earlier cards' eligible on-play effects once per Bouncy activation, suppressing Frog and Bouncy replay creation inside that replay. Lucky Duck and Deep Digger each add one activation of matching effects rather than recursively invoking one another. Deep Digger's winner-dependent extra activations occur after the winner is known; they cannot retroactively change legal play or trick rank. Copying, return-to-hand, and permanent mutation occur only at their defined lifecycle event, not every generic replay.

Spider permanently applies Cursed to a uniformly random other card already played; no prior card means no target. Golden's reward is per Golden card, not once per trick. Millionaire supplies Golden's income without replacing the card's enhancement or paying it twice if already Golden. King Midas writes the permanent enhancement when its roll succeeds, after the current play's original enhancement effects resolve.

### Legality, hand size, and mutations

Use printed/current suit for suit-based rewards and current face rank for rank-based rewards; use effective suit for play legality and bowers. Bear changes rank before evaluating whether the card acts as a bower. Wild can follow any suit; on lead its player explicitly chooses the effective suit for that trick. Off lead it follows the led suit when possible, otherwise its player chooses an effective suit. Its rank is retained, with normal bower interpretation for the chosen effective suit. A Wild card counts once per suit-based reward, not multiple times for the same effect.

Wild is the exception to the printed/current-suit reward rule: it matches each suit independently for abilities, including Hearts for Lover Boy, Clubs for Clubhouse, Diamonds for Millionaire, and Spades for Deep Digger. It supplies one match to each such ability. Its underlying suit continues to define color-based effects, avoiding a multi-color interpretation for Twin Flame.

For the Poker contract, proposed evaluation uses current face ranks and suits, including permanent suit changes and temporary copied faces. Combat abilities do not alter poker evaluation: Wild is not a poker wildcard and Bear does not change poker rank. Mutated duplicates are permitted; five-of-a-kind ranks above a straight flush. Other rankings use standard five-card categories, taking the best five out of six. This deliberately separates poker comparison from trick-play rank/suit abilities.

Chameleon reverses follow-suit only for the current trick: players must play outside the led effective suit if they can, otherwise they follow it. Determine legal cards authoritatively, including Wild options.

Eagle returns after trick resolution, at most once for that physical play. Hands still end after six tricks, so unplayed/returned cards remain unused and do not create a seventh trick. Stinky uses a constrained full-deck deal: select six non-Stinky cards for the human, then deal the remaining eighteen to other seats. Exchanges are not draws and may give the human a Stinky card. A held card made Stinky stays in hand until played or exchanged. Legal purchases/transformations must not create more than eighteen Stinky cards, which would make a six-card human deal impossible.

Sticky Fingers copies suit, rank, enhancement, and Sigil into a match-lifetime overlay on the leftmost remaining card by stable deal/exchange order, not visual drag order. Subsequent permanent mutations write to the underlying physical card and are visible after the overlay expires. Original identity remains stable. Dealing resets hand order, not the overlay. If no card remains, skip the copy. Medusa transforms the actual last-played physical card after the match is determined; if the match has no played card, do nothing.

### Inventory, accumulator, and special-action edges

Generated Tarot respects the two-slot limit; if full, skip creation with an explicit log message. No conversion to money. Vulture grants one Tarot per Vulture card to the trick winner's inventory. Proposed first-version bots keep generated rewards as tracked state but do not use Tarot; a fuller opponent economy is deferred.

Roller Coaster retains its value through the match, may become negative, adjusts after each High/Low trick, and pays its resulting value at the end of each High/Low hand. Déjà Vu retains value through the run, compares called contracts at hand setup, treats repeated High/Low as repeated modes, and ignores canceled Panic Button hands. Failure resets its value before its hand-end award. Penny Pincher totals `max(0, tricks won - numeric bid)` over hands the team bid during the match, awarding nothing for defenders or solo contracts.

For Déjà Vu, proposed calls from any bidder count, including opponents. The first completed contract selection establishes the comparison key and adds nothing. An immediately repeated key adds 1; a different key updates the comparison key without erasing the accumulated value. Resolve repeated-call growth before play; if the human's bidding team later fails, reset the accumulated value to zero before paying that hand's award. The hand just selected remains the comparison key for the next hand. Retain the prior key across match boundaries within the run.

With Vow of Silence, automatically pass on the human's bidding turn and do not expose Panic Button or special bids. If the human would be the forced dealer, select the next eligible bidder clockwise for the forced minimum bid; this exception avoids a deadlock. Slick Willy skips the human in initial dealer choice and dealer rotation, with the normal clockwise auction/lead rules relative to the chosen dealer.

## Decisions needing particular attention

1. **Last Laugh:** recommend changing its trigger to the sixth/final trick of every hand. “Final trick of the match” otherwise depends on the very scoring it doubles and can require reversing money, mutations, and Tarot generation. This is an explicit proposed wording change, not approved yet.
2. **Opponent economy:** recommend tracking card rewards for everyone while only the human buys/uses items initially. Opponent Tarot generation then has no immediate tactical benefit. Enabling bot Tarot use expands bot actions, timing, and inventory handling.
3. **Sigil mutations and copies:** Spider is proposed permanent; Sticky Fingers is proposed to copy both slots and face while retaining physical identity. These have large effects on the shared deck.
4. **Scoring:** the proposed base-trick/modifier breakdown specifies how Vow of Silence and other whole-trick multipliers retain value even after a failed contract; this needs explicit review.
5. **Endless and thresholds:** propose +4 target growth per Ante beyond Ante 5 and higher score deciding all threshold endings, with ties continuing.
6. **Wild and Poker:** propose all-suit reward eligibility for Wild, but current face-only poker evaluation, with five-of-a-kind permitted on the modified deck. Bear affects trick rank only. These rules avoid conflicting interpretations of the same card across systems.

Builder review completed: the review identified Wild reward matching, Poker on the mutated deck, and Déjà Vu transition timing. Each now has an explicit proposed resolution above. These remain proposals for user review rather than approved mechanics.

## Architecture and persistence

### Core engine

Retain one authoritative action/state machine. Add an opt-in campaign rules profile; ordinary matches use today's rules. Extend match options to support negative thresholds, hand limits, and tie-breaking continuation. Add physical card instances, permanent state, temporary overlays, owner-aware effect contexts, and an ordered event ledger. Enumerate legal actions including unlocks, Tarot targets, Wild suit choices, reveals, exchanges, duels, and Panic Button.

Random events use an injected seeded source; log results rather than rerolling during view construction or animation. Keep bid legality, effective rank/suit, scoring, and effect execution in Core. Views expose only authorized information, including explicit Owl/Poker/Cheater/Telepath reveals. UI rendering must never trigger game effects.

### App and campaign state

Extend `CampaignRun`/`CampaignStore` with a versioned run save containing the 24 permanent card instances, money, five-slot Joker inventory and state, Planet unlocks, two-slot Tarot inventory, shop offers/purchases, and run RNG state. Match-local state includes temporary copies, trick score entries, generated inventories, hand count, and accumulators. Do not persist UI controls as game state.

The existing campaign saves only between matches and treats an interrupted match as a forfeit. Preserve that behavior initially, but add atomic checkpoints for committed wallet/deck/inventory mutations during a match. On Continue, retain those mutations and forfeit the abandoned match without reconstructing or replaying it. Commit action costs, RNG outcomes, and their mutations in one transaction so restarts cannot refund a consumed Tarot or roll it again. Save failures must visibly report loss of durability without silently duplicating rewards. Mid-hand resume remains out of scope.

For pre-feature saves, migrate to a standard deck, empty inventory, and locked human Planet options while preserving earned money, lives, match position, and existing phase. Apply new match rules at the next actual match start; legacy in-progress matches continue to forfeit under the existing recovery policy.

### Desktop and bots

Add deck inspection and a target picker for Sigil purchases, shop inventory controls, a pre-bid Tarot window, unlocked bidding choices, and readable effect/score breakdowns in the table log. Display the hand count, current thresholds, and overtime state. Pending choices must pause bot progression until resolved. Respect the existing reduced-motion preference.

Campaign built-in bots receive legal-action masks and public revealed data and need policies for the changed cards, active seats, and contracts. A legality fallback can keep a vertical slice playable but is not evidence of competitive strength. External BEUCI engines currently assume ordinary cards and contracts; reject their use for the new campaign profile until a capability extension is implemented. Keep free-play and external-engine protocol payloads unchanged by default.

## Proposed delivery sequence

1. **Rules foundation:** stable card identities, campaign-only profile, deterministic events, revised match endings, explicit scoring buckets, and versioned state.
2. **Playable economy slice:** fixed-price five-offer shop, saved inventory, one Planet unlock, one Tarot transformation, and one Sigil purchase end to end.
3. **Card effects:** all ten enhancements and twelve Sigils, including constrained dealing, Wild/Chameleon legality, bounded replay, and temporary copies.
4. **Auction options:** all Planet unlocks and their setup phases, Panic Button hand consumption, reveals, solo/duel active seats, and default bot choices.
5. **Joker catalogue:** additive/scoring Jokers, economy Jokers, mutation/accumulator Jokers, and auction exceptions; resolve in engine events rather than UI callbacks.
6. **Integration and balance:** private-view audits, persistence/forfeit scenarios, catalogue interaction tests, explanatory UI, and bounded seeded campaign playtests.

Each phase should leave a playable vertical slice. This sequence is a feature roadmap; after design approval, write the separate implementation plan with specific files, tasks, and verification commands.

## Acceptance and verification plan

- All 77 catalogue entries have stable IDs, current descriptions, executable effects, and clearly scoped timing/ownership.
- Standard Table behavior and existing protocol fixtures remain unchanged by default.
- Shared-card changes survive redeals and matches; duplicate faces retain distinct identities; slot replacement and suit changes obey the agreed rules.
- Match tests cover each Ante's positive/negative threshold, both teams crossing, tied thresholds, hand eight, overtime, and Panic Button on the final/overtime hand.
- Score examples cover successful and failed bids, solo contracts, defending tricks, negative card effects, stacked multipliers, and Protective Bubble.
- Trigger tests use fixed RNG outcomes and cover Bouncy + Frog + Lucky Duck + Deep Digger without recursion or duplicate physical plays.
- Shop tests cover affordability, full inventories, cancellation, sales, sold offers, all Planets unlocked, and reopening without rerolling.
- Economy tests cover loss retention, victory margins at 1/4/5/9/10, Small/Medium/Boss rewards, Joker income ordering, and capped interest.
- Visibility tests cover ordinary private hands, Owl, Cheater, Telepath, Poker, and partner exchanges.
- Persistence tests cover migration, atomic consumptions/mutations, committed in-match changes followed by forfeit, and exactly-once match result settlement.
- Manually verify the native flow: purchase, inspect deck, apply Sigil, use Tarot before bidding, choose unlocked bids, resolve effects, finish match, revisit shop, and restart after mutations.
- Balance playtests should measure reward distributions, purchase rates, match length, negative/positive endings, solo-contract frequency, runaway multiplier combinations, and whether opponent use of shared upgrades remains interesting.

## Builder discretion

The builder may choose internal class names, collection types, event representation, seeded RNG implementation, rendering details, and exact bot heuristics within the approved semantics. Do not reinterpret the proposed game rules as approved until this design has been reviewed. No product code or new tests are part of this planning change.
