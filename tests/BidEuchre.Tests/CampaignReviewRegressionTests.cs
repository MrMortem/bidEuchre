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
    public static void StickyCopiesPreserveUnderlyingCards()
    {
        var r = Resources(17, JokerId.StickyFingers);
        r.Deck[2] = r.Deck[2] with { Enhancement = EnhancementId.Golden, Sigil = SigilId.Coin };
        var g = Fixture(r); Auction(g, trump: Suit.Clubs); Play(g, new(2)); for (var i = 0; i < 3; i++) Play(g);
        var copied = g.CreateCampaignView(0).Players[0].VisibleCards.Single(c => c.Id == new CardId(0));
        Assert.Equal(r.Deck[2].Face, copied.Face);
        Assert.Equal(EnhancementId.Golden, copied.Enhancement!.Value);
        Assert.Equal(SigilId.Coin, copied.Sigil!.Value);
        Assert.Equal(r.Deck[0], g.ExportCampaignSnapshot().Deck[0], "A copied face must preserve the underlying physical card.");
        while (g.Phase == GamePhase.Playing) Play(g);
        g.StartNextHand();
        var dealt = Enumerable.Range(0, 4).SelectMany(seat => g.CreateCampaignView(seat).Players[seat].VisibleCards).ToArray();
        Assert.Equal(24, dealt.Select(c => c.Id).Distinct().Count());
        Assert.Equal(copied, dealt.Single(c => c.Id == copied.Id), "Sticky Fingers copies last through the match.");
    }
    public static void DevilishDealUpdatesUnderlyingAndCopiedCards()
    {
        var observed = new HashSet<EnhancementId>();
        for (ulong seed = 1; seed <= 128; seed++)
        {
            var r = Resources(seed) with { Inventory = new([], [TarotId.DevilishDeal], []) };
            r.Deck[0] = r.Deck[0] with { Enhancement = EnhancementId.Golden, Sigil = SigilId.Fish };
            var overlays = new[]
            {
                new CardOverlay(new(0), new(Suit.Diamonds, Rank.Ace), EnhancementId.Silver, SigilId.Coin),
                new CardOverlay(new(2), new(Suit.Hearts, Rank.Ten), EnhancementId.Wild, SigilId.Frog)
            };
            var g = Fixture(r, overlays: overlays);
            g.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [new(0), new(1), new(2)]));
            var result = g.ExportCampaignSnapshot();
            Assert.Equal(0, result.Inventory.Tarot.Length);
            foreach (var index in new[] { 0, 1 })
            {
                var enhancement = result.Deck[index].Enhancement!.Value;
                Assert.True(Enum.IsDefined(enhancement), "Devilish Deal must choose an active enhancement.");
                observed.Add(enhancement);
            }
            Assert.Equal(EnhancementId.Cursed, result.Deck[2].Enhancement!.Value);
            Assert.Equal(r.Deck[0].Face, result.Deck[0].Face);
            Assert.Equal(SigilId.Fish, result.Deck[0].Sigil!.Value);
            foreach (var overlay in overlays)
            {
                var visible = g.CreateCampaignView(0).Players[0].VisibleCards.Single(c => c.Id == overlay.Id);
                Assert.Equal(overlay.Face, visible.Face);
                Assert.Equal(overlay.Sigil!.Value, visible.Sigil!.Value);
                Assert.Equal(result.Deck[overlay.Id.Value].Enhancement!.Value, visible.Enhancement!.Value);
            }
            Assert.True(result.Deck.Skip(3).SequenceEqual(r.Deck.Skip(3)), "Devilish Deal must only modify its three targets.");
        }
        Assert.True(observed.SetEquals(Enum.GetValues<EnhancementId>()), "Devilish Deal must be able to generate every active enhancement.");
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
    public static void GeneratedTarotUsesActiveCatalogue()
    {
        foreach (var winner in new[] { 0, 1 })
        {
            var observed = new HashSet<TarotId>();
            for (ulong seed = 1; seed <= 128; seed++)
            {
                var r = Resources(seed);
                r = r with { Deck = r.Deck.Select(c => c with { Sigil = SigilId.Vulture }).ToArray() };
                var g = Fixture(r); Auction(g, winner, trump: winner == 0 ? Suit.Clubs : Suit.Diamonds);
                for (var play = 0; play < 4; play++) Play(g, g.CurrentSeat == winner ? new CardId(winner * 6 + 2) : null);
                Assert.Equal(winner, g.CompletedTricks[0].Winner);
                var snapshot = g.ExportCampaignSnapshot();
                var generated = winner == 0 ? snapshot.Inventory.Tarot : snapshot.GeneratedTarot[winner];
                Assert.Equal(winner == 0 ? CampaignInventory.ItemSlots : 2, generated.Length);
                foreach (var tarot in generated)
                {
                    Assert.True(Enum.IsDefined(tarot), "Vulture must generate an active Tarot.");
                    Assert.True(FeatureCatalog.All.Any(feature => feature.Category == FeatureCategory.Tarot && feature.Key == tarot.ToString()));
                    observed.Add(tarot);
                }
            }
            Assert.True(observed.SetEquals(Enum.GetValues<TarotId>()), "Vulture must be able to generate every active Tarot for humans and bots.");
        }
    }
}
