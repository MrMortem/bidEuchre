using BidEuchre.Core;
internal static class CampaignEffectsTests
{
    private static GameEngine Start(EnhancementId? enhancement = null, SigilId? sigil = null, JokerId[]? jokers = null)
    {
        var s = CampaignSnapshot.Create(5);
        s = s with { Deck = s.Deck.Select(c => c with { Enhancement = enhancement, Sigil = sigil }).ToArray(), Inventory = new((jokers ?? []).Select(j => new JokerState(j)).ToArray(), [], []) };
        var g = new GameEngine(["You", "West", "Partner", "East"], 5, new MatchOptions(1000) { HandLimit = 8, NegativeThreshold = -1000 }, new CampaignContext(s));
        g.StartGame(3); g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three));
        for (var i = 1; i < 4; i++) g.ExecuteCampaignAction(i, new CampaignCommand.Pass());
        g.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
        return g;
    }
    private static void Hand(GameEngine g)
    {
        var guard = 0;
        while (g.Phase == GamePhase.Playing && guard++ < 100)
        { var seat = g.CurrentSeat!.Value; g.ExecuteCampaignAction(seat, g.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First()); }
        Assert.True(guard < 100);
    }
    public static void PointsAndMoney()
    {
        var plain = Start(); Hand(plain);
        var stone = Start(EnhancementId.Stone); Hand(stone);
        Assert.Equal(12, stone.Scores[0] - plain.Scores[0]);
        Assert.Equal(12, stone.Scores[1] - plain.Scores[1]);
        var fish = Start(sigil: SigilId.Fish); Hand(fish);
        Assert.Equal(24, fish.Scores[0] - plain.Scores[0]);
        var coin = Start(sigil: SigilId.Coin); Hand(coin);
        Assert.Equal(18, coin.ExportCampaignSnapshot().Wallets[0]);
        var frog = Start(EnhancementId.Stone, SigilId.Frog); Hand(frog);
        Assert.Equal(24, frog.Scores[0] - plain.Scores[0]);
    }
    public static void GlassAndReplay()
    {
        var g = Start(EnhancementId.Bouncy, SigilId.Frog, [JokerId.LastLaugh, JokerId.DeepDigger, JokerId.LuckyDuck]);
        Hand(g); Assert.Equal(GamePhase.HandComplete, g.Phase);
        var golden = Start(EnhancementId.Golden); Hand(golden);
        Assert.Equal(24, golden.ExportCampaignSnapshot().Wallets.Sum());
    }
    public static void ItemsOnAnyOwnTurn()
    {
        var s = CampaignSnapshot.Create(3) with { Inventory = new([], [TarotId.Glassblower, TarotId.LovePotion], []) { Sigils = [SigilId.Fish, SigilId.Coin] }, Wallets = [15, 0, 0, 0] };
        var g = new GameEngine(["You", "W", "P", "E"], 3, null, new CampaignContext(s)); g.StartGame(3);
        void Advance(Func<bool> until)
        {
            for (var step = 0; step < 200 && !until(); step++)
            {
                var seat = g.CampaignActor!.Value;
                g.ExecuteCampaignAction(seat, BidEuchre.App.CampaignBotPolicy.Choose(g.CreateCampaignView(seat), seat));
            }
            Assert.True(until());
        }
        bool Mine() => g.CampaignActor == 0;
        // A Sigil goes on a card in hand while bidding.
        Advance(() => Mine() && g.Phase == GamePhase.Bidding);
        var hand = g.CreateCampaignView(0).Players[0].VisibleCards;
        var revision = g.ExportCampaignSnapshot().Revision;
        g.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, hand[0].Id));
        Assert.Equal(SigilId.Fish, g.ExportCampaignSnapshot().Deck.Single(c => c.Id == hand[0].Id).Sigil!.Value);
        Assert.Equal(SigilId.Fish, g.CreateCampaignView(0).Players[0].VisibleCards.Single(c => c.Id == hand[0].Id).Sigil!.Value);
        Assert.Equal(SigilId.Coin, g.ExportCampaignSnapshot().Inventory.Sigils.Single());
        Assert.True(g.ExportCampaignSnapshot().Revision > revision);
        // Only a card in the human's hand, and only a slot that holds something.
        var away = s.Deck.Select(c => c.Id).First(id => hand.All(c => c.Id != id));
        Assert.Throws<GameRuleException>(() => g.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, away)));
        Assert.Throws<GameRuleException>(() => g.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(1, hand[0].Id)));
        Assert.Equal(1, g.ExportCampaignSnapshot().Inventory.Sigils.Length);
        // Not on another player's turn.
        g.ExecuteCampaignAction(0, BidEuchre.App.CampaignBotPolicy.Choose(g.CreateCampaignView(0), 0));
        Advance(() => !Mine() && g.CampaignActor is not null);
        Assert.Throws<GameRuleException>(() => g.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, hand[1].Id)));
        Assert.Throws<GameRuleException>(() => g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [hand[1].Id])));
        Assert.Equal(3, g.ExportCampaignSnapshot().Inventory.ItemCount);
        // Both kinds are still playable once cards are being played, and the turn stays the human's.
        Advance(() => Mine() && g.Phase == GamePhase.Playing);
        var playing = g.CreateCampaignView(0).Players[0].VisibleCards;
        g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [playing[0].Id]));
        Assert.Equal(EnhancementId.Glass, g.ExportCampaignSnapshot().Deck.Single(c => c.Id == playing[0].Id).Enhancement!.Value);
        g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [playing[^1].Id]));
        Assert.Equal(Suit.Hearts, g.CreateCampaignView(0).Players[0].VisibleCards.Single(c => c.Id == playing[^1].Id).Face.Suit);
        g.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, playing[0].Id));
        Assert.Equal(0, g.ExportCampaignSnapshot().Inventory.ItemCount);
        Assert.True(Mine() && g.Phase == GamePhase.Playing);
        // The hand still plays out with the changed cards.
        Advance(() => g.Phase is GamePhase.HandComplete or GamePhase.GameComplete);
    }
    public static void TarotMutation()
    {
        var s = CampaignSnapshot.Create(3) with { Inventory = new([], [TarotId.Glassblower, TarotId.TaxCollector], []), Wallets = [15, 0, 0, 0] };
        var g = new GameEngine(["You", "W", "P", "E"], 3, null, new CampaignContext(s)); g.StartGame(3);
        var id = g.CreateCampaignView(0).Players[0].VisibleCards[0].Id;
        g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [id]));
        Assert.Equal(EnhancementId.Glass, g.ExportCampaignSnapshot().Deck.Single(c => c.Id == id).Enhancement!.Value);
        g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, []));
        Assert.Equal(30, g.ExportCampaignSnapshot().Wallets[0]);
        Assert.Equal(0, g.ExportCampaignSnapshot().Inventory.Tarot.Length);
        Assert.Throws<GameRuleException>(() => g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [])));
    }
}
