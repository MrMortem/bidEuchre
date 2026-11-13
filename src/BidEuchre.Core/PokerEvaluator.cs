namespace BidEuchre.Core;

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
    public static PokerValue Best(IReadOnlyList<Card> sixCards)
    {
        if (sixCards.Count != 6) throw new GameRuleException("Poker requires six cards.");
        return Enumerable.Range(0, 6).Select(skip => Evaluate(sixCards.Where((_, i) => i != skip).ToArray())).Max()!;
    }
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
