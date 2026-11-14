# Gumball machine design

## Intent

The special-bid unlocks stop being Planet cards and become gumballs. In the shop a gumball machine
replaces the Planet card: the player pays, the machine plays an animation, and a gumball rolls out
and reveals which bid it unlocked. The pull is blind.

## Goals

- A gumball machine in the shop, where the Planet card was, that sells one blind pull per visit for $3.
- A pull animation with a payoff: coin in, crank turns, the gumballs in the globe jostle, one rolls
  out of the chute, settles, and opens to show the bid's name.
- "Planet" becomes "Gumball" everywhere the player or the code sees it.
- Runs saved before this change still load.

## Non-goals

- No change to what the eight bids do, what a pull costs, or how many are offered per visit.
- The machine is drawn in code. Each gumball uses its sprite from
  `assets/pixel/campaign-placeholders/gumball/<name>.png` when one exists, and a plain coloured ball until then.
- No sound.

## Anti-goals

- The player must not be able to see which bid is coming before paying.
- A pull must never be lost or repeated by leaving the shop or quitting during the animation.

## Approaches

1. **Blind pull, rolled when the player pays (chosen).** The shop holds a Gumball offer with no bid
   in it. Paying draws a bid the player does not own from the run's seeded random state and records
   it on the sold offer. Nothing about the result exists beforehand, and a bid unlocked earlier in
   the same visit by the Astronomer can never be drawn again.
2. Blind pull, rolled when the shop opens and hidden by the screen. Less code, but the result sits
   in the save file, and it can collide with an Astronomer unlock made before the pull.
3. Show the bid on the machine before paying. No rules change, but the animation has no surprise.
   The user picked a blind pull.

## Design

**Rules (Core and App)**

- `FeatureCategory.Planet` becomes `Gumball` and `PlanetId` becomes `GumballId`. Catalogue ids
  become `gumball:<name>`. Saved runs that hold `planet:<name>` are read as the matching gumball.
- `CampaignShop.Open` adds one Gumball offer at $3 with an empty feature id, unless all eight bids
  are owned.
- `CampaignShop.Buy` on that offer draws one unowned bid at random, unlocks it, and writes its id
  onto the sold offer. With nothing left to draw it refuses with "All bidding options are unlocked."
- The Astronomer keeps its name. Its text becomes "Unlock a random Gumball bid."

**Shop screen (Desktop)**

- The machine stands beside the cards, about two cards in size and as tall as both rows: a glass
  globe of coloured gumballs on a red base with a coin slot, a crank and a chute. Each of the eight
  bids has its own gumball colour; bids already owned are missing from the globe.
- Pressing it shows "GUMBALL · Mystery bid" in the side panel with a `Pull $3` button, disabled with
  the usual "You need $N more." when the player is short.
- Pressing `Pull`: the purchase is made and saved first. The rest of the shop stops taking input,
  then the animation plays for about two seconds and the gumball opens to show the bid's name. The
  shop then redraws with the note "The gumball unlocked the <name> bid!" and the machine marked
  SOLD.
- With motion switched off in settings, the animation is skipped and the shop goes straight to the
  note.
- With all eight bids owned the machine reads EMPTY and cannot be pressed.
- The `PLANET BIDS n/8` label becomes `GUMBALLS n/8` followed by a small ball in each owned bid's
  colour, with the bid's name as its tooltip.

**Housekeeping**

- The eight planet icons are no longer bundled with the app. The files stay in the repository.
- README and the tests follow the rename. New tests cover the blind pull, the Astronomer collision,
  the empty machine and loading an old save.

## Left to the builder

The exact drawing of the machine, the eight colours, and the timing and easing of each beat of the
animation.
