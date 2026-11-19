using BidEuchre.Core;
using BidEuchre.App;
using System.Text.Json.Nodes;

internal static class SettledEffectTests
{
    // Appending is deliberate: saved identities 0..30 must keep their meaning.
    private const JokerId DoubleDown = (JokerId)31;
    private static CampaignSnapshot Resources(ulong seed = 17, params JokerId[] jokers) => CampaignSnapshot.Create(seed) with
    { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], [GumballId.HighLow]) };
    private static GameEngine Fixture(CampaignSnapshot resources, int dealer = 3, int target = 1000)
    {
        var deck = GameRules.CreateDeck().ToArray();
        return GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(), dealer, [0, 0], Campaign: resources,
            Options: new MatchOptions(target) { HandLimit = 8, NegativeThreshold = -1000 }));
    }
    private static void Auction(GameEngine g, int bidder = 0, ContractMode mode = ContractMode.Trump, Suit? trump = Suit.Clubs)
    {
        while (g.Phase == GamePhase.Bidding)
        { var seat = g.CurrentSeat!.Value; g.ExecuteCampaignAction(seat, seat == bidder ? new CampaignCommand.Bid(BidLevel.Four) : new CampaignCommand.Pass()); }
        g.ExecuteCampaignAction(bidder, new CampaignCommand.ChooseContract(mode, mode == ContractMode.Trump ? trump : null));
    }
    private static void Play(GameEngine g, CardId? id = null)
    {
        var seat = g.CurrentSeat!.Value; var legal = g.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>();
        g.ExecuteCampaignAction(seat, id is { } wanted ? legal.First(p => p.Id == wanted) : legal.First());
    }
    private static void Finish(GameEngine g)
    { var guard = 0; while (g.Phase == GamePhase.Playing && guard++ < 100) Play(g); Assert.True(guard < 100); }
    private static int Effects(GameEngine g, string source) => g.CreateCampaignView(0).Events.Count(e => e.Contains(": " + source + " "));

    public static void LivePointsSurviveResumeAndSettleOnce()
    {
        var resources = Resources(17, JokerId.Clubhouse);
        for (var seat = 0; seat < 4; seat++)
            resources.Deck[seat * 6] = resources.Deck[seat * 6] with { Face = new(Suit.Clubs, Rank.Nine) };
        resources.Deck[0] = resources.Deck[0] with { Face = new(Suit.Clubs, Rank.Jack), Sigil = SigilId.Fish };
        resources.Deck[6] = resources.Deck[6] with { Enhancement = EnhancementId.Glass };
        var game = Fixture(resources, target: 2);
        Auction(game);
        var motion = new TableMotionTracker();
        motion.Update(game.CreateView(0), "campaign");
        Play(game, new(0));
        Assert.Equal(2, game.Scores[0], "Fish scores on play, before the trick ends.");
        Assert.Equal(0, game.Scores[1]);
        Assert.True(motion.Update(game.CreateView(0), "campaign").ScoreTeams.SetEquals([0]));
        Assert.Equal(GamePhase.Playing, game.Phase, "Reaching the target mid-hand does not end the game.");

        var resumed = GameEngine.RestoreCampaignMatch(game.ExportCampaignMatchSnapshot());
        Assert.Equal(2, resumed.CreateView(0).Scores[0]);
        foreach (var engine in new[] { game, resumed })
        {
            for (var id = 6; id <= 18; id += 6) Play(engine, new(id));
            Assert.Equal(5, engine.Scores[0], "Glass doubles Fish and Clubhouse scores when the trick is won.");
            Finish(engine);
            var hand = engine.HandHistory.Single();
            Assert.Equal(hand.ScoreBreakdowns[0].FinalDelta, engine.Scores[0]);
            Assert.Equal(hand.ScoreBreakdowns[1].FinalDelta, engine.Scores[1]);
        }
        Assert.True(game.Scores.SequenceEqual(resumed.Scores));
    }

    public static void DuckTrialsAndReplay()
    {
        // SplitMix64 seeds 1,2,3 produce first residues modulo3: 2,1,0.
        foreach (var (seed, expected) in new[] { (1UL, 0), (2UL, 0), (3UL, 1) })
        {
            var r = Resources(seed); r.Deck[0] = r.Deck[0] with { Sigil = SigilId.Duck };
            var g = Fixture(r); Auction(g); Play(g, new(0));
            Assert.Equal(expected, Effects(g, "Duck"), $"Duck seed {seed}");
            Assert.True(g.CreateCampaignView(0).Events.Where(e => e.Contains(": Duck ")).All(e => e.Contains("+4 points")));
        }
        var bounce = Resources(3); bounce.Deck[0] = bounce.Deck[0] with { Sigil = SigilId.Duck }; bounce.Deck[6] = bounce.Deck[6] with { Enhancement = EnhancementId.Bouncy };
        var replay = Fixture(bounce); Auction(replay); Play(replay, new(0)); Play(replay, new(6));
        Assert.Equal(2, Effects(replay, "Duck"), "Both seed3 Duck trials succeed, including the Bouncy replay.");
        Assert.True(replay.CreateCampaignView(0).Events.Where(e => e.Contains(": Duck ")).All(e => e.Contains("+4 points")));
    }

    private static int TrickDelta(JokerId joker, int winner, Card[] first, int bidder = 0, Suit trump = Suit.Clubs, EnhancementId? enhancement = null)
    {
        int Score(bool owned)
        {
            var r = Resources(17, owned ? [joker] : []);
            r = r with { Deck = r.Deck.Select(c => c with { Face = new(Suit.Diamonds, Rank.Nine) }).ToArray() };
            for (var seat = 0; seat < 4; seat++) r.Deck[seat * 6] = r.Deck[seat * 6] with { Face = first[seat], Enhancement = enhancement };
            var g = Fixture(r); Auction(g, bidder, trump: trump);
            for (var i = 0; i < 4; i++) Play(g, new(g.CurrentSeat!.Value * 6));
            Assert.Equal(winner, g.CompletedTricks[0].Winner);
            Finish(g); return g.Scores[winner % 2];
        }
        return Score(true) - Score(false);
    }
    public static void TeamScopeAndQualifyingCards()
    {
        foreach (var winner in new[] { 0, 2, 1, 3 })
        {
            var cards = Enumerable.Repeat(new Card(Suit.Hearts, Rank.Nine), 4).ToArray(); cards[winner] = new(Suit.Hearts, Rank.Ace);
            Assert.Equal(winner % 2 == 0 ? 12 : 0, TrickDelta(JokerId.CureAll, winner, cards, trump: Suit.Hearts, enhancement: EnhancementId.Cursed));
            Assert.Equal(winner % 2 == 0 ? 4 : 0, TrickDelta(JokerId.LoverBoy, winner, cards, trump: Suit.Hearts));
            Assert.Equal(0, TrickDelta(JokerId.LoverBoy, winner, cards, bidder: 2, trump: Suit.Hearts));
            Assert.Equal(0, TrickDelta(JokerId.LoverBoy, winner, cards, trump: Suit.Spades));
            foreach (var clubs in new[] { 0, 1, 4 })
            {
                var clubCards = Enumerable.Range(0, 4).Select(s => new Card(s < clubs ? Suit.Clubs : Suit.Hearts, Rank.Nine)).ToArray();
                clubCards[winner] = new(clubCards[winner].Suit, Rank.Ace);
                // High no-trump is unnecessary: select the winner's suit so every fixture remains legal and decisive.
                Assert.Equal(winner % 2 == 0 && clubs > 0 ? 1 : 0, TrickDelta(JokerId.Clubhouse, winner, clubCards, trump: clubCards[winner].Suit));
            }
            foreach (var complete in new[] { true, false })
            {
                var ranks = new[] { Rank.King, Rank.Queen, complete ? Rank.Jack : Rank.Ten }.ToList();
                var royal = Enumerable.Range(0, 4).Select(s => new Card(Suit.Hearts, s == winner ? Rank.Ace : ranks[0])).ToArray();
                for (var s = 0; s < 4; s++) if (s != winner) { royal[s] = new(Suit.Hearts, ranks[0]); ranks.RemoveAt(0); }
                Assert.Equal(winner % 2 == 0 && complete ? 1 : 0, TrickDelta(JokerId.RoyalCouncil, winner, royal, trump: Suit.Spades));
            }
        }
    }
    public static void SnakeEyesThreshold()
    {
        foreach (var winner in new[] { 0, 2, 1 })
        foreach (var aces in new[] { 0, 1, 2, 3, 4 })
        {
            var cards = Enumerable.Repeat(new Card(Suit.Hearts, Rank.Nine), 4).ToArray();
            var others = Enumerable.Range(0, 4).Where(s => s != winner).ToArray();
            cards[winner] = new(Suit.Clubs, aces == 0 ? Rank.King : Rank.Ace);
            for (var i = 0; i < aces - 1; i++) cards[others[i]] = new(Suit.Hearts, Rank.Ace);
            Assert.Equal(winner % 2 == 0 && aces >= 2 ? 4 : 0, TrickDelta(JokerId.SnakeEyes, winner, cards));
        }
    }
    public static void RollerCoasterResetsEachRound()
    {
        var g = Fixture(Resources(17, JokerId.RollerCoaster)); var plain = Fixture(Resources());
        var previous = 0;
        foreach (var mode in new[] { ContractMode.High, ContractMode.Low, ContractMode.Trump, ContractMode.High })
        {
            Auction(g, mode: mode); Auction(plain, mode: mode); Finish(g); Finish(plain);
            var difference = g.Scores[0] - plain.Scores[0];
            Assert.Equal(mode == ContractMode.Trump ? 0 : g.TricksByTeam[0] * 2 - 6, difference - previous, $"Round {g.HandNumber} {mode}");
            previous = difference; g.StartNextHand(); plain.StartNextHand();
        }
    }

    public static void DejaVuAwardsOnePerWonTrickOnConsecutiveMatchingRounds()
    {
        var game = Fixture(Resources(17, JokerId.DejaVu));
        var sawFailedBidWithTricks = false;
        foreach (var (trump, repeats) in new[]
        {
            (Suit.Clubs, false), (Suit.Clubs, true), (Suit.Clubs, true),
            (Suit.Hearts, false), (Suit.Hearts, true), (Suit.Clubs, false)
        })
        {
            Auction(game, trump: trump);
            Finish(game);
            var result = game.HandHistory[^1];
            Assert.Equal(repeats ? game.TricksByTeam[0] : 0, result.ScoreBreakdowns[0].JokerPoints);
            Assert.Equal(0, result.ScoreBreakdowns[1].JokerPoints);
            sawFailedBidWithTricks |= repeats && !result.MadeContract && game.TricksByTeam[0] > 0;
            game.StartNextHand();
        }
        Assert.True(sawFailedBidWithTricks, "A failed bid must still earn the bonus on team tricks won.");
    }

    public static void DejaVuResumesLegacyRoundsWithStoredStreaks()
    {
        foreach (var repeats in new[] { false, true })
        {
            var resources = Resources(17) with
            {
                Inventory = new([new(JokerId.DejaVu, 4)], [], []),
                PreviousContract = new(ContractMode.Trump, repeats ? Suit.Clubs : Suit.Hearts)
            };
            var game = Fixture(resources);
            Auction(game);
            while (game.CompletedTricks.Count == 0) Play(game);
            var checkpoint = game.ExportCampaignMatchSnapshot();
            var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(checkpoint));
            var path = Path.Combine(Path.GetTempPath(), $"deja-vu-{Guid.NewGuid()}.json");
            try
            {
                Assert.True(CampaignStore.Save(run, path));
                var current = CampaignStore.Load(path)!.LiveMatch!;
                var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                var live = json["Run"]!["LiveMatch"]!.AsObject();
                live.Remove("RepeatsPreviousTrump");
                live["DejaValue"] = repeats ? 5 : 4;
                File.WriteAllText(path, json.ToJsonString());
                var legacy = CampaignStore.Load(path)!.LiveMatch!;
                foreach (var saved in new[] { current, legacy })
                {
                    var restored = GameEngine.RestoreCampaignMatch(saved);
                    Finish(restored);
                    Assert.Equal(5, restored.TricksByTeam[0]);
                    Assert.Equal(repeats ? 5 : 0, restored.HandHistory[^1].ScoreBreakdowns[0].JokerPoints);
                }
            }
            finally { File.Delete(path); }
        }
    }

    public static void DejaVuDoesNotBridgePokerRounds()
    {
        var resources = Resources(17) with
        {
            Inventory = new([new(JokerId.DejaVu)], [], [GumballId.Poker]),
            PreviousContract = new(ContractMode.Trump, Suit.Clubs)
        };
        var game = Fixture(resources);
        while (game.Phase == GamePhase.Bidding)
        {
            var seat = game.CurrentSeat!.Value;
            game.ExecuteCampaignAction(seat, seat == 0
                ? new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker)
                : new CampaignCommand.Pass());
        }
        Assert.Equal(GamePhase.PokerReveal, game.Phase);
        game.ExecuteCampaignAction(0, new CampaignCommand.CompletePoker());
        Auction(game);
        Finish(game);
        Assert.True(game.TricksByTeam[0] > 0);
        Assert.Equal(0, game.HandHistory[^1].ScoreBreakdowns[0].JokerPoints);
    }

    public static void DoubleDownFirstHumanPlayAndRoundReset()
    {
        var r = Resources(17, DoubleDown, JokerId.LastLaugh); r = r with { Deck = r.Deck.Select(c => c with { Enhancement = EnhancementId.Stone }).ToArray() };
        var g = Fixture(r, dealer: 0); Auction(g, bidder: 1);
        for (var round = 0; round < 2; round++)
        {
            var humanPlays = 0;
            while (g.Phase == GamePhase.Playing)
            {
                var seat = g.CurrentSeat!.Value; var count = Effects(g, "Stone");
                var before = g.CreateCampaignView(seat).Players[seat].CardCount;
                var expected = (g.CompletedTricks.Count == 5 ? 2 : 1) * (seat == 0 && humanPlays++ == 0 ? 2 : 1);
                Play(g);
                Assert.Equal(expected, Effects(g, "Stone") - count);
                Assert.Equal(before - 1, g.CreateCampaignView(seat).Players[seat].CardCount);
            }
            Assert.Equal(6, humanPlays);
            g.StartNextHand(); Auction(g, bidder: 1);
        }
    }
    public static void DoubleDownIndependentReplaySources()
    {
        foreach (var frog in new[] { false, true })
        {
            var r = Resources(17, DoubleDown, JokerId.LuckyDuck, JokerId.LastLaugh);
            r.Deck[0] = r.Deck[0] with { Enhancement = EnhancementId.Lucky, Sigil = frog ? SigilId.Frog : null };
            r.Deck[6] = r.Deck[6] with { Enhancement = EnhancementId.Bouncy };
            var g = Fixture(r); Auction(g); Play(g, new(0)); Play(g, new(6));
            Assert.Equal(frog ? 0x2e2ac13ef8e8d8e3UL : 0xf1bbcdcbfa53e0b9UL, g.ExportCampaignSnapshot().RandomState,
                "Lucky uses two draws per activation: original + Lucky Duck + Double Down + optional Frog + one Bouncy replay.");
            Finish(g);
        }
        var b = Resources(17, DoubleDown); b.Deck[0] = b.Deck[0] with { Enhancement = EnhancementId.Bouncy, Sigil = SigilId.Frog }; b.Deck[6] = b.Deck[6] with { Enhancement = EnhancementId.Stone };
        var bounded = Fixture(b, dealer: 0); Auction(bounded, bidder: 1); Play(bounded, new(6)); Play(bounded); Play(bounded); Play(bounded, new(0));
        Assert.Equal(4, Effects(bounded, "Stone"), "Bouncy original, Frog, and Double Down each replay prior Stone once.");
        Finish(bounded);
        var glass = Resources(17, DoubleDown, JokerId.LastLaugh); glass.Deck[0] = glass.Deck[0] with { Sigil = SigilId.Fish }; glass.Deck[6] = glass.Deck[6] with { Enhancement = EnhancementId.Glass };
        var normal = glass with { Inventory = glass.Inventory with { Jokers = [new(JokerId.LastLaugh)] } };
        var doubled = Fixture(glass); var control = Fixture(normal); Auction(doubled); Auction(control); Finish(doubled); Finish(control);
        Assert.Equal(4, doubled.Scores[0] - control.Scores[0], "One extra Fish +2 is multiplied once by Glass.");
    }
    public static void DoubleDownShopAndPersistence()
    {
        Assert.Equal("DoubleDown", Enum.GetName(DoubleDown)!);
        var definition = FeatureCatalog.Get(FeatureCategory.Joker, "DoubleDown");
        Assert.Equal("joker:double-down", definition.Id);
        Assert.Equal("Boomerang Joker", definition.Name);
        var seen = false;
        for (ulong seed = 1; seed <= 128 && !seen; seed++)
        {
            var run = CampaignShop.Open(new CampaignRun(Money: 20, Phase: CampaignPhase.Shop) { Resources = Resources(seed) });
            var offer = run.Shop!.Offers.FirstOrDefault(o => o.Category == FeatureCategory.Joker && o.FeatureId == "DoubleDown");
            if (offer is null) continue;
            var purchased = CampaignShop.Buy(run, offer.Id); Assert.True(purchased.Resources.Inventory.Has(DoubleDown));
            var path = Path.Combine(Path.GetTempPath(), $"double-down-{Guid.NewGuid()}.json");
            try { Assert.True(CampaignStore.Save(purchased, path)); Assert.True(CampaignStore.Load(path)!.Resources.Inventory.Has(DoubleDown)); }
            finally { File.Delete(path); }
            seen = true;
        }
        Assert.True(seen, "Double Down must occur in random shop stock.");
    }
}
