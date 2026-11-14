using BidEuchre.Core;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace BidEuchre.App;

public static class CampaignStore
{
    public static CampaignRun? Load(string path)
    {
        var result = LoadSave(path);
        // Desktop has historically hidden completed runs; the envelope API retains them for
        // callers that persist an explicit final result screen.
        return result.Save?.Run is { IsOver: false } run ? run : null;
    }

    public static CampaignLoadResult LoadSave(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Corrupt();
            CampaignSave save;
            if (root.TryGetProperty("Version", out var version))
            {
                if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number is not (2 or 3)) return Corrupt();
                if (number == 3 && root.TryGetProperty("Run", out var runJson) && runJson.TryGetProperty("LiveMatch", out var liveJson)
                    && liveJson.ValueKind != JsonValueKind.Null && (!Members(runJson, "Arrangement", "Resources", "Money", "LastAppliedRevision", "LegacyMatchRestart")
                        || !CompleteResourcesJson(runJson.GetProperty("Resources")) || !CompleteLiveJson(liveJson))) return Corrupt();
                var node = JsonNode.Parse(text)!.AsObject();
                RemoveRetiredFeatures(node);
                save = node.Deserialize<CampaignSave>(CampaignJson.Options)!;
                if (save?.Run is null) return Corrupt();
                if (number == 2)
                {
                    // A v2 save never represented a live engine. Do not accept an invalid live
                    // object disguised as an old resource-only save.
                    if (save.Run.LiveMatch is not null) return Corrupt();
                    save = save with { Run = save.Run with { LegacyMatchRestart = save.Run.Phase == CampaignPhase.InMatch } };
                }
            }
            else
            {
                var run = JsonSerializer.Deserialize<CampaignRun>(text);
                if (run is null || run.LiveMatch is not null || run.Lives <= 0) return Corrupt();
                run = run with { Resources = CampaignSnapshot.Create(1) with { Wallets = new[] { run.Money, 0, 0, 0 } }, LegacyMatchRestart = run.Phase == CampaignPhase.InMatch };
                save = new(2, run);
            }
            return ValidSave(save) ? new(CampaignLoadStatus.Loaded, save with { Version = 3 }) : Corrupt();
        }
        catch (FileNotFoundException) { return new(CampaignLoadStatus.Missing); }
        catch (DirectoryNotFoundException) { return new(CampaignLoadStatus.Missing); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        { return Corrupt(); }
    }

    // The live format is strict even where older resource/domain records have optional
    // defaults for v2 compatibility. Every newly serialized continuation field must exist.
    private static bool Members(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object && names.All(n => value.TryGetProperty(n, out _));
    private static bool CompleteLiveJson(JsonElement live)
    {
        if (!live.TryGetProperty("Options", out var options) || !Members(options, "TargetScore", "StartingScoreTeam0", "StartingScoreTeam1", "NegativeThreshold", "HandLimit", "OpponentSuccessfulBidBonus")) return false;
        if (!live.TryGetProperty("Resources", out var resources) || !CompleteResourcesJson(resources)) return false;
        if (!live.TryGetProperty("HandHistory", out var history) || history.ValueKind != JsonValueKind.Array) return false;
        foreach (var hand in history.EnumerateArray())
        {
            if (!Members(hand, "MadeContract", "ScoreBreakdowns")) return false;
            var parts = hand.GetProperty("ScoreBreakdowns"); if (parts.ValueKind != JsonValueKind.Array) return false;
            foreach (var part in parts.EnumerateArray()) if (!Members(part, "GumballBonuses")) return false;
        }
        return true;
    }

    private static bool CompleteResourcesJson(JsonElement resources)
    {
        if (!Members(resources, "Revision", "Deck", "Inventory", "Wallets", "RandomState", "PreviousContract", "GeneratedTarot")) return false;
        var inventory = resources.GetProperty("Inventory");
        if (!Members(inventory, "Jokers", "Tarot", "Unlocks", "Sigils", "GumballExtraCopies")) return false;
        var deck = resources.GetProperty("Deck"); if (deck.ValueKind != JsonValueKind.Array) return false;
        foreach (var card in deck.EnumerateArray()) if (!Members(card, "Id", "Face", "Enhancement", "Sigil")) return false;
        var jokers = inventory.GetProperty("Jokers"); if (jokers.ValueKind != JsonValueKind.Array) return false;
        foreach (var joker in jokers.EnumerateArray()) if (!Members(joker, "Id", "Value")) return false;
        return true;
    }

    private static CampaignLoadResult Corrupt() => new(CampaignLoadStatus.Corrupt, Error: "The campaign save is unreadable or invalid. It has not been changed.");

    private static bool ValidSave(CampaignSave save)
    {
        var run = save.Run;
        if (run is null || run.Resources is null || !run.Resources.IsValid() || !ValidShop(run.Shop)
            || run.Match < 1 || run.Money < 0 || run.MatchesWon < 0 || run.LastAppliedRevision < 0 || !Enum.IsDefined(run.Phase)
            || run.IsOver && run.Phase != CampaignPhase.Shop) return false;
        if (run.LiveMatch is { } live)
        {
            if (run.Phase != CampaignPhase.InMatch || run.LegacyMatchRestart || !new CampaignCheckpoint(live, run.Arrangement).IsValid()
                || run.Money != live.Resources.Wallets[0] || run.LastAppliedRevision != live.Resources.Revision
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(run.Resources, CampaignJson.Options), JsonSerializer.SerializeToNode(live.Resources, CampaignJson.Options))) return false;
        }
        else if (run.Arrangement is not null || run.Phase == CampaignPhase.InMatch && !(save.Version == 2 && run.LegacyMatchRestart)) return false;
        if (run.LegacyMatchRestart && run.Phase != CampaignPhase.InMatch) return false;
        if (save.Metadata is { } metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.RunId) || metadata.StateRevision < 0) return false;
            if (metadata.LastResult is { } result)
            {
                if (result.Outcome is null || result.Scores is null || result.Scores.Length is not (0 or 2) || run.Phase == CampaignPhase.InMatch) return false;
                if (result.Summary is { } summary && (summary.PlayerNames is not { Length: 4 } || summary.PlayerNames.Any(string.IsNullOrWhiteSpace)
                    || summary.HandHistory is null || summary.HandHistory.Any(h => h is null) || summary.PublicEvents is null || summary.PublicEvents.Any(e => e is null))) return false;
            }
        }
        return true;
    }

    // Old blind gumball stock used "Blind"; current stock uses an empty feature until pulled.
    // Other offers must name a feature in the category the shop can actually sell.
    private static bool ValidShop(ShopState? shop)
    {
        if (shop is null) return true;
        if (shop.MatchJustFinished < 0 || shop.Offers is null || shop.RerollCount < 0 || shop.RerollCount > int.MaxValue - 2) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var machines = 0;
        foreach (var offer in shop.Offers)
        {
            if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id)
                || offer.Price < 0 || offer.FeatureId is null) return false;
            if (offer.Category == FeatureCategory.Gumball && ++machines > 1) return false;
            if (offer.Category == FeatureCategory.Gumball && !offer.Sold && offer.FeatureId is "" or "Blind") continue;
            if (offer.Category is not (FeatureCategory.Joker or FeatureCategory.Tarot or FeatureCategory.Sigil or FeatureCategory.Gumball)
                || !FeatureCatalog.All.Any(feature => feature.Category == offer.Category && feature.Key == offer.FeatureId)) return false;
        }
        return true;
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
                    if (node is JsonObject card)
                    {
                        if (card["Enhancement"] is JsonValue enhancement && enhancement.TryGetValue<string>(out var id)
                            && id is "enhancement:emerald" or "enhancement:stinky") card["Enhancement"] = null;
                        if (card["Sigil"] is JsonValue sigil && sigil.TryGetValue<string>(out var sigilId)
                            && IsRetiredSigil(sigilId)) card["Sigil"] = null;
                    }

            if (resources["Inventory"] is JsonObject inventory)
            {
                RemoveRetiredItems(inventory["Tarot"], IsRetiredTarot);
                RemoveRetiredItems(inventory["Sigils"], IsRetiredSigil);
            }
            if (resources["GeneratedTarot"] is JsonArray generated)
                foreach (var seat in generated) RemoveRetiredItems(seat, IsRetiredTarot);
        }

        if (run["Shop"] is not JsonObject shop || shop["Offers"] is not JsonArray offers) return;
        for (var index = offers.Count - 1; index >= 0; index--)
        {
            if (offers[index] is not JsonObject offer
                || offer["Category"] is not JsonValue categoryValue || !categoryValue.TryGetValue<int>(out var category)
                || offer["FeatureId"] is not JsonValue featureValue || !featureValue.TryGetValue<string>(out var feature)) continue;
            if ((category == (int)FeatureCategory.Tarot && feature is "Merchant" or "Beggar" or "LovePotion" or "Nightshade" or "CloverTonic" or "CrystalElixir" or "Glassblower")
                || (category == (int)FeatureCategory.Sigil && feature is "Coin" or "Snake" or "Owl" or "Peacock")
                || (category == (int)FeatureCategory.Enhancement && feature is "Emerald" or "Stinky"))
                offers.RemoveAt(index);
        }
    }

    private static bool IsRetiredTarot(string id) => id is "tarot:merchant" or "tarot:beggar" or "tarot:love-potion"
        or "tarot:nightshade" or "tarot:clover-tonic" or "tarot:crystal-elixir" or "tarot:glassblower";
    private static bool IsRetiredSigil(string id) => id is "sigil:coin" or "sigil:snake" or "sigil:owl" or "sigil:peacock";
    private static void RemoveRetiredItems(JsonNode? node, Func<string, bool> retired)
    {
        if (node is not JsonArray cards) return;
        for (var index = cards.Count - 1; index >= 0; index--)
            if (cards[index] is JsonValue value && value.TryGetValue<string>(out var id) && retired(id))
                cards.RemoveAt(index);
    }

    public static bool Save(CampaignRun run, string path) => Save(new CampaignSave(3, run), path);

    public static bool Save(CampaignSave save, string path)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var run = save.Run;
            // Legacy/between-match callers set Money directly. Live resources instead come
            // from the authoritative checkpoint and must agree without silent repair.
            if (run.LiveMatch is null)
                run = run with { Resources = run.Resources with { Wallets = new[] { run.Money, run.Resources.Wallets[1], run.Resources.Wallets[2], run.Resources.Wallets[3] } } };
            save = save with { Version = 3, Run = run };
            if (!ValidSave(save)) return false;
            var json = JsonSerializer.Serialize(save, CampaignJson.Options);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or IndexOutOfRangeException) { return false; }
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
