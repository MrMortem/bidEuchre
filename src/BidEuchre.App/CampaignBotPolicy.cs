using BidEuchre.Core;
namespace BidEuchre.App;
public static class CampaignBotPolicy
{
    public static CampaignCommand Choose(CampaignGameView view,int seat)
    {
        var legal=view.LegalActions;
        if(legal.Count==0)throw new GameRuleException("No campaign action is available.");
        var hand=view.Players[seat].VisibleCards;
        if(legal.OfType<CampaignCommand.Play>().Any())
        {
            // Use visible card strength only. Following-suit and Wild choices are already masked.
            return legal.OfType<CampaignCommand.Play>().OrderBy(p=>(int)hand.Single(c=>c.Id==p.Id).Face.Rank).First();
        }
        if(legal.OfType<CampaignCommand.ChooseContract>().Any())
        {
            var bestSuit=hand.GroupBy(c=>c.Face.Suit).OrderByDescending(g=>g.Sum(c=>(int)c.Face.Rank)).First().Key;
            return legal.OfType<CampaignCommand.ChooseContract>().FirstOrDefault(c=>c.Trump==bestSuit)??legal[0];
        }
        if(legal.OfType<CampaignCommand.Exchange>().Any())return legal.OfType<CampaignCommand.Exchange>().OrderBy(p=>(int)hand.Single(c=>c.Id==p.Id).Face.Rank).First();
        return legal.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand
            ?? legal.OfType<CampaignCommand.Bid>().OrderBy(b=>(int)b.Level).FirstOrDefault() as CampaignCommand
            ?? legal[0];
    }
}
