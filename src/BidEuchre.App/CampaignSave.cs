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
public sealed record ShopOffer(string Id, FeatureCategory Category, string FeatureId, int Price, bool Sold = false);
public sealed record ShopState(int MatchJustFinished, ShopOffer[] Offers, int RerollCount = 0);
