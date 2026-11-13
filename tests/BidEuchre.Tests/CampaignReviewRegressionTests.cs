using BidEuchre.Core;
using BidEuchre.App;
internal static class CampaignReviewRegressionTests
{
    private static CampaignSnapshot Resources(ulong seed = 17, params JokerId[] jokers) => CampaignSnapshot.Create(seed) with { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], []) };
    private static GameEngine Fixture(CampaignSnapshot resources, int dealer = 3, CardOverlay[]? overlays = null)
    {
        var deck = GameRules.CreateDeck().ToArray();
        return GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), dealer, [0, 0], Campaign: resources, Options: new MatchOptions(1000) { HandLimit = 8, NegativeThreshold = -1000 }, CampaignOverlays: overlays));
    }
    private static void Auction(GameEngine game, int bidder = 0, ContractMode mode = ContractMode.Trump, Suit? trump = Suit.Spades)
    {
        while (game.Phase == GamePhase.Bidding)
        { var seat = game.CurrentSeat!.Value; game.ExecuteCampaignAction(seat, seat == bidder ? new CampaignCommand.Bid(mode == ContractMode.High ? BidLevel.Four : BidLevel.Three) : new CampaignCommand.Pass()); }
        game.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(mode, trump));
    }
    private static void Play(GameEngine game, CardId? preferred = null)
    {
        var seat = game.CurrentSeat!.Value; var choices = game.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>();
        game.ExecuteCampaignAction(seat, preferred is { } id ? choices.First(c => c.Id == id) : choices.First());
    }
    public static void ReplayBonusesAreAdditive()
    {
        foreach (var frog in new[] { false, true })
        {
            var r = Resources(17, JokerId.LuckyDuck, frog ? JokerId.Clubhouse : JokerId.DeepDigger);
            r.Deck[5] = r.Deck[5] with { Face = new(Suit.Spades, Rank.Ace), Enhancement = EnhancementId.Lucky, Sigil = frog ? SigilId.Frog : null };
            var g = Fixture(r); Auction(g); Play(g, new(5)); for (var i = 0; i < 3; i++) Play(g);
            Assert.Equal(0, g.CompletedTricks[0].Winner);
            Assert.Equal(0xb54cda58fbbee88fUL, g.ExportCampaignSnapshot().RandomState, "Lucky Duck plus one independent replay must make six RNG draws, not eight.");
        }
        var b = Resources(17, JokerId.LuckyDuck); b.Deck[5] = b.Deck[5] with { Face = new(Suit.Spades, Rank.Ace), Enhancement = EnhancementId.Lucky }; b.Deck[6] = b.Deck[6] with { Enhancement = EnhancementId.Bouncy };
        var bounce = Fixture(b); Auction(bounce); Play(bounce, new(5)); Play(bounce, new(6));
        Assert.Equal(0xb54cda58fbbee88fUL, bounce.ExportCampaignSnapshot().RandomState);
    }
    public static void FinalTrickTiming()
    {
        var r = Resources(17, JokerId.LastLaugh, JokerId.DeepDigger);
        r = r with { Deck = r.Deck.Select((c, i) => i < 6 ? c with { Face = c.Face with { Suit = Suit.Spades }, Sigil = SigilId.Coin } : i >= 18 ? c with { Face = c.Face with { Suit = Suit.Clubs } } : c).ToArray() };
        var g = Fixture(r); Auction(g); var order = new[] { 2, 5, 4, 3, 1, 0 };
        while (g.CompletedTricks.Count < 5) Play(g, g.CurrentSeat == 0 ? new CardId(order[g.CompletedTricks.Count]) : null);
        Assert.Equal(30, g.ExportCampaignSnapshot().Wallets[0], "Fifth-trick Deep Digger must not receive Last Laugh.");
        while (g.Phase == GamePhase.Playing) Play(g, g.CurrentSeat == 0 ? new CardId(order[g.CompletedTricks.Count]) : null);
        Assert.Equal(42, g.ExportCampaignSnapshot().Wallets[0], "Last Laugh doubles both ordinary and Deep Digger sixth-trick rewards once.");
    }
    public static void ChameleonFallback()
    {
        var r = Resources(); r.Deck[0] = r.Deck[0] with { Enhancement = EnhancementId.Wild };
        for (var i = 6; i < 12; i++) r.Deck[i] = r.Deck[i] with { Face = r.Deck[i].Face with { Suit = Suit.Clubs } };
        r.Deck[6] = r.Deck[6] with { Sigil = SigilId.Chameleon };
        var g = Fixture(r, dealer: 0); Auction(g, 1, ContractMode.High, null); Play(g, new(6)); Play(g); Play(g);
        var choices = g.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Play>().Where(p => p.Id == new CardId(0)).ToArray();
        Assert.Equal(1, choices.Length); Assert.Equal(Suit.Clubs, choices[0].EffectiveSuit!.Value);
    }
    public static void StinkyCopiesStayDealable()
    {
        var r = Resources(17, JokerId.StickyFingers); var non = new[] { 0, 1, 3, 4, 5, 6 };
        r = r with { Deck = r.Deck.Select(c => non.Contains(c.Id.Value) ? c : c with { Enhancement = EnhancementId.Stinky }).ToArray() };
        var g = Fixture(r); Auction(g, trump: Suit.Clubs); Play(g, new(2)); for (var i = 0; i < 3; i++) Play(g);
        Assert.True(g.CreateCampaignView(0).Players[0].VisibleCards.Single(c => c.Id == new CardId(0)).Enhancement != EnhancementId.Stinky, "Sticky Fingers must not leave fewer than six drawable cards.");
        while (g.Phase == GamePhase.Playing) Play(g);
        g.StartNextHand(); Assert.True(g.CreateCampaignView(0).Players[0].VisibleCards.All(c => c.Enhancement != EnhancementId.Stinky));
    }
    public static void DevilishDealUsesUnderlyingState()
    {
        var r = Resources(7); r = r with { Deck = r.Deck.Select((c, i) => i >= 6 ? c with { Enhancement = EnhancementId.Stinky } : c).ToArray(), Inventory = new([], [TarotId.DevilishDeal], []) };
        var overlays = new[] { new CardOverlay(new(6), r.Deck[6].Face, EnhancementId.Golden, null), new CardOverlay(new(0), r.Deck[0].Face, EnhancementId.Stinky, null) };
        var g = Fixture(r, overlays: overlays);
        g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [new(0), new(1), new(2)]));
        Assert.Equal(0, g.ExportCampaignSnapshot().Inventory.Tarot.Length);
        Assert.True(g.ExportCampaignSnapshot().Deck.Count(c => c.Enhancement == EnhancementId.Stinky) <= 18);
        var effectiveFull = Resources(7) with { Deck = CampaignSnapshot.Create(7).Deck.Select((c, i) => i >= 7 ? c with { Enhancement = EnhancementId.Stinky } : c).ToArray(), Inventory = new([], [TarotId.DevilishDeal], []) };
        var effectiveGame = Fixture(effectiveFull, overlays: [new(new(0), effectiveFull.Deck[0].Face, EnhancementId.Stinky, null)]);
        effectiveGame.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [new(1), new(2), new(3)]));
        Assert.Equal(0, effectiveGame.ExportCampaignSnapshot().Inventory.Tarot.Length);
    }
    public static void StaleDiscardDoesNotShiftTarget()
    {
        var run = new CampaignRun(Phase: CampaignPhase.Shop) { Resources = Resources() with { Inventory = new([], [TarotId.Gambler, TarotId.TaxCollector], []) } };
        var revision = run.Resources.Revision; var changed = CampaignShop.DiscardTarot(run, 0, revision);
        Assert.Throws<GameRuleException>(() => CampaignShop.DiscardTarot(changed, 0, revision));
        Assert.Equal(TarotId.TaxCollector, changed.Resources.Inventory.Tarot[0]);
    }
    public static void NonhumanTarotIsTracked()
    {
        var r = Resources(); r = r with { Deck = r.Deck.Select(c => c with { Sigil = SigilId.Vulture }).ToArray() }; var g = Fixture(r); Auction(g, 1, trump: Suit.Diamonds);
        for (var i = 0; i < 4; i++) Play(g);
        Assert.Equal(1, g.CompletedTricks[0].Winner);
        Assert.Equal(2, g.ExportCampaignSnapshot().GeneratedTarot[1].Length);
    }
}
