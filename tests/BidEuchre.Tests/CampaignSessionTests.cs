using BidEuchre.App;
using BidEuchre.Core;
internal static class CampaignSessionTests
{
    public static async Task CheckpointsAndStaleCommands()
    {
        var catalog=new EngineCatalog();
        var seats=Enumerable.Range(0,4).Select(s=>new SeatConfiguration(s,$"P{s}",PlayerKind.Human,null)).ToArray();
        CampaignSnapshot? saved=null;
        await using var session=new GameSession("campaign-test","Campaign",seats,catalog,42,TimeSpan.Zero,
            campaign:new CampaignContext(CampaignSnapshot.Create(42)),checkpoint:s=>{saved=s;return true;});
        await session.StartAsync();
        var seat=session.Game!.CampaignActor!.Value;
        var view=session.Game.CreateCampaignView(seat);
        var command=view.LegalActions.First();
        var request=new CampaignActionRequest(seat,view.Revision,view.DealSerial,command);
        await session.ExecuteCampaignAsync(request);
        Assert.True(saved is not null);
        var revision=saved!.Revision;
        await Assert.ThrowsAsync<GameRuleException>(()=>session.ExecuteCampaignAsync(request));
        Assert.Equal(revision,session.Game.ExportCampaignSnapshot().Revision);
    }
}
