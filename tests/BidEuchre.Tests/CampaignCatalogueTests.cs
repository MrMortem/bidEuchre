using BidEuchre.Core;
internal static class CampaignCatalogueTests
{
    private static GameEngine Game(CampaignSnapshot s, int dealer = 3)
    { var g = new GameEngine(["You", "W", "P", "E"], 7, new MatchOptions(1000) { NegativeThreshold = -1000, HandLimit = 8 }, new CampaignContext(s)); g.StartGame(dealer); return g; }
    private static void Step(GameEngine g)
    {
        var seat = g.CampaignActor!.Value;
        var legal = g.CreateCampaignView(seat).LegalActions;
        var action = legal.OfType<CampaignCommand.Bid>().OrderBy(b => b.Level).FirstOrDefault() as CampaignCommand
            ?? legal.First();
        if (legal.OfType<CampaignCommand.Pass>().Any()) action = new CampaignCommand.Pass();
        g.ExecuteCampaignAction(seat, action);
    }
    public static void EveryJokerAndSigilCompletes()
    {
        foreach (var joker in Enum.GetValues<JokerId>())
        {
            var s = CampaignSnapshot.Create(12) with { Inventory = new([new(joker)], [], Enum.GetValues<GumballId>()) };
            var g = Game(s); var guard = 0;
            while (g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && guard++ < 80) Step(g);
            Assert.True(guard < 80, $"Joker {joker} stalled");
        }
        foreach (var sigil in Enum.GetValues<SigilId>())
        {
            var s = CampaignSnapshot.Create(12);
            s = s with { Deck = s.Deck.Select(c => c with { Sigil = sigil }).ToArray() };
            var g = Game(s); var guard = 0;
            while (g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && guard++ < 100) Step(g);
            Assert.True(guard < 100, $"Sigil {sigil} stalled");
        }
    }
    public static void SpecialContracts()
    {
        foreach (var variant in new[] { GumballId.Chaos, GumballId.Poker, GumballId.Duelist, GumballId.Telepath })
        {
            var s = CampaignSnapshot.Create(17) with { Inventory = new([], [], [variant]) }; var g = Game(s);
            var bid = variant == GumballId.Telepath ? BidLevel.Five : BidLevel.Four;
            g.ExecuteCampaignAction(0, new CampaignCommand.Bid(bid, variant));
            for (var seat = 1; seat < 4; seat++) g.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
            var guard = 0;
            while (g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && guard++ < 80) Step(g);
            Assert.True(guard < 80, $"Gumball {variant} stalled");
            Assert.Equal(6, g.CompletedTricks.Count);
            Assert.Equal(variant == GumballId.Duelist ? 2 : 4, g.CompletedTricks[0].Plays.Count);
        }
    }
    private static GameEngine AllEagle()
    {
        var s = CampaignSnapshot.Create(12);
        return Game(s with { Deck = s.Deck.Select(c => c with { Sigil = SigilId.Eagle }).ToArray() });
    }
    // An Eagle card comes back only to the team that lost the trick, never after the last trick.
    public static void EagleReturnsToTheLosingTeamOnly()
    {
        var g = AllEagle(); var guard = 0;
        while (g.CompletedTricks.Count == 0 && guard++ < 40) Step(g);
        var winner = g.CompletedTricks[0].Winner;
        var counts = g.CreateCampaignView(null).Players.Select(p => p.CardCount).ToArray();
        for (var seat = 0; seat < 4; seat++)
            Assert.Equal(seat % 2 == winner % 2 ? 5 : 6, counts[seat]);
    }
    public static void EagleLeavesNoCardsAfterTheHand()
    {
        var g = AllEagle(); var guard = 0;
        while (g.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && guard++ < 100) Step(g);
        Assert.Equal(6, g.CompletedTricks.Count);
        Assert.Equal(0, g.CreateCampaignView(null).Players.Sum(p => p.CardCount));
        Assert.Equal(0, g.CreateView(0).Players.Sum(p => p.CardCount));
    }
    // Chaos rolls its own trump, so the bidder is never asked for one and Déjà Vu sees the rolled suit.
    public static void ChaosSkipsTheContractPrompt()
    {
        var s = CampaignSnapshot.Create(17) with { Inventory = new([], [], [GumballId.Chaos]) }; var g = Game(s);
        g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Chaos));
        for (var seat = 1; seat < 4; seat++) g.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        Assert.Equal(GamePhase.Playing, g.Phase);
        var view = g.CreateView(0);
        Assert.Equal(ContractMode.Trump, view.Contract!.Mode);
        Assert.True(view.Contract.Trump is not null);
        Assert.False(g.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.ChooseContract>().Any());
        Assert.Equal(new ContractKey(ContractMode.Trump, view.Contract.Trump), g.ExportCampaignSnapshot().PreviousContract);
    }
    // The seat shown as "to act" is the one that owes a pending choice, not the next bidder.
    public static void ViewNamesThePendingActor()
    {
        var s = CampaignSnapshot.Create(17) with { Inventory = new([new(JokerId.OneUp)], [], []) }; var g = Game(s, dealer: 0);
        Assert.Equal(1, g.CurrentSeat);
        Assert.Equal(0, g.CampaignActor);
        Assert.Equal(0, g.CreateView(0).CurrentSeat);
        g.ExecuteCampaignAction(0, g.CreateCampaignView(0).LegalActions.First());
        Assert.Equal(1, g.CreateView(0).CurrentSeat);

        s = CampaignSnapshot.Create(17) with { Inventory = new([], [], [GumballId.Telepath]) }; g = Game(s);
        g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Five, GumballId.Telepath));
        for (var seat = 1; seat < 4; seat++) g.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        Assert.Equal(2, g.CampaignActor);
        Assert.Equal(2, g.CreateView(0).CurrentSeat);
    }
    // High and Low are the human's to choose only once the High/Low gumball has been pulled.
    public static void HighLowGumball()
    {
        foreach (var unlocked in new[] { false, true })
        {
            var s = CampaignSnapshot.Create(17) with { Inventory = new([], [], unlocked ? [GumballId.HighLow] : []) }; var g = Game(s);
            g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four));
            for (var seat = 1; seat < 4; seat++) g.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
            Assert.Equal(GamePhase.ChoosingContract, g.Phase);
            var modes = g.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.ChooseContract>().Select(c => c.Mode).Distinct().ToArray();
            Assert.Equal(unlocked ? 3 : 1, modes.Length);
            Assert.True(modes.Contains(ContractMode.Trump));
            if (unlocked) g.ChooseContract(0, ContractMode.Low);
            else Assert.Throws<GameRuleException>(() => g.ChooseContract(0, ContractMode.High));
        }
        // The other seats are never locked.
        var bots = Game(CampaignSnapshot.Create(17));
        bots.ExecuteCampaignAction(0, new CampaignCommand.Pass());
        bots.ExecuteCampaignAction(1, new CampaignCommand.Bid(BidLevel.Four));
        bots.ExecuteCampaignAction(2, new CampaignCommand.Pass()); bots.ExecuteCampaignAction(3, new CampaignCommand.Pass());
        Assert.Equal(3, bots.CreateCampaignView(1).LegalActions.OfType<CampaignCommand.ChooseContract>().Select(c => c.Mode).Distinct().Count());
    }
    public static void EnhancedDealsPreservePhysicalCards()
    {
        foreach (var enhancement in Enum.GetValues<EnhancementId>())
        {
            var s = CampaignSnapshot.Create(42);
            s = s with { Deck = s.Deck.Select(c => c with { Enhancement = enhancement }).ToArray() };
            var g = Game(s);
            var dealt = Enumerable.Range(0, 4).SelectMany(seat =>
            {
                var hand = g.CreateCampaignView(seat).Players[seat].VisibleCards;
                Assert.Equal(6, hand.Count);
                Assert.True(hand.All(c => c.Enhancement == enhancement));
                return hand;
            }).ToArray();
            Assert.Equal(24, dealt.Select(c => c.Id).Distinct().Count());
            Assert.True(dealt.Select(c => c.Id).ToHashSet().SetEquals(s.Deck.Select(c => c.Id)));
        }
    }
}
