using BidEuchre.Arena;

internal static class ArenaStatisticsTests
{
    internal static readonly EngineSpec Candidate = new("candidate", "/synthetic/candidate", [], ".", "c", "model-c");
    internal static readonly EngineSpec Incumbent = new("incumbent", "/synthetic/incumbent", [], ".", "i", "model-i");
    internal static ArenaManifest SyntheticManifest(int primary = 1000, int replacement = 500, GateType gate = GateType.Promotion) =>
        ArenaManifest.Create("schema", "source", "rules", Candidate, Incumbent, [Candidate, Incumbent], new(),
            [new("fixed", Incumbent.Id, Incumbent.Id, Incumbent.Id)], primary, replacement, 100, 42, gate);

    // Explicit synthetic statistics fixtures; these are never used by the live match runner.
    internal static MatchResult SyntheticResult(MatchSpec spec, int winner) =>
        new(spec, MatchStatus.Complete, winner, winner == 0 ? [40, 2] : [2, 40], 12, null, "synthetic-statistics-fixture");
    internal static MatchResult[] SyntheticAllWins(ArenaManifest manifest) => manifest.Legs.Select(spec =>
        SyntheticResult(spec, manifest.Blocks.Single(b => b.Id == spec.BlockId).Kind == BlockKind.Primary ? spec.LegId : 0)).ToArray();
    internal static MatchResult[] SyntheticAllHalfWins(ArenaManifest manifest) => manifest.Legs.Select(spec => SyntheticResult(spec, 0)).ToArray();

    public static void StratifiedBootstrapIsDeterministic()
    {
        var manifest = ArenaManifest.Create("schema", "source", "rules", Candidate, Incumbent, [Candidate, Incumbent], new(),
            [new("positive", Incumbent.Id, Incumbent.Id, Incumbent.Id), new("negative", Incumbent.Id, Incumbent.Id, Incumbent.Id)],
            4, 9, 100, 77);
        var results = manifest.Legs.Select(spec =>
        {
            var block = manifest.Blocks.Single(b => b.Id == spec.BlockId);
            return SyntheticResult(spec, block.Kind == BlockKind.Primary ? (spec.Seed % 3 == 0 ? spec.LegId : 0)
                : block.ConditionId == "positive" ? spec.LegId : 1 - spec.LegId);
        }).ToArray();
        var a = TournamentStatistics.Summarize(manifest, results);
        var b = TournamentStatistics.Summarize(manifest, results.Reverse().ToArray());
        Assert.Equal(0d, a.ReplacementDifference!.Value);
        Assert.Equal(0d, a.ReplacementLower95!.Value);
        Assert.Equal(0d, a.ReplacementUpper95!.Value);
        Assert.Equal(1d, a.Conditions[0].Difference!.Value);
        Assert.Equal(-1d, a.Conditions[1].Difference!.Value);
        Assert.Equal(a.PrimaryLower95!.Value, b.PrimaryLower95!.Value);
        Assert.Equal(a.PrimaryUpper95!.Value, b.PrimaryUpper95!.Value);
        Assert.True(a.PrimaryLower95 < a.PrimaryUpper95);
    }

    public static void IncompleteEvidenceCannotPromote()
    {
        var manifest = SyntheticManifest();
        var wins = SyntheticAllWins(manifest);
        var allWins = TournamentStatistics.Summarize(manifest, wins);
        Assert.Equal(1d, allWins.PrimaryLower95!.Value);
        Assert.True(allWins.CanPromote);
        Assert.False((allWins with { ReplacementLower95 = -.02 }).CanPromote);
        Assert.False((allWins with { GateErrors = ["persistent protocol failure"] }).CanPromote);
        var allHalfWins = TournamentStatistics.Summarize(manifest, SyntheticAllHalfWins(manifest));
        Assert.Equal(.5, allHalfWins.PrimaryLower95!.Value);
        Assert.False(allHalfWins.CanPromote);
        Assert.False(TournamentStatistics.Summarize(manifest, wins[..^1]).CanPromote);
        foreach (var status in new[] { MatchStatus.Interrupted, MatchStatus.Failed })
        {
            var changed = wins.ToArray();
            changed[0] = changed[0] with { Status = status, Winner = null, Error = "synthetic interruption/failure" };
            Assert.False(TournamentStatistics.Summarize(manifest, changed).CanPromote);
        }
        Assert.False(TournamentStatistics.Summarize(manifest with { Gate = GateType.Screening }, wins).CanPromote);
        Assert.False(TournamentStatistics.Summarize(manifest, wins.Append(wins[0]).ToArray()).CanPromote);
    }
}
