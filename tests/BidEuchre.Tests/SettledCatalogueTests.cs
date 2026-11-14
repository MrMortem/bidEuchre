using BidEuchre.App;
using BidEuchre.Core;
using System.Text.Json.Nodes;

internal static class SettledCatalogueTests
{
    private static readonly string[] Sigils = ["Bear", "Eagle", "Fish", "Spider", "Frog", "Duck", "Vulture", "Chameleon"];
    private static readonly string[] Tarots = ["Chameleon", "Grindstone", "Prospector", "DevilishDeal", "Gambler", "Traitor", "Astronomer", "SpringLoaded", "TaxCollector"];
    public static void ActiveRosters()
    {
        Assert.True(Enum.GetNames<SigilId>().ToHashSet().SetEquals(Sigils), "Only the eight approved Sigils are active.");
        Assert.True(Enum.GetNames<TarotId>().ToHashSet().SetEquals(Tarots), "Only the nine approved Tarots are active.");
        Assert.True(FeatureCatalog.All.Where(f => f.Category == FeatureCategory.Sigil).Select(f => f.Key).ToHashSet().SetEquals(Sigils));
        Assert.True(FeatureCatalog.All.Where(f => f.Category == FeatureCategory.Tarot).Select(f => f.Key).ToHashSet().SetEquals(Tarots));
        Assert.True(Enum.GetValues<SigilId>().Select(x => (int)x).SequenceEqual(new[] { 1, 2, 3, 4, 6, 8, 9, 10 }), "Saved Sigil values cannot shift.");
        Assert.True(Enum.GetValues<TarotId>().Select(x => (int)x).SequenceEqual(new[] { 4, 5, 6, 7, 8, 11, 12, 14, 15 }), "Saved Tarot values cannot shift.");
        Assert.True(FeatureCatalog.All.Where(f => f.Category == FeatureCategory.Tarot).Select(f => f.Name).ToHashSet().SetEquals(new[] { "Festival", "Stone mason", "Prospector", "Devilish Deal", "Gambler", "Traitor", "Astronomer", "Taxes", "Ectoplasm" }));
        Assert.Equal("Safety Net", FeatureCatalog.Get(FeatureCategory.Gumball, "Lowballer").Name);
        Assert.Equal("Chameleon", FeatureCatalog.Get(FeatureCategory.Sigil, "Chameleon").Name);
    }

