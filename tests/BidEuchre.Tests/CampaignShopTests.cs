using BidEuchre.App;
using BidEuchre.Core;
internal static class CampaignShopTests
{
    public static void Purchases()
    {
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
        var planet = run.Shop.Offers.Single(o => o.Category == FeatureCategory.Planet);
        var unlocked = CampaignShop.Buy(run, planet.Id);
        Assert.Equal(1, unlocked.Resources.Inventory.Unlocks.Length);
        var joker = run.Shop.Offers.First(o => o.Category == FeatureCategory.Joker);
        var owned = CampaignShop.Buy(run, joker.Id);
        Assert.Equal(26, owned.Money);
        var sold = CampaignShop.SellJoker(owned, owned.Resources.Inventory.Jokers[0].Id);
        Assert.Equal(28, sold.Money);
        var full = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Tarot = [TarotId.Gambler, TarotId.Beggar] } } };
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(full, run.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id));
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
        var full = start with { Resources = start.Resources with { Inventory = start.Resources.Inventory with { Tarot = [TarotId.Gambler, TarotId.Beggar] } } };
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

        var hearts = CampaignShop.BuyAndUse(Stocked(TarotId.LovePotion), "t", [start.Resources.Deck.First(c => c.Face.Suit == Suit.Clubs).Id]);
        Assert.Equal(7, hearts.Resources.Deck.Count(c => c.Face.Suit == Suit.Hearts));

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
        var all = astronomer with { Resources = astronomer.Resources with { Inventory = astronomer.Resources.Inventory with { Unlocks = Enum.GetValues<PlanetId>() } } };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(all, "t", []));

        // Six cards must stay dealable to the human.
        var beggar = Stocked(TarotId.Beggar);
        var stinky = beggar with { Resources = beggar.Resources with { Deck = beggar.Resources.Deck.Select((c, i) => i < 18 ? c with { Enhancement = EnhancementId.Stinky } : c).ToArray() } };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(stinky, "t", [stinky.Resources.Deck[20].Id]));

        var joker = start with { Shop = new(0, [new ShopOffer("j", FeatureCategory.Joker, "Alpha", 4)]) };
        Assert.Throws<GameRuleException>(() => CampaignShop.BuyAndUse(joker, "j", []));
    }
    public static void Poker()
    {
        var five = PokerEvaluator.Best([new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Ace), new(Suit.Clubs, Rank.Nine)]);
        var straight = PokerEvaluator.Best([new(Suit.Hearts, Rank.Ten), new(Suit.Hearts, Rank.Jack), new(Suit.Hearts, Rank.Queen), new(Suit.Hearts, Rank.King), new(Suit.Hearts, Rank.Ace), new(Suit.Clubs, Rank.Nine)]);
        Assert.True(five.CompareTo(straight) > 0);
    }
}
