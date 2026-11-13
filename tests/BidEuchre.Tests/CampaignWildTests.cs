using BidEuchre.Core;
internal static class CampaignWildTests
{
    public static void JackFollowsChosenSuit()
    {
        var deck = GameRules.CreateDeck().ToArray(); var resources = CampaignSnapshot.Create(1);
        var id = resources.Deck.Single(c => c.Face.Code == "JC").Id;
        resources.Deck[id.Value] = resources.Deck[id.Value] with { Enhancement = EnhancementId.Wild };
        var g = GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), 0, [0, 0], Campaign: resources));
        g.ExecuteCampaignAction(1, new CampaignCommand.Bid(BidLevel.Three)); g.ExecuteCampaignAction(2, new CampaignCommand.Pass()); g.ExecuteCampaignAction(3, new CampaignCommand.Pass()); g.ExecuteCampaignAction(0, new CampaignCommand.Pass());
        g.ExecuteCampaignAction(1, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
        for (var seat = 1; seat <= 3; seat++) g.ExecuteCampaignAction(seat, g.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First());
        g.ExecuteCampaignAction(0, new CampaignCommand.Play(id, Suit.Diamonds));
        Assert.Equal(Suit.Diamonds, g.CreateCampaignView(0).LastTrick.Single(p => p.Seat == 0).EffectiveSuit);
        Assert.Equal(2, g.CompletedTricks[0].Winner);
    }
}
