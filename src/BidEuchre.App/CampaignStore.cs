using System.Text.Json;

namespace BidEuchre.App;

public static class CampaignStore
{
    // A missing, unreadable or impossible save is simply no save.
    public static CampaignRun? Load(string path)
    {
        try
        {
            var run = JsonSerializer.Deserialize<CampaignRun>(File.ReadAllText(path));
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(run));
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
