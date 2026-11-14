using BidEuchre.Core;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace BidEuchre.App;

public static class CampaignStore
{
    // A missing, unreadable or impossible save is simply no save.
    public static CampaignRun? Load(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            CampaignRun? run;
            if (root.TryGetProperty("Version", out var version))
            {
                if (!version.TryGetInt32(out var number) || number != 2) return null;
                var save = JsonNode.Parse(text)!.AsObject();
                RemoveRetiredFeatures(save);
                run = save.Deserialize<CampaignSave>(CampaignJson.Options)?.Run;
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

    // Retired IDs are removed only from their saved feature fields; all other
    // feature IDs still pass through the strict converters and validation.
    private static void RemoveRetiredFeatures(JsonObject save)
    {
        if (save["Run"] is not JsonObject run) return;
        if (run["Resources"] is JsonObject resources)
        {
            if (resources["Deck"] is JsonArray deck)
                foreach (var node in deck)
                    if (node is JsonObject card && card["Enhancement"] is JsonValue enhancement
                        && enhancement.TryGetValue<string>(out var id) && id is "enhancement:emerald" or "enhancement:stinky")
                        card["Enhancement"] = null;

            if (resources["Inventory"] is JsonObject inventory)
                RemoveRetiredTarot(inventory["Tarot"]);
            if (resources["GeneratedTarot"] is JsonArray generated)
                foreach (var seat in generated) RemoveRetiredTarot(seat);
        }

        if (run["Shop"] is not JsonObject shop || shop["Offers"] is not JsonArray offers) return;
        for (var index = offers.Count - 1; index >= 0; index--)
        {
            if (offers[index] is not JsonObject offer
                || offer["Category"] is not JsonValue categoryValue || !categoryValue.TryGetValue<int>(out var category)
                || offer["FeatureId"] is not JsonValue featureValue || !featureValue.TryGetValue<string>(out var feature)) continue;
            if ((category == (int)FeatureCategory.Tarot && feature is "Merchant" or "Beggar")
                || (category == (int)FeatureCategory.Enhancement && feature is "Emerald" or "Stinky"))
                offers.RemoveAt(index);
        }
    }

    private static void RemoveRetiredTarot(JsonNode? node)
    {
        if (node is not JsonArray cards) return;
        for (var index = cards.Count - 1; index >= 0; index--)
            if (cards[index] is JsonValue value && value.TryGetValue<string>(out var id) && id is "tarot:merchant" or "tarot:beggar")
                cards.RemoveAt(index);
    }

    public static bool Save(CampaignRun run, string path)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CampaignSave(2, run with { Resources = run.Resources with { Wallets = new[] { run.Money, run.Resources.Wallets[1], run.Resources.Wallets[2], run.Resources.Wallets[3] } } }), CampaignJson.Options));
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
