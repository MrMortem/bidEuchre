using BidEuchre.App;
using BidEuchre.Core;
internal static class CampaignShopTests
{
    public static void RetiredItemsNeverReturn()
    {
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 256; seed++)
        {
            var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0] } };
            var shop = CampaignShop.Open(run);
            Assert.True(shop.Shop!.Offers.All(o => o.FeatureId is not ("Merchant" or "Beggar")));
            var dealt = CampaignShop.UseTicket(Waiting(run, TarotId.DevilishDeal), "t", TarotId.DevilishDeal, [new(0), new(1), new(2)]);
            foreach (var card in dealt.Resources.Deck.Take(2))
            {
                var name = card.Enhancement!.Value.ToString();
                Assert.True(name is not ("Emerald" or "Stinky"));
                seen.Add(name);
            }
            Assert.True(dealt.Resources.Deck[2].Enhancement == EnhancementId.Cursed);
        }
        Assert.True(seen.SetEquals(["Lucky", "Golden", "Wild", "Cursed", "Stone", "Silver", "Glass", "Bouncy"]));
    }

    /// <summary>A shop whose Fortune Teller has already printed (and been paid for) these tickets.</summary>
    private static CampaignRun Waiting(CampaignRun run, params TarotId[] printed) =>
        run with { Shop = new(0, [new ShopOffer("t", FeatureCategory.Tarot, "", CampaignShop.TicketPrice) { Printed = printed.Select(t => t.ToString()).ToArray(), TargetCards = run.Resources.Deck.Take(6).Select(c => c.Id).ToArray() }]) };

    private static void Rerolls()
    {
        static string Snapshot(CampaignRun run) => System.Text.Json.JsonSerializer.Serialize(new CampaignSave(2, run));
        var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 30, Phase: CampaignPhase.Shop)
        { Resources = CampaignSnapshot.Create(27) with { Wallets = [30, 0, 0, 0] } });
        Assert.Equal(2, CampaignShop.RerollPrice(run));
        var original = Snapshot(run);
        var first = CampaignShop.Reroll(run, run.Resources.Revision);
        Assert.Equal(28, first.Money);
        Assert.Equal(28, first.Resources.Wallets[0]);
        Assert.Equal(1, first.Shop!.RerollCount);
        Assert.Equal(3, CampaignShop.RerollPrice(first));
        Assert.Equal(run.Resources.Revision + 1, first.Resources.Revision);
        Assert.True(first.Resources.RandomState != run.Resources.RandomState);
        Assert.Equal(original, Snapshot(run));
        Assert.Equal(Snapshot(first), Snapshot(CampaignShop.Reroll(run, run.Resources.Revision)));
        var gumball = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
        Assert.Equal(gumball, first.Shop.Offers.Single(o => o.Category == FeatureCategory.Gumball));
        var oldOffer = run.Shop.Offers.First(o => o.Category != FeatureCategory.Gumball);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(first, oldOffer.Id));
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(first, run.Resources.Revision));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(first, gumball.Id, expectedRevision: run.Resources.Revision));
        var freshTarot = first.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot);
        Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(first, freshTarot.Id, 1, run.Resources.Revision));
        var second = CampaignShop.Reroll(first);
        Assert.Equal(25, second.Money);
        Assert.Equal(2, second.Shop!.RerollCount);
        Assert.Equal(4, CampaignShop.RerollPrice(second));
        var nextVisit = CampaignShop.Open(second with { Match = 3 });
        Assert.Equal(0, nextVisit.Shop!.RerollCount);
        Assert.Equal(2, CampaignShop.RerollPrice(nextVisit));
        Assert.Equal(2, CampaignShop.Open(second).Shop!.RerollCount);

        var poor = first with { Money = 2, Resources = first.Resources with { Wallets = [2, 0, 0, 0] } };
        var poorSnapshot = Snapshot(poor);
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(poor));
        Assert.Equal(poorSnapshot, Snapshot(poor));
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(run with { Phase = CampaignPhase.Intro }));
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(run with { Shop = null }));

        var bought = CampaignShop.Buy(CampaignShop.Buy(run, gumball.Id), run.Shop.Offers.First(o => o.Category == FeatureCategory.Joker).Id);
        var rerolled = CampaignShop.Reroll(bought);
        Assert.Equal(bought.Shop!.Offers.Single(o => o.Id == gumball.Id), rerolled.Shop!.Offers.Single(o => o.Id == gumball.Id));
        Assert.True(rerolled.Shop.Offers.All(o => o.Id != oldOffer.Id));
        // The Fortune Teller comes back with each reroll, blind and unprinted.
        Assert.True(rerolled.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot) is { FeatureId: "", Sold: false, Printed: null });
        Assert.Equal(3, rerolled.Shop.Offers.Count(o => o.Category != FeatureCategory.Gumball && !o.Sold));
        foreach (var jokerOffer in rerolled.Shop.Offers.Where(o => o.Category == FeatureCategory.Joker))
            Assert.True(!rerolled.Resources.Inventory.Has(Enum.Parse<JokerId>(jokerOffer.FeatureId)));
        var soldOut = run;
        foreach (var regular in run.Shop.Offers.Where(o => o.Category is FeatureCategory.Joker or FeatureCategory.Sigil)) soldOut = CampaignShop.Buy(soldOut, regular.Id);
        var teller = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id;
        soldOut = CampaignShop.PrintTickets(soldOut, teller, 1);
        // Paid-for tickets must be taken before the stock can change.
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(soldOut));
        soldOut = CampaignShop.KeepTicket(soldOut, teller, Enum.Parse<TarotId>(soldOut.Shop!.Offers.Single(o => o.Id == teller).Printed![0]));
        var replenished = CampaignShop.Reroll(soldOut);
        Assert.Equal(3, replenished.Shop!.Offers.Count(o => o.Category != FeatureCategory.Gumball && !o.Sold));
        Assert.True(replenished.Shop.Offers.Where(o => o.Category != FeatureCategory.Gumball).All(o => run.Shop.Offers.All(old => old.Id != o.Id)));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(rerolled, gumball.Id));
        Assert.Equal(1, rerolled.Shop.Offers.Count(o => o.Category == FeatureCategory.Gumball));
        Assert.Equal(1, rerolled.Resources.Inventory.Unlocks.Length);
        Assert.Throws<GameRuleException>(() => CampaignShop.SellJoker(rerolled, rerolled.Resources.Inventory.Jokers[0].Id, bought.Resources.Revision));

        var path = Path.Combine(Path.GetTempPath(), $"reroll-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(second, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.Equal(Snapshot(second), Snapshot(loaded));
            Assert.Equal(Snapshot(CampaignShop.Reroll(second)), Snapshot(CampaignShop.Reroll(loaded)));
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["Run"]!["Shop"]!["RerollCount"] = -1;
            File.WriteAllText(path, json.ToJsonString());
            Assert.True(CampaignStore.Load(path) is null);
            json["Run"]!["Shop"]!["RerollCount"] = int.MaxValue;
            File.WriteAllText(path, json.ToJsonString());
            Assert.True(CampaignStore.Load(path) is null);
            json["Run"]!["Shop"]!.AsObject().Remove("RerollCount");
            File.WriteAllText(path, json.ToJsonString());
            Assert.Equal(0, CampaignStore.Load(path)!.Shop!.RerollCount);
        }
        finally { File.Delete(path); }
    }

    public static void Purchases()
    {
        Rerolls();
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop);
        run = CampaignShop.Open(run);
        Assert.Equal(4, run.Shop!.Offers.Length);
        Assert.Equal(2, run.Shop.Offers.Count(o => o.Category == FeatureCategory.Joker));
        Assert.True(run.Shop.Offers.All(o => o.Category != FeatureCategory.Sigil));
        // Retain purchase compatibility for a legacy Sigil offer already on screen.
        run = run with { Shop = run.Shop with { Offers = [.. run.Shop.Offers,
            new ShopOffer("legacy-sigil", FeatureCategory.Sigil, "Fish", 3) { TargetCards = run.Resources.Deck.Take(6).Select(c => c.Id).ToArray() }] } };
        var offer = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Sigil);
        var id = CampaignShop.TargetCards(run, offer.Id)[0].Id;
        var bought = CampaignShop.Buy(run, offer.Id, id);
        Assert.Equal(27, bought.Money);
        Assert.True(bought.Resources.Deck.Single(c => c.Id == id).Sigil is not null);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(bought, offer.Id, id));
        Assert.Equal(run.Shop.Offers[0], CampaignShop.Open(run).Shop!.Offers[0]);
        var joker = run.Shop.Offers.First(o => o.Category == FeatureCategory.Joker);
        var owned = CampaignShop.Buy(run, joker.Id);
        Assert.Equal(26, owned.Money);
        var sold = CampaignShop.SellJoker(owned, owned.Resources.Inventory.Jokers[0].Id);
        Assert.Equal(28, sold.Money);
        // Tarots and Sigils share four slots: two of each leave no room for either kind.
        var tarotOffer = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id;
        var sigilOffer = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Sigil).Id;
        var three = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Tarot = [TarotId.Gambler, TarotId.Prospector], Sigils = [SigilId.Bear] } } };
        // Tickets cannot be bought off the shelf: they are printed at the Fortune Teller.
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(three, tarotOffer));
        var printed = CampaignShop.PrintTickets(three, tarotOffer, 1);
        var ticket = Enum.Parse<TarotId>(printed.Shop!.Offers.Single(o => o.Id == tarotOffer).Printed!.Single());
        Assert.Equal(4, CampaignShop.KeepTicket(printed, tarotOffer, ticket).Resources.Inventory.ItemCount);
        Assert.Equal(4, CampaignShop.Buy(three, sigilOffer).Resources.Inventory.ItemCount);
        var full = three with { Resources = three.Resources with { Inventory = three.Resources.Inventory with { Sigils = [SigilId.Bear, SigilId.Bear] } } };
        var fullPrinted = CampaignShop.PrintTickets(full, tarotOffer, 1);
        Assert.Throws<GameRuleException>(() => CampaignShop.KeepTicket(fullPrinted, tarotOffer, Enum.Parse<TarotId>(fullPrinted.Shop!.Offers.Single(o => o.Id == tarotOffer).Printed!.Single())));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(full, sigilOffer));
        // A full inventory still lets a Sigil be bought and used on the spot.
        Assert.Equal(4, CampaignShop.Buy(full, sigilOffer, id).Resources.Inventory.ItemCount);
    }
    // The gumball machine sells a blind pull: the bid is drawn when it is paid for.
    public static void GumballPull()
    {
        var run = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop));
        var offer = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
        Assert.Equal("", offer.FeatureId);
        Assert.Equal(3, offer.Price);
        var pulled = CampaignShop.Buy(run, offer.Id);
        Assert.Equal(27, pulled.Money);
        Assert.Equal(1, pulled.Resources.Inventory.Unlocks.Length);
        var sold = pulled.Shop!.Offers.Single(o => o.Id == offer.Id);
        Assert.True(sold.Sold);
        Assert.Equal(pulled.Resources.Inventory.Unlocks[0].ToString(), sold.FeatureId);
        Assert.True(pulled.Resources.RandomState != run.Resources.RandomState);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(pulled, offer.Id));

        CampaignRun Owning(CampaignRun source, params GumballId[] ids) =>
            source with { Resources = source.Resources with { Inventory = source.Resources.Inventory with { Unlocks = ids } } };
        // A bid unlocked earlier in the visit, by the Astronomer say, never comes out again.
        var all = Enum.GetValues<GumballId>();
        foreach (var last in all)
        {
            var rest = Owning(run, all.Where(id => id != last).ToArray());
            var result = CampaignShop.Buy(rest, offer.Id).Resources.Inventory.Unlocks;
            Assert.Equal(CampaignInventory.GumballCount, result.Length);
            Assert.True(result[^1] == last);
        }
        // A run saved before the pull was blind names a bid on the offer; it is ignored.
        var stale = Owning(run, GumballId.Chaos) with { Shop = new(0, [new("g", FeatureCategory.Gumball, "Chaos", 3)]) };
        var redrawn = CampaignShop.Buy(stale, "g").Resources.Inventory.Unlocks;
        Assert.Equal(2, redrawn.Length);
        Assert.True(redrawn[1] != GumballId.Chaos);

        var full = Owning(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop), all);
        Assert.Equal(1, CampaignShop.Open(full).Shop!.Offers.Count(o => o.Category == FeatureCategory.Gumball));
        Assert.Equal(1, CampaignShop.Buy(Owning(run, all), offer.Id).Resources.Inventory.GumballExtraCopies.Values.Sum());

        var poor = run with { Money = 2 };
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(poor, offer.Id));
    }
    public static void StoredCardsOutsideMatch()
    {
        var shop = new CampaignRun(Money: 10, Phase: CampaignPhase.Shop) { Shop = new(0, []) };
        shop = shop with { Resources = shop.Resources with { Wallets = [10, 0, 0, 0], Inventory = shop.Resources.Inventory with { Tarot = [TarotId.Prospector, TarotId.TaxCollector], Sigils = [SigilId.Fish, SigilId.Bear] } } };
        foreach (var run in new[] { shop, shop.LeaveShop(), shop.LeaveShop().StartMatch() })
        {
            var before = System.Text.Json.JsonSerializer.Serialize(run);
            Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(run, 0, [run.Resources.Deck[5].Id], run.Resources.Revision));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(run, 1, []));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(run, 0, run.Resources.Deck[5].Id));
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(run));
        }
        // Discarding still frees a shared slot in the shop and before a match.
        var discarded = CampaignShop.DiscardTarot(shop, 0, shop.Resources.Revision);
        Assert.Equal(TarotId.TaxCollector, discarded.Resources.Inventory.Tarot.Single());
        Assert.Throws<GameRuleException>(() => CampaignShop.DiscardTarot(discarded, 0, shop.Resources.Revision));
        var emptied = CampaignShop.DiscardSigil(discarded.LeaveShop(), 0);
        Assert.Equal(SigilId.Bear, emptied.Resources.Inventory.Sigils.Single());
    }
    public static void BuyAndUseTarot()
    {
        CampaignRun Stocked(params TarotId[] printed) => Waiting(new CampaignRun(Money: 27, Phase: CampaignPhase.Shop) { Resources = CampaignSnapshot.Create(0) with { Wallets = [27, 0, 0, 0] } }, printed);
        var start = Stocked(TarotId.Prospector, TarotId.Gambler, TarotId.Chameleon);
        var target = start.Resources.Deck[5].Id;
        // A full inventory does not block using a printed ticket on the spot; it was paid for when printed.
        var full = start with { Resources = start.Resources with { Inventory = start.Resources.Inventory with { Tarot = [TarotId.Gambler, TarotId.Prospector], Sigils = [SigilId.Bear, SigilId.Fish] } } };
        Assert.Throws<GameRuleException>(() => CampaignShop.KeepTicket(full, "t", TarotId.Prospector));
        var used = CampaignShop.UseTicket(full, "t", TarotId.Prospector, [target]);
        Assert.Equal(27, used.Money);
        Assert.Equal(27, used.Resources.Wallets[0]);
        Assert.True(used.Resources.Deck[5].Enhancement == EnhancementId.Golden);
        Assert.Equal(2, used.Resources.Inventory.Tarot.Length);
        Assert.True(used.Shop!.Offers[0] is { Sold: true, Printed: null, FeatureId: "Prospector" });
        Assert.True(used.Resources.Revision > full.Resources.Revision);
        // The other printed tickets are gone, and nothing can be taken twice.
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(used, "t", TarotId.Gambler, []));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(start, "t", TarotId.TaxCollector, []));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(start, "t", TarotId.Prospector, []));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(start, "t", TarotId.Prospector, [new CardId(99)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(start, "t", [target]));

        var festival = CampaignShop.UseTicket(start, "t", TarotId.Chameleon, [target]);
        Assert.True(festival.Resources.Deck[5].Enhancement == EnhancementId.Wild);
        var kept = CampaignShop.KeepTicket(start, "t", TarotId.Gambler);
        Assert.Equal(TarotId.Gambler, kept.Resources.Inventory.Tarot.Single());
        Assert.Equal(27, kept.Money);

        var deal = Stocked(TarotId.DevilishDeal);
        var ids = deal.Resources.Deck.Take(3).Select(c => c.Id).ToArray();
        var dealt = CampaignShop.UseTicket(deal, "t", TarotId.DevilishDeal, ids);
        Assert.True(dealt.Resources.Deck[0].Enhancement is not null && dealt.Resources.Deck[1].Enhancement is not null);
        Assert.True(dealt.Resources.Deck[2].Enhancement == EnhancementId.Cursed);
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTicket(deal, "t", TarotId.DevilishDeal, [ids[0], ids[0], ids[1]]));
        FortuneTellerPrints();

        var lucky = CampaignShop.UseTicket(Stocked(TarotId.Gambler), "t", TarotId.Gambler, [new(1), new(4)]);
        Assert.Equal(2, lucky.Resources.Deck.Count(c => c.Enhancement == EnhancementId.Lucky));
        Assert.True(lucky.Resources.Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id).ToHashSet().SetEquals([new(1), new(4)]));
        Assert.Equal(start.Resources.RandomState, lucky.Resources.RandomState);

        // Doubling applies to what is left after paying for the print, and gains at most $20.
        var poor = Stocked(TarotId.TaxCollector) with { Money = 7, Resources = Stocked().Resources with { Wallets = [7, 0, 0, 0] } };
        Assert.Equal(14, CampaignShop.UseTicket(poor, "t", TarotId.TaxCollector, []).Money);
        Assert.Equal(47, CampaignShop.UseTicket(Stocked(TarotId.TaxCollector), "t", TarotId.TaxCollector, []).Money);

        var astronomer = Stocked(TarotId.Astronomer);
        Assert.Equal(1, CampaignShop.UseTicket(astronomer, "t", TarotId.Astronomer, []).Resources.Inventory.Unlocks.Length);
        var all = astronomer with { Resources = astronomer.Resources with { Inventory = astronomer.Resources.Inventory with { Unlocks = Enum.GetValues<GumballId>() } } };
        Assert.Equal(1, CampaignShop.UseTicket(all, "t", TarotId.Astronomer, []).Resources.Inventory.GumballExtraCopies.Values.Sum());

        var joker = start with { Shop = new(0, [new ShopOffer("j", FeatureCategory.Joker, "Alpha", 4)]) };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(joker, "j", []));
    }
    public static void BuyAndUseSigil()
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Shop = new(0, [new ShopOffer("s", FeatureCategory.Sigil, "Fish", 3) { TargetCards = Enumerable.Range(0, 6).Select(i => new CardId(i)).ToArray() }]) };
        run.Resources.Deck[5] = run.Resources.Deck[5] with { Enhancement = EnhancementId.Glass, Sigil = SigilId.Bear };
        var target = run.Resources.Deck[5].Id;
        var used = CampaignShop.BuyAndUse(run, "s", [target]);
        Assert.Equal(27, used.Money);
        Assert.Equal(27, used.Resources.Wallets[0]);
        Assert.Equal(SigilId.Fish, used.Resources.Deck[5].Sigil!.Value);
        Assert.Equal(EnhancementId.Glass, used.Resources.Deck[5].Enhancement!.Value);
        Assert.Equal(1, used.Resources.Deck.Count(c => c.Sigil == SigilId.Fish));
        Assert.Equal(SigilId.Bear, run.Resources.Deck[5].Sigil!.Value);
        Assert.True(used.Shop!.Offers[0].Sold);
        Assert.Equal(run.Resources.Revision + 1, used.Resources.Revision);
        Assert.Equal(run.Resources.RandomState, used.Resources.RandomState);
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(used, "s", [target]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "s", []));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "s", [new CardId(99)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run, "s", [target, run.Resources.Deck[0].Id]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(run with { Money = 2 }, "s", [target]));
    }
    public static void StoreAndUseSigil()
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Shop = new(0, [new ShopOffer("s", FeatureCategory.Sigil, "Fish", 3) { TargetCards = Enumerable.Range(0, 6).Select(i => new CardId(i)).ToArray() }]) };
        var bought = CampaignShop.Buy(run, "s");
        Assert.Equal(27, bought.Money);
        Assert.Equal(SigilId.Fish, bought.Resources.Inventory.Sigils.Single());
        Assert.True(bought.Resources.Deck.SequenceEqual(run.Resources.Deck));
        Assert.Equal(0, run.Resources.Inventory.Sigils.Length);
        var copy = bought.Resources.Copy();
        copy.Inventory.Sigils[0] = SigilId.Bear;
        Assert.Equal(SigilId.Fish, bought.Resources.Inventory.Sigils[0]);
        var path = Path.Combine(Path.GetTempPath(), $"sigil-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(bought, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.Equal(SigilId.Fish, loaded.Resources.Inventory.Sigils.Single());
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded, 0, loaded.Resources.Deck[7].Id));
            var game = new GameEngine(["You", "W", "P", "E"], 3, campaign: new CampaignContext(loaded.Resources));
            game.StartGame(3);
            var target = game.CreateCampaignView(0).Players[0].VisibleCards[0].Id;
            var beforeUse = game.ExportCampaignSnapshot();
            game.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, target));
            var used = loaded with { Resources = game.ExportCampaignSnapshot() };
            Assert.Equal(27, used.Money);
            Assert.Equal(SigilId.Fish, used.Resources.Deck.Single(c => c.Id == target).Sigil!.Value);
            Assert.Equal(0, used.Resources.Inventory.Sigils.Length);
            Assert.Equal(1, loaded.Resources.Inventory.Sigils.Length);
            Assert.Equal(beforeUse.Revision + 1, used.Resources.Revision);
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(used, 0, target));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded, 0, target, loaded.Resources.Revision - 1));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded, 0, new CardId(99)));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded.LeaveShop().StartMatch(), 0, target));
            Assert.True(CampaignStore.Save(used, path));
            Assert.Equal(SigilId.Fish, CampaignStore.Load(path)!.Resources.Deck.Single(c => c.Id == target).Sigil!.Value);
            // Saves from before stored Sigils existed still load with an empty inventory.
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            json["Run"]!["Resources"]!["Inventory"]!.AsObject().Remove("Sigils");
            File.WriteAllText(path, json.ToJsonString());
            Assert.Equal(0, CampaignStore.Load(path)!.Resources.Inventory.Sigils.Length);
        }
        finally { File.Delete(path); }
    }
    public static void Poker()
    {
        var five = PokerEvaluator.Best([new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Nine)]);
        var straight = PokerEvaluator.Best([new(Suit.Hearts, Rank.Ten), new(Suit.Hearts, Rank.Jack), new(Suit.Hearts, Rank.Queen), new(Suit.Hearts, Rank.King), new(Suit.Hearts, Rank.Ace), new(Suit.Clubs, Rank.Nine)]);
        Assert.True(five.CompareTo(straight) > 0);
    }

    // $3 prints one blind ticket, $6 prints three different ones; the print is paid and saved before any choice.
    private static void FortuneTellerPrints()
    {
        foreach (var (count, price) in new[] { (1, 3), (3, 6) })
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var run = CampaignShop.Open(new CampaignRun(Money: 10, Phase: CampaignPhase.Shop) { Resources = CampaignSnapshot.Create(seed) with { Wallets = [10, 0, 0, 0] } });
                var teller = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
                Assert.True(teller is { FeatureId: "", Price: CampaignShop.TicketPrice, Printed: null });
                var printed = CampaignShop.PrintTickets(run, teller.Id, count, run.Resources.Revision);
                Assert.Equal(10 - price, printed.Money);
                Assert.Equal(10 - price, printed.Resources.Wallets[0]);
                var tickets = printed.Shop!.Offers.Single(o => o.Id == teller.Id).Printed!;
                Assert.Equal(count, tickets.Length);
                Assert.Equal(count, tickets.Distinct().Count());
                Assert.True(tickets.All(t => Enum.TryParse<TarotId>(t, out _)));
                Assert.True(printed.Resources.RandomState != run.Resources.RandomState);
                // One print a visit, and the shop cannot be left while tickets wait.
                Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(printed, teller.Id, 1));
                Assert.Throws<GameRuleException>(() => printed.LeaveShop());
                // Saved and reloaded, the same tickets are still waiting.
                var path = Path.Combine(Path.GetTempPath(), $"teller-{Guid.NewGuid():N}.json");
                try
                {
                    Assert.True(CampaignStore.Save(printed, path));
                    Assert.True(CampaignStore.Load(path)!.Shop!.Offers.Single(o => o.Id == teller.Id).Printed!.SequenceEqual(tickets));
                }
                finally { File.Delete(path); }
                var kept = CampaignShop.KeepTicket(printed, teller.Id, Enum.Parse<TarotId>(tickets[^1]));
                Assert.Equal(Enum.Parse<TarotId>(tickets[^1]), kept.Resources.Inventory.Tarot.Single());
                Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(kept, teller.Id, 1));
                Assert.Equal(CampaignPhase.Intro, kept.LeaveShop().Phase);
            }
        var broke = CampaignShop.Open(new CampaignRun(Money: 5, Phase: CampaignPhase.Shop) { Resources = CampaignSnapshot.Create(9) with { Wallets = [5, 0, 0, 0] } });
        var offer = broke.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id;
        Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(broke, offer, 3));
        Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(broke, offer, 2));
        Assert.Equal(2, CampaignShop.PrintTickets(broke, offer, 1).Money);
    }
}
