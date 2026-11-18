using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class SessionPauseTests
{
    public static void Run()
    {
        foreach (var campaign in new[] { false, true })
        {
            Task.Run(() => PauseDiscardsInFlightDecision(campaign)).GetAwaiter().GetResult();
            Task.Run(() => DisposePausedDeal(campaign)).GetAwaiter().GetResult();
            Console.WriteLine($"PASS  {(campaign ? "Campaign" : "Free-play")} pause stays responsive and discards in-flight decisions");
        }
        Task.Run(() => RejectsStaleDecision(redeal: true)).GetAwaiter().GetResult();
        Task.Run(() => RejectsStaleDecision(redeal: false)).GetAwaiter().GetResult();
        Task.Run(TerminalObservationDoesNotHoldTable).GetAwaiter().GetResult();
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static SeatConfiguration[] Bots() => Enumerable.Range(0, 4)
        .Select(seat => new SeatConfiguration(seat, $"Bot {seat}", PlayerKind.Bot, EngineCatalog.BuiltInEngineId)).ToArray();
    private static async Task<string> State(GameSession session) => JsonSerializer.Serialize(await session.GetViewAsync(0));
    private static async Task Reject(Task task)
    {
        try { await task; throw new Exception("A hidden table action must be rejected."); }
        catch (GameRuleException error) { Require(error.Message.Contains("paused"), "The paused lifecycle gate must reject before action-specific validation."); }
    }

    private static async Task PauseDiscardsInFlightDecision(bool campaign)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new GameSession("locked-pause", "Pause", Bots(), new EngineCatalog(), 41,
            botActionDelay: TimeSpan.Zero, completedTrickDelay: TimeSpan.Zero,
            botDriverFactory: (_, _) => new HeldBot(entered, release),
            campaign: campaign ? new CampaignContext(CampaignSnapshot.Create(41)) : null,
            campaignPolicy: (view, seat) =>
            {
                entered.TrySetResult(); release.Task.GetAwaiter().GetResult();
                return CampaignBotPolicy.Choose(view, seat);
            });
        try
        {
            await session.StartAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = await State(session).WaitAsync(TimeSpan.FromSeconds(1));
            await session.PauseAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Require(await State(session) == before, "Reading and pausing while a bot thinks must not apply its action.");
            release.TrySetResult();
            await session.PauseAsync(); // a repeated pause must retain the same pending wake-up
            var game = session.Game;
            var paused = await State(session);
            await Task.Delay(80);
            Require(await State(session) == paused, "A bot may not mutate state after PauseAsync returns.");
            await Reject(session.ExecuteAsync(new(0, "pass", null, null, null, null)));
            await Reject(session.ExecuteCampaignAsync(new(0, 0, 0, new CampaignCommand.Pass())));
            await Reject(session.DeveloperAsync(engine => engine.DeveloperSetScores(99, 88)));
            await Reject(session.ArrangeCampaignHandAsync(0, []));
            await Reject(session.StartNextHandAsync());
            await Reject(session.StartAsync());
            Require(ReferenceEquals(game, session.Game) && await State(session) == paused,
                "Rejected hidden actions must retain the engine, cards, scores and revision.");
            await session.ResumeAsync(); await session.ResumeAsync();
            await session.WaitForBotsAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(await State(session) != paused && session.BotLoopFault is null, "Resuming must wake exactly the live bot loop without a stale-deal deadlock.");
        }
        finally { release.TrySetResult(); await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    private static async Task RejectsStaleDecision(bool redeal)
    {
        var first = new TaskCompletionSource<GameView>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<GameView>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var driver = new TestBot(async (view, seat, token) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                first.TrySetResult(view);
                await reply.Task.WaitAsync(token);
                if (redeal) throw new ProtocolException("A stale response must not display an error.");
                return FreePlayBotPolicy.Choose(view, seat);
            }
            second.TrySetResult(view);
            await Task.Delay(Timeout.Infinite, token);
            return FreePlayBotPolicy.Choose(view, seat);
        });
        await using var session = new GameSession("stale", "Stale", Bots(), new EngineCatalog(), 41,
            botActionDelay: TimeSpan.Zero, botDriverFactory: (_, _) => driver);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await session.StartAsync(deadline.Token);
        var initial = await first.Task.WaitAsync(deadline.Token);
        if (redeal) await session.DeveloperAsync(game => game.Redeal(), deadline.Token);
        else
        {
            await session.PauseAsync(deadline.Token);
            await session.ResumeAsync(deadline.Token);
        }
        var changed = (await session.GetViewAsync(null, deadline.Token))!;
        Require(initial.CurrentSeat == changed.CurrentSeat && (initial.DealSerial != changed.DealSerial) == redeal,
            "The fixture must keep the same actor while changing the deal or pause generation.");
        reply.TrySetResult();
        var current = await second.Task.WaitAsync(deadline.Token);
        Require(current.DealSerial == changed.DealSerial && current.Auction.Count == 0 && session.LastError is null,
            "A stale response must neither apply a move nor display an error.");
        Console.WriteLine(redeal
            ? "PASS  A redeal rejects a stale bot failure even when the actor is unchanged"
            : "PASS  A valid late reply is discarded after pause and resume even when the position looks identical");
    }

    private static async Task TerminalObservationDoesNotHoldTable()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new TestBot((view, seat, _) => Task.FromResult(FreePlayBotPolicy.Choose(view, seat)),
            async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); });
        await using var session = new GameSession("observe", "Observe", Bots(), new EngineCatalog(), 41,
            botActionDelay: TimeSpan.Zero, completedTrickDelay: TimeSpan.Zero, botDriverFactory: (_, _) => driver);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await session.StartAsync(deadline.Token);
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            var view = await session.GetViewAsync(null, deadline.Token);
            Require(view!.Phase is GamePhase.HandComplete or GamePhase.GameComplete,
                "The final state must be readable while bots observe the completed hand.");
            await session.PauseAsync(deadline.Token);
        }
        finally { release.TrySetResult(); }
        await session.WaitForBotsAsync(deadline.Token);
        Console.WriteLine("PASS  Slow end-of-hand observations leave reads and pause responsive");
    }

    private sealed class TestBot(Func<GameView, int, CancellationToken, Task<BotAction>> decide,
        Func<CancellationToken, Task>? observe = null) : IBotDriver
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NewGameAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken) => decide(view, seat, cancellationToken);
        public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken) => observe?.Invoke(cancellationToken) ?? Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task DisposePausedDeal(bool campaign)
    {
        var session = new GameSession("paused-deal", "Pause", Bots(), new EngineCatalog(), 41,
            botActionDelay: TimeSpan.FromMilliseconds(10),
            campaign: campaign ? new CampaignContext(CampaignSnapshot.Create(41)) : null);
        await session.StartAsync(waitForDeal: true);
        await session.PauseAsync();
        Require(!session.WaitingForDeal, "Pausing must acknowledge an abandoned visual deal.");
        await Task.Delay(50); // allow the bot loop to enter its resume wait
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await session.DisposeAsync();
    }

    private sealed class HeldBot(TaskCompletionSource entered, TaskCompletionSource release) : IBotDriver
    {
        private readonly BuiltInBotDriver _real = new();
        public Task StartAsync(CancellationToken cancellationToken) => _real.StartAsync(cancellationToken);
        public Task NewGameAsync(CancellationToken cancellationToken) => _real.NewGameAsync(cancellationToken);
        public async Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken)
        {
            entered.TrySetResult(); await release.Task.WaitAsync(cancellationToken);
            return await _real.ChooseActionAsync(view, seat, cancellationToken);
        }
        public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken) => _real.ObserveAsync(view, seat, cancellationToken);
        public ValueTask DisposeAsync() => _real.DisposeAsync();
    }
}
