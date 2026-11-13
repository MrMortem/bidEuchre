using BidEuchre.Core;
namespace BidEuchre.App;

public sealed record CampaignSave(int Version, CampaignRun Run);
public sealed record ShopOffer(string Id, FeatureCategory Category, string FeatureId, int Price, bool Sold = false);
public sealed record ShopState(int MatchJustFinished, ShopOffer[] Offers);
