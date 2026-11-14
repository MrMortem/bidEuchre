using BidEuchre.App;
using BidEuchre.Core;

internal static class SettledAnteTests
{
    private static readonly string[] Names = ["You", "West", "Partner", "East"];

    // Real started matches catch incorrect ante tables, boss handicaps, and option plumbing.
    public static void EveryMatchUsesApprovedRules()
    {
        foreach (var (ante, threshold, bonus) in new[] { (1, 12, 0), (2, 16, 1), (3, 24, 2), (4, 32, 3), (5, 42, 4), (6, 44, 0), (7, 48, 0) })
        foreach (var slot in new[] { 1, 2, 3 })
        {
            var run = new CampaignRun(Match: (ante - 1) * 3 + slot);
            var options = run.NextMatch();
            Assert.Equal(threshold, run.Threshold);
            Assert.Equal(threshold, options.TargetScore);
            Assert.Equal(-threshold, options.NegativeThreshold!.Value);
            Assert.Equal(8, options.HandLimit!.Value);
            Assert.Equal(bonus, options.OpponentSuccessfulBidBonus);
            var game = new GameEngine(Names, 7, options, new CampaignContext(run.Resources));
            game.StartGame(3);
            Assert.Equal(threshold, game.CreateCampaignView(0).Threshold);
            Assert.Equal(0, game.Scores[0]);
            Assert.Equal(0, game.Scores[1]);
        }
    }

