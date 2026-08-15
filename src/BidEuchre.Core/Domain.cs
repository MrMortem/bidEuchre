namespace BidEuchre.Core;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum Rank
{
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14
}

public readonly record struct Card(Suit Suit, Rank Rank)
{
    public static string DisplayRank(Rank rank) => rank switch
    {
        Rank.Nine => "9",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    public string Code => $"{Rank switch
    {
        Rank.Nine => "9",
        Rank.Ten => "T",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => throw new ArgumentOutOfRangeException()
    }}{Suit switch
    {
        Suit.Clubs => "C",
        Suit.Diamonds => "D",
        Suit.Hearts => "H",
        Suit.Spades => "S",
        _ => throw new ArgumentOutOfRangeException()
    }}";

    public static Card Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var code = value.Trim().ToUpperInvariant();
        if (code.Length is not 2)
        {
            throw new FormatException($"Card '{value}' must use a two-character code such as AS or 9H.");
        }

        var rank = code[0] switch
        {
            '9' => Rank.Nine,
            'T' => Rank.Ten,
            'J' => Rank.Jack,
            'Q' => Rank.Queen,
            'K' => Rank.King,
            'A' => Rank.Ace,
            _ => throw new FormatException($"Card '{value}' has an unknown rank.")
        };
        var suit = code[1] switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            'S' => Suit.Spades,
            _ => throw new FormatException($"Card '{value}' has an unknown suit.")
        };

        return new Card(suit, rank);
    }

    public override string ToString() => Code;
}

public enum BidLevel
{
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    PartnersBest = 7,
    Alone = 8
}

public enum ContractMode
{
    High,
    Low,
    Trump
}

public enum GamePhase
{
    NotStarted,
    Bidding,
    ChoosingContract,
    ExchangingBidderCard,
    ExchangingPartnerCard,
    Playing,
    HandComplete,
    GameComplete
}

public sealed record Contract(BidLevel Bid, ContractMode Mode, Suit? Trump)
{
    public int RequiredTricks => Bid is >= BidLevel.Three and <= BidLevel.Six ? (int)Bid : 6;
    public bool IsPartnersBest => Bid is BidLevel.PartnersBest;
    public bool IsAlone => Bid is BidLevel.Alone;
    internal bool PartnerSitsOut => IsPartnersBest || IsAlone;

    public static Contract Create(BidLevel bid, ContractMode mode, Suit? trump)
    {
        if (!Enum.IsDefined(bid))
        {
            throw new GameRuleException("Unknown bid level.");
        }

        if (bid is BidLevel.Three && mode is not ContractMode.Trump)
        {
            throw new GameRuleException("A 3 bid must become a trump contract.");
        }

        if (bid is BidLevel.PartnersBest && mode is not ContractMode.Trump)
        {
            throw new GameRuleException("Partners Best must use a trump suit.");
        }

        if (mode is ContractMode.Trump && trump is null)
        {
            throw new GameRuleException("A trump contract must name a trump suit.");
        }

        if (mode is not ContractMode.Trump && trump is not null)
        {
            throw new GameRuleException("High and Low contracts cannot name a trump suit.");
        }

        return new Contract(bid, mode, trump);
    }
}

public sealed record AuctionAction(int Seat, BidLevel? Bid)
{
    public bool IsPass => Bid is null;
}

public sealed record CardPlay(int Seat, Card Card);

public sealed record CompletedTrick(int Number, int Leader, int Winner, IReadOnlyList<CardPlay> Plays);

public sealed record HandResult(
    int HandNumber,
    int Bidder,
    Contract Contract,
    int BiddingTeamTricks,
    int DefendingTeamTricks,
    int TeamZeroDelta,
    int TeamOneDelta,
    string Reason);

public sealed class GameRuleException(string message) : InvalidOperationException(message);
