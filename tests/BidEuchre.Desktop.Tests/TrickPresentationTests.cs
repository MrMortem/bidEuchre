using BidEuchre.App;

internal static class TrickPresentationTests
{
    public static void Run()
    {
        Task.Run(HoldsBotUntilMatchingAcknowledgment).GetAwaiter().GetResult();
        CampaignResumeUiTests.FinalScoringChainFinishesBeforeResults();
    }

    private static async Task HoldsBotUntilMatchingAcknowledgment()
    {
        var seats = Enumerable.Range(0, 4).Select(seat => new SeatConfiguration(seat, $"Bot {seat}", PlayerKind.Bot, null)).ToArray();
        var constructor = typeof(GameSession).GetConstructors().Single();
        await using var session = (GameSession)constructor.Invoke(constructor.GetParameters().Select(p => p.Name switch
        {
            "id" or "name" => (object)"trick test", "seats" => seats, "catalog" => new EngineCatalog(), "seed" => 41,
            "botActionDelay" or "completedTrickDelay" => TimeSpan.Zero, "waitForTrickPresentation" => true,
            _ => p.DefaultValue
        }).ToArray());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.StartAsync(deadline.Token);
        var property = typeof(GameSession).GetProperty("WaitingForTrickPresentation");
        var complete = typeof(GameSession).GetMethod("CompleteTrickPresentationAsync");
        bool Pending() => property?.GetValue(session) is true;
        async Task Complete(long deal, int trick) => await (Task)complete!.Invoke(session, [deal, trick, deadline.Token])!;
        while (!Pending() && (await session.GetViewAsync(null, deadline.Token))!.CompletedTricks.Count == 0)
            await Task.Delay(5, deadline.Token);
        if (!Pending()) throw new Exception("The next lead must wait for completed-trick presentation.");
        var view = (await session.GetViewAsync(null, deadline.Token))!;
        var deal = view.DealSerial!.Value;
        await Complete(deal + 1, 1);
        await Complete(deal, 2);
        await Task.Delay(100, deadline.Token);
        view = (await session.GetViewAsync(null, deadline.Token))!;
        if (!Pending() || view.CompletedTricks.Count != 1 || view.CurrentTrick.Count != 0)
            throw new Exception("Stale acknowledgments must not let a bot lead.");
        await Complete(deal, 1);
        while ((await session.GetViewAsync(null, deadline.Token))!.CompletedTricks.Count == 1)
            await Task.Delay(5, deadline.Token);
        await session.PauseAsync(deadline.Token);
        if (Pending()) throw new Exception("Pausing must release the abandoned presentation.");
        Console.WriteLine("PASS  Trick presentation holds the next lead until a matching acknowledgment or pause");
    }
}
