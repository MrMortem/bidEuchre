namespace BidEuchre.Core;

public static class GameRules
{
    private static readonly BidLevel[] Bids =
    [
        BidLevel.Three,
        BidLevel.Four,
        BidLevel.Five,
        BidLevel.Six,
        BidLevel.PartnersBest,
        BidLevel.Alone
    ];

    public static IReadOnlyList<Card> CreateDeck() =>
        Enum.GetValues<Suit>()
            .SelectMany(suit => Enum.GetValues<Rank>().Select(rank => new Card(suit, rank)))
            .ToArray();

    public static IReadOnlyList<BidLevel> LegalRaises(BidLevel? currentBid) =>
        Bids.Where(bid => currentBid is null || bid > currentBid).ToArray();

    public static Suit EffectiveSuit(Card card, Contract contract)
    {
        if (contract.Mode is ContractMode.Trump &&
            card.Rank is Rank.Jack &&
            card.Suit != contract.Trump &&
            SameColor(card.Suit, contract.Trump!.Value))
        {
            return contract.Trump.Value;
        }

        return card.Suit;
    }

    public static IReadOnlyList<Card> LegalCards(
        IReadOnlyCollection<Card> hand,
        IReadOnlyList<CardPlay> currentTrick,
        Contract contract)
    {
        if (currentTrick.Count is 0)
        {
            return hand.OrderBy(CardSortKey).ToArray();
        }

        var ledSuit = EffectiveSuit(currentTrick[0].Card, contract);
        var following = hand.Where(card => EffectiveSuit(card, contract) == ledSuit)
            .OrderBy(CardSortKey)
            .ToArray();
        return following.Length > 0 ? following : hand.OrderBy(CardSortKey).ToArray();
    }

    public static int DetermineTrickWinner(IReadOnlyList<CardPlay> plays, Contract contract)
    {
        if (plays.Count is 0)
        {
            throw new GameRuleException("Cannot determine the winner of an empty trick.");
        }

        var ledSuit = EffectiveSuit(plays[0].Card, contract);

        if (contract.Mode is ContractMode.Low)
        {
            return plays.Where(play => EffectiveSuit(play.Card, contract) == ledSuit)
                .MinBy(play => (int)play.Card.Rank)!.Seat;
        }

        if (contract.Mode is ContractMode.Trump)
        {
            var trump = contract.Trump!.Value;
            var trumpPlays = plays.Where(play => EffectiveSuit(play.Card, contract) == trump).ToArray();
            if (trumpPlays.Length > 0)
            {
                return trumpPlays.MaxBy(play => TrumpStrength(play.Card, trump))!.Seat;
            }
        }

        return plays.Where(play => EffectiveSuit(play.Card, contract) == ledSuit)
            .MaxBy(play => (int)play.Card.Rank)!.Seat;
    }

    public static int TeamForSeat(int seat)
    {
        ValidateSeat(seat);
        return seat % 2;
    }

    public static int PartnerOf(int seat)
    {
        ValidateSeat(seat);
        return (seat + 2) % 4;
    }

    public static int NextSeat(int seat) => (seat + 1) % 4;

    public static void ValidateSeat(int seat)
    {
        if (seat is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(seat), "Seat must be between 0 and 3.");
        }
    }

    private static bool SameColor(Suit first, Suit second) =>
        IsRed(first) == IsRed(second);

    private static bool IsRed(Suit suit) => suit is Suit.Diamonds or Suit.Hearts;

    private static int TrumpStrength(Card card, Suit trump)
    {
        if (card.Rank is Rank.Jack && card.Suit == trump)
        {
            return 100;
        }

        if (card.Rank is Rank.Jack && card.Suit != trump && SameColor(card.Suit, trump))
        {
            return 99;
        }

        return (int)card.Rank;
    }

    private static int CardSortKey(Card card) => ((int)card.Suit * 100) + (int)card.Rank;
}
