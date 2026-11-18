using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignConsumableTests
{
    private static string Snapshot(CampaignRun run) => JsonSerializer.Serialize(run);
    private static CampaignRun Shop(ulong seed = 41) => CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
    { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0] } });
    private static CardId[] Six() => Enumerable.Range(0, 6).Select(i => new CardId(i)).ToArray();
    private static CampaignRun Printed(params TarotId[] tickets) => new CampaignRun(Money: 27, Phase: CampaignPhase.Shop)
    {
        Resources = CampaignSnapshot.Create(41) with { Wallets = [27, 0, 0, 0] },
        Shop = new(0, [new ShopOffer("ticket", FeatureCategory.Tarot, "", 3) { Printed = tickets.Select(t => t.ToString()).ToArray(), TargetCards = Six() }])
    };

    public static void ShopRejectsOutsideSample()
    {
        // Exercise the saved-offer boundary: this paid opportunity exposes cards 0 through 5.
        var node = JsonSerializer.SerializeToNode(new ShopOffer("sigil", FeatureCategory.Sigil, "Fish", 3))!;
        node["TargetCards"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 6).Select(i => new CardId(i)).ToArray());
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Resources = CampaignSnapshot.Create(41) with { Wallets = [30, 0, 0, 0] }, Shop = new(0, [node.Deserialize<ShopOffer>()!]) };
        var before = JsonSerializer.Serialize(run);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(run, "sigil", new CardId(6)));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "sigil", [new CardId(6)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(run, "sigil", new CardId(99)));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "sigil", [new CardId(99)]));
        Assert.Equal(before, JsonSerializer.Serialize(run));

        var ticket = Printed(TarotId.Prospector, TarotId.DevilishDeal);
        var original = Snapshot(ticket);
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(ticket, "ticket", TarotId.Prospector, [new(6)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(ticket, "ticket", TarotId.Prospector, [new(99)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(ticket, "ticket", TarotId.DevilishDeal, [new(0), new(6)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(ticket, "ticket", TarotId.DevilishDeal, [new(0), new(0)]));
        Assert.Equal(original, Snapshot(ticket));

        var full = run with { Resources = run.Resources with { Inventory = new([], [TarotId.Prospector, TarotId.Gambler], []) { Sigils = [SigilId.Bear, SigilId.Fish] } } };
        var used = CampaignShop.BuyAndUse(full, "sigil", [new(2)]);
        Assert.Equal(27, used.Money);
        Assert.Equal(4, used.Resources.Inventory.ItemCount);
        Assert.Equal(SigilId.Fish, used.Resources.Deck[2].Sigil!.Value);
        Assert.True(used.Resources.Deck.Where(c => c.Id.Value != 2).SequenceEqual(full.Resources.Deck.Where(c => c.Id.Value != 2)));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(used, 0, new(2)));
    }

    public static void SamplesPersistWithoutRedrawing()
    {
        var samples = new HashSet<string>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var run = Shop(seed);
            Assert.Equal(Snapshot(run), Snapshot(Shop(seed)));
            Assert.True(ReferenceEquals(run, CampaignShop.Open(run)));
            foreach (var other in run.Shop!.Offers)
                Assert.Equal(0, CampaignShop.TargetCards(run, other.Id).Length);

            var teller = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot);
            var printed = CampaignShop.PrintTickets(run, teller.Id, 3);
            var offer = printed.Shop!.Offers.Single(o => o.Id == teller.Id);
            var cards = CampaignShop.TargetCards(printed, teller.Id);
            Assert.Equal(6, cards.Length);
            Assert.Equal(6, cards.Select(c => c.Id).Distinct().Count());
            Assert.True(cards.All(c => printed.Resources.Deck.Contains(c)));
            samples.Add(string.Join(",", cards.Select(c => c.Id.Value)));
            var beforeRead = Snapshot(printed);
            cards[0] = cards[0] with { Sigil = SigilId.Bear };
            Assert.Equal(beforeRead, Snapshot(printed));
            Assert.Equal(24, printed.Money);
            Assert.Equal(6, offer.TargetCards!.Distinct().Count());
            Assert.Equal(3, offer.Printed!.Length);
            var waiting = Snapshot(printed);
            foreach (var id in offer.Printed)
            {
                var tarot = Enum.Parse<TarotId>(id);
                var chosen = offer.TargetCards!.Take(Tarot.TargetCount(tarot)).ToArray();
                var result = CampaignShop.UseTicket(printed, teller.Id, tarot, chosen);
                Assert.True(result.Shop!.Offers.Single(o => o.Id == teller.Id).Sold);
                Assert.True(result.Resources.Deck.Where(c => !offer.TargetCards.Contains(c.Id)).SequenceEqual(printed.Resources.Deck.Where(c => !offer.TargetCards.Contains(c.Id))));
                Assert.Equal(waiting, Snapshot(printed));
            }

            var path = Path.Combine(Path.GetTempPath(), $"sample-{Guid.NewGuid():N}.json");
            try
            {
                Assert.True(CampaignStore.Save(printed, path));
                var loaded = CampaignStore.Load(path)!;
                Assert.Equal(waiting, Snapshot(loaded));
                Assert.True(ReferenceEquals(loaded, CampaignShop.Open(loaded)));
                Assert.True(CampaignShop.TargetCards(loaded, teller.Id).Select(c => c.Id).SequenceEqual(offer.TargetCards!));
            }
            finally { File.Delete(path); }
        }
        Assert.True(samples.Count > 1, "Different run seeds must offer different cards.");
    }

    public static void LegacySamplesUpgradeOnce()
    {
        var legacy = Printed(TarotId.Prospector) with
        {
            Shop = new(0, [new("sigil", FeatureCategory.Sigil, "Fish", 3),
                new ShopOffer("ticket", FeatureCategory.Tarot, "", 3) { Printed = [nameof(TarotId.Prospector)] },
                new("sold", FeatureCategory.Sigil, "Bear", 3, true)])
        };
        var path = Path.Combine(Path.GetTempPath(), $"legacy-sample-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(legacy, path));
            var loaded = CampaignStore.Load(path)!;
            var original = Snapshot(loaded);
            var upgraded = CampaignShop.Open(loaded);
            Assert.Equal(original, Snapshot(loaded));
            Assert.Equal(27, upgraded.Money);
            Assert.Equal(loaded.Resources.Revision + 1, upgraded.Resources.Revision);
            Assert.True(upgraded.Resources.RandomState != loaded.Resources.RandomState);
            Assert.Equal(Snapshot(upgraded), Snapshot(CampaignShop.Open(loaded)));
            Assert.True(ReferenceEquals(upgraded, CampaignShop.Open(upgraded)));
            Assert.True(upgraded.Shop!.Offers.Single(o => o.Id == "ticket").Printed!.SequenceEqual([nameof(TarotId.Prospector)]));
            Assert.True(upgraded.Shop.Offers.All(o => o.Id != "sigil"));
            Assert.Equal(6, CampaignShop.TargetCards(upgraded, "ticket").Length);
            Assert.True(upgraded.Shop.Offers.Single(o => o.Id == "sold") is { Sold: true, TargetCards: null });
            Assert.True(CampaignStore.Save(upgraded, path));
            Assert.Equal(Snapshot(upgraded), Snapshot(CampaignShop.Open(CampaignStore.Load(path)!)));
        }
        finally { File.Delete(path); }
    }

    public static void GamblerOnlyChangesTheOfferedCards()
    {
        var run = Printed(TarotId.Gambler);
        var before = Snapshot(run);
        foreach (var targets in new CardId[][]
        {
            [new(1)], [new(5), new(2)], [new(1), new(2), new(3)], [new(1), new(1)], [new(1), new(6)], [new(1), new(99)]
        })
        {
            Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(run, "ticket", TarotId.Gambler, targets));
            Assert.Equal(before, Snapshot(run));
        }
        var after = CampaignShop.UseTicket(run, "ticket", TarotId.Gambler, []);
        var lucky = after.Resources.Deck.Single(c => c.Enhancement == EnhancementId.Lucky);
        Assert.Contains(lucky.Id, run.Shop!.Offers.Single().TargetCards!);
        Assert.True(after.Resources.Deck.Where(c => c.Id != lucky.Id).SequenceEqual(run.Resources.Deck.Where(c => c.Id != lucky.Id)));
        Assert.True(run.Resources.RandomState != after.Resources.RandomState);
        Assert.Equal(Snapshot(after), Snapshot(CampaignShop.UseTicket(run, "ticket", TarotId.Gambler, [])));
        Assert.Equal(run.Money, after.Money);
        Assert.True(after.Shop!.Offers.Single().Sold);
        Assert.Equal(before, Snapshot(run));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(after, "ticket", TarotId.Gambler, []));
    }

    public static void MalformedSavedSamplesAreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bad-sample-{Guid.NewGuid():N}.json");
        try
        {
            var run = Printed(TarotId.Prospector);
            Assert.True(CampaignStore.Save(run, path));
            var original = File.ReadAllText(path);
            foreach (var sample in new JsonNode?[]
            {
                JsonSerializer.SerializeToNode(new[] { new CardId(0) }),
                JsonSerializer.SerializeToNode(new[] { new CardId(0), new(1), new(2), new(3), new(4), new(4) }),
                JsonSerializer.SerializeToNode(new[] { new CardId(0), new(1), new(2), new(3), new(4), new(99) }),
                JsonNode.Parse("[{}, {\"Value\":1}, {\"Value\":2}, {\"Value\":3}, {\"Value\":4}, {\"Value\":5}]")
            })
            {
                var node = JsonNode.Parse(original)!;
                node["Run"]!["Shop"]!["Offers"]![0]!["TargetCards"] = sample;
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
                // Bare legacy runs use a separate deserializer and must reject the same malformed IDs.
                var legacy = JsonSerializer.SerializeToNode(run)!;
                legacy["Shop"]!["Offers"]![0]!["TargetCards"] = sample?.DeepClone();
                File.WriteAllText(path, legacy.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
        }
        finally { File.Delete(path); }
    }

    public static void LiveTargetsMustStillBeInHand()
    {
        var resources = CampaignSnapshot.Create(41) with { Inventory = new([], [TarotId.Prospector], []) { Sigils = [SigilId.Fish] } };
        var game = new GameEngine(["You", "W", "P", "E"], 3, campaign: new CampaignContext(resources));
        game.StartGame(3);
        void Reject(CardId id)
        {
            var before = JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot());
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [id])));
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, id)));
            Assert.Equal(before, JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot()));
        }
        var hand = game.CreateCampaignView(0).Players[0].VisibleCards.Select(c => c.Id).ToArray();
        foreach (var hidden in resources.Deck.Where(c => !hand.Contains(c.Id))) Reject(hidden.Id);
        Reject(new(99));
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three));
        for (var seat = 1; seat <= 3; seat++) game.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Clubs));
        var play = game.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Play>().First();
        game.ExecuteCampaignAction(0, play);
        for (var step = 0; step < 20 && game.CampaignActor != 0; step++)
        {
            var seat = game.CampaignActor!.Value;
            game.ExecuteCampaignAction(seat, CampaignBotPolicy.Choose(game.CreateView(seat), seat));
        }
        Assert.Equal(0, game.CampaignActor!.Value);
        Reject(play.Id);
        var remaining = game.CreateCampaignView(0).Players[0].VisibleCards[0].Id;
        game.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, remaining));
        game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [remaining]));
        Assert.Equal(SigilId.Fish, game.ExportCampaignSnapshot().Deck.Single(c => c.Id == remaining).Sigil!.Value);
        Assert.Equal(EnhancementId.Golden, game.ExportCampaignSnapshot().Deck.Single(c => c.Id == remaining).Enhancement!.Value);
    }
}
