using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.Core;
using BidEuchre.App;

internal static class GumballDuplicateTests
{
    private static CampaignInventory WithExtras(params (GumballId Id, int Count)[] extras)
    {
        var json = JsonSerializer.SerializeToNode(new CampaignInventory([], [], Enum.GetValues<GumballId>()))!;
        json["GumballExtraCopies"] = new JsonObject(extras.Select(e => new KeyValuePair<string, JsonNode?>(e.Id.ToString(), JsonValue.Create(e.Count))));
        return json.Deserialize<CampaignInventory>()!;
    }

    public static void MatchAstronomerAfterAllUnlocks()
    {
        var inventory = new CampaignInventory([], [TarotId.Astronomer], Enum.GetValues<GumballId>());
        var snapshot = CampaignSnapshot.Create(3) with { Inventory = inventory };
        var game = new GameEngine(["You", "W", "P", "E"], 3, null, new CampaignContext(snapshot));
        game.StartGame(3);
        game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, []));
        Assert.Equal(0, game.ExportCampaignSnapshot().Inventory.Tarot.Length);
        Assert.Equal(9, game.ExportCampaignSnapshot().Inventory.Unlocks.Length);
        var counts = JsonSerializer.SerializeToNode(game.ExportCampaignSnapshot().Inventory)!["GumballExtraCopies"]!.AsObject();
        Assert.Equal(1, counts.Sum(p => p.Value!.GetValue<int>()));
        Assert.Equal(0, game.Scores.Sum());
    }

    // Removing the duplicate settlement reward leaves equal scores for the paired, identical hands.
    public static void SuccessfulHumanBonus()
    {
        var plain = Play(5, 0, (BidLevel)2, null, ContractMode.Trump, new([], [], Enum.GetValues<GumballId>()));
        var duplicate = Play(5, 0, (BidLevel)2, null, ContractMode.Trump, WithExtras((GumballId.Lowballer, 3), (GumballId.Poker, 7)));
        Assert.True(plain.TricksByTeam[0] >= 2);
        Assert.Equal(3, duplicate.Scores[0] - plain.Scores[0]);
        Assert.Equal(plain.Scores[1], duplicate.Scores[1]);
    }

    public static void AllMatchingTypesAndExclusions()
    {
        var cases = new (BidLevel Bid, GumballId? Variant, ContractMode Mode, GumballId[] Matching)[]
        {
            ((BidLevel)2, null, ContractMode.Trump, [GumballId.Lowballer]),
            (BidLevel.Four, GumballId.Chaos, ContractMode.Trump, [GumballId.Chaos]),
            (BidLevel.Four, GumballId.Poker, ContractMode.Trump, [GumballId.Poker]),
            (BidLevel.Four, GumballId.Duelist, ContractMode.Trump, [GumballId.Duelist]),
            (BidLevel.Five, GumballId.Telepath, ContractMode.Trump, [GumballId.Telepath]),
            (BidLevel.PartnersBest, null, ContractMode.Trump, [GumballId.PartnersBest]),
            (BidLevel.Alone, null, ContractMode.Trump, [GumballId.Alone]),
            (BidLevel.Four, null, ContractMode.High, [GumballId.HighLow]),
            (BidLevel.Four, null, ContractMode.Low, [GumballId.HighLow]),
            (BidLevel.Alone, null, ContractMode.High, [GumballId.Alone, GumballId.HighLow]),
            (BidLevel.Four, GumballId.Poker, ContractMode.High, [GumballId.Poker, GumballId.HighLow]),
            (BidLevel.Four, GumballId.Duelist, ContractMode.Low, [GumballId.Duelist, GumballId.HighLow]),
            (BidLevel.Three, null, ContractMode.Trump, [])
        };
        var extras = WithExtras(Enum.GetValues<GumballId>().Select(id => (id, 2)).ToArray());
        foreach (var c in cases)
        {
            var plain = Play(5, 0, c.Bid, c.Variant, c.Mode, new([], [], Enum.GetValues<GumballId>()));
            var bonus = Play(5, 0, c.Bid, c.Variant, c.Mode, extras);
            Assert.Equal(6, plain.TricksByTeam[0]);
            Assert.Equal(c.Matching.Length * 2, bonus.Scores[0] - plain.Scores[0]);
            Assert.Equal(plain.Scores[1], bonus.Scores[1]);
            Assert.Equal(0, bonus.ExportCampaignSnapshot().Wallets.Sum());
        }
        // The partner's successful contract never earns the human's duplicate rewards.
        foreach (var bid in new[] { (BidLevel)2, BidLevel.Alone, BidLevel.PartnersBest })
        {
            var plain = Play(5, 2, bid, null, ContractMode.Trump, new([], [], Enum.GetValues<GumballId>()));
            var bonus = Play(5, 2, bid, null, ContractMode.Trump, extras);
            Assert.Equal(6, plain.TricksByTeam[0]);
            Assert.Equal(plain.Scores[0], bonus.Scores[0]);
        }
        // A failed matching contract pays no duplicate reward.
        var failed = Play(5, 0, BidLevel.Alone, null, ContractMode.High, new([], [], Enum.GetValues<GumballId>()), false);
        var failedBonus = Play(5, 0, BidLevel.Alone, null, ContractMode.High, extras, false);
        Assert.Equal(0, failed.TricksByTeam[0]);
        Assert.Equal(failed.Scores[0], failedBonus.Scores[0]);
    }

    public static void InvalidExtraCopies()
    {
        Assert.True(!(CampaignSnapshot.Create(1) with { Inventory = WithExtras((GumballId.Chaos, -1)) }).IsValid());
        Assert.True(!(CampaignSnapshot.Create(1) with { Inventory = WithExtras(((GumballId)999, 1)) }).IsValid());
        var unowned = WithExtras((GumballId.Chaos, 1)) with { Unlocks = [] };
        Assert.True(!(CampaignSnapshot.Create(1) with { Inventory = unowned }).IsValid());
        Assert.True(!(CampaignSnapshot.Create(1) with { Inventory = WithExtras((GumballId.Chaos, int.MaxValue), (GumballId.Alone, 1)) }).IsValid());
    }

    public static void ShopGrantAndPersistence()
    {
        var all = Enum.GetValues<GumballId>();
        var run = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Resources = CampaignSnapshot.Create(41) with { Wallets = [30, 0, 0, 0], Inventory = new([], [], all) } });
        var offer = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
        var pulled = CampaignShop.Buy(run, offer.Id, expectedRevision: run.Resources.Revision);
        var selected = Enum.Parse<GumballId>(pulled.Shop!.Offers.Single(o => o.Id == offer.Id).FeatureId);
        Assert.Equal(9, pulled.Resources.Inventory.Unlocks.Length);
        Assert.Equal(27, pulled.Money);
        Assert.Equal(1, ExtraCount(pulled.Resources.Inventory));
        Assert.Equal(0, ExtraCount(run.Resources.Inventory));
        var rerolled = CampaignShop.Reroll(pulled, pulled.Resources.Revision);
        Assert.Equal(1, rerolled.Shop!.RerollCount);
        Assert.True(ReferenceEquals(pulled.Shop.Offers.Single(o => o.Id == offer.Id), rerolled.Shop.Offers.Single(o => o.Id == offer.Id)));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(rerolled, offer.Id));
        Assert.Equal(1, ExtraCount(rerolled.Resources.Inventory));
        var path = Path.Combine(Path.GetTempPath(), $"gumball-duplicates-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(rerolled, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.True(loaded is not null);
            Assert.Equal(1, ExtraCount(loaded!.Resources.Inventory));
            Assert.Equal(1, loaded.Shop!.RerollCount);
            var next = CampaignShop.Reroll(loaded);
            Assert.Equal(2, next.Shop!.RerollCount);
            Assert.Equal(selected.ToString(), next.Shop.Offers.Single(o => o.Id == offer.Id).FeatureId);
            Assert.True(next.Shop.Offers.Single(o => o.Id == offer.Id).Sold);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["Run"]!["Resources"]!["Inventory"]!.AsObject().Remove("GumballExtraCopies");
            File.WriteAllText(path, json.ToJsonString());
            Assert.Equal(0, ExtraCount(CampaignStore.Load(path)!.Resources.Inventory));
        }
        finally { File.Delete(path); }
    }

    public static void LegacyCompletedShopKeepsDuplicatePullAvailable()
    {
        var offers = new ShopOffer[] { new("1:0", FeatureCategory.Joker, "Alpha", 4), new("1:1", FeatureCategory.Joker, "Bully", 4),
            new("1:2", FeatureCategory.Tarot, "Prospector", 3, true), new("1:3", FeatureCategory.Sigil, "Fish", 3) };
        var legacy = new CampaignRun(Match: 2, Money: 30, Phase: CampaignPhase.Shop)
        {
            Resources = CampaignSnapshot.Create(43) with { Revision = 7, Wallets = [30, 0, 0, 0], Inventory = new([], [], Enum.GetValues<GumballId>()) },
            Shop = new(1, offers, 3)
        };
        var upgraded = CampaignShop.Open(legacy);
        Assert.Equal(5, upgraded.Shop!.Offers.Length);
        Assert.Equal(1, upgraded.Shop.Offers.Count(offer => offer.Category == FeatureCategory.Gumball));
        Assert.True(upgraded.Shop.Offers.Select(offer => offer.Id).Distinct().Count() == 5);
        for (var i = 0; i < 4; i++) Assert.True(ReferenceEquals(offers[i], upgraded.Shop.Offers[i]));
        Assert.Equal(3, upgraded.Shop.RerollCount);
        Assert.Equal(legacy.Resources.RandomState, upgraded.Resources.RandomState);
        Assert.Equal(30, upgraded.Money);
        Assert.Equal(8L, upgraded.Resources.Revision);
        Assert.Equal(4, legacy.Shop!.Offers.Length);
        Assert.True(ReferenceEquals(upgraded, CampaignShop.Open(upgraded)));
        var pull = upgraded.Shop.Offers.Single(offer => offer.Category == FeatureCategory.Gumball);
        var bought = CampaignShop.Buy(upgraded, pull.Id);
        Assert.Equal(1, ExtraCount(bought.Resources.Inventory));
        var revisited = CampaignShop.Open(bought);
        Assert.True(ReferenceEquals(bought, revisited));
        Assert.True(revisited.Shop!.Offers.Single(offer => offer.Id == pull.Id).Sold);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(revisited, pull.Id));
    }

    public static void ShopAstronomerWithFullInventory()
    {
        foreach (var full in new[] { false, true })
        {
            var inventory = new CampaignInventory([], [TarotId.Astronomer, TarotId.Prospector], full ? Enum.GetValues<GumballId>() : []) { Sigils = [SigilId.Bear, SigilId.Fish] };
            var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(3) with { Inventory = inventory }, Shop = new(0, [new("astro", FeatureCategory.Tarot, nameof(TarotId.Astronomer), 3)]) };
            var used = CampaignShop.BuyAndUse(run, "astro", []);
            Assert.Equal(27, used.Money);
            Assert.Equal(4, used.Resources.Inventory.ItemCount);
            Assert.Equal(full ? 9 : 1, used.Resources.Inventory.Unlocks.Length);
            Assert.Equal(full ? 1 : 0, ExtraCount(used.Resources.Inventory));
            Assert.Equal(0, ExtraCount(run.Resources.Inventory));
            Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(used, "astro", []));
            var held = CampaignShop.UseTarot(run, 0, []);
            Assert.Equal(3, held.Resources.Inventory.ItemCount);
            Assert.Equal(30, held.Money);
            Assert.Equal(JsonSerializer.Serialize(used.Resources.Inventory with { Tarot = [TarotId.Prospector] }), JsonSerializer.Serialize(held.Resources.Inventory));
            Assert.Equal(used.Resources.RandomState, held.Resources.RandomState);
        }
    }

    public static void CopyRandomAndOverflow()
    {
        var inventory = new CampaignInventory([], [], Enum.GetValues<GumballId>()) { GumballExtraCopies = new() { [GumballId.Chaos] = 2 } };
        var snapshot = CampaignSnapshot.Create(5) with { Inventory = inventory };
        var context = new CampaignContext(snapshot);
        inventory.GumballExtraCopies[GumballId.Chaos] = 99;
        var detached = context.Snapshot();
        Assert.Equal(2, detached.Inventory.GumballExtraCopies[GumballId.Chaos]);
        detached.Inventory.GumballExtraCopies[GumballId.Chaos] = 88;
        Assert.Equal(2, context.Snapshot().Inventory.GumballExtraCopies[GumballId.Chaos]);
        var game = Play(5, 0, BidLevel.Four, GumballId.Chaos, ContractMode.Trump, context.Snapshot().Inventory);
        var exported = game.ExportCampaignSnapshot();
        exported.Inventory.GumballExtraCopies[GumballId.Chaos] = 77;
        Assert.Equal(2, game.ExportCampaignSnapshot().Inventory.GumballExtraCopies[GumballId.Chaos]);

        var seen = new HashSet<GumballId>();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var grant = GumballGrants.Pull(context.Snapshot().Inventory, seed);
            var replay = GumballGrants.Pull(context.Snapshot().Inventory, seed);
            Assert.Equal(grant.Id, replay.Id);
            Assert.Equal(grant.RandomState, replay.RandomState);
            Assert.True(grant.RandomState != seed);
            Assert.Equal(9, grant.Inventory.Unlocks.Length);
            seen.Add(grant.Id);
        }
        Assert.Equal(9, seen.Count);
        Assert.True(seen.Contains(GumballId.PanicButton) && seen.Contains(GumballId.HighLow));
        foreach (var missing in Enum.GetValues<GumballId>())
        {
            var almost = new CampaignInventory([], [], Enum.GetValues<GumballId>().Where(id => id != missing).ToArray());
            var grant = GumballGrants.Pull(almost, 1);
            Assert.Equal(missing, grant.Id);
            Assert.Equal(9, grant.Inventory.Unlocks.Length);
            Assert.Equal(0, grant.Inventory.GumballExtraCopies.Count);
        }
        var max = new CampaignInventory([], [], Enum.GetValues<GumballId>()) { GumballExtraCopies = new() { [GumballId.Chaos] = int.MaxValue } };
        Assert.True((snapshot with { Inventory = max }).IsValid());
        Assert.Equal(2147483648L, max.GumballCopies(GumballId.Chaos));
        var before = JsonSerializer.Serialize(max);
        Assert.Throws<GameRuleException>(() => GumballGrants.Pull(max, 17));
        Assert.Equal(before, JsonSerializer.Serialize(max));
    }

    public static void MalformedSavedCounts()
    {
        var run = new CampaignRun(Money: 10) { Resources = CampaignSnapshot.Create(1) with { Inventory = new([], [], Enum.GetValues<GumballId>()) } };
        var path = Path.Combine(Path.GetTempPath(), $"invalid-gumball-counts-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run, path));
            var original = File.ReadAllText(path);
            foreach (var map in new[] { "null", "[]", "{\"gumball:chaos\":-1}", "{\"gumball:unknown\":1}", "{\"gumball:chaos\":1.5}", "{\"gumball:chaos\":2147483648}", "{\"gumball:chaos\":2147483647,\"gumball:alone\":1}" })
            {
                var json = JsonNode.Parse(original)!;
                json["Run"]!["Resources"]!["Inventory"]!["GumballExtraCopies"] = JsonNode.Parse(map);
                File.WriteAllText(path, json.ToJsonString());
                Assert.True(CampaignStore.Load(path) is null, $"Malformed counts must be rejected: {map}");
            }
            var unowned = JsonNode.Parse(original)!;
            unowned["Run"]!["Resources"]!["Inventory"]!["Unlocks"] = new JsonArray();
            unowned["Run"]!["Resources"]!["Inventory"]!["GumballExtraCopies"] = JsonNode.Parse("{\"gumball:chaos\":1}");
            File.WriteAllText(path, unowned.ToJsonString());
            Assert.True(CampaignStore.Load(path) is null);
            File.WriteAllText(path, original.Replace("\"GumballExtraCopies\":{}", "\"GumballExtraCopies\":{\"gumball:chaos\":1,\"gumball:chaos\":2}"));
            Assert.True(CampaignStore.Load(path) is null);
        }
        finally { File.Delete(path); }
    }

    public static void PanicButtonHasNoSuccessReward()
    {
        var resources = CampaignSnapshot.Create(3) with { Inventory = WithExtras((GumballId.PanicButton, 7)) };
        var game = new GameEngine(["You", "W", "P", "E"], 3, new MatchOptions(1000) { NegativeThreshold = -1000 }, new CampaignContext(resources));
        game.StartGame(3);
        game.ExecuteCampaignAction(0, new CampaignCommand.PanicButton());
        Assert.Equal(-8, game.Scores[0]);
        Assert.Equal(7, game.ExportCampaignSnapshot().Inventory.GumballExtraCopies[GumballId.PanicButton]);
        Assert.True(game.CreateCampaignView(0).Events.All(e => !e.Contains("duplicates:")));
    }

    private static int ExtraCount(CampaignInventory inventory) =>
        JsonSerializer.SerializeToNode(inventory)!["GumballExtraCopies"]?.AsObject().Sum(p => p.Value!.GetValue<int>()) ?? 0;

    private static GameEngine Play(ulong seed, int bidder, BidLevel bid, GumballId? variant, ContractMode mode, CampaignInventory inventory, bool success = true)
    {
        var deck = GameRules.CreateDeck().ToArray();
        var snapshot = CampaignSnapshot.Create(seed) with
        {
            Inventory = inventory,
            Deck = CampaignCards.CreateDeck().Select(c => c with { Face = new Card(Suit.Clubs, (c.Id.Value / 6 % 2 == 0) == (success != (mode == ContractMode.Low)) ? Rank.Ace : Rank.Nine) }).ToArray()
        };
        var game = GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"],
            Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), 3, [0, 0],
            Campaign: snapshot, Options: new MatchOptions(1000) { NegativeThreshold = -1000 }));
        while (game.CampaignActor != bidder) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(bidder, new CampaignCommand.Bid(bid, variant));
        while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
        while (game.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete))
        {
            var seat = game.CampaignActor!.Value;
            var legal = game.CreateCampaignView(seat).LegalActions;
            var action = legal.OfType<CampaignCommand.ChooseContract>().Any()
                ? legal.OfType<CampaignCommand.ChooseContract>().First(c => c.Mode == mode && (mode != ContractMode.Trump || c.Trump == Suit.Clubs))
                : legal.First(a => a is not (CampaignCommand.UseTarot or CampaignCommand.UseSigil));
            game.ExecuteCampaignAction(seat, action);
        }
        return game;
    }
}
