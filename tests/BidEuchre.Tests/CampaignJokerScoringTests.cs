using BidEuchre.Core;
internal static class CampaignJokerScoringTests
{
    private static GameEngine Sweep(JokerId[] jokers, BidLevel bid = BidLevel.Three, EnhancementId? enhancement = null, int bidder = 0, SigilId? sigil = null, Suit trump = Suit.Clubs)
    {
        var deck = GameRules.CreateDeck().ToArray();
        var resources = CampaignSnapshot.Create(1) with { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], []) };
        resources = resources with { Deck = resources.Deck.Select(c => c with { Enhancement = enhancement, Sigil = sigil }).ToArray() };
        var g = GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), 3, [0, 0], Campaign: resources, Options: new MatchOptions(6)));
        for (var seat = 0; seat < 4; seat++) g.ExecuteCampaignAction(seat, seat == bidder ? new CampaignCommand.Bid(bid) : new CampaignCommand.Pass());
        g.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(ContractMode.Trump, trump));
        var order = new[] { Rank.Jack, Rank.Ace, Rank.King, Rank.Queen, Rank.Ten, Rank.Nine };
        while (g.Phase == GamePhase.Playing)
        {
            var seat = g.CurrentSeat!.Value; var view = g.CreateCampaignView(seat); var legal = view.LegalActions.OfType<CampaignCommand.Play>().ToArray();
            var action = seat == 0 ? legal.First(p => view.Players[0].VisibleCards.Single(c => c.Id == p.Id).Face.Rank == order[g.CompletedTricks.Count]) : legal[0];
            g.ExecuteCampaignAction(seat, action);
        }
        if (trump == Suit.Clubs) Assert.Equal(6, g.TricksByTeam[0]); return g;
    }
    public static void LiteralScoring()
    {
        Assert.Equal(6, Sweep([]).Scores[0]);
        Assert.Equal(8, Sweep([JokerId.ThreesCompany]).Scores[0]);
        Assert.Equal(9, Sweep([JokerId.HighRoller], BidLevel.Five).Scores[0]);
        Assert.Equal(11, Sweep([JokerId.SnakeEyes]).Scores[0]);
        Assert.Equal(12, Sweep([JokerId.TwinFlame]).Scores[0]);
        Assert.Equal(12, Sweep([JokerId.Clubhouse]).Scores[0]);
        Assert.Equal(10, Sweep([JokerId.TheUndertaker]).Scores[0]);
        Assert.Equal(11, Sweep([JokerId.Contrarian]).Scores[0]); // Left Bower shares Clubs in trick one.
        Assert.Equal(12, Sweep([JokerId.VowofSilence], bidder: 2).Scores[0]);
        Assert.Equal(54, Sweep([JokerId.CureAll], enhancement: EnhancementId.Cursed).Scores[0]);
        Assert.Equal(7, Sweep([JokerId.LastLaugh]).Scores[0]);
        Assert.Equal(3, Sweep([JokerId.PennyPincher]).ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(3, Sweep([JokerId.Bully]).ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(5, Sweep([JokerId.PeoplePleaser]).ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(18, Sweep([], enhancement: EnhancementId.Stone).Scores[0]);
        Assert.Equal(9, Sweep([], enhancement: EnhancementId.Stone, bidder: 1).Scores[1]);
        Assert.Equal(6, Sweep([], BidLevel.Six, EnhancementId.Stone, trump: Suit.Diamonds).Scores[0]);
        Assert.Equal(12, Sweep([JokerId.ProtectiveBubble], BidLevel.Six, EnhancementId.Stone, trump: Suit.Diamonds).Scores[0]);
        Assert.Equal(30, Sweep([], enhancement: EnhancementId.Stone, sigil: SigilId.Frog).Scores[0]);
        Assert.Equal(390, Sweep([], enhancement: EnhancementId.Glass, sigil: SigilId.Fish).Scores[0]);
        Assert.Equal(102, Sweep([], sigil: SigilId.Peacock).Scores[0]);
        Assert.Equal(28, Sweep([JokerId.LastLaugh], enhancement: EnhancementId.Golden).ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(24, Sweep([], enhancement: EnhancementId.Golden).ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(120, Sweep([], enhancement: EnhancementId.Silver).ExportCampaignSnapshot().Wallets[1]);
        Assert.Equal(-18, Sweep([], enhancement: EnhancementId.Cursed).Scores[0]);
    }
}
