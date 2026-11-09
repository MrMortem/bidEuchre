using System.Text.Json;
using System.Security.Cryptography;
using BidEuchre.Arena;
using BidEuchre.Core;

internal static class ArenaResumeTests
{
    private static ArenaManifest LiveManifest(string mode = "good", BudgetSpec? budget = null)
    {
        var executable = Environment.ProcessPath!;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)));
        var candidate = new EngineSpec("candidate", executable, ["--arena-test-child", mode], AppContext.BaseDirectory, hash, null);
        var incumbent = candidate with { Id = "incumbent" };
        return ArenaManifest.Create("schema", "source", "rules", candidate, incumbent, [candidate, incumbent], budget ?? new(),
            [new("fixed", incumbent.Id, incumbent.Id, incumbent.Id)], 1, 1, 921, 3);
    }
    public static async Task BoundedTournamentResumesAndRetainsFailures()
    {
        var path = Path.Combine(Path.GetTempPath(), "arena-live-" + Guid.NewGuid().ToString("N"));
        var manifest = LiveManifest();
        try
        {
            var initial = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromMilliseconds(25), default);
            Assert.False(initial.Complete);
            Assert.False(initial.CanPromote);
            using (var store = RunStore.Open(path, manifest))
                Assert.True(store.ReadAttempts().Any(a => a.Result.Status == MatchStatus.Interrupted));
            var resumed = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromSeconds(15), default);
            Assert.True(resumed.Complete);
            Assert.Equal(0, resumed.GateErrors.Length);
            Assert.True(resumed.Decisions > 0);
            Assert.True(File.ReadAllText(Path.Combine(path, "report.md")).Contains("Condition"));
            var again = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromSeconds(1), default);
            Assert.Equal(resumed.Attempts, again.Attempts);
            Assert.Equal(4, Directory.GetFiles(Path.Combine(path, "results"), "*.json").Length);
        }
        finally { Directory.Delete(path, true); }
        path += "-failed";
        manifest = LiveManifest("exit");
        try
        {
            var failed = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromSeconds(5), default);
            Assert.True(failed.GateErrors.Length > 0);
            var again = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromSeconds(1), default);
            Assert.Equal(failed.Attempts, again.Attempts);
            Assert.Equal(failed.GateErrors.Length, again.GateErrors.Length);
            Assert.False(again.CanPromote);
        }
        finally { Directory.Delete(path, true); }
    }
    public static async Task StopDeadlineViolationTaintsGate()
    {
        var path = Path.Combine(Path.GetTempPath(), "arena-latency-" + Guid.NewGuid().ToString("N"));
        var manifest = LiveManifest("late", new(100, 150, 2000, 3000));
        try
        {
            var report = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromMilliseconds(1400), default);
            Assert.True(report.GateErrors.Any(e => e.Contains("StopMs")));
            var again = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromMilliseconds(1), default);
            Assert.True(again.GateErrors.Any(e => e.Contains("StopMs")));
            Assert.False(again.CanPromote);
        }
        finally { Directory.Delete(path, true); }
        path += "-committed";
        manifest = LiveManifest();
        try
        {
            using (var store = RunStore.Open(path, manifest))
            {
                var trace = Path.Combine(path, "traces", "synthetic-latency-fixture.jsonl");
                File.WriteAllText(trace, "{\"kind\":\"action\",\"elapsedDecisionMs\":3501}\n");
                store.Commit(ArenaStatisticsTests.SyntheticResult(manifest.Legs[0], 0) with { TracePath = trace });
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var report = await TournamentRunner.RunAsync(manifest, path, TimeSpan.FromSeconds(1), cancelled.Token);
            Assert.True(report.GateErrors.Any(e => e.Contains("StopMs")));
        }
        finally { Directory.Delete(path, true); }
    }

    public static void PairedLegsShareDeals()
    {
        var manifest = ArenaStatisticsTests.SyntheticManifest(8, 8, GateType.Screening);
        Assert.Equal(4, manifest.Blocks.Take(4).Select(b => manifest.Legs.First(l => l.BlockId == b.Id).Dealer).Distinct().Count());
        foreach (var block in manifest.Blocks)
        {
            var legs = manifest.Legs.Where(l => l.BlockId == block.Id).ToArray();
            Assert.Equal(legs[0].Seed, legs[1].Seed);
            Assert.Equal(legs[0].Dealer, legs[1].Dealer);
            var games = legs.Select(l => { var g = new GameEngine(["a", "b", "c", "d"], l.Seed); g.StartGame(l.Dealer); return g; }).ToArray();
            for (int hand = 0; hand < 3; hand++)
            {
                for (int seat = 0; seat < 4; seat++)
                    Assert.Equal(string.Join(',', games[0].CreateView(seat).Players[seat].Cards!), string.Join(',', games[1].CreateView(seat).Players[seat].Cards!));
                foreach (var game in games) game.Redeal();
            }
            if (block.Kind == BlockKind.Primary)
            {
                Assert.Equal(legs[0].Seats[0].Id, legs[1].Seats[1].Id);
                Assert.Equal(legs[0].Seats[2].Id, legs[1].Seats[3].Id);
            }
            else
                for (int seat = 1; seat < 4; seat++) Assert.Equal(legs[0].Seats[seat].Id, legs[1].Seats[seat].Id);
        }
    }
    public static void ResumeDoesNotDuplicateLegs()
    {
        var path = Path.Combine(Path.GetTempPath(), "arena-store-" + Guid.NewGuid().ToString("N"));
        var manifest = ArenaStatisticsTests.SyntheticManifest(1, 0, GateType.Screening);
        var result = ArenaStatisticsTests.SyntheticAllWins(manifest)[0];
        try
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                using var store = RunStore.Open(path, manifest);
                store.Commit(result);
                Assert.True(store.TryReadCompleted(result.Spec.BlockId, result.Spec.LegId, out var read));
                Assert.Equal(result.Winner!.Value, read.Winner!.Value);
                Assert.Throws<InvalidDataException>(() => store.Commit(result with { Hands = 99 }));
            }
            Assert.Equal(1, Directory.GetFiles(Path.Combine(path, "results"), "*.json").Length);
        }
        finally { Directory.Delete(path, true); }
    }
    public static void ChangedManifestIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "arena-store-" + Guid.NewGuid().ToString("N"));
        var manifest = ArenaStatisticsTests.SyntheticManifest(1, 0, GateType.Screening);
        try
        {
            using (RunStore.Open(path, manifest)) { }
            Assert.Throws<InvalidDataException>(() => RunStore.Open(path, manifest with { SourceFingerprint = "changed" }));
            Assert.Throws<InvalidDataException>(() => RunStore.Open(path, manifest with { RuleFingerprint = "changed" }));
            Assert.Throws<InvalidDataException>(() => RunStore.Open(path, manifest with { SchemaFingerprint = "changed" }));
            Assert.Throws<InvalidDataException>(() => RunStore.Open(path, manifest with { Budget = new(2000, 3500, 5000) }));
        }
        finally { Directory.Delete(path, true); }
    }
}
