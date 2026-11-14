using BidEuchre.App;
using BidEuchre.Core;
using System.Text.Json.Nodes;

internal static class CampaignSaveTests
{
    public static void ResourcesAndMigration()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "campaign.json");
        try
        {
            File.WriteAllText(path, "{\"Lives\":2,\"Match\":7,\"Money\":23,\"Phase\":1}");
            var legacy = CampaignStore.Load(path)!;
            Assert.Equal(23, legacy.Resources.Wallets[0]);
            Assert.Equal(24, legacy.Resources.Deck.Length);
            var deck = legacy.Resources.Deck.ToArray();
            deck[0] = deck[0] with { Enhancement = EnhancementId.Glass, Sigil = SigilId.Frog };
            legacy.Resources.GeneratedTarot[1] = [TarotId.TaxCollector];
            var run = legacy with { Resources = legacy.Resources with { Deck = deck } };
            Assert.True(CampaignStore.Save(run, path));
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("enhancement:glass", json.RootElement.GetProperty("Run").GetProperty("Resources").GetProperty("Deck")[0].GetProperty("Enhancement").GetString()!);
            var loaded = CampaignStore.Load(path)!;
            Assert.Equal(EnhancementId.Glass, loaded.Resources.Deck[0].Enhancement!.Value);
            Assert.Equal(SigilId.Frog, loaded.Resources.Deck[0].Sigil!.Value);
            Assert.Equal(TarotId.TaxCollector, loaded.Resources.GeneratedTarot[1][0]);
            File.WriteAllText(path, "{\"Version\":999,\"Run\":{}}");
            Assert.True(CampaignStore.Load(path) is null);
        }
        finally { Directory.Delete(folder, true); }
    }
    // Gumballs were called Planets, and runs saved then must still load.
    public static void LegacyPlanetIds()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "campaign.json");
        try
        {
            var run = new CampaignRun(Money: 5, Phase: CampaignPhase.Shop);
            run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Unlocks = [GumballId.Chaos] } } };
            Assert.True(CampaignStore.Save(run, path));
            var json = File.ReadAllText(path);
            Assert.True(json.Contains("\"gumball:chaos\""));
            File.WriteAllText(path, json.Replace("\"gumball:chaos\"", "\"planet:chaos\""));
            var loaded = CampaignStore.Load(path)!;
            Assert.Equal(1, loaded.Resources.Inventory.Unlocks.Length);
            Assert.True(loaded.Resources.Inventory.Unlocks[0] == GumballId.Chaos);
            Assert.True(CampaignStore.Save(loaded, path));
            Assert.True(!File.ReadAllText(path).Contains("planet:"));
        }
        finally { Directory.Delete(folder, true); }
    }

    public static void RetiredFeaturesMigrate()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "campaign.json");
        try
        {
            var resources = CampaignSnapshot.Create(789) with
            {
                Revision = 17,
                Wallets = [43, 12, 8, 19],
                PreviousContract = new(ContractMode.High, null),
                Inventory = new([new(JokerId.DeepDigger, 9)], [TarotId.TaxCollector, TarotId.Glassblower], [GumballId.Chaos]),
                GeneratedTarot = [[TarotId.TaxCollector], [TarotId.SpringLoaded], [], [TarotId.Traitor]]
            };
            resources.Deck[0] = resources.Deck[0] with { Sigil = SigilId.Frog };
            resources.Deck[1] = resources.Deck[1] with { Sigil = SigilId.Coin };
            resources.Deck[2] = resources.Deck[2] with { Enhancement = EnhancementId.Glass, Sigil = SigilId.Owl };
            var run = new CampaignRun(Lives: 2, Match: 7, Money: 43, MatchesWon: 5, Phase: CampaignPhase.Shop)
            {
                Resources = resources,
                LastAppliedRevision = 16,
                Shop = new(6,
                [
                    new("kept-tarot", FeatureCategory.Tarot, "TaxCollector", 4, true),
                    // A retired ID in an unrelated string must not be rewritten.
                    new("tarot:merchant", FeatureCategory.Gumball, "Blind", 5)
                ])
            };
            Assert.True(CampaignStore.Save(run, path));
            var expected = JsonNode.Parse(File.ReadAllText(path))!;
            var legacy = expected.DeepClone();
            var savedRun = legacy["Run"]!;
            var savedResources = savedRun["Resources"]!;
            savedResources["Deck"]![0]!["Enhancement"] = "enhancement:emerald";
            savedResources["Deck"]![1]!["Enhancement"] = "enhancement:stinky";
            var tarot = savedResources["Inventory"]!["Tarot"]!.AsArray();
            tarot.Insert(0, "tarot:merchant");
            tarot.Insert(2, "tarot:beggar");
            var generated = savedResources["GeneratedTarot"]!;
            generated[0]!.AsArray().Add("tarot:merchant");
            generated[1]!.AsArray().Insert(0, "tarot:beggar");
            generated[2]!.AsArray().Add("tarot:merchant");
            generated[2]!.AsArray().Add("tarot:beggar");
            generated[3]!.AsArray().Insert(0, "tarot:merchant");
            var offers = savedRun["Shop"]!["Offers"]!.AsArray();
            offers.Insert(0, new JsonObject { ["Id"] = "old-merchant", ["Category"] = 4, ["FeatureId"] = "Merchant", ["Price"] = 4, ["Sold"] = false });
            offers.Add(new JsonObject { ["Id"] = "old-beggar", ["Category"] = 4, ["FeatureId"] = "Beggar", ["Price"] = 4, ["Sold"] = true });
            offers.Add(new JsonObject { ["Id"] = "old-emerald", ["Category"] = 0, ["FeatureId"] = "Emerald", ["Price"] = 4, ["Sold"] = false });
            offers.Add(new JsonObject { ["Id"] = "old-stinky", ["Category"] = 0, ["FeatureId"] = "Stinky", ["Price"] = 4, ["Sold"] = false });
            var original = legacy.ToJsonString();
            File.WriteAllText(path, original);

            var loaded = CampaignStore.Load(path);
            Assert.True(loaded is not null, "A run with retired features must still load.");
            Assert.True(loaded!.Resources.Deck[0].Enhancement is null, "Emerald must be cleared from saved cards.");
            Assert.True(loaded.Resources.Deck[1].Enhancement is null, "Stinky must be cleared from saved cards.");
            Assert.Equal(original, File.ReadAllText(path), "Loading must not rewrite the source save.");
            Assert.True(CampaignStore.Save(loaded, path));
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "Migration must preserve all progress and unrelated resources.");
            var reloaded = CampaignStore.Load(path);
            Assert.True(reloaded is not null);
            Assert.True(CampaignStore.Save(reloaded!, path));
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "A migrated save must round-trip without further changes.");
        }
        finally { Directory.Delete(folder, true); }
    }

    public static void UnknownFeatureIdsRemainInvalid()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "campaign.json");
        try
        {
            Assert.True(CampaignStore.Save(new CampaignRun(Money: 11), path));
            var valid = JsonNode.Parse(File.ReadAllText(path))!;
            Action<JsonNode>[] corruptions =
            [
                resources => resources["Deck"]![0]!["Enhancement"] = "enhancement:unknown",
                resources => resources["Deck"]![0]!["Enhancement"] = 5,
                resources => resources["Inventory"]!["Tarot"]!.AsArray().Add("tarot:unknown"),
                resources => resources["GeneratedTarot"]![2]!.AsArray().Add("tarot:unknown"),
                resources => resources["Deck"]![0]!["Sigil"] = "enhancement:emerald"
            ];
            foreach (var corrupt in corruptions)
            {
                var invalid = valid.DeepClone();
                var resources = invalid["Run"]!["Resources"]!;
                resources["Deck"]![1]!["Enhancement"] = "enhancement:stinky";
                resources["Inventory"]!["Tarot"]!.AsArray().Add("tarot:merchant");
                corrupt(resources);
                var original = invalid.ToJsonString();
                File.WriteAllText(path, original);
                Assert.True(CampaignStore.Load(path) is null, "Retirement migration must not permit malformed or unknown feature IDs.");
                Assert.Equal(original, File.ReadAllText(path));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    public static void DuplicatePropertiesAreRejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "campaign.json");
        try
        {
            Assert.True(CampaignStore.Save(new CampaignRun(), path));
            var valid = File.ReadAllText(path);
            string[] malformed =
            [
                valid.Replace("\"Run\":", "\"Run\":null,\"Run\":"),
                valid.Replace("\"Enhancement\":null", "\"Enhancement\":null,\"Enhancement\":null")
            ];
            foreach (var duplicate in malformed)
            {
                File.WriteAllText(path, duplicate);
                Assert.True(CampaignStore.Load(path) is null, "Duplicate properties must be rejected without throwing.");
                Assert.Equal(duplicate, File.ReadAllText(path));
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
