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
                if (root.TryGetProperty("Run", out var shopRun) && !CompleteShopTargetsJson(shopRun)) return Corrupt();
                if (HasCurrentShopState(shopRun) && (number != 3
                    || !Members(shopRun, "Resources", "Money", "Phase", "Match", "LiveMatch", "Arrangement", "LegacyMatchRestart")
                    || !CompleteResourcesJson(shopRun.GetProperty("Resources")))) return Corrupt();
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
                if (!CompleteShopTargetsJson(root)) return Corrupt();
                // Bare legacy runs rebuild their resources. A committed purchase must never
                // be accepted here, where that migration would erase its already-paid state.
                if (HasCurrentShopState(root)) return Corrupt();
                var legacyNode = JsonNode.Parse(text)!.AsObject();
                RemoveRetiredPanic(legacyNode, numericIds: true);
                var run = legacyNode.Deserialize<CampaignRun>();
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
    private static bool HasCurrentShopState(JsonElement run) => run.ValueKind == JsonValueKind.Object
        && run.TryGetProperty("Shop", out var shop) && shop.ValueKind == JsonValueKind.Object
        && (Present("UseSession", shop) || Present("Arcade", shop) || Present("Booster", shop) || Present("BonusBooster", shop));

    private static bool Present(string member, JsonElement owner) =>
        owner.TryGetProperty(member, out var value) && value.ValueKind != JsonValueKind.Null;

    private static bool CompleteShopTargetsJson(JsonElement run)
    {
        if (run.ValueKind != JsonValueKind.Object) return false;
        if (!run.TryGetProperty("Shop", out var shop) || shop.ValueKind == JsonValueKind.Null) return true;
        if (shop.ValueKind != JsonValueKind.Object || !shop.TryGetProperty("Offers", out var offers) || offers.ValueKind != JsonValueKind.Array) return false;
        if (run.TryGetProperty("Resources", out var resources) && resources.ValueKind == JsonValueKind.Object
            && resources.TryGetProperty("Inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Object
            && inventory.TryGetProperty("Sodas", out var sodas) && sodas.ValueKind == JsonValueKind.Array && sodas.GetArrayLength() > 0
            && !Members(shop, "SodaPurchased", "GumballPulls", "ArcadeRoll")) return false;
        foreach (var property in new[] { "Booster", "BonusBooster" })
            if (shop.TryGetProperty(property, out var booster) && booster.ValueKind != JsonValueKind.Null
                && !Members(booster, "Theme", "Choices", "Sold")) return false;
        if (shop.TryGetProperty("Arcade", out var arcade) && arcade.ValueKind != JsonValueKind.Null && !CompleteArcadeJson(arcade)) return false;
        if (shop.TryGetProperty("UseSession", out var session) && session.ValueKind != JsonValueKind.Null
            && (!Members(session, "OfferId", "Category", "FeatureId", "PurchasedItemUsed")
                || session.GetProperty("OfferId").ValueKind != JsonValueKind.String
                || session.GetProperty("FeatureId").ValueKind != JsonValueKind.String
                || session.GetProperty("Category").ValueKind != JsonValueKind.Number || !session.GetProperty("Category").TryGetInt32(out _)
                || session.GetProperty("PurchasedItemUsed").ValueKind is not (JsonValueKind.True or JsonValueKind.False))) return false;
        foreach (var offer in offers.EnumerateArray())
        {
            if (offer.ValueKind != JsonValueKind.Object) return false;
            if (offer.TryGetProperty("TargetCards", out var targets) && targets.ValueKind != JsonValueKind.Null
                && !EveryJson(targets, CompleteIdJson)) return false;
        }
        return true;
    }
    private static bool CompleteArcadeJson(JsonElement arcade)
    {
        if (!Members(arcade, "PlinkoPlayed", "RoulettePlayed", "Pending")
            || arcade.GetProperty("PlinkoPlayed").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || arcade.GetProperty("RoulettePlayed").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        var pending = arcade.GetProperty("Pending");
        if (pending.ValueKind == JsonValueKind.Null) return true;
        if (!Members(pending, "PlayId", "Game", "Choice", "Landing", "Score", "Path", "PrizeCategory", "FeatureId", "TargetCards")
            || pending.GetProperty("PlayId").ValueKind != JsonValueKind.String
            || pending.GetProperty("FeatureId").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || !EveryJson(pending.GetProperty("Path"), value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _))
            || !EveryJson(pending.GetProperty("TargetCards"), CompleteIdJson)
            || pending.TryGetProperty("PackCards", out var pack) && pack.ValueKind != JsonValueKind.Null
                && !EveryJson(pack, card => card.ValueKind == JsonValueKind.String)) return false;
        foreach (var member in new[] { "Game", "Choice", "Landing", "Score", "PrizeCategory" })
        {
            var value = pending.GetProperty(member);
            if (member == "PrizeCategory" && value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _)) return false;
        }
        return true;
    }
    private static bool CompleteLiveJson(JsonElement live)
    {
        if (!CompleteOptionalPokerJson(live)) return false;
        if (!live.TryGetProperty("Options", out var options) || !Members(options, "TargetScore", "StartingScoreTeam0", "StartingScoreTeam1", "NegativeThreshold", "HandLimit", "OpponentSuccessfulBidBonus")) return false;
        if (!live.TryGetProperty("Resources", out var resources) || !CompleteResourcesJson(resources) || !CompleteLiveCardValuesJson(live)) return false;
        if (!live.TryGetProperty("HandHistory", out var history) || history.ValueKind != JsonValueKind.Array) return false;
        foreach (var hand in history.EnumerateArray())
        {
            if (!Members(hand, "MadeContract", "ScoreBreakdowns") || !CompleteOptionalPokerJson(hand)) return false;
            var parts = hand.GetProperty("ScoreBreakdowns"); if (parts.ValueKind != JsonValueKind.Array) return false;
            foreach (var part in parts.EnumerateArray()) if (!Members(part, "GumballBonuses")) return false;
        }
        return true;
    }

    // Older checkpoints predate PokerResult. When present, its physical winning
    // identities must be explicit; CardId's default zero is a real deck card.
    private static bool CompleteOptionalPokerJson(JsonElement owner) =>
        !owner.TryGetProperty("PokerResult", out var result) || result.ValueKind == JsonValueKind.Null
        || Members(result, "Winners", "TeamPoints")
            && EveryJson(result.GetProperty("Winners"), winner => Members(winner, "Seat", "Category", "CardIds")
                && winner.GetProperty("Seat").ValueKind == JsonValueKind.Number
                && winner.GetProperty("Category").ValueKind == JsonValueKind.String
                && EveryJson(winner.GetProperty("CardIds"), CompleteIdJson))
            && EveryJson(result.GetProperty("TeamPoints"), point => point.ValueKind == JsonValueKind.Number && point.TryGetInt32(out _));

    private static bool CompleteResourcesJson(JsonElement resources)
    {
        if (!Members(resources, "Revision", "Deck", "Inventory", "Wallets", "RandomState", "PreviousContract", "GeneratedTarot")) return false;
        var inventory = resources.GetProperty("Inventory");
        if (!Members(inventory, "Jokers", "Tarot", "Unlocks", "Sigils", "GumballExtraCopies")) return false;
        var deck = resources.GetProperty("Deck"); if (deck.ValueKind != JsonValueKind.Array) return false;
        foreach (var card in deck.EnumerateArray())
            if (!Members(card, "Enhancement", "Sigil") || !CompleteCardJson(card)) return false;
        var jokers = inventory.GetProperty("Jokers"); if (jokers.ValueKind != JsonValueKind.Array) return false;
        foreach (var joker in jokers.EnumerateArray()) if (!Members(joker, "Id", "Value")) return false;
        return true;
    }

    // Card and CardId are record structs: constructor-required JSON settings do not
    // prevent their missing members from becoming zero. Keep this schema-1 inventory
    // explicit and local to live v3 files so resource-only legacy defaults stay intact.
    private static bool CompleteIdJson(JsonElement id) => Members(id, "Value");
    private static bool CompleteFaceJson(JsonElement face) => Members(face, "Suit", "Rank");
    private static bool CompleteCardJson(JsonElement card) => Members(card, "Id", "Face")
        && CompleteIdJson(card.GetProperty("Id")) && CompleteFaceJson(card.GetProperty("Face"));
    private static bool CompleteIdMemberJson(JsonElement owner, string member) => Members(owner, member) && CompleteIdJson(owner.GetProperty(member));
    private static bool CompleteNullableIdJson(JsonElement owner, string member) => Members(owner, member)
        && (owner.GetProperty(member).ValueKind == JsonValueKind.Null || CompleteIdJson(owner.GetProperty(member)));
    private static bool EveryJson(JsonElement array, Func<JsonElement, bool> valid) => array.ValueKind == JsonValueKind.Array && array.EnumerateArray().All(valid);
    private static bool EveryMemberJson(JsonElement owner, string member, Func<JsonElement, bool> valid) => Members(owner, member) && EveryJson(owner.GetProperty(member), valid);
    private static bool CompleteLiveCardValuesJson(JsonElement live) =>
        EveryMemberJson(live, "Hands", hand => EveryJson(hand, CompleteIdJson))
        && EveryMemberJson(live, "CurrentTrick", play => CompleteIdMemberJson(play, "Id"))
        && EveryMemberJson(live, "CompletedTricks", trick => EveryMemberJson(trick, "Plays", play => CompleteIdMemberJson(play, "Id")))
        && EveryMemberJson(live, "Overlays", CompleteCardJson)
        && EveryMemberJson(live, "WildSuits", wild => CompleteIdMemberJson(wild, "Id"))
        && EveryMemberJson(live, "Reveals", reveal => CompleteIdMemberJson(reveal, "Id"))
        && EveryMemberJson(live, "TrickHistory", trick => EveryMemberJson(trick, "Plays", play => Members(play, "Card") && CompleteCardJson(play.GetProperty("Card"))))
        && CompleteNullableIdJson(live, "PendingExchangeCard")
        && CompleteNullableIdJson(live, "LastMatchCard");

    private static CampaignLoadResult Corrupt() => new(CampaignLoadStatus.Corrupt, Error: "The campaign save is unreadable or invalid. It has not been changed.");

    private static bool ValidSave(CampaignSave save)
    {
        var run = save.Run;
        if (run is null || run.Resources is null || !run.Resources.IsValid() || !ValidShop(run.Shop, run.Resources)
            || run.Match < 1 || run.Money < 0 || run.MatchesWon < 0 || run.LastAppliedRevision < 0 || !Enum.IsDefined(run.Phase)
            || run.IsOver && run.Phase != CampaignPhase.Shop) return false;
        if ((run.Shop?.UseSession is not null || run.Shop?.Arcade?.Pending is not null || run.Shop?.PendingBooster is not null) && (save.Version != 3 || run.Phase != CampaignPhase.Shop || run.IsOver
            || run.Shop.MatchJustFinished != run.Match - 1 || run.LiveMatch is not null || run.Arrangement is not null
            || run.LegacyMatchRestart || save.Metadata?.LastResult is not null || run.Money != run.Resources.Wallets[0])) return false;
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
    private static bool ValidShop(ShopState? shop, CampaignSnapshot resources)
    {
        if (shop is null) return true;
        var deck = resources.Deck;
        if (shop.MatchJustFinished < 0 || shop.Offers is null || shop.RerollCount < 0 || shop.RerollCount > int.MaxValue - 2) return false;
        if (shop.SodaPurchased && resources.Inventory.Sodas.Length == 0 || shop.GumballPulls < 0
            || shop.GumballPulls > 1 && !resources.Inventory.Has(SodaId.RainbowStripes)) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var machines = 0;
        foreach (var offer in shop.Offers)
        {
            if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id)
                || offer.Price < 0 || offer.FeatureId is null) return false;
            if (offer.IsTellerSigil && (!offer.Sold || offer.Category != FeatureCategory.Sigil || offer.IsArcadePrize
                || !resources.Inventory.Has(SodaId.SpectralSprite))) return false;
            if (offer.Category == FeatureCategory.Gumball && (++machines > 1 || (long)offer.Price + shop.GumballPulls > int.MaxValue
                || shop.GumballPulls > 0 && (offer.Sold == resources.Inventory.Has(SodaId.RainbowStripes)))) return false;
            if (offer.TargetCards is { } targets && (offer.Category is not (FeatureCategory.Sigil or FeatureCategory.Tarot)
                || targets.Length != CampaignShop.ShopTargetCount || targets.Distinct().Count() != targets.Length
                || targets.Any(id => deck.All(c => c.Id != id))
                || offer.Category == FeatureCategory.Tarot && !offer.Sold && offer.Printed is null)) return false;
            if (offer.Printed is { } printed && (offer.Category != FeatureCategory.Tarot || offer.Sold || printed.Length is not (1 or CampaignShop.TicketSpread)
                || printed.Distinct().Count() != printed.Length || printed.Any(id => CampaignShop.PrintedCard(id) is not { } card || card.Category == FeatureCategory.Sigil && !resources.Inventory.Has(SodaId.SpectralSprite)))) return false;
            // A migrated retired pull stays sold without naming an active reward.
            if (offer.Category == FeatureCategory.Gumball && (offer.FeatureId == "" || !offer.Sold && offer.FeatureId == "Blind")) continue;
            // An unprinted Fortune Teller has no ticket yet; older saves named one, which is now ignored.
            if (offer.Category == FeatureCategory.Tarot && !offer.Sold && offer.FeatureId == "") continue;
            if (offer.Category is not (FeatureCategory.Joker or FeatureCategory.Tarot or FeatureCategory.Sigil or FeatureCategory.Gumball)
                || !FeatureCatalog.All.Any(feature => feature.Category == offer.Category && feature.Key == offer.FeatureId)) return false;
        }
        if (shop.GumballPulls > 0 && machines != 1) return false;
        if (!CampaignArcade.IsValid(shop, deck, resources.Inventory) || !CampaignShop.IsValidBooster(shop, resources.Inventory)) return false;
        if (shop.UseSession is not { } session) return true;
        if (string.IsNullOrWhiteSpace(session.OfferId) || string.IsNullOrWhiteSpace(session.FeatureId)
            || session.Category is not (FeatureCategory.Sigil or FeatureCategory.Tarot)) return false;
        var committed = shop.Offers.SingleOrDefault(o => o.Id == session.OfferId);
        return committed is { Sold: true, Printed: null, TargetCards: not null }
            && committed.Category == session.Category && committed.FeatureId == session.FeatureId;
    }

    // Retired IDs are removed only from their saved feature fields; all other
    // feature IDs still pass through the strict converters and validation.
    private static void RemoveRetiredFeatures(JsonObject save)
    {
        if (save["Run"] is not JsonObject run) return;
        RemoveRetiredPanic(run);
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

    private static bool IsRetiredPanic(string id) => id is "gumball:panic-button" or "planet:panic-button";

    private static bool IsRetiredPanicValue(JsonNode? node, bool numericIds) => node is JsonValue value
        && (value.TryGetValue<string>(out var id) && IsRetiredPanic(id)
            || numericIds && value.TryGetValue<int>(out var number) && number == 7);
    private static bool IsRetiredPanicKey(string key, bool numericIds) => IsRetiredPanic(key)
        || numericIds && key is "7" or "PanicButton";

    private static (int? UnlockIndex, bool HasCopies, int Copies) RetiredPanicState(JsonNode? resources, bool numericIds)
    {
        if (resources is not JsonObject resource || resource["Inventory"] is not JsonObject inventory) return default;
        int? unlockIndex = null;
        if (inventory["Unlocks"] is JsonArray unlocks)
            for (var index = 0; index < unlocks.Count; index++)
                if (IsRetiredPanicValue(unlocks[index], numericIds))
                {
                    if (unlockIndex is not null) throw new JsonException("Duplicate retired unlock.");
                    unlockIndex = index;
                }
        var extras = inventory["GumballExtraCopies"] as JsonObject;
        var retiredKeys = extras?.Select(pair => pair.Key).Where(key => IsRetiredPanicKey(key, numericIds)).ToArray() ?? [];
        if (retiredKeys.Length > 1) throw new JsonException("Duplicate retired gumball count.");
        var hasCopies = retiredKeys.Length == 1;
        var copies = 0;
        if (hasCopies && (extras![retiredKeys[0]] is not JsonValue value || !value.TryGetValue<int>(out copies)
            || copies < 0 || unlockIndex is null)) throw new JsonException("Invalid retired gumball count.");
        if (unlockIndex is not null || hasCopies)
        {
            // Validate the original total before deleting the retired contribution;
            // otherwise an overfull or malformed inventory could appear valid afterward.
            long total = 0;
            if (extras is not null)
                foreach (var pair in extras)
                {
                    if (pair.Value is not JsonValue count || !count.TryGetValue<int>(out var amount) || amount < 0)
                        throw new JsonException("Invalid gumball copy count.");
                    total += amount;
                    if (total > int.MaxValue) throw new JsonException("Too many gumball copies.");
                }
        }
        return (unlockIndex, hasCopies, copies);
    }

    // Strip the retired unlock from both authoritative resource copies before their
    // equality check. Historical scores, hand numbers and event text remain intact.
    private static void RemoveRetiredPanic(JsonObject run, bool numericIds = false)
    {
        var resources = run["Resources"];
        var liveResources = run["LiveMatch"] is JsonObject live ? live["Resources"] : null;
        var retired = RetiredPanicState(resources, numericIds);
        if (liveResources is not null && retired != RetiredPanicState(liveResources, numericIds))
            throw new JsonException("Run and checkpoint retired gumballs differ.");
        foreach (var savedResources in new[] { resources, liveResources })
        {
            if (savedResources is not JsonObject resource || resource["Inventory"] is not JsonObject inventory) continue;
            if (inventory["Unlocks"] is JsonArray unlocks)
                for (var index = unlocks.Count - 1; index >= 0; index--)
                    if (IsRetiredPanicValue(unlocks[index], numericIds)) unlocks.RemoveAt(index);
            if (inventory["GumballExtraCopies"] is JsonObject copies)
                foreach (var key in copies.Select(pair => pair.Key).Where(key => IsRetiredPanicKey(key, numericIds)).ToArray()) copies.Remove(key);
        }
        if (run["Shop"] is not JsonObject shop || shop["Offers"] is not JsonArray offers) return;
        foreach (var node in offers)
            if (node is JsonObject offer && offer["Category"] is JsonValue category && category.TryGetValue<int>(out var value)
                && value == (int)FeatureCategory.Gumball && offer["FeatureId"] is JsonValue feature
                && feature.TryGetValue<string>(out var id) && id == "PanicButton") offer["FeatureId"] = "";
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
