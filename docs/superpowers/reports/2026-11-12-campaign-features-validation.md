# Campaign features: implementation and validation

Date: 2026-11-12. Branch: `codex/campaign-features`.

## Delivered

Implemented the approved 77-entry catalogue: 10 enhancements, 31 Jokers, 8 permanent Planet bidding unlocks, 12 Sigils, and 16 Tarot consumables. Native Play includes the five-offer shop, shared-deck inspection/targeting, inventory, pre-bid Tarot, unlocked contracts, Panic Button, revised match endings/rewards, and persistent card changes. Ordinary Table and its external-engine protocol remain available.

The existing authoritative engine remains responsible for turns, legality, trick resolution and scoring. Campaign actions use stable physical card identities, including when transformed faces duplicate one another. Versioned saves retain resources, fixed shop offers, randomness and committed in-match mutations; interrupted matches still forfeit on Continue.

## Verification evidence

Final `./test.sh` completed successfully after the review fixes:

- 118/118 .NET executable tests, including 35 campaign tests/groups.
- 26/26 browser regressions, zero skipped or failed browser tests.
- C++ heuristic unit target plus transcript/recovery checks.
- Python suites: 28 tests and 30 tests, both `OK`.
- Prolog's 12 unit cases plus transcript/recovery checks.
- Mac bundle content/rebuild checks.
- Solution build: zero warnings and zero errors.

Final `./package.sh` completed successfully **after** the test script, producing `dist/osx-arm64/Bid Euchre.app`. Runtime-specific publishing and ordinary solution restores must run sequentially because they share `obj/project.assets.json`.

The campaign integration sample completed 12 seeded runs, 119 matches, 655 hands and 259 shop purchases. No negative-threshold endings occurred under this sample policy. This is bounded correctness evidence, not proof of balanced rewards or competitive bot strength.

Fixed-deal scoring tests assert literal outcomes for successful and failed contracts, retained bonuses, Protective Bubble, Cure-All, whole-trick multipliers, stacked Glass, Peacock, Golden/Silver/Emerald rewards, and Last Laugh. Separate tests cover duplicate physical cards, private views, save migration, stable JSON feature IDs, RNG continuation, stale commands, write-failure visibility, all Tarot, all Joker/Sigil hand completion, special setup phases, and constrained Stinky dealing.

## Native verification

Used a disposable profile under `/tmp/bid-euchre-ui-profile`, selected through `BIDEUCHRE_DATA_DIRECTORY`. The user's normal save/preferences were not changed.

Verified in the packaged native app:

- New-run intro: three lives, $0, both teams at zero, ±24 threshold and eight-hand limit.
- Private opponent hands and initially locked human solo bids.
- Five shop offers and disabled Tarot purchase at full capacity.
- Buying Fish for card #1 replaced Coin, preserved Lucky, and changed money from $30 to $27.
- Joker and Alone purchases persisted, reducing money to $20 and exposing the unlocked bid.
- Tax Collector consumed its slot and increased $20 to $40.
- Glassblower targeted a held Queen of Diamonds; its Glass enhancement appeared immediately and its Tarot slot disappeared.
- Panic Button ended the auction at -8/0 and the next deal displayed hand two.
- Leaving and continuing forfeited once, retained $40 and both card changes, then awarded capped $5 interest.
- The refreshed shop and the next boss intro appeared; a second match started in the same app session.
- Choosing a contract and clicking a highlighted physical card submitted the correct play and removed only that card.

This pass caught and fixed the old table's logical-parent retention when rebuilding a second native session. The reproduced startup failure was followed by a successful second-match launch after detaching shared controls before reuse.

## Independent review and fixes

One fresh, read-only whole-branch review inspected the implementation. It found five Important defects and one initially Minor omission. All were fixed in one pass with regressions observed failing first:

