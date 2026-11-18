# Bid Euchre Rules

These are the complete rules of a standard game, and the game follows them
exactly.

## 1. Game Components

- There are four players divided into two teams of two.
- Partners sit opposite each other.
- The deck contains 24 cards: `A`, `K`, `Q`, `J`, `10`, and `9` in each of the
  four suits.
- All 24 cards are dealt, so every player receives six cards and there is no
  undealt pile.
- A hand contains six tricks because each player begins with six cards.

## 2. Hand Sequence

Each hand has the following phases:

1. Choose a dealer and deal six cards to every player.
2. Hold the bidding auction.
3. Set up the winning contract. Partners Best includes a private card exchange;
   after that exchange, Partners Best and Alone both remove the bidder's partner
   from trick play.
4. Play six tricks.
5. Score the contract.

The first dealer of a game is selected randomly. After each hand, the dealer
position moves one seat clockwise. The dealer deals all 24 cards but otherwise
has no special role during trick play.

## 3. Bidding

### 3.1 What a Bid Contains

During the auction, a player announces only one of these bid levels:

```text
3, 4, 5, 6, Partners Best, or Alone
```

The player does **not** announce High, Low, Trump, or a trump suit as part of the
bid. Only the auction winner chooses the contract, after the auction has ended.

The choices available to the winner depend on the winning bid:

| Winning bid | Contract choices after the auction |
| --- | --- |
| 3 | Trump only; choose one of the four suits |
| 4, 5, or 6 | High, Low, or Trump; choose a suit if Trump |
| Partners Best | Partners Best; choose one of the four trump suits |
| Alone | High, Low, or Trump; choose a suit if Trump |

Partners Best and Alone always require all six tricks regardless of their
chosen play mode.

### 3.2 Bid Order and Legal Raises

- Bidding proceeds clockwise.
- The player immediately left of the dealer bids first, and the dealer bids
  last.
- Each player receives exactly one bidding turn per hand.
- A player may either make a legal bid or pass.
- The first bid establishes the current high bid.
- A later bid must be strictly stronger than the current high bid.
- A player who passes does not receive another opportunity to bid.
- For numeric bids, a larger number is always stronger than a smaller number.
- Partners Best beats every normal bid, including a normal bid of 6.
- Alone beats Partners Best and every normal bid.
- There is no bid above Alone.
- If the first three players all pass, the dealer is required to make a bid.
- If another player has already bid, the dealer may either make a strictly
  stronger bid or pass.
- The auction ends after the dealer's bidding turn. The player who made the
  strongest bid becomes the bidder and then selects an allowed contract.

The complete strength order is therefore:

```text
3 tricks < 4 tricks < 5 tricks < 6 tricks < Partners Best < Alone
```

A 3-trick bid can only become a trump-suit contract. High and Low are available
only when the winning numeric bid is at least 4.

## 4. Card Rank and Effective Suit

### 4.1 High and Ordinary Non-Trump Suits

From highest to lowest, cards rank:

```text
A > K > Q > J > 10 > 9
```

### 4.2 Low

Low uses the reverse outcome: the lowest card in the suit led wins. From lowest
to highest, cards rank:

```text
9 < 10 < J < Q < K < A
```

There is no trump in Low.

### 4.3 Trump Suits and Bowers

In a trump contract, trump cards rank from highest to lowest:

1. Right Bower: the Jack of the trump suit.
2. Left Bower: the Jack of the other suit of the same color as trump.
3. Ace of trump.
4. King of trump.
5. Queen of trump.
6. Ten of trump.
7. Nine of trump.

The Left Bower is treated as a member of the trump suit for the entire trick.
It is not treated as a member of the suit printed on the card. This affects both
following suit and determining the winner. For example, if Hearts are trump, the
Jack of Diamonds is a Heart, not a Diamond.

Non-trump suits use the normal high ranking:

```text
A > K > Q > J > 10 > 9
```

