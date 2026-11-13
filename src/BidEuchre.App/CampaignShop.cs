using BidEuchre.Core;
namespace BidEuchre.App;

public static class CampaignShop
{
    public static CampaignRun Open(CampaignRun run)
    {
        if(run.Phase is not (CampaignPhase.Shop or CampaignPhase.Victory))throw new GameRuleException("Shop is only available between matches.");
        if(run.Shop?.MatchJustFinished==run.Match-1)return run;
        var random=new CampaignRandom(run.Resources.RandomState);
        var inventory=run.Resources.Inventory;
        var offers=new List<ShopOffer>();
        void Roll(FeatureCategory category,int number)
        {
            var options=FeatureCatalog.All.Where(f=>f.Category==category)
                .Where(f=>category!=FeatureCategory.Joker || !inventory.Jokers.Any(j=>j.Id.ToString()==f.Key))
                .Where(f=>category!=FeatureCategory.Planet || !inventory.Unlocks.Any(p=>p.ToString()==f.Key)).ToList();
            for(var i=0;i<number;i++)
            {
                if(options.Count==0)continue;
                var choice=options[random.NextInt(options.Count)];options.Remove(choice);
                offers.Add(new($"{run.Match-1}:{offers.Count}",category,choice.Key,choice.Price));
            }
        }
        Roll(FeatureCategory.Joker,2);Roll(FeatureCategory.Planet,1);Roll(FeatureCategory.Tarot,1);Roll(FeatureCategory.Sigil,1);
        return run with {Shop=new(run.Match-1,offers.ToArray()),Resources=run.Resources with {RandomState=random.State,Revision=run.Resources.Revision+1,Wallets=new[]{run.Money,run.Resources.Wallets[1],run.Resources.Wallets[2],run.Resources.Wallets[3]}}};
    }
    public static CampaignRun Buy(CampaignRun run,string offerId,CardId? sigilTarget=null)
    {
        if(run.Phase!=CampaignPhase.Shop||run.Shop is null)throw new GameRuleException("The shop is closed.");
        var offer=run.Shop.Offers.SingleOrDefault(o=>o.Id==offerId);
        if(offer is null||offer.Sold)throw new GameRuleException("That item is sold out.");
        if(run.Money<offer.Price)throw new GameRuleException("Not enough money.");
        var resources=run.Resources.Copy();var inventory=resources.Inventory;
        switch(offer.Category)
        {
            case FeatureCategory.Joker:
                var joker=Enum.Parse<JokerId>(offer.FeatureId);
                if(inventory.Jokers.Length>=5||inventory.Has(joker))throw new GameRuleException("Five Joker slots; no duplicates.");
                inventory=inventory with {Jokers=[..inventory.Jokers,new(joker)]};break;
            case FeatureCategory.Planet:
                var planet=Enum.Parse<PlanetId>(offer.FeatureId);
                if(inventory.Unlocks.Contains(planet))throw new GameRuleException("Already unlocked.");
                inventory=inventory with {Unlocks=[..inventory.Unlocks,planet]};break;
            case FeatureCategory.Tarot:
                if(inventory.Tarot.Length>=2)throw new GameRuleException("Tarot inventory full. Discard a card first.");
                inventory=inventory with {Tarot=[..inventory.Tarot,Enum.Parse<TarotId>(offer.FeatureId)]};break;
            case FeatureCategory.Sigil:
                if(sigilTarget is null||!resources.Deck.Any(c=>c.Id==sigilTarget))throw new GameRuleException("Choose a card from the shared deck.");
                resources=resources with {Deck=resources.Deck.Select(c=>c.Id==sigilTarget?c with {Sigil=Enum.Parse<SigilId>(offer.FeatureId)}:c).ToArray()};break;
            default:throw new GameRuleException("This item cannot be purchased.");
        }
        resources.Wallets[0]=run.Money-offer.Price;
        return run with {Money=resources.Wallets[0],Resources=resources with {Inventory=inventory,Revision=resources.Revision+1},Shop=run.Shop with {Offers=run.Shop.Offers.Select(o=>o.Id==offerId?o with {Sold=true}:o).ToArray()}};
    }
    public static CampaignRun SellJoker(CampaignRun run,JokerId id)
    {
        if(run.Phase!=CampaignPhase.Shop||!run.Resources.Inventory.Has(id))throw new GameRuleException("That Joker cannot be sold now.");
        var resources=run.Resources.Copy();resources.Wallets[0]=run.Money+2;
        return run with {Money=run.Money+2,Resources=resources with {Revision=resources.Revision+1,Inventory=resources.Inventory with {Jokers=resources.Inventory.Jokers.Where(j=>j.Id!=id).ToArray()}}};
    }
    public static CampaignRun DiscardTarot(CampaignRun run,int slot)
    {
        if(run.Phase!=CampaignPhase.Shop||slot<0||slot>=run.Resources.Inventory.Tarot.Length)throw new GameRuleException("No Tarot in that slot.");
        return run with {Resources=run.Resources with {Revision=run.Resources.Revision+1,Inventory=run.Resources.Inventory with {Tarot=run.Resources.Inventory.Tarot.Where((_,i)=>i!=slot).ToArray()}}};
    }
}
