namespace BidEuchre.Core;

public abstract record CampaignCommand
{
    public sealed record Bid(BidLevel Level, GumballId? Variant = null) : CampaignCommand;
    public sealed record Pass : CampaignCommand;
    public sealed record PanicButton : CampaignCommand;
    public sealed record Play(CardId Id, Suit? EffectiveSuit = null) : CampaignCommand;
    public sealed record Exchange(CardId Id) : CampaignCommand;
    public sealed record ChooseContract(ContractMode Mode, Suit? Trump = null) : CampaignCommand;
    public sealed record UseTarot(int Slot, IReadOnlyList<CardId> Targets) : CampaignCommand;
    public sealed record UseSigil(int Slot, CardId Target) : CampaignCommand;
    public sealed record SwapPartnerCard(CardId Id) : CampaignCommand;
    public sealed record ChooseRevealSeat(int Seat) : CampaignCommand;
    public sealed record RevealPartnerCard(CardId Id) : CampaignCommand;
    public sealed record SelectOpponent(int Seat) : CampaignCommand;
}
