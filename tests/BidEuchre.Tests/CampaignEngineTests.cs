using BidEuchre.Core;
internal static class CampaignEngineTests
{
    public static void DuplicateAndPrivacy()
    {
        var s = CampaignSnapshot.Create(42);
        s.Deck[1] = s.Deck[1] with { Face = s.Deck[0].Face };
        var game = new GameEngine(["You", "West", "Partner", "East"], 42, new MatchOptions(24) { HandLimit = 8, NegativeThreshold = -24 }, new CampaignContext(s));
        game.StartGame(3);
        var view = game.CreateCampaignView(0);
        Assert.Equal(6, view.Players[0].VisibleCards.Count);
        Assert.Equal(0, view.Players[1].VisibleCards.Count);
        Assert.Equal(6, view.Players[1].CardCount);
        Assert.Throws<GameRuleException>(() => game.PlayCard(0, s.Deck[0].Face));
        var before = game.ExportCampaignSnapshot();
        game.CreateView(0); game.CreateCampaignView(0);
        Assert.Equal(before.RandomState, game.ExportCampaignSnapshot().RandomState);
    }
    public static void ScoringEventsIdentifyPhysicalCardsAcrossResume()
    {
        var state = CampaignSnapshot.Create(73);
        for (var i = 0; i < state.Deck.Length; i++)
            state.Deck[i] = state.Deck[i] with { Enhancement = EnhancementId.Stone };
        var game = new GameEngine(["You", "West", "Partner", "East"], 73, campaign: new CampaignContext(state));
        game.StartGame(3);
        while (game.Phase == GamePhase.Bidding)
        {
            var seat = game.CampaignActor!.Value;
            var legal = game.CreateCampaignView(seat).LegalActions;
            game.ExecuteCampaignAction(seat, legal.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand
                ?? legal.OfType<CampaignCommand.Bid>().First());
        }
        var bidder = game.CampaignActor!.Value;
        game.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));

        CampaignEffectEvent PlayStone(int sequence)
        {
            var seat = game.CampaignActor!.Value;
            var play = game.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First();
            game.ExecuteCampaignAction(seat, play);
            var observation = game.CreateCampaignView(null);
            Assert.Equal(sequence, observation.EffectSequence);
            return new(sequence, CampaignScoringEffect.Stone, CampaignEffectReward.Points, 1, seat % 2, play.Id, seat, 1);
        }

        var first = PlayStone(1);
        Assert.Equal(first, game.CreateCampaignView(null).ScoringEffects.Single());
        game = GameEngine.RestoreCampaignMatch(game.ExportCampaignMatchSnapshot());
        Assert.Equal(1, game.CreateCampaignView(null).EffectSequence);
        Assert.Equal(first, game.CreateCampaignView(null).ScoringEffects.Single());
        var second = PlayStone(2);
        Assert.Equal(second, game.CreateCampaignView(null).ScoringEffects.Last());
        Assert.True(first.SourceCard != second.SourceCard);
    }

    public static void BoundedFullMatch()
    {
        var s = CampaignSnapshot.Create(11) with { Inventory = new([], [], [GumballId.Lowballer]) };
        var game = new GameEngine(["You", "West", "Partner", "East"], 11, new MatchOptions(24) { HandLimit = 8, NegativeThreshold = -24 }, new CampaignContext(s));
        game.StartGame(3);
        Assert.Equal(0, game.Scores[0]);
        Assert.Equal(GamePhase.Bidding, game.Phase);
        var guard = 0;
        while (game.Phase != GamePhase.GameComplete && guard++ < 1000)
        {
            if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); continue; }
            var v = game.CreateCampaignView(game.CurrentSeat);
            game.ExecuteCampaignAction(game.CurrentSeat!.Value, v.LegalActions[0]);
        }
        Assert.True(guard < 1000);
        Assert.True(game.GameWinner is not null);
        Assert.Equal(24, game.ExportCampaignSnapshot().Deck.Length);
    }
}
