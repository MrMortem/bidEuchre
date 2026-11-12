using BidEuchre.App;
using BidEuchre.Core;

internal static class DealGateTests
{
    public static async Task BotsWait()
    {
        await using var session = new GameSession("deal", "Deal", Enumerable.Range(0,4)
            .Select(i => new SeatConfiguration(i,$"Bot {i}",PlayerKind.Bot,null)).ToArray(),
            new EngineCatalog(), 21, botActionDelay:TimeSpan.Zero);
        await session.StartAsync(waitForDeal:true);
        await Task.Delay(60);
        if (!session.WaitingForDeal || session.Game!.Auction.Count != 0) throw new Exception("Bots bid before deal completion");
        await session.CompleteDealAsync(session.Game.HandNumber);
        await session.WaitForBotsAsync();
        if (session.Game.Phase != GamePhase.HandComplete) throw new Exception("Bots did not resume after dealing");
        var oldHand = session.Game.HandNumber;
        await session.StartNextHandAsync(waitForDeal:true);
        await session.CompleteDealAsync(oldHand);
        await Task.Delay(60);
        if (!session.WaitingForDeal || session.Game.Auction.Count != 0) throw new Exception("Old completion released a new deal");
        await session.CompleteDealAsync(session.Game.HandNumber);
        await session.WaitForBotsAsync();
    }

    public static async Task HumansWait()
    {
        await using var session = new GameSession("deal-human", "Deal", Enumerable.Range(0,4)
            .Select(i => new SeatConfiguration(i,$"Human {i}",PlayerKind.Human,null)).ToArray(), new EngineCatalog(),21);
        await session.StartAsync(waitForDeal:true);
        var request = new GameActionRequest(session.Game!.CurrentSeat!.Value,"bid",BidLevel.Three,null,null,null);
        try { await session.ExecuteAsync(request); throw new Exception("Human bid accepted before dealing finished"); }
        catch (GameRuleException) { }
        await session.CompleteDealAsync(session.Game.HandNumber);
        await session.ExecuteAsync(request);
        if (session.Game.Auction.Count != 1) throw new Exception("Human could not bid after dealing");
    }
}
