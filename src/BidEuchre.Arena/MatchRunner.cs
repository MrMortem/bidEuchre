using System.Diagnostics;
using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Arena;

public static class MatchRunner
{
    public static async Task<MatchResult> RunAsync(MatchSpec spec, Func<EngineSpec, IBotDriver> factory,
        string tracePath, CancellationToken cancellationToken)
    {
        var drivers = new List<IBotDriver>();
        GameEngine? game = null;
        StreamWriter? trace = null;
        MatchStatus status = MatchStatus.Failed;
        string? error = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spec.Seats.Length != 4) throw new ArgumentException("Exactly four engine seats are required.");
            GameRules.ValidateSeat(spec.Dealer);
            trace = new StreamWriter(tracePath);
            await Write(new { schema = "arena-trace-v1", kind = "match", spec });
            foreach (var engine in spec.Seats)
            {
                var driver = factory(engine);
                if (drivers.Any(existing => ReferenceEquals(existing, driver)))
                    throw new ArgumentException("Every seat requires an independent driver.");
                drivers.Add(driver);
                await driver.StartAsync(cancellationToken);
                await driver.NewGameAsync(cancellationToken);
            }
            game = new GameEngine(["Seat 0", "Seat 1", "Seat 2", "Seat 3"], spec.Seed);
            game.StartGame(spec.Dealer);
            await Write(new { kind = "deal", state = FixtureExporter.Project(game, null, true) });
            while (game.Phase != GamePhase.GameComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (game.Phase == GamePhase.HandComplete)
                {
                    game.StartNextHand();
                    await Write(new { kind = "deal", state = FixtureExporter.Project(game, null, true) });
                }
                var seat = game.CurrentSeat ?? throw new InvalidOperationException("Active hand has no actor.");
                var view = game.CreateView(seat);
                await Write(new { kind = "delivery", delivery = "decision", seat, view = FixtureExporter.Project(game, seat) });
                var decisionStarted = Stopwatch.GetTimestamp();
                var action = await drivers[seat].ChooseActionAsync(view, seat, cancellationToken);
                var elapsedDecisionMs = Stopwatch.GetElapsedTime(decisionStarted).TotalMilliseconds;
                ActionApplier.Apply(game, seat, action);
                await Write(new { kind = "action", seat, action = FixtureExporter.ActionId(action), elapsedDecisionMs, state = FixtureExporter.Project(game, null, true) });
                if (game.Phase is GamePhase.HandComplete or GamePhase.GameComplete)
                {
                    for (var observer = 0; observer < 4; observer++)
                    {
                        await Write(new { kind = "delivery", delivery = "terminal", seat = observer, view = FixtureExporter.Project(game, observer) });
                        await drivers[observer].ObserveAsync(game.CreateView(observer), observer, cancellationToken);
                    }
                }
            }
            status = MatchStatus.Complete;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = MatchStatus.Interrupted;
            error = "Match interrupted.";
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            foreach (var driver in drivers)
            {
                try { await driver.DisposeAsync(); }
                catch (Exception exception) { status = MatchStatus.Failed; error ??= exception.Message; }
            }
            if (trace is not null)
            {
                try { await trace.DisposeAsync(); }
                catch (Exception exception) { status = MatchStatus.Failed; error ??= exception.Message; }
            }
        }
        return new MatchResult(spec, status, status == MatchStatus.Complete ? game?.GameWinner : null,
            game?.Scores.ToArray() ?? [0, 0], game?.HandHistory.Count ?? 0, error, tracePath);

        async Task Write(object value) => await trace!.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellationToken);
    }
}
