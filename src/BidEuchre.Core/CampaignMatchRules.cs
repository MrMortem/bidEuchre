namespace BidEuchre.Core;
public static class CampaignMatchRules
{
    public static int? Winner(int team0, int team1, int completedHands, int threshold)
    {
        if (team0 == team1) return null;
        return completedHands >= 8 || Math.Max(team0,team1) >= threshold || Math.Min(team0,team1) <= -threshold
            ? team0 > team1 ? 0 : 1 : null;
    }
}
