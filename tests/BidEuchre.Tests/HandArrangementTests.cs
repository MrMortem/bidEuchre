using BidEuchre.Core;

internal static class HandArrangementTests
{
    private static GameEngine Fixture(CampaignSnapshot resources)
    {
        // Seat 0 holds the six clubs, ids 0-5 (9, 10, J, Q, K, A), in deal order.
        var deck = GameRules.CreateDeck().ToArray();
        return GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(),
            3, [0, 0], Campaign: resources, Options: new MatchOptions(1000) { HandLimit = 8, NegativeThreshold = -1000 }));
    }
    private static CampaignSnapshot Resources(params JokerId[] jokers) => CampaignSnapshot.Create(17) with { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], []) };
    private static CardId[] Hand(GameEngine game) => game.CreateCampaignView(0).Players[0].VisibleCards.Select(c => c.Id).ToArray();
    private static CardId[] Ids(params int[] values) => values.Select(v => new CardId(v)).ToArray();

    public static void ArrangedOrderIsKept()
    {
        var game = Fixture(Resources());
        var revision = game.ExportCampaignSnapshot().Revision;
        game.ArrangeCampaignHand(0, Ids(5, 3, 1, 0, 2, 4));
        Assert.Equal("5,3,1,0,2,4", string.Join(',', Hand(game).Select(id => id.Value)));
        Assert.Equal(revision, game.ExportCampaignSnapshot().Revision, "Arranging a hand is not a table action.");
        Assert.Equal(GamePhase.Bidding, game.Phase); Assert.Equal(0, game.CurrentSeat!.Value);
    }

    public static void ArrangementMustBeTheSameCards()
    {
        var game = Fixture(Resources());
        foreach (var bad in new[] { Ids(0, 1, 2, 3, 4), Ids(0, 1, 2, 3, 4, 4), Ids(0, 1, 2, 3, 4, 6), Ids(0, 1, 2, 3, 4, 5, 6) })
            Assert.Throws<GameRuleException>(() => game.ArrangeCampaignHand(0, bad));
        Assert.Throws<GameRuleException>(() => game.ArrangeCampaignHand(0, null!));
        Assert.Equal("0,1,2,3,4,5", string.Join(',', Hand(game).Select(id => id.Value)), "A rejected arrangement leaves the hand as it was.");
        var free = new GameEngine(["You", "W", "P", "E"], 1); free.StartGame(0);
        Assert.Throws<GameRuleException>(() => free.ArrangeCampaignHand(0, Ids(0, 1, 2, 3, 4, 5)));
    }

    // Issue #7: Sticky Fingers copies onto the card the player has on the left, not the first-dealt card.
    public static void StickyFingersCopiesOntoLeftmostArrangedCard()
    {
        var game = Fixture(Resources(JokerId.StickyFingers));
        game.ArrangeCampaignHand(0, Ids(5, 0, 1, 2, 3, 4));
        while (game.Phase == GamePhase.Bidding)
        { var seat = game.CurrentSeat!.Value; game.ExecuteCampaignAction(seat, seat == 0 ? new CampaignCommand.Bid(BidLevel.Three) : new CampaignCommand.Pass()); }
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Clubs));
        // The right bower (J♣, id 2) wins the first trick for the human.
        game.ExecuteCampaignAction(0, new CampaignCommand.Play(new(2)));
        for (var i = 0; i < 3; i++)
        { var seat = game.CurrentSeat!.Value; game.ExecuteCampaignAction(seat, game.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First()); }
        Assert.Equal(0, game.CompletedTricks[0].Winner);
        var hand = game.CreateCampaignView(0).Players[0].VisibleCards;
        Assert.Equal(new CardId(5), hand[0].Id, "Copying a card does not move it.");
        Assert.Equal(new Card(Suit.Clubs, Rank.Jack), hand[0].Face, "The leftmost card copies the winning card.");
        Assert.Equal(new Card(Suit.Clubs, Rank.Nine), hand.Single(c => c.Id == new CardId(0)).Face, "The first-dealt card is not the target.");
    }

    public static void GamblerIgnoresHandOrder()
    {
        string Enhanced(bool reversed)
        {
            var game = Fixture(Resources() with { Inventory = new([], [TarotId.Gambler], []) });
            if (reversed) game.ArrangeCampaignHand(0, Ids(5, 4, 3, 2, 1, 0));
            game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, []));
            return string.Join(',', game.ExportCampaignSnapshot().Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id.Value));
        }
        Assert.Equal(Enhanced(false), Enhanced(true), "Arranging the hand must not change which cards Gambler picks.");
    }
}
