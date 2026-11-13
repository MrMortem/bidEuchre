using BidEuchre.Core;
internal static class CampaignCatalogueTests
{
    private static GameEngine Game(CampaignSnapshot s,int dealer=3)
    {var g=new GameEngine(["You","W","P","E"],7,new MatchOptions(1000){NegativeThreshold=-1000,HandLimit=8},new CampaignContext(s));g.StartGame(dealer);return g;}
    private static void Step(GameEngine g)
    {
        var seat=g.CampaignActor!.Value;
        var legal=g.CreateCampaignView(seat).LegalActions;
        var action=legal.OfType<CampaignCommand.Bid>().OrderBy(b=>b.Level).FirstOrDefault() as CampaignCommand
            ??legal.First();
        if(legal.OfType<CampaignCommand.Pass>().Any())action=new CampaignCommand.Pass();
        g.ExecuteCampaignAction(seat,action);
    }
    public static void EveryJokerAndSigilCompletes()
    {
        foreach(var joker in Enum.GetValues<JokerId>())
        {
            var s=CampaignSnapshot.Create(12) with {Inventory=new([new(joker)],[],Enum.GetValues<PlanetId>())};
            var g=Game(s);var guard=0;
            while(g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete)&&guard++<80)Step(g);
            Assert.True(guard<80,$"Joker {joker} stalled");
        }
        foreach(var sigil in Enum.GetValues<SigilId>())
        {
            var s=CampaignSnapshot.Create(12);
            s=s with {Deck=s.Deck.Select(c=>c with {Sigil=sigil}).ToArray()};
            var g=Game(s);var guard=0;
            while(g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete)&&guard++<100)Step(g);
            Assert.True(guard<100,$"Sigil {sigil} stalled");
        }
    }
    public static void SpecialContracts()
    {
        foreach(var variant in new[]{PlanetId.Chaos,PlanetId.Poker,PlanetId.Duelist,PlanetId.Telepath})
        {
            var s=CampaignSnapshot.Create(17) with {Inventory=new([],[],[variant])};var g=Game(s);
            var bid=variant==PlanetId.Telepath?BidLevel.Five:BidLevel.Four;
            g.ExecuteCampaignAction(0,new CampaignCommand.Bid(bid,variant));
            for(var seat=1;seat<4;seat++)g.ExecuteCampaignAction(seat,new CampaignCommand.Pass());
            var guard=0;
            while(g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete)&&guard++<80)Step(g);
            Assert.True(guard<80,$"Planet {variant} stalled");
            Assert.Equal(6,g.CompletedTricks.Count);
            Assert.Equal(variant==PlanetId.Duelist?2:4,g.CompletedTricks[0].Plays.Count);
        }
    }
    public static void StinkyAndWild()
    {
        var s=CampaignSnapshot.Create(42);
        s=s with {Deck=s.Deck.Select((c,i)=>c with {Enhancement=i<18?EnhancementId.Stinky:EnhancementId.Wild}).ToArray()};
        var g=Game(s);var own=g.CreateCampaignView(0).Players[0].VisibleCards;
        Assert.Equal(6,own.Count);Assert.True(own.All(c=>c.Enhancement==EnhancementId.Wild));
        var invalid=s with {Deck=s.Deck.Select((c,i)=>i==18?c with {Enhancement=EnhancementId.Stinky}:c).ToArray()};
        Assert.Throws<GameRuleException>(()=>new CampaignContext(invalid));
    }
}
