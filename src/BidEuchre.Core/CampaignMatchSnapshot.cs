namespace BidEuchre.Core;

/// <summary>Versioned, detached campaign continuation. Physical IDs retain order and historical faces remain frozen.</summary>
public sealed partial record CampaignMatchSnapshot
{
    public required int Version { get; init; }
    public required string[] PlayerNames { get; init; }
    public required MatchOptions Options { get; init; }
    public required CampaignSnapshot Resources { get; init; }
    public required CardId[][] Hands { get; init; }
    public required AuctionAction[] Auction { get; init; }
    public required SnapshotPlay[] CurrentTrick { get; init; }
    public required SnapshotTrick[] CompletedTricks { get; init; }
    public required HandResult[] HandHistory { get; init; }
    public required string[] Events { get; init; }
    public required string[] PriorHandEvents { get; init; }
    public required CardId? PendingExchangeCard { get; init; }
    public required CardOverlay[] Overlays { get; init; }
    public required SnapshotWildSuit[] WildSuits { get; init; }
    public required SnapshotReveal[] Reveals { get; init; }
    public required int[] StickyTriggered { get; init; }
    public required SnapshotTrickHistory[] TrickHistory { get; init; }
    public required SnapshotEffectPoint[] TrickPoints { get; init; }
    public required GamePhase Phase { get; init; }
    public required int HandNumber { get; init; }
    public required long DealSerial { get; init; }
    public required int Dealer { get; init; }
    public required int? CurrentSeat { get; init; }
    public required BidLevel? HighBid { get; init; }
    public required int? Bidder { get; init; }
    public required Contract? Contract { get; init; }
    public required int? GameWinner { get; init; }
    public required string? CompletionReason { get; init; }
    public required int BiddingTurns { get; init; }
    public required GumballId? Variant { get; init; }
    public required string? PendingChoice { get; init; }
    public required int? PendingActor { get; init; }
    public required int? DuelOpponent { get; init; }
    public required bool DuelistUsedThisMatch { get; init; }
    public required bool PokerRevealed { get; init; }
    public PokerRoundResult? PokerResult { get; init; }
    public required bool TelepathDone { get; init; }
    public required bool FirstMatchCard { get; init; }
    public required bool FirstHumanRoundCard { get; init; }
    public required CardId? LastMatchCard { get; init; }
    public required int RollerValue { get; init; }
    // Older checkpoints stored the accumulated streak here.
    public required int DejaValue { get; init; }
    public bool? RepeatsPreviousTrump { get; init; }
    public required int EffectSequence { get; init; }
    public CampaignEffectEvent[] ScoringEffects { get; init; } = [];
    public required int[] Scores { get; init; }
    public required int[] TricksByTeam { get; init; }
    public required int[] CardPoints { get; init; }
    public required int[] JokerPoints { get; init; }
    public required int[] ModifierPoints { get; init; }
    public required int[] MatchOvertricks { get; init; }
    public required int[] HandWalletStart { get; init; }
}

public sealed record SnapshotPlay(int Seat, CardId Id);
public sealed record SnapshotTrick(int Number, int Leader, int Winner, SnapshotPlay[] Plays);
public sealed record SnapshotWildSuit(CardId Id, Suit Suit);
public sealed record SnapshotReveal(int Viewer, CardId Id);
public sealed record SnapshotTrickHistory(int Number, CampaignTrickPlay[] Plays);
public sealed record SnapshotEffectPoint(int Team, int Amount, string Source);
