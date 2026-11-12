using BidEuchre.Core;
internal static class CampaignMatchTests
{
    public static void Endings()
    {
        Assert.True(CampaignMatchRules.Winner(24,24,3,24) is null);
        Assert.Equal(1, CampaignMatchRules.Winner(-24,-20,2,24)!.Value);
        Assert.Equal(0, CampaignMatchRules.Winner(4,3,8,24)!.Value);
        Assert.True(CampaignMatchRules.Winner(4,4,8,24) is null);
        Assert.Equal(0, CampaignMatchRules.Winner(5,4,9,24)!.Value);
        Assert.Equal(1, CampaignMatchRules.Winner(25,30,3,24)!.Value);
    }
}
