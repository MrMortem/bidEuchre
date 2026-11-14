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
            var stocked = run with { Shop = new(0, [new("t", FeatureCategory.Tarot, "DevilishDeal", 3)]) };
            var dealt = CampaignShop.BuyAndUse(stocked, "t", [new(0), new(1), new(2)]);
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
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(first, freshTarot.Id, [], run.Resources.Revision));
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

        var bought = CampaignShop.Buy(CampaignShop.Buy(run, gumball.Id), oldOffer.Id);
        var rerolled = CampaignShop.Reroll(bought);
        Assert.Equal(bought.Shop!.Offers.Single(o => o.Id == gumball.Id), rerolled.Shop!.Offers.Single(o => o.Id == gumball.Id));
        Assert.True(rerolled.Shop.Offers.All(o => o.Id != oldOffer.Id));
        Assert.Equal(4, rerolled.Shop.Offers.Count(o => o.Category != FeatureCategory.Gumball && !o.Sold));
        foreach (var jokerOffer in rerolled.Shop.Offers.Where(o => o.Category == FeatureCategory.Joker))
            Assert.True(!rerolled.Resources.Inventory.Has(Enum.Parse<JokerId>(jokerOffer.FeatureId)));
        var soldOut = run;
        foreach (var regular in run.Shop.Offers.Where(o => o.Category != FeatureCategory.Gumball)) soldOut = CampaignShop.Buy(soldOut, regular.Id);
        var replenished = CampaignShop.Reroll(soldOut);
        Assert.Equal(4, replenished.Shop!.Offers.Count(o => o.Category != FeatureCategory.Gumball && !o.Sold));
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
        Assert.Equal(5, run.Shop!.Offers.Length);
        Assert.Equal(2, run.Shop.Offers.Count(o => o.Category == FeatureCategory.Joker));
        var offer = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Sigil);
        var id = run.Resources.Deck[0].Id;
        var bought = CampaignShop.Buy(run, offer.Id, id);
        Assert.Equal(27, bought.Money);
        Assert.True(bought.Resources.Deck[0].Sigil is not null);
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
        Assert.Equal(4, CampaignShop.Buy(three, tarotOffer).Resources.Inventory.ItemCount);
        Assert.Equal(4, CampaignShop.Buy(three, sigilOffer).Resources.Inventory.ItemCount);
        var full = three with { Resources = three.Resources with { Inventory = three.Resources.Inventory with { Sigils = [SigilId.Bear, SigilId.Bear] } } };
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(full, tarotOffer));
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
            Assert.Equal(9, result.Length);
            Assert.True(result[^1] == last);
        }
        // A run saved before the pull was blind names a bid on the offer; it is ignored.
        var stale = Owning(run, GumballId.Chaos) with { Shop = new(0, [new("g", FeatureCategory.Gumball, "Chaos", 3)]) };
        var redrawn = CampaignShop.Buy(stale, "g").Resources.Inventory.Unlocks;
        Assert.Equal(2, redrawn.Length);
        Assert.True(redrawn[1] != GumballId.Chaos);

        var full = Owning(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop), all);
        Assert.Equal(0, CampaignShop.Open(full).Shop!.Offers.Count(o => o.Category == FeatureCategory.Gumball));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(Owning(run, all), offer.Id));

        var poor = run with { Money = 2 };
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(poor, offer.Id));
    }
    public static void StoredCardsOutsideMatch()
    {
        var run = new CampaignRun(Money: 10, Phase: CampaignPhase.Shop) { Shop = new(0, []) };
        run = run with { Resources = run.Resources with { Wallets = [10, 0, 0, 0], Inventory = run.Resources.Inventory with { Tarot = [TarotId.Prospector, TarotId.TaxCollector], Sigils = [SigilId.Fish, SigilId.Bear] } } };
        var target = run.Resources.Deck[5].Id;
        // A stored Tarot is played on the shared deck from the shop, at no charge.
        var used = CampaignShop.UseTarot(run, 0, [target], run.Resources.Revision);
        Assert.Equal(EnhancementId.Golden, used.Resources.Deck[5].Enhancement!.Value);
        Assert.Equal(10, used.Money);
        Assert.Equal(TarotId.TaxCollector, used.Resources.Inventory.Tarot.Single());
        Assert.Equal(2, used.Resources.Inventory.Sigils.Length);
        Assert.Equal(2, run.Resources.Inventory.Tarot.Length);
        Assert.True(run.Resources.Deck[5].Enhancement is null);
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(run, 0, []));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(run, 2, [target]));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(run, 0, [target], run.Resources.Revision - 1));
        // The screen before a match takes the same plays, and money earned there is kept.
        var intro = used.LeaveShop();
        var taxed = CampaignShop.UseTarot(intro, 0, []);
        Assert.Equal(20, taxed.Money);
        Assert.Equal(20, taxed.Resources.Wallets[0]);
        Assert.Equal(0, taxed.Resources.Inventory.Tarot.Length);
        var marked = CampaignShop.UseSigil(taxed, 0, target);
        Assert.Equal(SigilId.Fish, marked.Resources.Deck[5].Sigil!.Value);
        Assert.Equal(SigilId.Bear, marked.Resources.Inventory.Sigils.Single());
        var emptied = CampaignShop.DiscardSigil(marked, 0, marked.Resources.Revision);
        Assert.Equal(0, emptied.Resources.Inventory.ItemCount);
        Assert.Throws<GameRuleException>(() => CampaignShop.DiscardSigil(emptied, 0));
        // Not during a match: the table plays them there.
        Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(marked.StartMatch(), 0, target));
        Assert.Throws<GameRuleException>(() => CampaignShop.UseTarot(intro.StartMatch(), 0, []));
    }
    public static void BuyAndUseTarot()
    {
        CampaignRun Stocked(TarotId tarot, int money = 30)
        {
            var run = new CampaignRun(Money: money, Phase: CampaignPhase.Shop);
            return run with { Shop = new(0, [new ShopOffer("t", FeatureCategory.Tarot, tarot.ToString(), 3)]) };
        }
        var start = Stocked(TarotId.Prospector);
        var target = start.Resources.Deck[5].Id;
        // A full Tarot inventory does not block using the card on the spot.
        var full = start with { Resources = start.Resources with { Inventory = start.Resources.Inventory with { Tarot = [TarotId.Gambler, TarotId.Prospector], Sigils = [SigilId.Bear, SigilId.Fish] } } };
        var used = CampaignShop.BuyAndUse(full, "t", [target]);
        Assert.Equal(27, used.Money);
        Assert.Equal(27, used.Resources.Wallets[0]);
        Assert.True(used.Resources.Deck[5].Enhancement == EnhancementId.Golden);
        Assert.Equal(2, used.Resources.Inventory.Tarot.Length);
        Assert.True(used.Shop!.Offers[0].Sold);
        Assert.True(used.Resources.Revision > full.Resources.Revision);
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(used, "t", [target]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(start, "t", []));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(start, "t", [new CardId(99)]));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(Stocked(TarotId.Prospector, 2), "t", [target]));

        var festival = CampaignShop.BuyAndUse(Stocked(TarotId.Chameleon), "t", [target]);
        Assert.True(festival.Resources.Deck[5].Enhancement == EnhancementId.Wild);

        var deal = Stocked(TarotId.DevilishDeal);
        var ids = deal.Resources.Deck.Take(3).Select(c => c.Id).ToArray();
        var dealt = CampaignShop.BuyAndUse(deal, "t", ids);
        Assert.True(dealt.Resources.Deck[0].Enhancement is not null && dealt.Resources.Deck[1].Enhancement is not null);
        Assert.True(dealt.Resources.Deck[2].Enhancement == EnhancementId.Cursed);
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(deal, "t", [ids[0], ids[0], ids[1]]));

        var lucky = CampaignShop.BuyAndUse(Stocked(TarotId.Gambler), "t", []);
        Assert.Equal(2, lucky.Resources.Deck.Count(c => c.Enhancement == EnhancementId.Lucky));
        Assert.True(lucky.Resources.RandomState != start.Resources.RandomState);

        // Doubling applies to what is left after paying, and gains at most $20.
        Assert.Equal(14, CampaignShop.BuyAndUse(Stocked(TarotId.TaxCollector, 10), "t", []).Money);
        Assert.Equal(47, CampaignShop.BuyAndUse(Stocked(TarotId.TaxCollector), "t", []).Money);

        var astronomer = Stocked(TarotId.Astronomer);
        Assert.Equal(1, CampaignShop.BuyAndUse(astronomer, "t", []).Resources.Inventory.Unlocks.Length);
        var all = astronomer with { Resources = astronomer.Resources with { Inventory = astronomer.Resources.Inventory with { Unlocks = Enum.GetValues<GumballId>() } } };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(all, "t", []));

        var joker = start with { Shop = new(0, [new ShopOffer("j", FeatureCategory.Joker, "Alpha", 4)]) };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(joker, "j", []));
    }
    public static void BuyAndUseSigil()
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Shop = new(0, [new("s", FeatureCategory.Sigil, "Fish", 3)]) };
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
        { Shop = new(0, [new("s", FeatureCategory.Sigil, "Fish", 3)]) };
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
            var target = loaded.Resources.Deck[7].Id;
            loaded.Resources.Deck[7] = loaded.Resources.Deck[7] with { Enhancement = EnhancementId.Glass, Sigil = SigilId.Bear };
            var used = CampaignShop.UseSigil(loaded, 0, target, loaded.Resources.Revision);
            Assert.Equal(27, used.Money);
            Assert.Equal(SigilId.Fish, used.Resources.Deck[7].Sigil!.Value);
            Assert.Equal(EnhancementId.Glass, used.Resources.Deck[7].Enhancement!.Value);
            Assert.Equal(0, used.Resources.Inventory.Sigils.Length);
            Assert.Equal(1, loaded.Resources.Inventory.Sigils.Length);
            Assert.Equal(loaded.Resources.Revision + 1, used.Resources.Revision);
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(used, 0, target));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded, 0, target, loaded.Resources.Revision - 1));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded, 0, new CardId(99)));
            Assert.Throws<GameRuleException>(() => CampaignShop.UseSigil(loaded.LeaveShop().StartMatch(), 0, target));
            Assert.True(CampaignStore.Save(used, path));
            Assert.Equal(SigilId.Fish, CampaignStore.Load(path)!.Resources.Deck[7].Sigil!.Value);
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
}
