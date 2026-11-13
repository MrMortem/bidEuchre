using BidEuchre.Core;

namespace BidEuchre.App;

public enum HandSort
{
    Suit,
    Rank
}

/// <summary>How a face-up hand is laid out for its player. Display only: the game never sees this order.</summary>
public static class HandOrder
{
    // Alternating colours keep neighbouring suit groups easy to tell apart.
    private static readonly Suit[] Cycle = [Suit.Spades, Suit.Hearts, Suit.Clubs, Suit.Diamonds];

    /// <summary>Strongest first. The sort is stable, so identical campaign cards keep their order.</summary>
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> cards, Func<T, Card> face, HandSort sort, Contract? contract)
    {
        var trump = contract is { Mode: ContractMode.Trump } ? contract.Trump : null;
        var first = trump is { } suit ? Array.IndexOf(Cycle, suit) : 0;
        int SuitOrder(T card)
        {
            var effective = contract is null ? face(card).Suit : GameRules.EffectiveSuit(face(card), contract);
            return (Array.IndexOf(Cycle, effective) - first + Cycle.Length) % Cycle.Length;
        }
        int Strength(T card)
        {
            var value = face(card);
            if (trump is null || value.Rank is not Rank.Jack || SuitOrder(card) != 0) return (int)value.Rank;
            return value.Suit == trump ? 100 : 99;
        }
        return sort is HandSort.Rank
            ? cards.OrderByDescending(Strength).ThenBy(SuitOrder).ToArray()
            : cards.OrderBy(SuitOrder).ThenByDescending(Strength).ToArray();
    }
}
