using BidEuchre.App;
using BidEuchre.Core;
using System.Text.Json.Nodes;

internal static class RemainingCampaignSaveTests
{
    public static void InvalidVersionKinds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-version-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var version in new[] { "\"bogus\"", "{}", "[]", "null", "true", "2.5", "999999999999999999999999" })
            {
                File.WriteAllText(path, "{\"Version\":" + version + ",\"Run\":{}}");
                Assert.True(CampaignStore.Load(path) is null, $"Version {version} must be rejected without throwing.");
            }
        }
        finally { File.Delete(path); }
    }

    public static void InvalidShopRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-shop-{Guid.NewGuid():N}.json");
        try
        {
            var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 20, Phase: CampaignPhase.Shop));
            Assert.True(CampaignStore.Save(run, path));
            var original = JsonNode.Parse(File.ReadAllText(path))!;
            Action<JsonNode>[] corruptions =
            [
                shop => shop["Offers"] = null,
                shop => shop["Offers"]!.AsArray().Add((JsonNode?)null),
                shop => shop["Offers"]!.AsArray().Add(shop["Offers"]![0]!.DeepClone()),
                shop => shop["Offers"]![0]!["Id"] = null,
                shop => shop["Offers"]![0]!["Id"] = "",
                shop => shop["Offers"]![0]!["FeatureId"] = "UnknownFeature",
                shop => shop["Offers"]![0]!["FeatureId"] = null,
                shop => shop["Offers"]![0]!["Price"] = -1,
                shop => shop["Offers"]![0]!["Category"] = 99,
                shop => shop["Offers"]![0]!["Category"] = (int)FeatureCategory.Enhancement,
                shop => shop["MatchJustFinished"] = -1,
                shop =>
                {
                    var duplicate = shop["Offers"]!.AsArray().First(node => (int)node!["Category"]! == (int)FeatureCategory.Gumball)!.DeepClone();
                    duplicate["Id"] = "second-machine";
                    shop["Offers"]!.AsArray().Add(duplicate);
                }
            ];
            foreach (var corrupt in corruptions)
            {
                var malformed = original.DeepClone(); corrupt(malformed["Run"]!["Shop"]!);
                File.WriteAllText(path, malformed.ToJsonString());
                Assert.True(CampaignStore.Load(path) is null, "Invalid shop stock must be rejected before Continue can enumerate it.");
            }
            File.WriteAllText(path, original.ToJsonString());
            var valid = CampaignStore.Load(path);
            Assert.True(valid is not null, "Valid unsold blind gumballs and stock must round-trip.");
            var gumball = valid!.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
            var bought = CampaignShop.Buy(valid, gumball.Id);
            Assert.True(CampaignStore.Save(bought, path));
            Assert.True(CampaignStore.Load(path) is not null, "Sold gumballs retain their known drawn feature ID.");
        }
        finally { File.Delete(path); }
    }

    public static void RetiredDealConstraintIsGone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-retired-{Guid.NewGuid():N}.json");
        try
        {
            var resources = CampaignSnapshot.Create(17) with
            { Inventory = new([new(JokerId.StickyFingers)], [TarotId.DevilishDeal], []) };
            Assert.True(CampaignStore.Save(new CampaignRun { Resources = resources }, path));
            var legacy = JsonNode.Parse(File.ReadAllText(path))!;
            var saved = legacy["Run"]!["Resources"]!;
            for (var index = 7; index < 24; index++) saved["Deck"]![index]!["Enhancement"] = "enhancement:stinky";
            saved["Inventory"]!["Tarot"]!.AsArray().Add("tarot:beggar");
            File.WriteAllText(path, legacy.ToJsonString());
            var run = CampaignStore.Load(path)!;
            Assert.True(run is not null, "The retired feature save must migrate.");
            Assert.Equal(1, run!.Resources.Inventory.Tarot.Length);
            Assert.Equal(TarotId.DevilishDeal, run.Resources.Inventory.Tarot[0]);
            Assert.True(run.Resources.Deck.All(card => card.Enhancement is null));
            var game = new GameEngine(["You", "West", "Partner", "East"], 5,
                new MatchOptions(1000) { NegativeThreshold = -1000, HandLimit = 8 }, new CampaignContext(run.Resources));
            game.StartGame();
            for (var hand = 0; hand < 3; hand++)
            {
                Assert.True(game.CreateCampaignView(0).Players.All(player => player.CardCount == 6), "Every seat must receive six physical cards.");
                var used = hand > 0;
                for (var step = 0; game.Phase != GamePhase.HandComplete && step < 100; step++)
                {
                    var actor = game.CampaignActor!.Value;
                    var view = game.CreateCampaignView(actor);
                    Assert.True(view.LegalActions.Count > 0, "A migrated deck cannot deadlock from a constrained deal.");
                    if (!used && actor == 0)
                    {
                        var targets = view.Players[0].VisibleCards.Take(3).Select(card => card.Id).ToArray();
                        game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, targets)); used = true;
                    }
                    else game.ExecuteCampaignAction(actor, view.LegalActions.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand ?? view.LegalActions[0]);
                }
                Assert.Equal(GamePhase.HandComplete, game.Phase);
                Assert.True(game.ExportCampaignSnapshot().Deck.All(card => card.Enhancement is null || Enum.IsDefined(card.Enhancement.Value)), "Devilish Deal must only draw active enhancements.");
                if (hand < 2) game.StartNextHand();
            }
        }
        finally { File.Delete(path); }
    }
}
