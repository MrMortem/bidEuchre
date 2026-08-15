# Bid Euchre Rules — Implementation Specification

This document is the authoritative implementation specification for the game.

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