    private static GameEngine Begin(int bidder, int sweeper, MatchOptions options, BidLevel bid = BidLevel.Three,
        ContractMode mode = ContractMode.Trump, int[]? scores = null, int hand = 1, int opponentStones = 0,
        GumballId? variant = null, ulong seed = 17, EnhancementId? enhancement = null, bool weakNoTrump = false)
    {
        var deck = GameRules.CreateDeck().ToArray();
        var hands = Enumerable.Range(0, 4).Select(s => deck.Skip(((s - sweeper + 4) % 4) * 6).Take(6).ToArray()).ToArray();
        if (weakNoTrump)
        {
            var ranked = (mode == ContractMode.Low ? deck.OrderByDescending(c => c.Rank) : deck.OrderBy(c => c.Rank)).ToArray();
            var seats = new[] { bidder, (bidder + 2) % 4, (bidder + 1) % 4, (bidder + 3) % 4 };
            for (var i = 0; i < 4; i++) hands[seats[i]] = ranked.Skip(i * 6).Take(6).ToArray();
        }
        var resources = CampaignSnapshot.Create(seed) with { Inventory = new([], [], Enum.GetValues<GumballId>()) };
        var stoneFaces = hands[1].Concat(hands[3]).Take(opponentStones).ToHashSet();
        resources = resources with { Deck = resources.Deck.Select(c => c with { Enhancement = stoneFaces.Contains(c.Face) ? EnhancementId.Stone : enhancement }).ToArray() };
        var game = GameEngine.CreateFixture(new FixtureStart(Names, hands, (bidder + 3) % 4, scores ?? [0, 0], hand,
            Campaign: resources, Options: options));
        game.ExecuteCampaignAction(bidder, new CampaignCommand.Bid(bid, variant));
        while (game.Phase == GamePhase.Bidding)
            game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
        if (game.Phase == GamePhase.ChoosingContract)
            game.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(mode, mode == ContractMode.Trump ? Suit.Clubs : null));
        if (game.Phase == GamePhase.ExchangingBidderCard)
        {
            var sent = game.CreateCampaignView(bidder).LegalActions.OfType<CampaignCommand.Exchange>().First();
            game.ExecuteCampaignAction(bidder, sent);
            game.ExecuteCampaignAction((bidder + 2) % 4, sent); // The same physical card can legally be returned.
        }
        Assert.Equal(GamePhase.Playing, game.Phase);
        return game;
    }

    private static void PlayOne(GameEngine game)
    {
        var seat = game.CurrentSeat!.Value;
        var view = game.CreateCampaignView(seat);
        var plays = view.LegalActions.OfType<CampaignCommand.Play>();
        var action = plays.OrderByDescending(p =>
        {
            var face = view.Players[seat].VisibleCards.Single(c => c.Id == p.Id).Face;
            return face.Rank == Rank.Jack && GameRules.EffectiveSuit(face, game.Contract!) == game.Contract!.Trump
                ? face.Suit == game.Contract.Trump ? 100 : 99 : (int)face.Rank;
        }).First();
        game.ExecuteCampaignAction(seat, action);
    }

    private static void Finish(GameEngine game)
    {
        var guard = 60;
        while (game.Phase == GamePhase.Playing && guard-- > 0) PlayOne(game);
        Assert.True(guard > 0);
        Assert.Equal(6, game.CompletedTricks.Count);
    }

    // Wrong team, per-trick payment, missing bonus, premature settlement, and cash payment all fail here.
    public static void SuccessfulOpponentBonusIsOnceAndPointsOnly()
    {
        foreach (var (match, bonus, opponentScore) in new[] { (1, 0, 6), (4, 1, 7), (7, 2, 8), (10, 3, 9), (13, 4, 10), (16, 0, 6) })
        foreach (var bidder in new[] { 0, 1, 2, 3 })
        {
            var run = new CampaignRun(Match: match);
            var game = Begin(bidder, bidder, run.NextMatch());
            while (game.Phase == GamePhase.Playing)
            {
                Assert.Equal(0, game.Scores[0]);
                Assert.Equal(0, game.Scores[1]);
                PlayOne(game);
            }
            var expected = bidder % 2 == 1 ? opponentScore : 6;
            Assert.Equal(6, game.TricksByTeam[bidder % 2]);
            Assert.Equal(expected, game.Scores[bidder % 2]);
            Assert.Equal(0, game.Scores[1 - bidder % 2]);
            Assert.True(game.ExportCampaignSnapshot().Wallets.All(m => m == 0));
            Assert.Equal(expected, bidder % 2 == 0 ? game.HandHistory.Single().TeamZeroDelta : game.HandHistory.Single().TeamOneDelta);
            if (bonus > 0 && bidder % 2 == 1)
                Assert.True(game.CreateCampaignView(0).Events.Any(e => e.Contains($"opponent bid bonus +{bonus}")));
        }
    }

    public static void FailedBidsAndDefenderTricksReceiveNoBonus()
    {
        foreach (var bidder in new[] { 0, 1, 2, 3 })
        foreach (var (bid, failure) in new[] { (BidLevel.Three, -3), ((BidLevel)2, -2), (BidLevel.PartnersBest, -12), (BidLevel.Alone, -24) })
        {
            var game = Begin(bidder, (bidder + 1) % 4, new CampaignRun(Match: 13).NextMatch(), bid);
            Finish(game);
            Assert.Equal(0, game.TricksByTeam[bidder % 2]);
            Assert.Equal(failure, game.Scores[bidder % 2]);
            Assert.Equal(6, game.Scores[1 - bidder % 2]);
            Assert.True(game.ExportCampaignSnapshot().Wallets.All(m => m == 0));
        }
    }

    public static void FailedHighLowContractsReceiveNoBonus()
    {
        foreach (var bidder in new[] { 1, 3 })
        foreach (var mode in new[] { ContractMode.High, ContractMode.Low })
        {
            var game = Begin(bidder, bidder, new CampaignRun(Match: 13).NextMatch(), BidLevel.Six, mode, weakNoTrump: true);
            Finish(game);
            Assert.True(game.TricksByTeam[1] < 6);
            Assert.Equal(-6, game.Scores[1]);
        }
    }

    public static void BonusCanReachTheAnteTargetAndDoesNotAffectFreePlay()
    {
        var game = Begin(1, 1, new CampaignRun(Match: 4).NextMatch(), scores: [0, 9]);
        Finish(game);
        Assert.Equal(16, game.Scores[1]);
        Assert.Equal(1, game.GameWinner!.Value);

        var deck = GameRules.CreateDeck().ToArray();
        var hands = Enumerable.Range(0, 4).Select(s => deck.Skip(((s + 3) % 4) * 6).Take(6).ToArray()).ToArray();
        game = GameEngine.CreateFixture(new FixtureStart(Names, hands, 0, [0, 0], Options: new MatchOptions(42) { OpponentSuccessfulBidBonus = 4 }));
        game.PlaceBid(1, BidLevel.Three);
        while (game.Phase == GamePhase.Bidding) game.PlaceBid(game.CurrentSeat!.Value, null);
        game.ChooseContract(1, ContractMode.Trump, Suit.Clubs);
        while (game.Phase == GamePhase.Playing)
        {
            var seat = game.CurrentSeat!.Value;
            var legal = game.CreateView(seat).LegalActions.Cards;
            var card = legal.OrderByDescending(c => c.Rank == Rank.Jack && GameRules.EffectiveSuit(c, game.Contract!) == Suit.Clubs ? c.Suit == Suit.Clubs ? 100 : 99 : (int)c.Rank).First();
            game.PlayCard(seat, card);
        }
        Assert.Equal(6, game.Scores[1]);
        Assert.Equal(0, game.Scores[0]);
    }

    public static void UnlockedSpecialContractsReceiveTheSameFixedBonus()
    {
        foreach (var bidder in new[] { 0, 1, 2, 3 })
        foreach (var (bid, mode, playerScore, opponentScore) in new[] {
            ((BidLevel)2, ContractMode.Trump, 6, 10),
            (BidLevel.Six, ContractMode.High, 6, 10),
            (BidLevel.Six, ContractMode.Low, 6, 10),
            (BidLevel.PartnersBest, ContractMode.Trump, 12, 16),
            (BidLevel.Alone, ContractMode.Trump, 24, 28) })
        {
            var game = Begin(bidder, bidder, new CampaignRun(Match: 13).NextMatch(), bid, mode);
            Finish(game);
            Assert.Equal(6, game.TricksByTeam[bidder % 2]);
            Assert.Equal(bidder % 2 == 0 ? playerScore : opponentScore, game.Scores[bidder % 2]);
            Assert.Equal(0, game.Scores[1 - bidder % 2]);
        }
    }

    // A made contract may still have a negative total due to card effects; tricks decide eligibility.
    public static void SuccessfulBonusSurvivesNegativeCardEffects()
    {
        var game = Begin(1, 1, new CampaignRun(Match: 13).NextMatch(), enhancement: EnhancementId.Cursed);
        Finish(game);
        Assert.Equal(6, game.TricksByTeam[1]);
        Assert.Equal(-14, game.Scores[1]); // Six contract points minus 24 Cursed points plus the fixed four.
        Assert.Equal(0, game.Scores[0]);
    }

    // Chaos is human-only: there is no legal opponent Chaos contract to synthesize.
    // Human contracts and opponent defender tricks must never receive an ante bonus, even at ×2.
    public static void ChaosDoesNotMultiplyOrLeakOpponentBonuses()
    {
        var successes = 0;
        var failures = 0;
        for (ulong seed = 1; seed <= 16; seed++)
        foreach (var sweeper in new[] { 0, 1 })
        {
            var withBonus = Begin(0, sweeper, new CampaignRun(Match: 13).NextMatch(), BidLevel.Four, variant: GumballId.Chaos, seed: seed);
            var baseline = Begin(0, sweeper, new MatchOptions(42) { NegativeThreshold = -42, HandLimit = 8 }, BidLevel.Four, variant: GumballId.Chaos, seed: seed);
            Finish(withBonus);
            Finish(baseline);
            Assert.Equal(baseline.Scores[0], withBonus.Scores[0]);
            Assert.Equal(baseline.Scores[1], withBonus.Scores[1]);
            Assert.True(withBonus.CreateCampaignView(0).Events.Any(e => e.Contains("hand ×2; opponent bid bonus +0 after hand multiplier")));
            if (withBonus.TricksByTeam[0] >= 4) successes++; else failures++;
        }
        Assert.True(successes > 0, "Chaos scenarios must include a made human contract.");
        Assert.True(failures > 0, "Chaos scenarios must include a failed human contract.");
    }

    public static void ThresholdsUseCompletedHandsAndExactBoundaries()
    {
        foreach (var (match, threshold) in new[] { (1, 12), (4, 16), (7, 24), (10, 32), (13, 42) })
        {
            var options = new CampaignRun(Match: match).NextMatch();
            foreach (var (start, winner) in new[] { (threshold - 7, -1), (threshold - 6, 0) })
            {
                var game = Begin(0, 0, options, scores: [start, 0]);
                while (game.Phase == GamePhase.Playing)
                {
                    Assert.True(game.GameWinner is null);
                    PlayOne(game);
                }
                Assert.Equal(winner, game.GameWinner ?? -1);
                Assert.Equal(start + 6, game.Scores[0]);
            }
            foreach (var (start, winner) in new[] { (-threshold + 4, -1), (-threshold + 3, 0) })
            {
                var game = Begin(1, 0, options, scores: [0, start]);
                Finish(game);
                Assert.Equal(start - 3, game.Scores[1]);
                Assert.Equal(winner, game.GameWinner ?? -1);
            }
        }
    }

    public static void EighthHandAndRepeatedTiesRequireFullOvertimeHands()
    {
        var options = new CampaignRun(Match: 13).NextMatch();
        var seventh = Begin(0, 0, options, hand: 7);
        Finish(seventh);
        Assert.Equal(GamePhase.HandComplete, seventh.Phase);
        var eighth = Begin(0, 0, options, hand: 8);
        Finish(eighth);
        Assert.Equal(GamePhase.GameComplete, eighth.Phase);
        Assert.Equal(0, eighth.GameWinner!.Value);
        foreach (var hand in new[] { 8, 9 })
        {
            var tied = Begin(0, 0, options, hand: hand, opponentStones: 6);
            Finish(tied);
            Assert.Equal(6, tied.Scores[0]);
            Assert.Equal(6, tied.Scores[1]);
            Assert.Equal(GamePhase.HandComplete, tied.Phase);
            Assert.True(tied.GameWinner is null);
            tied.StartNextHand();
            Assert.Equal(hand + 1, tied.HandNumber);
            Assert.Equal(GamePhase.Bidding, tied.Phase);
        }
        var broken = Begin(0, 0, options, hand: 9, opponentStones: 7);
        while (broken.Phase == GamePhase.Playing)
        {
            Assert.True(broken.GameWinner is null);
            PlayOne(broken);
        }
        Assert.Equal(6, broken.Scores[0]);
        Assert.Equal(7, broken.Scores[1]);
        Assert.Equal(1, broken.GameWinner!.Value);
    }
}
