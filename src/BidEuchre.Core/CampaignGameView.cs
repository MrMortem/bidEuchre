namespace BidEuchre.Core;

public sealed record CampaignCardView(CardId Id, Card Face, EnhancementId? Enhancement, SigilId? Sigil);
public sealed record CampaignPlayerView(int Seat, int CardCount, IReadOnlyList<CampaignCardView> VisibleCards, bool IsSittingOut);
public sealed record CampaignTrickPlay(int Seat, CampaignCardView Card, Suit EffectiveSuit);
public sealed record CampaignGameView(long Revision, long DealSerial, int? ViewerSeat,
    IReadOnlyList<CampaignPlayerView> Players, IReadOnlyList<CampaignCommand> LegalActions,
    IReadOnlyList<CampaignTrickPlay> Trick, IReadOnlyList<string> Events,
    CampaignInventory Inventory, int Money, int HandNumber, int Threshold, string? PendingChoice)
{
    public IReadOnlyList<CampaignTrickPlay> LastTrick { get; init; } = [];
}
