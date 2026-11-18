using System.Text.Json;
using BidEuchre.Core;

internal static class PokerShowdownTests
{
    public static void RevealsAndScores()
    {
        var game = Deal([Rank.Nine, Rank.Ace, Rank.Nine, Rank.King], extras: 3);
        BidPoker(game);
        Assert.Equal(GamePhase.PokerReveal, game.Phase);
        Assert.True(game.Contract is null);
        Assert.Equal(0, game.CurrentSeat!.Value);
        Assert.True(game.Scores.SequenceEqual(new[] { 0, 4 }));
        Assert.Equal(0, game.CompletedTricks.Count);
        Assert.Equal(0, game.CurrentTrick.Count);
        Assert.True(game.TricksByTeam.All(tricks => tricks == 0));
        Assert.True(game.ExportCampaignSnapshot().Wallets.All(money => money == 0));
        foreach (int? viewer in new int?[] { null, 0, 1, 2, 3 })
        {
            var view = game.CreateCampaignView(viewer);
            Assert.True(view.Players.All(player => player.VisibleCards.Count == 6));
            var winningHand = view.PokerResult!.Winners.Single();
            Assert.Equal(1, winningHand.Seat);
            Assert.Equal("Five of a kind", winningHand.Category);
            Assert.True(winningHand.CardIds.Select(id => id.Value).SequenceEqual(new[] { 6, 7, 8, 9, 10 }));
            Assert.Equal(viewer == 0 ? 1 : 0, view.LegalActions.Count);
        }
        Assert.True(game.CreateCampaignView(0).LegalActions.Single() is CampaignCommand.CompletePoker);
        var hand = game.HandHistory.Single();
        Assert.True(hand.Contract is null && !hand.MadeContract);
        Assert.Equal(0, hand.ScoreBreakdowns[0].GumballBonuses.Count);
        Assert.Equal(0, hand.ScoreBreakdowns[1].OpponentBidBonus);
        Assert.Equal(0, hand.ScoreBreakdowns[1].CardPoints);
        Assert.Equal(0, hand.ScoreBreakdowns[1].JokerPoints);
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.High)));
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.Play(new(0))));
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, [])));
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, new(0))));
        Assert.Throws<GameRuleException>(() => game.ArrangeCampaignHand(0, [new(0), new(1), new(2), new(3), new(4), new(5)]));
        Assert.Throws<GameRuleException>(() => game.Redeal());
    }

    public static void TiesAndLosingBidder()
    {
        foreach (var (ranks, scores, seats) in new (Rank[] Ranks, int[] Scores, int[] Seats)[]
        {
            ([Rank.Ace, Rank.Nine, Rank.Ace, Rank.Nine], [7, 0], [0, 2]),
            ([Rank.Ace, Rank.Ace, Rank.Nine, Rank.Ace], [7, 4], [0, 1, 3]),
            ([Rank.Nine, Rank.Nine, Rank.Ace, Rank.Nine], [7, 0], [2]),
            ([Rank.Nine, Rank.Ace, Rank.Nine, Rank.Ace], [0, 4], [1, 3])
        })
        {
            var game = Deal(ranks, extras: 3);
            BidPoker(game);
            Assert.True(game.Scores.SequenceEqual(scores));
            var result = game.CreateCampaignView(null).PokerResult!;
            Assert.True(result.Winners.Select(winner => winner.Seat).SequenceEqual(seats));
            var breakdown = game.HandHistory.Single().ScoreBreakdowns[0];
            if (seats.Any(seat => seat % 2 == 0))
                Assert.Equal(new GumballBidBonus(0, GumballId.Poker, 3), breakdown.GumballBonuses.Single());
            else
                Assert.Equal(0, breakdown.GumballBonuses.Count);
        }
    }

    public static void CompletesMatchAfterReveal()
    {
        foreach (var options in new[] { new MatchOptions(4), new MatchOptions(100) { HandLimit = 1 } })
        {
            var game = Deal([Rank.Nine, Rank.Ace, Rank.Nine, Rank.Nine], options: options);
            BidPoker(game);
            Assert.Equal(GamePhase.PokerReveal, game.Phase);
            Assert.True(game.GameWinner is null);
            game.ExecuteCampaignAction(0, game.CreateCampaignView(0).LegalActions.Single());
            Assert.Equal(GamePhase.GameComplete, game.Phase);
            Assert.Equal(1, game.GameWinner!.Value);
            Assert.Equal(1, game.HandNumber);
            Assert.Equal(1, game.HandHistory.Count);
            Assert.True(game.ExportCampaignMatchSnapshot().IsValid());
        }
    }

    public static void ResumeAndRedeal()
    {
        var game = Deal([Rank.Nine, Rank.King, Rank.Ace, Rank.Nine]);
        BidPoker(game);
        var checkpoint = game.ExportCampaignMatchSnapshot();
        var result = checkpoint.PokerResult!;
        var best = result.Winners.Single();
        Assert.Throws<NotSupportedException>(() => ((IList<CardId>)best.CardIds)[0] = new(0));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.TeamPoints)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<PokerWinningHand>)result.Winners).Clear());
        foreach (var invalid in new[]
        {
            checkpoint with { PokerResult = null },
            checkpoint with { PokerResult = result with { TeamPoints = [5, 0] } },
            checkpoint with { PokerResult = result with { Winners = [best, best] } },
            checkpoint with { PokerResult = result with { Winners = [best with { CardIds = [new(0), .. best.CardIds.Skip(1)] }] } },
            checkpoint with { HandHistory = [] }
        }) Assert.Throws<GameRuleException>(() => GameEngine.RestoreCampaignMatch(invalid));
        var restored = GameEngine.RestoreCampaignMatch(JsonSerializer.Deserialize<CampaignMatchSnapshot>(JsonSerializer.Serialize(checkpoint))!);
        Assert.Equal(JsonSerializer.Serialize(game.CreateView(0)), JsonSerializer.Serialize(restored.CreateView(0)));
        var complete = restored.CreateCampaignView(0).LegalActions.Single();
        Assert.Throws<GameRuleException>(() => restored.ExecuteCampaignAction(2, complete));
        restored.ExecuteCampaignAction(0, complete);
        Assert.Equal(GamePhase.Bidding, restored.Phase);
        Assert.Equal(2, restored.HandNumber);
        Assert.Equal(2L, restored.DealSerial);
        Assert.Equal(0, restored.Dealer);
        Assert.True(restored.Scores.SequenceEqual(new[] { 4, 0 }));
        Assert.Equal(1, restored.HandHistory.Count);
        Assert.True(restored.CreateCampaignView(null).Players.All(p => p.VisibleCards.Count == 0));
        Assert.Throws<GameRuleException>(() => restored.ExecuteCampaignAction(0, complete));
        Assert.True(restored.ExportCampaignMatchSnapshot().IsValid());
    }

    public static void FourOfAKindUsesWinningKicker()
    {
        Card[][] faces =
        [
            [new(Suit.Clubs, Rank.Ace), new(Suit.Diamonds, Rank.Ace), new(Suit.Hearts, Rank.Ace), new(Suit.Spades, Rank.Ace), new(Suit.Clubs, Rank.Nine), new(Suit.Clubs, Rank.King)],
            [new(Suit.Clubs, Rank.Ace), new(Suit.Diamonds, Rank.Ace), new(Suit.Hearts, Rank.Ace), new(Suit.Spades, Rank.Ace), new(Suit.Clubs, Rank.Queen), new(Suit.Hearts, Rank.Jack)],
            [new(Suit.Clubs, Rank.Nine), new(Suit.Clubs, Rank.Ten), new(Suit.Diamonds, Rank.Jack), new(Suit.Clubs, Rank.Queen), new(Suit.Clubs, Rank.King), new(Suit.Clubs, Rank.Ace)],
            [new(Suit.Hearts, Rank.Nine), new(Suit.Clubs, Rank.Nine), new(Suit.Diamonds, Rank.Jack), new(Suit.Spades, Rank.Jack), new(Suit.Hearts, Rank.King), new(Suit.Hearts, Rank.Ace)]
        ];
        var game = Deal([], faces: faces);
        BidPoker(game);
        var winner = game.CreateCampaignView(0).PokerResult!.Winners.Single();
        Assert.Equal(0, winner.Seat);
        Assert.Equal("Four of a kind", winner.Category);
        Assert.True(winner.CardIds.Select(id => id.Value).SequenceEqual(new[] { 0, 1, 2, 3, 5 }));
    }

    public static void OverflowLeavesTheAuctionOpen()
    {
        var game = Deal([Rank.Ace, Rank.Ace, Rank.Nine, Rank.Nine], options: new MatchOptions(int.MaxValue));
        game.DeveloperSetScores(int.MaxValue - 2, 0);
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        game.ExecuteCampaignAction(1, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(2, new CampaignCommand.Pass());
        var before = JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot());
        Assert.Throws<GameRuleException>(() => game.ExecuteCampaignAction(3, new CampaignCommand.Pass()));
        Assert.Equal(before, JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot()));
        game.DeveloperSetScores(int.MaxValue - 4, 0);
        game.ExecuteCampaignAction(3, new CampaignCommand.Pass());
        Assert.True(game.Scores.SequenceEqual(new[] { int.MaxValue, 4 }));
    }

    public static void BiddingMoneySurvivesTheRevealCheckpoint()
    {
        var game = Deal([Rank.Ace, Rank.Nine, Rank.Nine, Rank.Nine]);
        game.DeveloperReplaceResources(resources => resources with
        {
            Wallets = [12, 0, 0, 0],
            Inventory = resources.Inventory with { Tarot = [TarotId.TaxCollector] }
        });
        game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, []));
        BidPoker(game);
        var restored = GameEngine.RestoreCampaignMatch(game.ExportCampaignMatchSnapshot());
        Assert.Equal(24, restored.CreateCampaignView(0).Money);
        var reward = restored.CreateCampaignView(0).ScoringEffects.Single();
        Assert.Equal(CampaignScoringEffect.Taxes, reward.Effect);
        Assert.Equal(12, reward.Amount);
        Assert.True(restored.Scores.SequenceEqual(new[] { 4, 0 }));
    }

    public static void LegacyPokerCheckpointResumes()
    {
        var game = Deal([Rank.Ace, Rank.Nine, Rank.Nine, Rank.Nine]);
        BidPoker(game);
        var legacy = game.ExportCampaignMatchSnapshot() with
        {
            Phase = GamePhase.ChoosingContract,
            PokerResult = null,
            HandHistory = [],
            Scores = [0, 0],
            CompletionReason = null
        };
        var migrated = GameEngine.RestoreCampaignMatch(legacy);
        Assert.Equal(GamePhase.PokerReveal, migrated.Phase);
        Assert.True(migrated.Scores.SequenceEqual(new[] { 4, 0 }));
        Assert.Equal(legacy.Resources.Revision, migrated.ExportCampaignMatchSnapshot().Resources.Revision);
        Assert.True(migrated.ExportCampaignMatchSnapshot().IsValid());

        var alreadyPlaying = GameEngine.RestoreCampaignMatch(legacy with
        {
            Phase = GamePhase.Playing,
            Contract = new(BidLevel.Four, ContractMode.Trump, Suit.Clubs),
            PokerRevealed = true,
            JokerPoints = [4, 0]
        });
        Assert.Equal(GamePhase.Playing, alreadyPlaying.Phase);
        Assert.Equal(0, alreadyPlaying.HandHistory.Count);
        Assert.True(alreadyPlaying.CreateCampaignView(null).Players.All(p => p.VisibleCards.Count == 6));
        while (alreadyPlaying.Phase == GamePhase.Playing)
        {
            var seat = alreadyPlaying.CurrentSeat!.Value;
            alreadyPlaying.ExecuteCampaignAction(seat, alreadyPlaying.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First());
        }
        Assert.Equal(6, alreadyPlaying.CompletedTricks.Count);
        var hand = alreadyPlaying.HandHistory.Single();
        Assert.True(hand.Contract is { Bid: BidLevel.Four } && hand.PokerResult is null);
        Assert.Equal(4, hand.ScoreBreakdowns[0].JokerPoints);
    }

    public static void Overcalled()
    {
        var game = Deal([Rank.Ace, Rank.Nine, Rank.Nine, Rank.Nine]);
        Assert.True(!game.CreateCampaignView(0).LegalActions.OfType<CampaignCommand.Bid>()
            .Any(command => command.Variant == GumballId.Poker && command.Level != BidLevel.Four));
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        game.ExecuteCampaignAction(1, new CampaignCommand.Bid(BidLevel.Five));
        game.ExecuteCampaignAction(2, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(3, new CampaignCommand.Pass());
        Assert.Equal(GamePhase.ChoosingContract, game.Phase);
        Assert.Equal(0, game.HandHistory.Count);
        Assert.True(game.Scores.All(score => score == 0));
        Assert.True(game.CreateCampaignView(null).Players.All(p => p.VisibleCards.Count == 0));
    }

    private static void BidPoker(GameEngine game)
    {
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        for (var seat = 1; seat < 4; seat++)
            game.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
    }

    private static GameEngine Deal(Rank[] ranks, int extras = 0, MatchOptions? options = null, Card[][]? faces = null)
    {
        var inventory = new CampaignInventory([new(JokerId.LastLaugh)], [TarotId.Astronomer], [GumballId.Poker, GumballId.HighLow])
        {
            GumballExtraCopies = extras > 0 ? new() { [GumballId.Poker] = extras, [GumballId.HighLow] = 2 } : [],
            Sigils = [SigilId.Bear]
        };
        var tokens = GameRules.CreateDeck().ToArray();
        var resources = CampaignSnapshot.Create(5) with
        {
            Inventory = inventory,
            Deck = CampaignCards.CreateDeck().Select(card => card with
            {
                Face = faces is null ? new Card(Suit.Clubs, ranks[card.Id.Value / 6]) : faces[card.Id.Value / 6][card.Id.Value % 6],
                Enhancement = EnhancementId.Golden,
                Sigil = SigilId.Fish
            }).ToArray()
        };
        return GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
            Enumerable.Range(0, 4).Select(seat => tokens.Skip(seat * 6).Take(6).ToArray()).ToArray(),
            3, [0, 0], Campaign: resources, Options: options ?? new MatchOptions(100) { OpponentSuccessfulBidBonus = 4 }));
    }
}
