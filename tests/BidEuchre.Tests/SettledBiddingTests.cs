using BidEuchre.Core;
using BidEuchre.App;

internal static class SettledBiddingTests
{
    private static CampaignSnapshot Resources(GumballId[]? unlocks = null, params JokerId[] jokers) =>
        CampaignSnapshot.Create(17) with { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], unlocks ?? []) };

    private static GameEngine Game(CampaignSnapshot resources, int dealer = 3)
    {
        var game = new GameEngine(["You", "West", "Partner", "East"], 7,
            new MatchOptions(1000) { NegativeThreshold = -1000 }, new CampaignContext(resources));
        game.StartGame(dealer);
        return game;
    }

    private static void PassTo(GameEngine game, int seat)
    {
        for (var turns = 0; game.CurrentSeat != seat && turns < 4; turns++)
            game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
        Assert.Equal(seat, game.CurrentSeat!.Value);
    }

    private static void FinishAuction(GameEngine game)
    {
        while (game.Phase == GamePhase.Bidding)
            game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
    }

    private static void FinishHand(GameEngine game)
    {
        var guard = 60;
        while (game.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) && guard-- > 0)
        {
            var seat = game.CampaignActor!.Value;
            var legal = game.CreateCampaignView(seat).LegalActions;
            var action = legal.OfType<CampaignCommand.ChooseContract>().FirstOrDefault(c => c.Trump == Suit.Clubs)
                ?? legal.First();
            game.ExecuteCampaignAction(seat, action);
        }
        Assert.True(guard > 0, "Hand did not finish.");
    }

    // Catches any seat bypassing the shared inventory gates, in offered and submitted actions.
    public static void AllSeatsRespectBidUnlocks()
    {
        foreach (var seat in Enumerable.Range(0, 4))
        foreach (var (unlock, bid) in new[] { (GumballId.PartnersBest, BidLevel.PartnersBest), (GumballId.Alone, BidLevel.Alone) })
        foreach (var unlocked in new[] { false, true })
        {
            var game = Game(Resources(unlocked ? [unlock] : []), (seat + 3) % 4);
            var command = new CampaignCommand.Bid(bid);
            Assert.Equal(unlocked, game.CreateCampaignView(seat).LegalActions.Contains(command));
            Assert.Equal(unlocked, game.GetLegalBids().Contains(bid));
            if (!unlocked)
            {
                var revision = game.ExportCampaignSnapshot().Revision;
                Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(seat, command));
                Assert.Equal(revision, game.ExportCampaignSnapshot().Revision);
                Assert.True(game.HighBid is null);
            }
            else
            {
                game.ExecuteCampaignAction(seat, command);
                FinishAuction(game);
                game.ExecuteCampaignAction(seat, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Clubs));
                Assert.Equal(bid == BidLevel.PartnersBest ? GamePhase.ExchangingBidderCard : GamePhase.Playing, game.Phase);
                Assert.Equal(seat, game.Bidder!.Value);
            }
        }
    }

    // Catches either public contract entry point allowing a bot to choose locked High/Low.
    public static void AllSeatsRespectContractUnlocks()
    {
        foreach (var seat in Enumerable.Range(0, 4))
        foreach (var mode in new[] { ContractMode.High, ContractMode.Low })
        foreach (var unlocked in new[] { false, true })
        {
            var game = Game(Resources(unlocked ? [GumballId.HighLow] : []), (seat + 3) % 4);
            game.ExecuteCampaignAction(seat, new CampaignCommand.Bid(BidLevel.Four));
            FinishAuction(game);
            var command = new CampaignCommand.ChooseContract(mode);
            Assert.Equal(unlocked, game.CreateCampaignView(seat).LegalActions.Contains(command));
            Assert.Equal(unlocked, game.CreateView(seat).LegalActions.ContractModes.Contains(mode));
            if (unlocked)
            {
                game.ExecuteCampaignAction(seat, command);
                Assert.Equal(mode, game.Contract!.Mode);
            }
            else
            {
                Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(seat, command));
                Assert.Throws<GameRuleException>(() => game.ChooseContract(seat, mode));
                Assert.True(game.Contract is null);
                Assert.Equal(GamePhase.ChoosingContract, game.Phase);
            }
        }
    }

    // Catches expanding human-targeted variants to bots, or omitting any seat's Safety Net path.
    public static void SafetyNetAndHumanVariantSeats()
    {
        foreach (var seat in Enumerable.Range(0, 4))
        foreach (var unlocked in new[] { false, true })
        {
            var game = Game(Resources(unlocked ? Enum.GetValues<GumballId>() : []), (seat + 3) % 4);
            var low = new CampaignCommand.Bid((BidLevel)2);
            var expected = unlocked;
            Assert.Equal(expected, game.CreateCampaignView(seat).LegalActions.Contains(low));
            if (seat != 0)
            {
                Assert.False(game.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Bid>()
                    .Any(b => b.Variant is GumballId.Chaos or GumballId.Poker or GumballId.Duelist or GumballId.Telepath));
                foreach (var (variant, bid) in new[] { (GumballId.Chaos, BidLevel.Four), (GumballId.Poker, BidLevel.Four), (GumballId.Duelist, BidLevel.Four), (GumballId.Telepath, BidLevel.Five) })
                    Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(seat, new CampaignCommand.Bid(bid, variant)));
            }
            if (expected) game.ExecuteCampaignAction(seat, low);
            else Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(seat, low));
        }
    }

    public static void OverAchieverPassesTheLeadAndScoresOvertricks()
    {
        var id = GumballId.OverAchiever;
        var bid = new CampaignCommand.Bid(BidLevel.Three, id);
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var dealer = (seat + 3) % 4;
            var locked = Game(Resources(), dealer);
            Assert.Throws<GameRuleException>(() => locked.ExecuteCampaignAction(seat, bid));

            var deck = GameRules.CreateDeck().ToArray();
            var resources = Resources([id]);
            for (var i = 0; i < resources.Deck.Length; i++)
                resources.Deck[i] = resources.Deck[i] with { Face = new(Suit.Clubs, i / 6 == seat ? Rank.Jack : Rank.Nine) };
            var game = GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
                Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), dealer, [0, 0],
                Campaign: resources, Options: new MatchOptions(1000)));
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(seat, new CampaignCommand.Bid(BidLevel.Four, id)));
            game.ExecuteCampaignAction(seat, bid);
            FinishAuction(game);
            game.ExecuteCampaignAction(seat, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Clubs));

            int leader = GameRules.NextSeat(seat);
            for (var trick = 1; trick <= 6; trick++)
            {
                Assert.Equal(leader, game.CurrentSeat!.Value);
                for (var play = 0; play < 4; play++)
                {
                    var actor = game.CurrentSeat!.Value;
                    game.ExecuteCampaignAction(actor, game.CreateCampaignView(actor).LegalActions.OfType<CampaignCommand.Play>().First());
                }
                Assert.Equal(seat, game.CompletedTricks[^1].Winner);
                if (trick < 6)
                {
                    Assert.Equal(Math.Max(0, trick - 3), game.Scores[seat % 2]);
                    game = GameEngine.RestoreCampaignMatch(game.ExportCampaignMatchSnapshot());
                }
            }
            Assert.Equal(9, game.Scores[seat % 2]);
            Assert.Equal(0, game.Scores[1 - seat % 2]);
            Assert.Equal(3, game.HandHistory.Single().ScoreBreakdowns[seat % 2].GumballEffect!.Points);
            Assert.True(game.ExportCampaignMatchSnapshot().IsValid());
        }
    }

    public static void RollerCoasterGumballRequiresHighLowAndScoresBothWays()
    {
        var id = GumballId.RollerCoaster;
        var bid = new CampaignCommand.Bid(BidLevel.Four, id);
        Assert.Throws<GameRuleException>(() => Game(Resources([id])).ExecuteCampaignAction(0, bid));
        Assert.Throws<GameRuleException>(() => Game(Resources([GumballId.HighLow])).ExecuteCampaignAction(0, bid));
        foreach (var (bidder, mode, wins, hasJoker) in new[]
        {
            (0, ContractMode.High, false, false), (0, ContractMode.High, true, false), (1, ContractMode.Low, true, true)
        })
        {
            var team = bidder % 2;
            var deck = GameRules.CreateDeck().ToArray();
            var resources = Resources([id, GumballId.HighLow], hasJoker ? [JokerId.RollerCoaster] : []);
            for (var i = 0; i < resources.Deck.Length; i++)
            {
                var strong = (i / 6 % 2 == team) == wins;
                resources.Deck[i] = resources.Deck[i] with { Face = new(Suit.Clubs, strong == (mode == ContractMode.High) ? Rank.Ace : Rank.Nine) };
            }
            var game = GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
                Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), (bidder + 3) % 4, [0, 0],
                Campaign: resources, Options: new MatchOptions(1000)));
            game.ExecuteCampaignAction(bidder, bid);
            FinishAuction(game);
            Assert.False(game.CreateCampaignView(bidder).LegalActions.OfType<CampaignCommand.ChooseContract>().Any(c => c.Mode == ContractMode.Trump));
            Assert.Throws<GameRuleException>(() => game.ChooseContract(bidder, ContractMode.Trump, Suit.Clubs));
            game.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(mode));
            while (game.CompletedTricks.Count == 0)
            {
                var actor = game.CurrentSeat!.Value;
                game.ExecuteCampaignAction(actor, game.CreateCampaignView(actor).LegalActions.OfType<CampaignCommand.Play>().First());
            }
            Assert.Equal(wins ? 1 : -1, game.Scores[team]);
            Assert.Equal(hasJoker ? -1 : 0, game.Scores[1 - team]);
            var save = Path.Combine(Path.GetTempPath(), $"roller-coaster-{Guid.NewGuid():N}.json");
            try
            {
                var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot()));
                Assert.True(CampaignStore.Save(run, save));
                game = GameEngine.RestoreCampaignMatch(CampaignStore.Load(save)!.LiveMatch!);
            }
            finally { File.Delete(save); }
            FinishHand(game);
            Assert.Equal(wins ? 12 : -10, game.Scores[team]);
            Assert.Equal(wins ? 6 : -6, game.HandHistory.Single().ScoreBreakdowns[team].GumballEffect!.Points);
            Assert.Equal(hasJoker ? -6 : 0, game.HandHistory.Single().ScoreBreakdowns[1 - team].JokerPoints);
            Assert.True(game.ExportCampaignMatchSnapshot().IsValid());
        }
    }

    // A real auction must transfer each unlocked partner bid and preserve its normal setup flow.
    public static void AlphaMatchesPartnerSpecialBids()
    {
        foreach (var (unlock, bid) in new[] { (GumballId.Lowballer, (BidLevel)2), (GumballId.HighLow, BidLevel.Six),
                     (GumballId.PartnersBest, BidLevel.PartnersBest), (GumballId.Alone, BidLevel.Alone) })
        {
            var game = Game(Resources([unlock], JokerId.Alpha), dealer: 0);
            game.ExecuteCampaignAction(1, new CampaignCommand.Pass());
            game.ExecuteCampaignAction(2, new CampaignCommand.Bid(bid));
            game.ExecuteCampaignAction(3, new CampaignCommand.Pass());
            var match = new CampaignCommand.Bid(bid);
            Assert.True(game.CreateCampaignView(0).LegalActions.Contains(match), $"Alpha cannot match {bid}.");
            game.ExecuteCampaignAction(0, match);
            Assert.Equal(0, game.Bidder!.Value);
            Assert.Equal(GamePhase.ChoosingContract, game.Phase);
            game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Clubs));
            if (bid == BidLevel.PartnersBest)
            {
                Assert.Equal(GamePhase.ExchangingBidderCard, game.Phase);
                game.ExecuteCampaignAction(0, game.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Exchange>().First());
                Assert.Equal(GamePhase.ExchangingPartnerCard, game.Phase);
                game.ExecuteCampaignAction(2, game.CreateCampaignView(2).LegalActions.OfType<CampaignCommand.Exchange>().First());
            }
            Assert.Equal(GamePhase.Playing, game.Phase);
            Assert.Equal(bid, game.Contract!.Bid);
            FinishHand(game);
            Assert.Equal(6, game.CompletedTricks.Count);
            Assert.True(game.CompletedTricks.All(t => t.Plays.Count == (bid is BidLevel.Alone or BidLevel.PartnersBest ? 3 : 4)));
        }
    }

    public static void AlphaDoesNotBypassEligibility()
    {
        foreach (var alpha in new[] { false, true })
        foreach (var leader in new[] { 1, 2, 3 })
        {
            var game = Game(Resources([GumballId.Alone], alpha ? [JokerId.Alpha] : []), dealer: 0);
            for (var seat = 1; seat < 4; seat++)
                game.ExecuteCampaignAction(seat, seat == leader ? new CampaignCommand.Bid(BidLevel.Alone) : new CampaignCommand.Pass());
            var match = new CampaignCommand.Bid(BidLevel.Alone);
            Assert.Equal(alpha && leader == 2, game.CreateCampaignView(0).LegalActions.Contains(match));
            if (!alpha || leader != 2) Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, match));
        }
        var locked = Game(Resources([], JokerId.Alpha));
        Assert.Throws<GameRuleException>(() => locked.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Alone)));
        Assert.Throws<GameRuleException>(() => locked.ExecuteCampaignAction(0, new CampaignCommand.Bid((BidLevel)2)));
    }

    public static void DuelistAllowsOnlyFourAndFive()
    {
        var game = Game(Resources([GumballId.Duelist]));
        foreach (var (bid, allowed) in new[] { (BidLevel.Three, false), (BidLevel.Four, true), (BidLevel.Five, true), (BidLevel.Six, false) })
        {
            var command = new CampaignCommand.Bid(bid, GumballId.Duelist);
            Assert.Equal(allowed, game.CreateCampaignView(0).LegalActions.Contains(command));
            if (!allowed) Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, command));
        }
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist));
        FinishAuction(game);
        Assert.True(game.CreateCampaignView(0).LegalActions.Contains(new CampaignCommand.SelectOpponent(1)));
    }

    private static GameEngine DuelFixture(BidLevel bid, bool success, bool bubble, bool alpha = false)
    {
        var deck = GameRules.CreateDeck().ToArray();
        JokerId[] jokers = [.. bubble ? new[] { JokerId.ProtectiveBubble } : [], .. alpha ? new[] { JokerId.Alpha } : []];
        var resources = Resources([GumballId.Duelist], jokers);
        var game = GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
            Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), 3, [0, 0],
            Campaign: resources, Options: new MatchOptions(1000)));
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(bid, GumballId.Duelist));
        FinishAuction(game);
        game.ExecuteCampaignAction(0, new CampaignCommand.SelectOpponent(1));
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, success ? Suit.Clubs : Suit.Diamonds));
        FinishHand(game);
        return game;
    }

    // Literal contract results catch the extra penalty and Protective Bubble ordering.
    public static void DuelistPenaltyAndProtectiveBubble()
    {
        foreach (var (bid, failure) in new[] { (BidLevel.Four, -6), (BidLevel.Five, -7) })
        {
            var failed = DuelFixture(bid, success: false, bubble: false);
            Assert.Equal(0, failed.TricksByTeam[0]);
            Assert.Equal(failure, failed.Scores[0]);
            var won = DuelFixture(bid, success: true, bubble: false);
            Assert.Equal(6, won.TricksByTeam[0]);
            Assert.Equal(6, won.Scores[0]);
            var protectedGame = DuelFixture(bid, success: false, bubble: true);
            Assert.Equal(0, protectedGame.Scores[0]);
            Assert.False(protectedGame.ExportCampaignSnapshot().Inventory.Has(JokerId.ProtectiveBubble));
        }
    }

    public static void DuelistUseLastsForTheMatch()
    {
        var game = DuelFixture(BidLevel.Four, success: true, bubble: false, alpha: true);
        game.StartNextHand();
        game.ExecuteCampaignAction(1, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(2, new CampaignCommand.Bid(BidLevel.Four));
        game.ExecuteCampaignAction(3, new CampaignCommand.Pass());
        Assert.True(game.CreateCampaignView(0).LegalActions.Contains(new CampaignCommand.Bid(BidLevel.Four)), "Alpha should still match the plain partner bid.");
        Assert.False(game.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Bid>().Any(b => b.Variant == GumballId.Duelist));
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist)));
        game.Redeal();
        PassTo(game, 0);
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Five, GumballId.Duelist)));
        var newMatch = Game(game.ExportCampaignSnapshot());
        Assert.True(newMatch.CreateCampaignView(0).LegalActions.Contains(new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist)));

        // Winning the auction consumes the use even before the opponent choice is answered.
        newMatch.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist));
        FinishAuction(newMatch);
        Assert.True(newMatch.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.SelectOpponent>().Any());
        newMatch.Redeal();
        Assert.False(newMatch.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Bid>().Any(b => b.Variant == GumballId.Duelist));
    }

    public static void OvercalledDuelistDoesNotConsumeUse()
    {
        var game = Game(Resources([GumballId.Duelist]));
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(1, new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist)));
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Duelist));
        game.ExecuteCampaignAction(1, new CampaignCommand.Bid(BidLevel.Five));
        FinishAuction(game);
        Assert.Equal(1, game.Bidder!.Value);
        Assert.True(game.CreateCampaignView(1).LegalActions.OfType<CampaignCommand.ChooseContract>().Any());
        FinishHand(game);
        game.StartNextHand();
        PassTo(game, 0);
        var command = new CampaignCommand.Bid(BidLevel.Five, GumballId.Duelist);
        Assert.True(game.CreateCampaignView(0).LegalActions.Contains(command));
        game.ExecuteCampaignAction(0, command);
        Assert.Equal(GamePhase.ChoosingContract, game.Phase);
        Assert.True(game.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.SelectOpponent>().Any());
    }

    public static void ForcedDealerVowAndFreePlayRemainLegal()
    {
        foreach (var dealer in Enumerable.Range(0, 4))
        {
            var game = Game(Resources(), dealer);
            for (var turn = 0; turn < 3; turn++) game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
            Assert.Equal(dealer, game.CurrentSeat!.Value);
            Assert.False(game.CanPass);
            Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(dealer, new CampaignCommand.Pass()));
            game.ExecuteCampaignAction(dealer, new CampaignCommand.Bid(BidLevel.Three));
            Assert.Equal(GamePhase.ChoosingContract, game.Phase);

            game = Game(Resources([], JokerId.VowofSilence), dealer);
            while (game.Phase == GamePhase.Bidding)
            {
                var seat = game.CurrentSeat!.Value;
                Assert.True(seat != 0);
                game.ExecuteCampaignAction(seat, game.CanPass ? new CampaignCommand.Pass() : new CampaignCommand.Bid(BidLevel.Three));
            }
            Assert.True(game.Bidder != 0);
            Assert.True(game.CreateCampaignView(game.Bidder).LegalActions.OfType<CampaignCommand.ChooseContract>().Any());
        }
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var game = new GameEngine(["You", "West", "Partner", "East"]);
            game.StartGame((seat + 3) % 4);
            Assert.Contains(BidLevel.PartnersBest, game.GetLegalBids());
            Assert.Contains(BidLevel.Alone, game.GetLegalBids());
            game.PlaceBid(seat, BidLevel.Four);
            while (game.Phase == GamePhase.Bidding) game.PlaceBid(game.CurrentSeat!.Value, null);
            Assert.Contains(ContractMode.High, game.CreateView(seat).LegalActions.ContractModes);
            Assert.Contains(ContractMode.Low, game.CreateView(seat).LegalActions.ContractModes);
            game.ChooseContract(seat, ContractMode.High);
            Assert.Equal(ContractMode.High, game.Contract!.Mode);
        }
    }
}
