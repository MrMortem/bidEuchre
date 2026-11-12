using BidEuchre.Core;

using System.Text.Json;

namespace BidEuchre.App;

public static class CampaignStore
{
    // A missing, unreadable or impossible save is simply no save.
    public static CampaignRun? Load(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            CampaignRun? run;
            if (root.TryGetProperty("Version", out var version))
            {
                if (!version.TryGetInt32(out var number) || number != 2) return null;
                run = JsonSerializer.Deserialize<CampaignSave>(text)?.Run;
            }
            else
            {
                run = JsonSerializer.Deserialize<CampaignRun>(text);
                if (run is not null) run = run with { Resources = CampaignSnapshot.Create(1) with { Wallets = new[] { run.Money, 0, 0, 0 } } };
            }
            if (run?.Resources is null || !run.Resources.IsValid()) return null;
            return run is { Lives: > 0, Match: >= 1, Money: >= 0, MatchesWon: >= 0 } && Enum.IsDefined(run.Phase)
                ? run
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static bool Save(CampaignRun run, string path)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CampaignSave(2, run)));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
