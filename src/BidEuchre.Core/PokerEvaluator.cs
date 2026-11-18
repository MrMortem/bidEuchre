namespace BidEuchre.Core;

public sealed record PokerWinningHand(int Seat, string Category, IReadOnlyList<CardId> CardIds)
{
    private readonly IReadOnlyList<CardId> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<CardId> CardIds
    {
        get => _cardIds;
        init => _cardIds = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record PokerRoundResult(IReadOnlyList<PokerWinningHand> Winners, IReadOnlyList<int> TeamPoints)
{
    private readonly IReadOnlyList<PokerWinningHand> _winners = Array.AsReadOnly(Winners.ToArray());
    private readonly IReadOnlyList<int> _teamPoints = Array.AsReadOnly(TeamPoints.ToArray());
    public IReadOnlyList<PokerWinningHand> Winners
    {
        get => _winners;
        init => _winners = Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<int> TeamPoints
    {
        get => _teamPoints;
        init => _teamPoints = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record PokerValue(int Category, int[] Kickers) : IComparable<PokerValue>
{
    public int CompareTo(PokerValue? other)
    {
        if (other is null) return 1; var category = Category.CompareTo(other.Category); if (category != 0) return category;
        for (var i = 0; i < Math.Min(Kickers.Length, other.Kickers.Length); i++) { var rank = Kickers[i].CompareTo(other.Kickers[i]); if (rank != 0) return rank; }
        return 0;
    }
}
public static class PokerEvaluator
{
    internal const int WinningPoints = 4;
    private static readonly string[] Categories =
    [
        "High card", "One pair", "Two pair", "Three of a kind", "Straight", "Flush",
        "Full house", "Four of a kind", "Straight flush", "Five of a kind"
    ];

    public static PokerValue Best(IReadOnlyList<Card> sixCards) => BestCombination(sixCards).Value;

    private static (PokerValue Value, int Skipped) BestCombination(IReadOnlyList<Card> sixCards)
    {
        if (sixCards.Count != 6) throw new GameRuleException("Poker requires six cards.");

        // Try the first five cards first, then replace one. An equal value keeps the first combination.
        return Enumerable.Range(0, 6).Reverse()
            .Select(skip => (Value: Evaluate(sixCards.Where((_, i) => i != skip).ToArray()), Skipped: skip))
            .MaxBy(combination => combination.Value);
    }

    internal static PokerRoundResult Showdown(IEnumerable<IEnumerable<CardState>> hands)
    {
        var evaluated = hands.Select((hand, seat) =>
        {
            var cards = hand.OrderBy(card => card.Id.Value).ToArray();
            var (value, skipped) = BestCombination(cards.Select(card => card.Face).ToArray());
            var cardIds = cards.Where((_, i) => i != skipped).Select(card => card.Id).ToArray();
            return (Value: value, Hand: new PokerWinningHand(seat, Categories[value.Category], cardIds));
        }).ToArray();
        var best = evaluated.Max(hand => hand.Value);
        var winners = evaluated.Where(hand => hand.Value.CompareTo(best) == 0).Select(hand => hand.Hand).ToArray();
        var points = new int[2];
        foreach (var winner in winners) points[GameRules.TeamForSeat(winner.Seat)] = WinningPoints;
        return new(winners, points);
    }

    internal static bool IsCategory(string category) => Categories.Contains(category);

    private static PokerValue Evaluate(Card[] cards)
    {
        var groups = cards.GroupBy(c => (int)c.Rank).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToArray();
        var ranks = cards.Select(c => (int)c.Rank).OrderDescending().ToArray();
        var flush = cards.Select(c => c.Suit).Distinct().Count() == 1;
        var straight = ranks.Distinct().Count() == 5 && ranks[0] - ranks[4] == 4;
        if (groups[0].Count() == 5) return new(9, [groups[0].Key]);
        if (flush && straight) return new(8, [ranks[0]]);
        if (groups[0].Count() == 4) return new(7, groups.Select(g => g.Key).ToArray());
        if (groups[0].Count() == 3 && groups[1].Count() == 2) return new(6, groups.Select(g => g.Key).ToArray());
        if (flush) return new(5, ranks); if (straight) return new(4, [ranks[0]]);
        if (groups[0].Count() == 3) return new(3, groups.Select(g => g.Key).ToArray());
        if (groups[0].Count() == 2 && groups[1].Count() == 2) return new(2, groups.Select(g => g.Key).ToArray());
        if (groups[0].Count() == 2) return new(1, groups.Select(g => g.Key).ToArray());
        return new(0, ranks);
    }
}
