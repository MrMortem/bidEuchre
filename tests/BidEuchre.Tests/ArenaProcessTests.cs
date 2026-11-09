using System.Diagnostics;
using BidEuchre.Arena;
using BidEuchre.Core;

internal static class ArenaProcessTests
{
    private static EngineSpec Child(string mode) => new(mode, Environment.ProcessPath!,
        ["--arena-test-child", mode], AppContext.BaseDirectory, "", null);
    private static readonly BudgetSpec Budget = new(100, 150, 300, 3000);

    public static async Task FailClosed()
    {
        foreach (var mode in new[] { "info", "stderr", "exit", "illegal", "late", "longline", "startup" })
        {
            var path = ArenaTests.TracePath();
            var watch = Stopwatch.StartNew();
            try
            {
                var spec = ArenaTests.Spec(Budget) with { Seats = Enumerable.Repeat(Child(mode), 4).ToArray() };
                var result = await MatchRunner.RunAsync(spec, e => new ArenaProcessDriver(e, Budget), path, default);
                Assert.Equal(MatchStatus.Failed, result.Status, mode + ": " + result.Error);
                Assert.True(result.Winner is null);
                Assert.Equal(0, result.Hands);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(7), mode + " was unbounded");
            }
            finally { File.Delete(path); }
        }
    }

    public static async Task StderrIsDrainedAndBounded()
    {
        await using var driver = new ArenaProcessDriver(Child("stderr-success"), new());
        await driver.StartAsync();
        var game = new GameEngine(["a", "b", "c", "d"], 1);
        game.StartGame(0);
        var action = await driver.ChooseActionAsync(game.CreateView(1), 1);
        Assert.True(action is BidEuchre.Protocol.BotAction.Pass);
        Assert.Equal(ArenaProcessDriver.MaximumStderrTailBytes, driver.StderrTail.Length);
        Assert.True(driver.StderrTail.All(c => c == 'x'));
    }

    public static async Task LivePartnersBestHistory()
    {
        var path = ArenaTests.TracePath();
        try
        {
            var spec = ArenaTests.Spec() with { Seats = Enumerable.Repeat(Child("history"), 4).ToArray() };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await MatchRunner.RunAsync(spec, e => new ArenaProcessDriver(e, spec.Budget), path, deadline.Token);
            Assert.Equal(MatchStatus.Complete, result.Status, result.Error);
            Assert.True(result.Winner is 0 or 1);
            var trace = File.ReadAllText(path);
            Assert.True(trace.Contains("ExchangingPartnerCard"));
        }
        finally { File.Delete(path); }
    }

    public static async Task CannotReuseTimedOutStream()
    {
        await using var driver = new ArenaProcessDriver(Child("late"), Budget);
        await driver.StartAsync();
        var game = new GameEngine(["a", "b", "c", "d"], 1);
        game.StartGame(0);
        await Assert.ThrowsAsync<Exception>(async () => await driver.ChooseActionAsync(game.CreateView(1), 1));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<Exception>(async () => await driver.ChooseActionAsync(game.CreateView(1), 1));
        await Assert.ThrowsAsync<Exception>(() => driver.StartAsync());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }
}
