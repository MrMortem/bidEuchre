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
            Task.Run(() => PauseWaitsForLockedAction(campaign)).GetAwaiter().GetResult();
            Task.Run(() => DisposePausedDeal(campaign)).GetAwaiter().GetResult();
            Console.WriteLine($"PASS  {(campaign ? "Campaign" : "Free-play")} pause serializes bot actions, rejects hidden mutation, resumes and disposes");
        }
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

    // The driver controls one real bot decision's duration; all moves and observations still use
    // the production driver and engine. A pause without the same semaphore would return too early.
    private static async Task PauseWaitsForLockedAction(bool campaign)
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
            using var cancelled = new CancellationTokenSource();
            var abandonedPause = session.PauseAsync(cancelled.Token); cancelled.Cancel();
            try { await abandonedPause; throw new Exception("Cancellation must abandon a pending pause."); }
            catch (OperationCanceledException) { }
            var pause = session.PauseAsync();
            await Task.Delay(30);
            Require(!pause.IsCompleted, "Pause must wait for an in-flight locked bot action.");
            release.TrySetResult();
            await pause.WaitAsync(TimeSpan.FromSeconds(3));
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
