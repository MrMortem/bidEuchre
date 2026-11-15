using BidEuchre.Core;
namespace BidEuchre.App;

// Arrangement policy changes future automatic ordering and therefore Sticky Fingers.
// Presentation gates, pauses and animation state deliberately are not part of a checkpoint.
public sealed record CampaignArrangement(long DealSerial, bool ManuallyArranged, string? AutoStage);
public sealed record CampaignCheckpoint(CampaignMatchSnapshot Match, CampaignArrangement? Arrangement = null)
{
    public bool IsValid() => Match is not null && Match.IsValid() && (Arrangement is null || Arrangement.DealSerial == Match.DealSerial);
}
