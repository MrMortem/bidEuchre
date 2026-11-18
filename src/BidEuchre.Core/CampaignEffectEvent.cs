namespace BidEuchre.Core;

public enum CampaignScoringEffect { Lucky, Stone, Fish, Duck, Golden, Millionaire, Cursed, Silver, PeoplePleaser, PennyPincher, Bully, Taxes }
public enum CampaignEffectReward { Points, Money }

/// <summary>A public scoring activation. A card source is present only for a card already played publicly.</summary>
/// <param name="Recipient">Team index for Points, seat index for Money.</param>
public sealed record CampaignEffectEvent(int Sequence, CampaignScoringEffect Effect, CampaignEffectReward Reward,
    int Amount, int Recipient, CardId? SourceCard, int? SourceSeat, int? TrickNumber = null);