    public static void MigrationAndUnknownIds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settled-migration-{Guid.NewGuid():N}.json");
        try
        {
            var run = new CampaignRun(Money: 40, Match: 4, Phase: CampaignPhase.Shop)
            {
                Resources = CampaignSnapshot.Create(73) with
                {
                    Inventory = new([], [TarotId.Chameleon, TarotId.Grindstone], [GumballId.Lowballer]) { Sigils = [SigilId.Bear, SigilId.Chameleon] },
                    GeneratedTarot = [[TarotId.TaxCollector], [TarotId.SpringLoaded], [TarotId.Prospector], [TarotId.Traitor]]
                },
                Shop = new(3, [new("kept", FeatureCategory.Tarot, "Chameleon", 3, true), new("sigil", FeatureCategory.Sigil, "Bear", 3)])
            };
            Assert.True(CampaignStore.Save(run, path));
            var expected = JsonNode.Parse(File.ReadAllText(path))!;
            var legacy = expected.DeepClone();
            var resources = legacy["Run"]!["Resources"]!;
            string[] retiredSigils = ["coin", "snake", "owl", "peacock"];
            string[] retiredTarots = ["love-potion", "nightshade", "clover-tonic", "crystal-elixir", "glassblower"];
            for (var i = 0; i < retiredSigils.Length; i++)
            {
                resources["Deck"]![i]!["Sigil"] = "sigil:" + retiredSigils[i];
                resources["Inventory"]!["Sigils"]!.AsArray().Insert(i, "sigil:" + retiredSigils[i]);
            }
            foreach (var id in retiredTarots)
            {
                resources["Inventory"]!["Tarot"]!.AsArray().Insert(0, "tarot:" + id);
                for (var seat = 0; seat < 4; seat++) resources["GeneratedTarot"]![seat]!.AsArray().Insert(0, "tarot:" + id);
            }
            var offers = legacy["Run"]!["Shop"]!["Offers"]!.AsArray();
            foreach (var key in new[] { "LovePotion", "Nightshade", "CloverTonic", "CrystalElixir", "Glassblower", "Coin", "Snake", "Owl", "Peacock" })
                offers.Add(new JsonObject { ["Id"] = key, ["Category"] = key is "Coin" or "Snake" or "Owl" or "Peacock" ? 3 : 4, ["FeatureId"] = key, ["Price"] = 3, ["Sold"] = key.Length % 2 == 0 });
            File.WriteAllText(path, legacy.ToJsonString());
            var loaded = CampaignStore.Load(path);
            Assert.True(loaded is not null, "All retired saved locations must migrate without losing the run.");
            Assert.True(CampaignStore.Save(loaded!, path));
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "Migration must preserve all surviving IDs and progress.");
            Assert.True(CampaignStore.Save(CampaignStore.Load(path)!, path));
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "The migrated run must round-trip.");
            Action<JsonNode>[] corruptions =
            [
                n => n["Resources"]!["Deck"]![0]!["Sigil"] = "sigil:unknown",
                n => n["Resources"]!["Inventory"]!["Sigils"]!.AsArray().Add("sigil:unknown"),
                n => n["Resources"]!["Inventory"]!["Tarot"]!.AsArray().Add("tarot:unknown"),
                n => n["Resources"]!["GeneratedTarot"]![3]!.AsArray().Add("tarot:unknown"),
                n => n["Shop"]!["Offers"]![0]!["FeatureId"] = "Unknown"
            ];
            foreach (var corrupt in corruptions)
            {
                var invalid = legacy.DeepClone(); corrupt(invalid["Run"]!);
                File.WriteAllText(path, invalid.ToJsonString());
                Assert.True(CampaignStore.Load(path) is null, "Unknown items must still be rejected alongside retired items.");
            }
        }
        finally { File.Delete(path); }
    }

    public static void RenamedEffectsKeepTheirBehavior()
    {
        foreach (var (tarot, enhancement, name) in new[]
        {
            (TarotId.Chameleon, EnhancementId.Wild, "Festival"),
            (TarotId.Grindstone, EnhancementId.Stone, "Stone mason"),
            (TarotId.SpringLoaded, EnhancementId.Bouncy, "Ectoplasm")
        })
        {
            var resources = CampaignSnapshot.Create(31) with { Inventory = new([], [tarot], []) };
            var game = new GameEngine(["You", "W", "P", "E"], 3, campaign: new CampaignContext(resources));
            game.StartGame(3);
            var before = game.CreateCampaignView(0).Players[0].VisibleCards[0];
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [])));
            game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [before.Id]));
            var changed = game.ExportCampaignSnapshot().Deck.Single(c => c.Id == before.Id);
            Assert.Equal(enhancement, changed.Enhancement!.Value);
            Assert.Equal(before.Face, changed.Face);
            Assert.True(game.CreateCampaignView(0).Events.Contains($"Used {name}."));
            var run = CampaignShop.UseTarot(new CampaignRun { Resources = resources }, 0, [new(0)]);
            Assert.Equal(enhancement, run.Resources.Deck[0].Enhancement!.Value);
        }
        foreach (var wallet in new[] { 0, 15, 35 })
        {
            var resources = CampaignSnapshot.Create(31) with { Inventory = new([], [TarotId.TaxCollector], []), Wallets = [wallet, 0, 0, 0] };
            var game = new GameEngine(["You", "W", "P", "E"], 3, campaign: new CampaignContext(resources));
            game.StartGame(3);
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [new(0)])));
            game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, []));
            Assert.Equal(wallet + Math.Min(wallet, 20), game.ExportCampaignSnapshot().Wallets[0]);
            Assert.True(game.CreateCampaignView(0).Events.Any(e => e.Contains("Taxes +$")));
            var run = CampaignShop.UseTarot(new CampaignRun(Money: wallet) { Resources = resources }, 0, []);
            Assert.Equal(wallet + Math.Min(wallet, 20), run.Money);
        }
        var unlocked = CampaignSnapshot.Create(31) with { Inventory = new([], [], [GumballId.Lowballer]) };
        var safetyNet = new GameEngine(["You", "W", "P", "E"], 3, campaign: new CampaignContext(unlocked));
        safetyNet.StartGame(3);
        safetyNet.ExecuteCampaignAction(0, new CampaignCommand.Bid((BidLevel)2));
        Assert.Equal((BidLevel)2, safetyNet.HighBid!.Value);
    }

    public static void InvalidAcquisitionIsRejected()
    {
        foreach (var (category, keys) in new[]
        {
            (FeatureCategory.Sigil, new[] { "Coin", "Snake", "Owl", "Peacock", "0", "999", "Unknown" }),
            (FeatureCategory.Tarot, new[] { "LovePotion", "Nightshade", "CloverTonic", "CrystalElixir", "Glassblower", "0", "999", "Unknown" })
        })
        foreach (var key in keys)
        {
            var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop) { Shop = new(0, [new("x", category, key, 3)]) };
            Assert.Throws<GameRuleException>(() => CampaignShop.Buy(run, "x"));
            Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "x", [new(0)]));
            Assert.Equal(30, run.Money);
            Assert.Equal(0, run.Resources.Inventory.ItemCount);
        }
    }
}
