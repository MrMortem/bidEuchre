using System.Text.Json;

namespace BidEuchre.Arena;

public sealed record RunAttempt(MatchResult Result, double ElapsedMs, DateTimeOffset RecordedAt);

// Exclusive writer, atomic publication, and content-checked idempotency. Committed results are never replaced.
public sealed class RunStore : IDisposable
{
    private readonly string directory;
    private readonly ArenaManifest manifest;
    private readonly FileStream writerLock;
    private RunStore(string directory, ArenaManifest manifest, FileStream writerLock)
    { this.directory = directory; this.manifest = ArenaManifest.Copy(manifest); this.writerLock = writerLock; }

    public static RunStore Open(string path, ArenaManifest manifest)
    {
        Directory.CreateDirectory(path);
        var writerLock = new FileStream(Path.Combine(path, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var manifestPath = Path.Combine(path, "manifest.json");
            if (File.Exists(manifestPath) && File.ReadAllText(manifestPath) != ArenaManifest.Json(manifest))
                throw new InvalidDataException("Frozen manifest differs; use a new run directory.");
            manifest.Validate();
            if (!File.Exists(manifestPath)) Publish(manifestPath, ArenaManifest.Json(manifest));
            Directory.CreateDirectory(Path.Combine(path, "results"));
            Directory.CreateDirectory(Path.Combine(path, "attempts"));
            Directory.CreateDirectory(Path.Combine(path, "traces"));
            return new(path, manifest, writerLock);
        }
        catch { writerLock.Dispose(); throw; }
    }

    private string Key(string blockId, int legId)
    {
        int index = Array.FindIndex(manifest.Legs, l => l.BlockId == blockId && l.LegId == legId);
        if (index < 0) throw new InvalidDataException("Result is not in the frozen schedule.");
        return index.ToString("D8");
    }
    private void CheckSpec(MatchResult result)
    {
        Key(result.Spec.BlockId, result.Spec.LegId);
        if (ArenaManifest.Json(manifest.Legs.Single(l => l.BlockId == result.Spec.BlockId && l.LegId == result.Spec.LegId)) != ArenaManifest.Json(result.Spec))
            throw new InvalidDataException("Result spec differs from frozen leg.");
    }
    public bool TryReadCompleted(string blockId, int legId, out MatchResult result)
    {
        var file = Path.Combine(directory, "results", Key(blockId, legId) + ".json");
        result = null!;
        if (!File.Exists(file)) return false;
        result = JsonSerializer.Deserialize<MatchResult>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty committed result.");
        CheckSpec(result);
        if (!TournamentStatistics.IsComplete(result)) throw new InvalidDataException("Invalid committed result.");
        return true;
    }
    public void Commit(MatchResult result)
    {
        CheckSpec(result);
        if (!TournamentStatistics.IsComplete(result)) throw new InvalidDataException("Only authoritative complete matches can be committed.");
        string file = Path.Combine(directory, "results", Key(result.Spec.BlockId, result.Spec.LegId) + ".json");
        string json = ArenaManifest.Json(result);
        if (File.Exists(file))
        {
            if (File.ReadAllText(file) != json) throw new InvalidDataException("Immutable result conflict.");
            return;
        }
        Publish(file, json);
    }
    public void RecordAttempt(MatchResult result, double elapsedMs = 0)
    {
        CheckSpec(result);
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMs));
        Publish(Path.Combine(directory, "attempts", $"{Key(result.Spec.BlockId, result.Spec.LegId)}-{Guid.NewGuid():N}.json"),
            ArenaManifest.Json(new RunAttempt(result, elapsedMs, DateTimeOffset.UtcNow)));
    }
    public IReadOnlyList<RunAttempt> ReadAttempts() => Directory.GetFiles(Path.Combine(directory, "attempts"), "*.json")
        .Select(file => JsonSerializer.Deserialize<RunAttempt>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty attempt."))
        .Select(attempt => { CheckSpec(attempt.Result); return attempt; }).OrderBy(a => a.RecordedAt).ToArray();
    internal static void Publish(string path, string text, bool replace = false)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: replace);
        }
        finally { File.Delete(temporary); }
    }
    public void Dispose() => writerLock.Dispose();
}