The non-trump suit with the same color as trump has only five cards because its
Jack becomes the Left Bower.

## 5. Contract Rules

### 5.1 High

- There is no trump suit.
- Players must follow the suit led when able.
- The highest card in the suit led wins the trick.

### 5.2 Low

- There is no trump suit.
- Players must follow the suit led when able.
- The lowest card in the suit led wins the trick.

### 5.3 Trump

- The winning contract identifies one trump suit.
- Players must follow the effective suit led when able.
- If one or more trump cards are played, the highest trump wins.
- If no trump is played, the highest card in the suit led wins.
- A player who cannot follow suit may play any card, including a trump card.

### 5.4 Partners Best

- The winning contract identifies one trump suit.
- Before trick play, the bidder gives one card from their hand to their partner.
- The partner then gives one card from their hand to the bidder.
- Each player still has six cards after the exchange.
- After the exchange, the bidder's partner sits out and does not play any cards.
- The bidder plays against the two opponents, so three players participate in
  each trick.
- The bidder must win all six tricks; the sitting partner's six cards remain
  unused.
- Trick play and card ranking follow the Trump rules for the chosen suit.

The words “worst card” and “best card” describe the players' strategic choices;
the game should not automatically decide which cards are best or worst.

### 5.5 Alone

- The winning contract identifies High, Low, or one of the four trump suits.
- The bidder's partner sits out for the entire hand and does not play any cards.
- The bidder plays against the two opponents, so three players participate in
  each trick.
- The bidder must personally win all six tricks.
- The sitting partner's six cards remain unused.
- Trick play and card ranking follow the selected High, Low, or Trump rules.

## 6. Playing a Trick

The player immediately left of the dealer leads the first trick. In a Partners
Best or Alone contract, if that player is the bidder's sitting partner, play
skips clockwise to the next active player.

For each trick:

1. The trick leader plays one card.
2. The other active players play one card each in clockwise order.
3. A player must follow the effective suit led if their hand contains a card of
   that suit.
4. A player who cannot follow suit may play any card. In High and Low this is a
   discard; in a trump contract it may be either a trump card or a discard.
5. Determine the winner using the active contract's ranking rules.
6. The trick winner leads the next trick.

Cards outside the suit led cannot win a High or Low trick. In a trump contract,
a card outside the suit led can win only if it is trump.

## 7. Scoring

### 7.1 Normal Bids: High, Low, or Trump

Count all tricks won by the bidder's team.

- If the team wins at least the bid target, it scores the actual number of
  tricks it won.
- If the team wins fewer tricks than the target, it loses points equal to the
  bid target.
- The defending team scores one point for every trick it wins, whether or not
  the bidder's team fulfills its contract.

Examples:

| Bid | Tricks won | Score change |
| --- | ---: | ---: |
| 4 | 5 | +5 |
| 4 | 4 | +4 |
| 4 | 3 | -4 |

For example, if the bidder's team bids 4 and each team takes three tricks, the
bidder's team scores `-4` and the defending team scores `+3`.

### 7.2 Partners Best

- The bidder wins all six tricks: `+12` points for the bidder's team.
- The bidder loses one or more tricks: `-12` points for the bidder's team.
- The defending team also scores one point for each trick it wins.

### 7.3 Alone

- The bidder personally wins all six tricks: `+24` points for the bidder's team.
- The bidder loses one or more tricks: `-24` points for the bidder's team.
- The defending team also scores one point for each trick it wins.

## 8. Private Information

During Partners Best, each exchanged card is visible only to the bidder and the
bidder's partner. The opponents do not see either exchanged card.

## 9. Winning the Game

Scores are evaluated after each hand:

- If only one team has at least 40 points, that team wins.
- If both teams have at least 40 points, the team with the higher score wins.
- If both teams have at least 40 points and their scores are tied, play continues
  until a hand ends with one team having more points than the other.

