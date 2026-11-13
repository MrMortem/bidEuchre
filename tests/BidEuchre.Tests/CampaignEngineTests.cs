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
    public static void PanicAndFullMatch()
    {
        var s = CampaignSnapshot.Create(11) with { Inventory = new([], [], [PlanetId.PanicButton, PlanetId.Lowballer]) };
        var game = new GameEngine(["You", "West", "Partner", "East"], 11, new MatchOptions(24) { HandLimit = 8, NegativeThreshold = -24 }, new CampaignContext(s));
        game.StartGame(3);
        game.ExecuteCampaignAction(0, new CampaignCommand.PanicButton());
        Assert.Equal(-8, game.Scores[0]);
        Assert.Equal(GamePhase.HandComplete, game.Phase);
        game.StartNextHand();
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
