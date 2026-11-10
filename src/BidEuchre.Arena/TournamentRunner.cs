using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace BidEuchre.Arena;

public static class TournamentRunner
{
    // A no-play re-audit may recover durable attempt bookkeeping and refresh reports.
    public static Task<TournamentReport> AuditAsync(ArenaManifest manifest, string directory) =>
        RunAsync(manifest, directory, TimeSpan.FromSeconds(1), new CancellationToken(canceled: true));

    public static async Task<TournamentReport> RunAsync(ArenaManifest manifest, string directory, TimeSpan maxDuration,
        CancellationToken cancellationToken)
    {
        if (maxDuration <= TimeSpan.Zero || maxDuration > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(maxDuration), "A run must be bounded to at most 30 minutes.");
        manifest = ArenaManifest.Copy(manifest);
        using var store = RunStore.Open(directory, manifest);
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maxDuration);
        var attempts = store.ReadAttempts().ToList();
        // A process crash may leave only a durable start and a partial trace. Audit it before allowing a retry.
        foreach (var unfinished in store.ReadUnfinishedAttempts())
        {
            var recovered = new MatchResult(unfinished.Spec, MatchStatus.Interrupted, null, [0, 0], 0,
                "Recovered unfinished attempt after arena interruption.", unfinished.TracePath);
            try
            {
                if (File.Exists(recovered.TracePath)) ReadTimings(recovered, requireDecisions: false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            { recovered = recovered with { Status = MatchStatus.Failed, Error = exception.Message }; }
            // Elapsed compute time is unknown after a crash; do not count downtime as engine work.
            store.RecordAttempt(recovered);
            attempts.Add(new(recovered, 0, DateTimeOffset.UtcNow));
        }
        // A crash after recording a complete attempt but before publishing its result must not replay that leg.
        foreach (var attempt in attempts.Where(a => TournamentStatistics.IsComplete(a.Result)))
            store.Commit(attempt.Result);
        foreach (var spec in manifest.Legs)
        {
            if (store.TryReadCompleted(spec.BlockId, spec.LegId, out _)) continue;
            if (attempts.Any(a => a.Result.Spec.BlockId == spec.BlockId && a.Result.Spec.LegId == spec.LegId && a.Result.Status == MatchStatus.Failed)) continue;
            if (deadline.IsCancellationRequested) break;
            string trace = Path.GetFullPath(Path.Combine(directory, "traces", Guid.NewGuid().ToString("N") + ".jsonl"));
            store.StartAttempt(spec, trace);
            var started = Stopwatch.GetTimestamp();
            MatchResult result;
            try
            {
                foreach (var engine in spec.Seats.DistinctBy(e => e.Id)) VerifyExecutable(engine);
                result = await MatchRunner.RunAsync(spec, e => new ArenaProcessDriver(e, spec.Budget), trace, deadline.Token);
                if (File.Exists(result.TracePath)) ReadTimings(result, result.Status == MatchStatus.Complete);
                if (result.Status == MatchStatus.Complete)
                {
                    if (!TournamentStatistics.IsComplete(result)) throw new InvalidDataException("Match has no authoritative first-to-40 winner.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                result = new(spec, MatchStatus.Failed, null, [0, 0], 0, exception.Message, trace);
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            store.RecordAttempt(result, elapsed);
            attempts.Add(new(result, elapsed, DateTimeOffset.UtcNow));
            if (TournamentStatistics.IsComplete(result)) store.Commit(result);
            if (result.Status == MatchStatus.Interrupted) break;
        }
        var completed = manifest.Legs.Select(spec => store.TryReadCompleted(spec.BlockId, spec.LegId, out var result) ? result : null)
            .Where(result => result is not null).Cast<MatchResult>().ToArray();
        var report = TournamentStatistics.Summarize(manifest, completed);
        var errors = report.GateErrors.Concat(attempts.Where(a => a.Result.Status == MatchStatus.Failed)
            .Select(a => $"Failed attempt {a.Result.Spec.BlockId}/{a.Result.Spec.LegId}: {a.Result.Error}")).ToList();
        int decisions = 0;
        double maximum = 0;
        foreach (var result in attempts.Select(a => a.Result).Concat(completed)
            .DistinctBy(r => (r.Spec.BlockId, r.Spec.LegId, r.TracePath)))
        {
            if (result.Status != MatchStatus.Complete && !File.Exists(result.TracePath)) continue;
            try
            {
                var timing = ReadTimings(result, result.Status == MatchStatus.Complete);
                decisions += timing.Decisions;
                maximum = Math.Max(maximum, timing.MaximumMs);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                if (result.Status != MatchStatus.Failed)
                    errors.Add($"Invalid timing evidence {result.Spec.BlockId}/{result.Spec.LegId}: {exception.Message}");
            }
        }
        report = report with { GateErrors = errors.ToArray(), ElapsedMs = timer.Elapsed.TotalMilliseconds,
            Attempts = attempts.Count, AttemptElapsedMs = attempts.Sum(a => a.ElapsedMs), Decisions = decisions, MaximumDecisionMs = maximum };
        RunStore.Publish(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), replace: true);
        RunStore.Publish(Path.Combine(directory, "report.md"), Markdown(report), replace: true);
        return report;
    }

    private static void VerifyExecutable(EngineSpec engine)
    {
        if (!Path.IsPathFullyQualified(engine.Executable)) throw new InvalidDataException("Engine executable must be an absolute frozen path.");
        using var file = File.OpenRead(engine.Executable);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        if (!actual.Equals(engine.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Executable hash differs for {engine.Id}.");
    }
    private static (int Decisions, double MaximumMs) ReadTimings(MatchResult result, bool requireDecisions = true)
    {
        int count = 0;
        double maximum = 0;
        var backends = new HashSet<string>();
        foreach (var line in File.ReadLines(result.TracePath))
        {
            using var row = JsonDocument.Parse(line);
            if (!row.RootElement.TryGetProperty("kind", out var kind)) throw new InvalidDataException("Trace row has no kind.");
            if (kind.GetString() != "action") continue;
            if (!row.RootElement.TryGetProperty("elapsedDecisionMs", out var timing) || !timing.TryGetDouble(out var elapsed)
                || !double.IsFinite(elapsed) || elapsed < 0 || elapsed > result.Spec.Budget.StopMs)
                throw new InvalidDataException($"Decision exceeded frozen StopMs ({result.Spec.Budget.StopMs}) or has missing timing.");
            if (!row.RootElement.TryGetProperty("seat", out var seatElement) || !seatElement.TryGetInt32(out var seat) || seat is < 0 or > 3)
            {
                if (result.Spec.Seats.Any(e => e.ExpectedBackend is not null)) throw new InvalidDataException("Missing diagnostic seat.");
            }
            else
            {
                string? backend = row.RootElement.TryGetProperty("backend", out var backendElement) ? backendElement.GetString() : null;
                string? hash = row.RootElement.TryGetProperty("modelSha256", out var hashElement) ? hashElement.GetString() : null;
                if (backend is not null) backends.Add(backend);
                try { ArenaProcessDriver.ValidateDiagnostics(result.Spec.Seats[seat], backend, hash); }
                catch (BidEuchre.Protocol.ProtocolException exception) { throw new InvalidDataException(exception.Message); }
            }
            count++;
            maximum = Math.Max(maximum, elapsed);
        }
        if (requireDecisions && result.Spec.Seats.Any(e => e.ExpectedBackend is not null)
            && !backends.Order().SequenceEqual(result.EffectiveBackends))
            throw new InvalidDataException("Effective backend summary differs from trace diagnostics.");
        if (requireDecisions && count == 0) throw new InvalidDataException("Complete trace contains no decisions.");
        return (count, maximum);
    }
    private static string Markdown(TournamentReport r)
    {
        static string N(double? value) => value?.ToString("F4", CultureInfo.InvariantCulture) ?? "unavailable";
        var lines = new List<string>
        {
            "# Arena tournament report", "", $"Status: {r.Status}; gate: {r.Gate}; can promote: {r.CanPromote}.", "",
            $"Manifest: `{r.ManifestFingerprint}`", "",
            $"Complete legs: {r.CompletedLegs}/{r.RegisteredLegs}.",
            $"Primary paired blocks: {r.CompletedPrimaryBlocks}/{r.RegisteredPrimaryBlocks}; win rate {N(r.PrimaryWinRate)}; 95% CI [{N(r.PrimaryLower95)}, {N(r.PrimaryUpper95)}].",
            $"Replacement paired blocks: {r.CompletedReplacementBlocks}/{r.RegisteredReplacementBlocks}; equal-condition difference {N(r.ReplacementDifference)}; 95% CI [{N(r.ReplacementLower95)}, {N(r.ReplacementUpper95)}].", "",
            $"Bootstrap: {TournamentStatistics.BootstrapResamples} deterministic paired-block percentile resamples; replacement strata use equal condition weights.", "",
            "| Condition | Completed / registered blocks | Difference | 95% CI |", "| --- | ---: | ---: | --- |"
        };
        lines.AddRange(r.Conditions.Select(c => $"| {c.Id} | {c.CompletedBlocks}/{c.RegisteredBlocks} | {N(c.Difference)} | [{N(c.Lower95)}, {N(c.Upper95)}] |"));
        lines.AddRange(["", $"Run elapsed ms: {N(r.ElapsedMs)}; cumulative attempts: {r.Attempts}; attempt elapsed ms: {N(r.AttemptElapsedMs)}; decisions: {r.Decisions}; maximum decision ms: {N(r.MaximumDecisionMs)}.", "", $"Gate errors: {r.GateErrors.Length}."]);
        lines.AddRange(r.GateErrors.Select(e => "- " + e));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
