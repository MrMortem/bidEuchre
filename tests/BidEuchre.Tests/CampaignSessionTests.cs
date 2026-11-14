using BidEuchre.App;
using BidEuchre.Core;
internal static class CampaignSessionTests
{
    public static async Task SaveFailureVisible()
    {
        var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"P{s}", PlayerKind.Human, null)).ToArray();
        await using var session = new GameSession("failed-save", "Campaign", seats, new EngineCatalog(), 1, TimeSpan.Zero, campaign: new CampaignContext(CampaignSnapshot.Create(1)), checkpoint: _ => false);
        await session.StartAsync(); Assert.True(session.LastError?.Contains("save") is true);
    }
    public static async Task CheckpointsAndStaleCommands()
    {
        var catalog = new EngineCatalog();
        var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"P{s}", PlayerKind.Human, null)).ToArray();
        CampaignSnapshot? saved = null;
        await using var session = new GameSession("campaign-test", "Campaign", seats, catalog, 42, TimeSpan.Zero,
            campaign: new CampaignContext(CampaignSnapshot.Create(42)), checkpoint: s => { saved = s; return true; });
        await session.StartAsync();
        var seat = session.Game!.CampaignActor!.Value;
        var view = session.Game.CreateCampaignView(seat);
        var command = view.LegalActions.First();
        var request = new CampaignActionRequest(seat, view.Revision, view.DealSerial, command);
        await session.ExecuteCampaignAsync(request);
        Assert.True(saved is not null);
        var revision = saved!.Revision;
        await Assert.ThrowsAsync<GameRuleException>(() => session.ExecuteCampaignAsync(request));
        Assert.Equal(revision, session.Game.ExportCampaignSnapshot().Revision);
    }

    private static SeatConfiguration[] BotSeats() =>
        Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"P{s}", PlayerKind.Bot, null)).ToArray();

    private static async Task WaitForHandAsync(GameSession session)
    {
        for (var i = 0; i < 400 && session.Game!.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && session.BotLoopFault is null; i++)
            await Task.Delay(10);
    }

    public static async Task FailingBotFallsBackToALegalAction()
    {
        // A policy that never produces an action: every bot move goes through the fallback.
        await using var session = new GameSession("bot-fallback", "Campaign", BotSeats(), new EngineCatalog(), 7, TimeSpan.Zero,
            campaign: new CampaignContext(CampaignSnapshot.Create(7)),
            campaignPolicy: (_, _) => throw new GameRuleException("No campaign action is available."));
        await session.StartAsync();
        await WaitForHandAsync(session);
        Assert.True(session.BotLoopFault is null, session.BotLoopFault?.ToString());
        Assert.True(session.Game!.Phase is GamePhase.HandComplete or GamePhase.GameComplete, $"Table stuck in {session.Game.Phase}: {session.LastError}");
        // The fallback plays real cards, so the hand is scored by the campaign rules, not by penalty.
        Assert.False(session.Game.CreateView(0).Events.Any(e => e.Contains("illegal play")));
    }

    public static async Task RejectedBotCommandFallsBackToALegalAction()
    {
        await using var session = new GameSession("bot-rejected", "Campaign", BotSeats(), new EngineCatalog(), 9, TimeSpan.Zero,
            campaign: new CampaignContext(CampaignSnapshot.Create(9)),
            campaignPolicy: (_, _) => new CampaignCommand.SelectOpponent(1));
        await session.StartAsync();
        await WaitForHandAsync(session);
        Assert.True(session.BotLoopFault is null, session.BotLoopFault?.ToString());
        Assert.True(session.Game!.Phase is GamePhase.HandComplete or GamePhase.GameComplete, $"Table stuck in {session.Game.Phase}: {session.LastError}");
        Assert.False(session.Game.CreateView(0).Events.Any(e => e.Contains("illegal play")));
    }

    public static async Task FreePlayActionsRefusedOnCampaignTables()
    {
        var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"P{s}", PlayerKind.Human, null)).ToArray();
        await using var session = new GameSession("campaign-free-play", "Campaign", seats, new EngineCatalog(), 42, TimeSpan.Zero,
            campaign: new CampaignContext(CampaignSnapshot.Create(42)));
        await session.StartAsync();
        var game = session.Game!;
        await Assert.ThrowsAsync<GameRuleException>(() => session.ExecuteAsync(new GameActionRequest(game.CurrentSeat!.Value, "pass", null, null, null, null)));
        while (game.Phase is not GamePhase.Playing)
        {
            var actor = game.CampaignActor!.Value;
            var view = game.CreateCampaignView(actor);
            await session.ExecuteCampaignAsync(new CampaignActionRequest(actor, view.Revision, view.DealSerial, view.LegalActions.First()));
        }
        // A free-play card play used to be turned into an illegal-play penalty that skipped campaign scoring.
        var revision = game.ExportCampaignSnapshot().Revision;
        await Assert.ThrowsAsync<GameRuleException>(() => session.ExecuteAsync(new GameActionRequest(game.CurrentSeat!.Value, "play", null, null, null, "9C")));
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.Equal(revision, game.ExportCampaignSnapshot().Revision);
    }
}
