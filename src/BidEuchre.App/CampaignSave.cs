using BidEuchre.Core;
namespace BidEuchre.App;

public sealed record CampaignSave(int Version, CampaignRun Run)
{
    public CampaignSaveMetadata? Metadata { get; init; }
}
public sealed record CampaignSaveMetadata(string RunId, long StateRevision, CampaignSavedResult? LastResult = null);
public sealed record CampaignSavedResult(MatchOutcome Outcome, int[] Scores)
{
    public CampaignResultSummary? Summary { get; init; }
}
public sealed record CampaignResultSummary(string[] PlayerNames, HandResult[] HandHistory, string[] PublicEvents, string? CompletionReason);
public enum CampaignLoadStatus { Missing, Corrupt, Loaded }
public sealed record CampaignLoadResult(CampaignLoadStatus Status, CampaignSave? Save = null, string? Error = null);
public sealed record ShopOffer(string Id, FeatureCategory Category, string FeatureId, int Price, bool Sold = false)
{
    /// <summary>A hidden, already-paid Arcade prize backing the ordinary six-card use session.</summary>
    public bool IsArcadePrize { get; init; }
    /// <summary>Tickets the Fortune Teller has printed and paid for, waiting for the player to choose one. Null when none are waiting.</summary>
    public string[]? Printed { get; init; }
    /// <summary>The six physical cards offered for this purchase. Saved once; reopening never redraws them.</summary>
    public CardId[]? TargetCards { get; init; }
}
public sealed record ShopUseSession(string OfferId, FeatureCategory Category, string FeatureId, bool PurchasedItemUsed);
public sealed record ShopState(int MatchJustFinished, ShopOffer[] Offers, int RerollCount = 0)
{
    public ArcadeState? Arcade { get; init; }
    /// <summary>A paid immediate-use purchase and its saved six-card opportunity, retained until explicitly finished.</summary>
    public ShopUseSession? UseSession { get; init; }
}
public enum ArcadeGame { Plinko, Roulette }
public sealed record ArcadeResult(string PlayId, ArcadeGame Game, int Choice, int Landing, int Score,
    int[] Path, FeatureCategory? PrizeCategory, string? FeatureId, CardId[] TargetCards)
{
    /// <summary>A Collector's Pack: three distinct Sigil names or "Legendary". Null for Tarot prizes and older saves.</summary>
    public string[]? PackCards { get; init; }
}
public sealed record ArcadeState(bool PlinkoPlayed = false, bool RoulettePlayed = false, ArcadeResult? Pending = null);
