# Collector's Pack

## Intent

The user supplied pixel-art "collector cards" for the eight Sigils, a Legendary card and a pack wrapper. These cards are the Arcade's top prize. Opening a pack and choosing from three cards replaces "here is one random Sigil", which gives the top prize more excitement and an actual decision. A rare Legendary card adds a jackpot moment.

## Goals

- A Sigil-tier Arcade win (Plinko score 5–6, or roulette exact-number match) awards a **Collector's Pack** instead of one random Sigil.
- The pack opens to **three distinct cards**. Each card is a Sigil, or rarely the Legendary.
- The player picks exactly one card:
  - **Sigil card:** the existing six-card shop-use menu opens with that Sigil, and the player must apply it to one of the six cards. There is no Keep, Back or Cancel.
  - **Legendary card:** grants random Jokers until the five Joker slots are full, skipping the six-card menu.
- Each Sigil appears with its collector-card name and art wherever it is shown (Arcade, use menu, inventory, collection, rules):

  | Sigil | Card name | Attached image |
  |---|---|---|
  | Bear | Strength | 3.webp |
  | Fish | Abundance | 2.webp |
  | Frog | Echo | 4.webp |
  | Duck | Lucky | 5.webp |
  | Chameleon | Deception | 6.webp |
  | Eagle | Resilient | 7.webp |
  | Spider | Affliction | 8.webp |
  | Vulture | Resourceful | 10.webp |
  | (Legendary) | Legendary | 1.webp |
  | (pack wrapper) | — | 9.webp |

  The Sigil keeps its identity and effect (`sigil:bear`, Bear rules text). The card name is a title shown on the card art, alongside the Sigil name in text (e.g. "Strength — Bear Sigil").

## Non-goals

- Tarot-tier Arcade prizes are unchanged (Plinko 3–4, roulette colour match): one random Tarot ticket with Keep or Use Now.
- Prices, per-visit play limits, odds of each Arcade outcome, and Sigil effects are unchanged.
- Packs are not sold anywhere else and cannot be stored.

## Anti-goals

- A pack must never be lost, or opened twice, through a restart, a double click, browser polling or a stale request.
- Animation must never decide the result. The three cards and the eventual picks are saved before anything is revealed.
- The Legendary must never be a dead pick. It does not appear when the player has no free Joker slot.

## Rules

- **Drawing the three cards** happens in the same saved transaction as the Arcade play (payment, RNG, landing). The three cards are distinct. For each card slot, roll Legendary at 1 in 50 when at least one Joker slot is free and no Legendary is already among the three. Otherwise draw a Sigil not already shown. At most one Legendary per pack, about 6% of packs.
- The six target cards are drawn at the same time, as today, so a resumed pack shows the same three cards and the same six targets.
- **Picking a Sigil** commits immediately. It creates the existing hidden zero-price sold Arcade offer and `ShopUseSession` for that Sigil, and the player must apply it before Done. Held Tarots may also be used on the six cards, as today. A full inventory does not matter because nothing is stored.
- **Picking the Legendary** grants `5 − current Joker count` Jokers, drawn uniformly without duplicates from the Jokers the player could otherwise get in the shop and does not already own. If the pool is smaller, grant what exists. It clears the pending result. Show the granted Jokers to the player.
- The pending pack blocks other plays, rerolls and leaving the shop, like every pending Arcade result today.
- **Stored Sigils** remain a valid inventory item. Ones in older saves keep working in matches, show the new art, and can be discarded. Nothing new adds Sigils to inventory.
- **Older saves:** a pending Sigil-tier result saved before this change (one `FeatureId`, no pack cards) stays finishable exactly as before, with Keep or Use Now. Only new plays create packs.

## Approaches

1. **Extend the pending Arcade result (chosen).** Add the three pack cards to `ArcadeResult`, add one "pick pack card" command, then hand off to the existing use-session flow. This reuses all the save, resume, validation and six-card-menu code.
2. **A standalone pack system** with its own state and screen, reachable from the Arcade. It is cleaner if packs later come from elsewhere, but it duplicates the transaction, resume and validation logic now for no current need.
3. **Draw a Sigil as today and only restyle it as a card.** It is cheapest but drops the 3-card choice and the Legendary, which are the point.

The user approved approach 1.

## Data and clients

- `ArcadeResult` gains the pack's three cards, e.g. `string[]? PackCards` holding `sigil:<name>` keys or `legendary`. A Sigil-tier result has `PrizeCategory = Sigil`, `PackCards` of length 3, and `FeatureId = null` until a pick. Validation (`CampaignArcade.IsValid`) checks three distinct legal entries, at most one Legendary, and the six targets.
- Picking a card is a new App command, `PickPackCard(run, playId, index, expectedRevision)`, with a strict server endpoint (e.g. `shop/arcade/pick`) that takes only the index. The server never accepts card identities or Jokers from the client.
- The Keep action is rejected for pack results. Use Now is replaced by the pick.
- **Desktop (Avalonia) and browser** both get a pack-opening step on the Arcade result: the wrapper, then three face-down cards that flip to reveal, with the Legendary given a distinct glow. The player clicks one, with accessible names and keyboard support. Reduced motion shows the cards face-up immediately. Browser polling keeps the reveal from replaying on unchanged state.
- **Rules text** (`bid-euchre-final-rules.md`, in-game Rules page) is updated to describe the pack, the forced use and the Legendary.

## Art

- Import the ten attached images (1024×1536 webp, 2:3 card shape) through `tools/CuratedCardArtBuilder` and `assets/pixel/curated-art.json`, keeping original files and checksums. Store the originals inside the repo, e.g. `assets/pixel/source/collector/`, because the existing source folder outside the repo is not present on this machine.
- New category `collector`, rendered at 256×384 like Tarot cards. Resource names are `curated-collector-<sigil>`, `curated-collector-legendary` and `curated-collector-pack`.
- Existing 128×128 `curated-sigil-*` icons remain for small badges on playing cards. Anywhere a Sigil is shown as a card (Arcade, use menu, inventory, collection) uses the collector art.

## Validation

- Engine/App tests: pack drawing is deterministic from the saved RNG; cards are distinct; there is no Legendary with full Joker slots; the Legendary rate is roughly 1/50 per slot over many seeds; picking a Sigil starts a use session that cannot be skipped; picking the Legendary fills free slots without duplicates; Keep is rejected for packs; stale or replayed picks are rejected; resume mid-pack restores the same cards; and old pending Sigil results still offer Keep/Use.
- Server tests: the pick endpoint requires a valid index and revision, and the projection exposes the pack cards.
- Desktop and browser UI tests: reveal, pick, forced use, reduced motion and the Legendary path.
- Art tests: the curated-art manifest includes the new entries with matching checksums and sizes.

## Calls made without the user (reviewable)

- Legendary odds: 1 in 50 per card slot, at most one per pack, hidden when Joker slots are full.
- Legendary Joker pool: the same Jokers the shop could offer, excluding ones already owned.
- Card name is shown together with the Sigil name, and Sigil effects and keys are unchanged.
- Collector art at 256×384, with small Sigil icons kept for on-card badges.
- Original art stored in-repo under `assets/pixel/source/collector/`.
