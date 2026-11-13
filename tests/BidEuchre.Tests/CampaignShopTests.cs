using BidEuchre.App;
using BidEuchre.Core;
internal static class CampaignShopTests
{
    public static void Purchases()
    {
        var run=new CampaignRun(Money:30,Phase:CampaignPhase.Shop);
        run=CampaignShop.Open(run);
        Assert.Equal(5,run.Shop!.Offers.Length);
        Assert.Equal(2,run.Shop.Offers.Count(o=>o.Category==FeatureCategory.Joker));
        var offer=run.Shop.Offers.Single(o=>o.Category==FeatureCategory.Sigil);
        var id=run.Resources.Deck[0].Id;
        var bought=CampaignShop.Buy(run,offer.Id,id);
        Assert.Equal(27,bought.Money);
        Assert.True(bought.Resources.Deck[0].Sigil is not null);
        Assert.Throws<GameRuleException>(()=>CampaignShop.Buy(bought,offer.Id,id));
        Assert.Equal(run.Shop.Offers[0],CampaignShop.Open(run).Shop!.Offers[0]);
        var planet=run.Shop.Offers.Single(o=>o.Category==FeatureCategory.Planet);
        var unlocked=CampaignShop.Buy(run,planet.Id);
        Assert.Equal(1,unlocked.Resources.Inventory.Unlocks.Length);
        var joker=run.Shop.Offers.First(o=>o.Category==FeatureCategory.Joker);
        var owned=CampaignShop.Buy(run,joker.Id);
        Assert.Equal(26,owned.Money);
        var sold=CampaignShop.SellJoker(owned,owned.Resources.Inventory.Jokers[0].Id);
        Assert.Equal(28,sold.Money);
        var full=run with {Resources=run.Resources with {Inventory=run.Resources.Inventory with {Tarot=[TarotId.Gambler,TarotId.Beggar]}}};
        Assert.Throws<GameRuleException>(()=>CampaignShop.Buy(full,run.Shop.Offers.Single(o=>o.Category==FeatureCategory.Tarot).Id));
    }
    public static void Poker()
    {
        var five=PokerEvaluator.Best([new(Suit.Clubs,Rank.Ace),new(Suit.Clubs,Rank.Ace),new(Suit.Clubs,Rank.Ace),new(Suit.Clubs,Rank.Ace),new(Suit.Clubs,Rank.Ace),new(Suit.Clubs,Rank.Nine)]);
        var straight=PokerEvaluator.Best([new(Suit.Hearts,Rank.Ten),new(Suit.Hearts,Rank.Jack),new(Suit.Hearts,Rank.Queen),new(Suit.Hearts,Rank.King),new(Suit.Hearts,Rank.Ace),new(Suit.Clubs,Rank.Nine)]);
        Assert.True(five.CompareTo(straight)>0);
    }
}
