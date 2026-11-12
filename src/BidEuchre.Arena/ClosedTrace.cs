using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BidEuchre.Arena;

// A seal is produced only after the terminal row and file are durably closed.
// Audit reads evidence; it never launches an engine or reconstructs missing play.
internal static class ClosedTrace
{
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static void Verify(MatchResult result, int actions)
    {
        var seal = result.TraceSeal ?? throw new InvalidDataException("Complete trace has no closed-file seal.");
        if (FileHash(result.TracePath) != seal.Sha256 || actions != seal.Actions || actions == 0
            || Hash(JsonSerializer.Serialize(result.Spec)) != seal.SpecSha256)
            throw new InvalidDataException("Closed trace hash, action count or spec seal differs.");
        JsonElement first = default, last = default, lastAction = default;
        int index = 0, ends = 0;
        foreach (var line in File.ReadLines(result.TracePath))
        {
            using var row = JsonDocument.Parse(line);
            var value = row.RootElement.Clone();
            var kind = value.GetProperty("kind").GetString();
            if (index++ == 0) first = value;
            else if (kind == "match") throw new InvalidDataException("Duplicate trace header.");
            if (kind == "action") lastAction = value;
            if (kind == "end") ends++;
            last = value;
        }
        try
        {
            if (first.GetProperty("kind").GetString() != "match" || first.GetProperty("schema").GetString() != "arena-trace-v1"
                || JsonSerializer.Serialize(first.GetProperty("spec").Deserialize<MatchSpec>()) != JsonSerializer.Serialize(result.Spec))
                throw new InvalidDataException("Trace header differs from result spec.");
            var terminal = last.GetProperty("state");
            if (ends != 1 || last.GetProperty("kind").GetString() != "end" || last.GetProperty("actions").GetInt32() != actions
                || last.GetProperty("winner").GetInt32() != result.Winner || last.GetProperty("hands").GetInt32() != result.Hands
                || !last.GetProperty("scores").Deserialize<int[]>()!.SequenceEqual(result.Scores)
                || terminal.GetProperty("phase").GetString() != "GameComplete"
                || terminal.GetProperty("gameWinner").GetInt32() != result.Winner
                || !terminal.GetProperty("scores").Deserialize<int[]>()!.SequenceEqual(result.Scores)
                || terminal.GetProperty("handResults").GetArrayLength() != result.Hands
                || Hash(terminal.GetRawText()) != seal.TerminalSha256
                || lastAction.GetProperty("state").GetRawText() != terminal.GetRawText())
                throw new InvalidDataException("Trace end differs from terminal result.");
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Invalid complete trace header/end.", error); }
    }
}