## 10. Misdeals and Illegal Actions

- A misdeal causes the cards to be redealt.
- A player who makes an illegal bid may immediately correct it, provided the
  next player has not yet taken their bidding turn. Once the next player acts,
  the illegal bid is treated as a pass.
- If a defender makes an illegal card play, the bidder's team automatically
  receives the maximum successful score for its contract: `+6` for any normal
  numeric contract, `+12` for Partners Best, or `+24` for Alone. The defending
  team scores zero points for the hand, including no points for tricks it took
  before the illegal play.
- If a member of the bidding team makes an illegal card play, the bidding team
  automatically fails its contract. The bidding team receives the normal
  failure score for that contract (`-` the numeric bid, `-12` for Partners Best,
  or `-24` for Alone), and the defending team scores `+6`.


## 11. Campaign Card Effects

Campaign rounds use these additional card effects. “Your team” includes you and
your partner; a round is one six-trick hand.

During a match, stored Arcade Tickets (Tarots) and Sigils may be used on
your own turn. Any card they affect must currently be in your hand; cards already
played and cards held by other players are not eligible. Gambler makes exactly two distinct cards you select from your current hand Lucky. Effects that do not target cards retain their
normal behavior. Using a consumable does not end your turn.

The shop Arcade replaces direct Sigil sales. Plinko and roulette each cost $3,
once per game per shop visit. Rerolls do not restore plays. Plinko's printed scores
5–6 win a Collector's Pack, 3–4 win a Tarot ticket, and 1–2 win nothing. Choose one
of eight drop positions before paying. Roulette has twelve numbered pockets: matching
your chosen number wins a Collector's Pack, matching only its color wins a Tarot
ticket, and the opposite color wins nothing. Odd pockets are red; even pockets are
black. Payments and outcomes, including a pack's three cards, are saved before
animation. Keep a Tarot prize if inventory has room, or use it immediately even when
inventory is full. Collect the prize before another play, reroll or shop departure.
A new shop visit restores both plays.

A Collector's Pack opens to three different cards; choose exactly one. Each card is
a Sigil shown by its collector title: Strength (Bear), Abundance (Fish), Echo (Frog),
Lucky (Duck), Deception (Chameleon), Resilient (Eagle), Affliction (Spider) and
Resourceful (Vulture). A chosen Sigil cannot be kept: it must be applied to one of
the six offered cards below. Rarely, while a Joker slot is free, one card is
**Legendary** (a 1 in 50 chance per card, at most one per pack). Choosing it fills
every empty Joker slot with a random Joker you do not already own.

Shop rewards provide a separate opportunity: applying an Arcade prize,
or using a newly printed Fortune Teller ticket, displays six random cards from
the shared deck. Only those six can be affected, including both cards selected for Gambler.
All tickets in one print share the same six cards. Opening the six-card menu commits
the chosen prize or ticket, and you must apply that item before Done becomes
available. There is no Back or Cancel. Unfinished Sigil purchases from older saves
retain their original six cards and must also be completed.
While this menu is open, any Tarot already in your inventory may also be used on
those same six cards, before or after the purchased item. Stored Sigils cannot be
used in this menu. Effects with no targets can also be used here.
Closing and reopening the game restores the unfinished menu, the same six cards,
and all effects already applied without charging again. A paid shop reroll after
finishing the menu may provide a new selection. Before opening the menu, you may
instead keep a purchased consumable for a future match if a slot is available.
Stored consumables cannot otherwise be used in the shop or pre-match screen.

- **Duck Sigil:** When played, 1 in 3 chance to earn +4 points.
- **Snake Eyes:** If your team wins a trick containing 2 or more Aces, +4 points.
- **Roller Coaster:** +0 points each round when High/Low is trump. +1 when your
  team wins a High/Low trick. −1 when your team loses one. Award only that round’s
  accumulated value at its end; suit-trump rounds receive no Roller Coaster points.
