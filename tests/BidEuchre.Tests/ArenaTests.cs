using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Arena;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class ArenaTests
{
    internal static readonly EngineSpec BuiltIn = new("builtin", "", [], ".", "", null);
    internal static MatchSpec Spec(BudgetSpec? budget = null) => new("arena-tests", 0, 921, 0,
        Enumerable.Repeat(BuiltIn, 4).ToArray(), budget ?? new());
    internal static string TracePath() => Path.Combine(Path.GetTempPath(), $"arena-{Guid.NewGuid():N}.jsonl");

    public static async Task MatchRunsToForty()
    {
        var path = TracePath();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await MatchRunner.RunAsync(Spec(), _ => new BuiltInBotDriver(), path, deadline.Token);
            Assert.Equal(MatchStatus.Complete, result.Status);
            Assert.True(result.Winner is 0 or 1);
            Assert.True(result.Scores[result.Winner!.Value] >= 40);
            Assert.True(result.Scores[result.Winner.Value] > result.Scores[1-result.Winner.Value]);
            Assert.True(result.Hands > 1);
            var actionRows = File.ReadLines(path).Select(line => JsonDocument.Parse(line)).ToList();
            try
            {
                var decisions = actionRows.Where(row => row.RootElement.GetProperty("kind").GetString() == "action").ToList();
                Assert.True(decisions.Count > 0);
                Assert.True(decisions.All(row => row.RootElement.GetProperty("elapsedDecisionMs").GetDouble() >= 0));
            }
            finally { foreach (var row in actionRows) row.Dispose(); }
            var again = await MatchRunner.RunAsync(Spec(), _ => new BuiltInBotDriver(), path + ".again", deadline.Token);
            Assert.Equal(result.Hands, again.Hands);
            Assert.Equal(string.Join(',', result.Scores), string.Join(',', again.Scores));
        }
        finally { File.Delete(path); File.Delete(path + ".again"); }
    }

    public static async Task OnlyActorReceivesPrivateHand()
    {
        var drivers = new List<Recorder>();
        var path = TracePath();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await MatchRunner.RunAsync(Spec(), _ => { var d = new Recorder(); drivers.Add(d); return d; }, path, deadline.Token);
            Assert.Equal(MatchStatus.Complete, result.Status);
            foreach (var driver in drivers)
            {
                Assert.Equal(result.Hands, driver.Terminals);
                Assert.True(driver.Requests > 0);
                Assert.True(driver.Disposed);
                Assert.Equal(1, driver.Resets);
            }
            Assert.True(drivers.Any(d => d.Exchanges > 0));
        }
        finally { File.Delete(path); }
    }

    public static async Task CancellationIsInterrupted()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = TracePath();
        try
        {
            var result = await MatchRunner.RunAsync(Spec(), _ => new BuiltInBotDriver(), path, cancellation.Token);
            Assert.Equal(MatchStatus.Interrupted, result.Status);
            Assert.True(result.Winner is null);
        }
        finally { File.Delete(path); }
    }

    public static async Task FixturesReplay()
    {
        var path = TracePath();
        try
        {
            await FixtureExporter.ExportAsync(path, default);
            var fixtures = File.ReadLines(path).Select(line => JsonDocument.Parse(line)).ToList();
            Assert.True(fixtures.Count >= 58, "Include 32 bounded fixed-seed randomized authority traces.");
            var randomized = fixtures.Where(f => f.RootElement.GetProperty("id").GetString()!.StartsWith("random-")).ToArray();
            Assert.Equal(32, randomized.Length);
            Assert.Equal(32, randomized.Select(f => f.RootElement.GetProperty("seed").GetInt32()).Distinct().Count());
            Assert.True(randomized.All(f => f.RootElement.GetProperty("steps").GetArrayLength() <= 31));
            var secondPath = path + ".second";
            try
            {
                await FixtureExporter.ExportAsync(secondPath, default);
                var repeated = File.ReadLines(secondPath).Select(line => JsonDocument.Parse(line)).ToArray();
                var repeatedRandom = repeated.Where(f => f.RootElement.GetProperty("id").GetString()!.StartsWith("random-")).ToArray();
                Assert.True(randomized.Select(f => f.RootElement.GetRawText()).SequenceEqual(repeatedRandom.Select(f => f.RootElement.GetRawText())), "Fixed seeds must reproduce complete randomized authority snapshots.");
                foreach (var fixture in repeated) fixture.Dispose();
            }
            finally { File.Delete(secondPath); }
            foreach (var fixture in fixtures)
            {
                FixtureExporter.Verify(fixture.RootElement);
                Assert.Equal("fixture-v1", fixture.RootElement.GetProperty("schema").GetString()!);
                Assert.True(fixture.RootElement.GetProperty("steps").GetArrayLength() > 0);
            }
            Assert.True(fixtures.Any(f => f.RootElement.GetProperty("id").GetString() == "partners-best-same-card"));
            Assert.True(fixtures.Any(f => f.RootElement.GetProperty("id").GetString() == "tied-threshold"));
            var high = fixtures.Single(f => f.RootElement.GetProperty("id").GetString() == "high").RootElement;
            var low = fixtures.Single(f => f.RootElement.GetProperty("id").GetString() == "low").RootElement;
            static int FirstWinner(JsonElement fixture) => fixture.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetProperty("expected").GetProperty("state").GetProperty("completedTricks"))
                .First(tricks => tricks.GetArrayLength() > 0)[0].GetProperty("winner").GetInt32();
            Assert.False(FirstWinner(high) == FirstWinner(low), "High/Low fixtures must exercise contested rank ordering.");
            foreach (var name in new[] { "both-over-forty-zero", "both-over-forty-one", "partners-best-success", "alone-sitting-lead" })
                Assert.True(fixtures.Any(f => f.RootElement.GetProperty("id").GetString() == name), $"Missing authority boundary {name}.");
            foreach (var fixture in fixtures) fixture.Dispose();
        }
        finally { File.Delete(path); }
    }

    public static void ActionMappingRejectsInvalidBoundaries()
    {
        for (var id = 0; id <= 60; id++) Assert.Equal(id, FixtureExporter.ActionId(FixtureExporter.ActionFromId(id)));
        Assert.Throws<ProtocolException>(() => FixtureExporter.ActionFromId(61));
        var game = new GameEngine(["a", "b", "c", "d"], 2);
        game.StartGame(0);
        Assert.Throws<GameRuleException>(() => ActionApplier.Apply(game, 1,
            new BotAction.ChooseContract((ContractMode)99, null)));
        foreach (var seat in new[] { 1, 2, 3 }) game.PlaceBid(seat, null);
        game.PlaceBid(0, BidLevel.Four);
        Assert.Throws<GameRuleException>(() => ActionApplier.Apply(game, 0,
            new BotAction.ChooseContract((ContractMode)99, null)));
    }

    private sealed class Recorder : IBotDriver
    {
        private readonly BuiltInBotDriver inner = new();
        public int Terminals, Requests, Exchanges, Resets;
        public bool Disposed;
        private bool firstAuction = true;
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task NewGameAsync(CancellationToken token = default) { Resets++; return Task.CompletedTask; }
        private static void Check(GameView view, int seat)
        {
            Assert.Equal(1, view.Players.Count(p => p.Cards is not null));
            Assert.True(view.Players[seat].Cards is not null);
            Assert.True(view.Players.Where(p => p.Seat != seat).All(p => p.Cards is null));
        }
        public Task ObserveAsync(GameView view, int seat, CancellationToken token = default)
        {
            Check(view, seat);
            Assert.True(view.Phase is GamePhase.HandComplete or GamePhase.GameComplete);
            Terminals++;
            return Task.CompletedTask;
        }
        public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken token = default)
        {
            Check(view, seat);
            Assert.Equal(seat, view.CurrentSeat!.Value);
            Assert.False(view.Players[seat].IsSittingOut);
            Requests++;
            if (view.Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard) Exchanges++;
            if (view.Phase == GamePhase.Bidding && firstAuction)
            {
                firstAuction = false;
                return Task.FromResult<BotAction>(view.LegalActions.Bids.Contains(BidLevel.PartnersBest)
                    ? new BotAction.Bid(BidLevel.PartnersBest) : new BotAction.Pass());
            }
            return inner.ChooseActionAsync(view, seat, token);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
