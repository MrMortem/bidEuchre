using BidEuchre.App;
using BidEuchre.Core;

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
}