1. **Additive replay scheduling:** Frog, Bouncy and Deep Digger no longer reschedule Lucky Duck. The fixture asserts six RNG draws for three activations instead of eight for four.
2. **Explicit trick timing:** Deep Digger no longer receives Last Laugh on trick five. Coin totals are $30 after five tricks and $42 after the sixth with both Jokers.
3. **Transactional Tarot and Stinky overlays:** random enhancement pools validate underlying and effective deck feasibility; failed Tarot restores resources, RNG and events. Copying/dealing respects effective Stinky state.
4. **Stale discard:** shop callbacks capture the resource revision; an old slot-zero callback cannot discard the card shifted into that slot.
5. **Chameleon fallback:** when there is no actual off-suit card, Wild can only follow the led suit.
6. **Tracked nonhuman Tarot:** upgraded to Important because tracking was explicitly required. Rewards now persist per seat, respect two-slot capacity, and survive save round-trip; bots still do not use them.

All seven focused review regression groups failed before the fixes and passed afterward. The full suite passed after the fix pass. No second review was commissioned; no review findings are deferred.

## Execution decisions and their costs

These record the implementation rulings, including resolutions of conflicting general/specific wording:

| Decision | Reason | Cost or practical limit |
| --- | --- | --- |
| Dedicated feature branch in the existing checkout | Native execution was authorized in this workspace | Branch isolation rather than a second directory; working files change here |
| Existing standard Card values serve as immutable identity tokens internally | Preserve default engine/protocol fixtures while resolving campaign faces by ID | Every campaign boundary must use ID/current-face resolution |
| Integrate the minimal shared auction/Panic lifecycle with identity work | The initial bounded-match test needed real hand progression | Several planned tasks share a commit rather than introducing temporary engines |
| Focused partial-engine event handlers instead of separate public effect services | Retain one authoritative mutable state machine | Lifecycle coupling requires integration tests; the plan's proposed service class names are not reproduced verbatim |
| Silver pays the human wallet when the human team loses, otherwise a deterministic opposing seat | Money is spendable only by the human initially | A future team/opponent economy may need a different allocation policy |
| Optional `BIDEUCHRE_DATA_DIRECTORY` | macOS ignored `XDG_DATA_HOME`; native tests needed a disposable profile | Opt-in custom storage root; default location is unchanged |
| Explicit Wild suit overrides Left Bower reassignment; choosing trump with a Wild Jack gives Right Bower strength | Otherwise Wild can be offered a follow-suit action that silently changes suit | Wild Jack ranking differs from an ordinary same-color Jack when choosing a nontrump suit |
| Contrarian uses resolved played suits | Specific Task 16 wording takes precedence over the generic printed-suit reward rule | A Left Bower sharing the winning trump suit can prevent the bonus |
| Wild is excluded under Chameleon when a real off-suit card exists | Explicit Task 12 requirement | Wild cannot evade that restriction |
| Skip a Sticky Fingers copy that would leave fewer than six drawable cards | Preserve the shared 24-card/six-card-deal invariant | A rare Stinky copy does not apply; the table log explains why |
| Validate only feasible random enhancement outcomes | Avoid partially applied Devilish Deal and impossible later deals | The random pool excludes Stinky when that target would violate permanent or effective feasibility |
| Keep balance/bot-strength claims limited; no retuning of approved values | Correct execution and competitive strength are separate | Further playtesting is required |
| Test and package restores run sequentially | Concurrent builds of the same project raced on restore assets | Final verification is sequential |

## Remaining limits

- Campaign uses built-in bots; external engines are rejected for this profile before process launch.
- Bots receive tracked rewards but do not shop or use Tarot.
- No mid-hand resume, new boss modifiers, rarity tiers, paid rerolls, or web campaign parity.
- A reported save failure leaves current play in memory; it cannot guarantee durability across a later crash. Successful temp-file replacement and normal failure recovery were checked; process-kill fault injection was not performed.
- This is a local unsigned Mac build. No remote push, PR, merge, signing or publication was performed.
