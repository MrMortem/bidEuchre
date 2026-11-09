namespace BidEuchre.Arena;

public sealed record ConditionReport(string Id, int RegisteredBlocks, int CompletedBlocks,
    double? Difference, double? Lower95, double? Upper95);
public sealed record TournamentReport(string ManifestFingerprint, GateType Gate, int RegisteredLegs, int CompletedLegs,
    int RegisteredPrimaryBlocks, int CompletedPrimaryBlocks, int RegisteredReplacementBlocks, int CompletedReplacementBlocks,
    double? PrimaryWinRate, double? PrimaryLower95, double? PrimaryUpper95,
    double? ReplacementDifference, double? ReplacementLower95, double? ReplacementUpper95,
    ConditionReport[] Conditions, string[] GateErrors, double ElapsedMs = 0, int Attempts = 0,
    double AttemptElapsedMs = 0, int Decisions = 0, double MaximumDecisionMs = 0)
{
    public bool Complete => CompletedLegs == RegisteredLegs && CompletedPrimaryBlocks == RegisteredPrimaryBlocks
        && CompletedReplacementBlocks == RegisteredReplacementBlocks;
    public bool CanPromote => Gate == GateType.Promotion && Complete && GateErrors.Length == 0
        && RegisteredPrimaryBlocks == 1000 && RegisteredReplacementBlocks >= 500 && Conditions.Length > 0
        && Conditions.All(c => c.CompletedBlocks > 0)
        && Conditions.Max(c => c.RegisteredBlocks) - Conditions.Min(c => c.RegisteredBlocks) <= 1
        && PrimaryLower95 > .50 && ReplacementLower95 > -.02;
    public string Status => CanPromote ? "Passing" : !Complete ? "Incomplete" : GateErrors.Length > 0 ? "Invalid" : "NonPromoting";
}

public static class TournamentStatistics
{
    public const int BootstrapResamples = 10000;
    public static bool IsComplete(MatchResult result) => result.Status == MatchStatus.Complete && result.Error is null
        && result.Winner is 0 or 1 && result.Scores.Length == 2 && result.Hands > 0
        && result.Scores[result.Winner.Value] >= 40 && result.Scores[result.Winner.Value] > result.Scores[1 - result.Winner.Value];

    public static TournamentReport Summarize(ArenaManifest manifest, IReadOnlyList<MatchResult> results)
    {
        manifest.Validate();
        var errors = new List<string>();
        var accepted = new Dictionary<(string, int), MatchResult>();
        var specs = manifest.Legs.ToDictionary(l => (l.BlockId, l.LegId));
        foreach (var result in results)
        {
            var key = (result.Spec.BlockId, result.Spec.LegId);
            if (!specs.TryGetValue(key, out var spec) || ArenaManifest.Json(spec) != ArenaManifest.Json(result.Spec))
            { errors.Add($"Unregistered or changed leg: {key}."); continue; }
            if (!accepted.TryAdd(key, result)) errors.Add($"Duplicate leg: {key}.");
            if (result.Status == MatchStatus.Failed || (result.Status == MatchStatus.Complete && !IsComplete(result)))
                errors.Add($"Invalid/failed leg: {key}: {result.Error}");
        }
        var primary = new List<double>();
        var replacement = manifest.Conditions.ToDictionary(c => c.Id, _ => new List<double>());
        foreach (var block in manifest.Blocks)
        {
            if (!accepted.TryGetValue((block.Id, 0), out var a) || !accepted.TryGetValue((block.Id, 1), out var b)
                || !IsComplete(a) || !IsComplete(b)) continue;
            if (block.Kind == BlockKind.Primary) primary.Add(((a.Winner == 0 ? 1d : 0d) + (b.Winner == 1 ? 1d : 0d)) / 2);
            else replacement[block.ConditionId!].Add((a.Winner == 0 ? 1d : 0d) - (b.Winner == 0 ? 1d : 0d));
        }
        var rng = new Random(manifest.BootstrapSeed);
        var p = Bootstrap([primary.ToArray()], rng);
        var conditionReports = manifest.Conditions.Select(condition =>
        {
            var units = replacement[condition.Id].ToArray();
            var interval = Bootstrap([units], rng);
            return new ConditionReport(condition.Id, manifest.Blocks.Count(b => b.Kind == BlockKind.Replacement && b.ConditionId == condition.Id),
                units.Length, units.Length > 0 ? units.Average() : null, interval.Lower, interval.Upper);
        }).ToArray();
        var groups = manifest.Conditions.Select(c => replacement[c.Id].ToArray()).ToArray();
        var r = Bootstrap(groups, rng);
        var primaryCount = manifest.Blocks.Count(b => b.Kind == BlockKind.Primary);
        return new(manifest.Fingerprint, manifest.Gate, manifest.Legs.Length, accepted.Values.Count(IsComplete),
            primaryCount, primary.Count, manifest.Blocks.Length - primaryCount, replacement.Values.Sum(v => v.Count),
            primary.Count > 0 ? primary.Average() : null, p.Lower, p.Upper,
            groups.Length > 0 && groups.All(g => g.Length > 0) ? groups.Average(g => g.Average()) : null,
            r.Lower, r.Upper, conditionReports, errors.ToArray());
    }

    // Each draw resamples whole paired blocks within every condition, then applies equal predeclared condition weights.
    private static (double? Lower, double? Upper) Bootstrap(double[][] groups, Random rng)
    {
        if (groups.Length == 0 || groups.Any(g => g.Length == 0)) return (null, null);
        var samples = new double[BootstrapResamples];
        for (var resample = 0; resample < samples.Length; resample++)
        {
            double pooled = 0;
            foreach (var group in groups)
            {
                double sum = 0;
                for (var draw = 0; draw < group.Length; draw++) sum += group[rng.Next(group.Length)];
                pooled += sum / group.Length;
            }
            samples[resample] = pooled / groups.Length;
        }
        Array.Sort(samples);
        double Percentile(double quantile)
        {
            double index = quantile * (samples.Length - 1);
            int lower = (int)index;
            return samples[lower] + (index - lower) * (samples[(int)Math.Ceiling(index)] - samples[lower]);
        }
        return (Percentile(.025), Percentile(.975));
    }
}