- **Cure-All:** Cursed cards in tricks that your team wins give +2 points instead
  of −1. Opponent-won tricks still lose 1 point per Cursed card.
- **Lover Boy:** When you call Hearts as trump, each Heart in a trick your team
  wins adds +1 point to that trick.
- **Clubhouse:** Tricks your team wins that contain at least one Club are worth
  +1 point. Multiple Clubs still grant one bonus.
- **Royal Council:** Tricks your team wins that contain an Ace, King, Queen,
  and Jack are worth ×2 points.
- **Double Down:** Retrigger the first card you play each round. Earlier plays
  by other players do not consume it. Frog and Lucky Duck each add their own
  independent activation; replayed effects do not create further replay chains.
  A Bouncy activation replays earlier eligible on-play effects once. Glass
  multiplies card-effect points, including those from the extra activation.
- **Last Laugh:** Double all points and effects from the final trick of each
  round. This timing applies every round of the match.

## 12. Campaign Bidding

- Every seat shares the campaign inventory's unlock gates for Safety Net (bid 2),
  Partner's Best, Alone, and High/Low. Other special variants retain their human-only
  targeting. Free-play bids and contracts use the normal rules above.
- **Alpha:** You may outbid your partner by matching any eligible unlocked bid,
  including Safety Net, numeric bids, Partner's Best, and Alone. Alpha never
  permits matching an opponent or bypassing a bid's restrictions.
- **Poker:** Bid exactly 4. If you win the auction, reveal all four hands and
  compare each player's best five of six current card faces. Highlight the five
  cards of every tied winning player and award +4 points once per winning team.
  A tie within one team pays once; a tie across teams pays both teams. After the
  result is displayed, deal the next round automatically. Do not select trump,
  play tricks, or apply made/set contract scoring. A Poker showdown counts as
  one round; show its result before ending a match reached by those points or
  the round limit. Extra Poker copies add +1 each when your team wins Poker.
  Wild and Bear abilities do not change the poker comparison; actual copied or
  transformed faces do. Five of a kind ranks above a straight flush.
- **Duelist:** Bid 4 or 5 and play alone against one chosen opponent. A failed
  contract loses 2 additional points: −6 for Duelist-4 or −7 for Duelist-5 before
  independent effects. Protective Bubble still cancels the failed contract's
  penalty. Duelist activates once per match, consuming its use when it wins the
  auction. Rejected bids and bids overcalled by another player consume no use.
  Starting another round or redealing does not restore it; a new match does.


## 13. Campaign Ante Scoring

Each ante contains three matches, including its boss. Both teams start every
normal and boss match at 0 points. Evaluate scores after a complete
six-trick round or a Poker showdown. End the match when either team reaches the positive target or
negative limit below, or after eight complete rounds; the higher score wins.
A tie continues through additional complete rounds until a round ends untied.

| Ante | Positive target / negative limit | Successful opponent bid bonus |
| --- | --- | --- |
| 1 | +12 / −12 | +0 points |
| 2 | +16 / −16 | +1 point |
| 3 | +24 / −24 | +2 points |
| 4 | +32 / −32 | +3 points |
| 5 | +42 / −42 | +4 points |

Award the opponent bonus once when the opponent team makes its contract,
including unlocked Safety Net, High/Low, Partner's Best, and Alone contracts.
Failed opponent contracts, player-team contracts, and opponent defender tricks
receive no bonus. It changes team points only, never money or trick counts.
Add this fixed amount after all trick and whole-round multipliers, preserving
the table's exact amount. Chaos doubles the ordinary round contributions; it
remains a human-only variant and therefore earns no opponent successful-bid
bonus for either its bidder or defenders.

Endless antes beyond Ante 5 retain the prior target formula
`24 + 4 × (Ante − 1)` and zero opponent successful-bid bonus. The approved table
does not establish any new endless scaling policy.
