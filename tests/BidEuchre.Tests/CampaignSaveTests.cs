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
            var run = legacy with { Resources = legacy.Resources with { Deck = deck } };
            Assert.True(CampaignStore.Save(run, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.Equal(EnhancementId.Glass, loaded.Resources.Deck[0].Enhancement!.Value);
            Assert.Equal(SigilId.Frog, loaded.Resources.Deck[0].Sigil!.Value);
            File.WriteAllText(path, "{\"Version\":999,\"Run\":{}}");
            Assert.True(CampaignStore.Load(path) is null);
        }
        finally { Directory.Delete(folder, true); }
    }
}
