using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.Core;

internal static class ScoreExplanationTests
{
    private static readonly string[] Names = ["Alice", "Bea", "Cy", "Dee"];
    private static GameEngine Begin(bool campaign = true, int bidder = 0, bool success = true,
        BidLevel bid = BidLevel.Three, GumballId? variant = null, JokerId[]? jokers = null,
        EnhancementId? enhancement = null, SigilId? sigil = null, bool duplicates = false)
    {
        var deck = GameRules.CreateDeck().ToArray();
        CampaignSnapshot? resources = campaign ? CampaignSnapshot.Create(5) with
        {
            Inventory = new((jokers ?? []).Select(j => new JokerState(j)).ToArray(), [], Enum.GetValues<GumballId>())
            { GumballExtraCopies = duplicates ? new() { [GumballId.Chaos] = 3, [GumballId.Poker] = 3, [GumballId.HighLow] = 2 } : [] },
            Deck = CampaignCards.CreateDeck().Select(c => c with
            {
                Face = new Card(Suit.Clubs, ((c.Id.Value / 6 % 2 == bidder % 2) == success) ? Rank.Ace : Rank.Nine),
                Enhancement = enhancement, Sigil = sigil
            }).ToArray()
        } : null;
        var game = GameEngine.CreateFixture(new FixtureStart(Names,
            Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(),
            (bidder + 3) % 4, [0, 0], Campaign: resources,
            Options: new MatchOptions(1000) { OpponentSuccessfulBidBonus = 4 }));
        if (campaign)
        {
            game.ExecuteCampaignAction(bidder, new CampaignCommand.Bid(bid, variant));
            while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
            while (game.Phase is not (GamePhase.Playing or GamePhase.PokerReveal))
            {
                var seat = game.CampaignActor!.Value;
                var legal = game.CreateCampaignView(seat).LegalActions;
                var action = legal.OfType<CampaignCommand.ChooseContract>().Any()
                    ? legal.OfType<CampaignCommand.ChooseContract>().FirstOrDefault(c => c.Mode == ContractMode.High) ?? legal.OfType<CampaignCommand.ChooseContract>().First(c => c.Trump == Suit.Clubs)
                    : legal.First(a => a is not (CampaignCommand.UseTarot or CampaignCommand.UseSigil));
                game.ExecuteCampaignAction(seat, action);
            }
        }
        else
        {
            game.PlaceBid(bidder, bid);
            while (game.Phase == GamePhase.Bidding) game.PlaceBid(game.CurrentSeat!.Value, null);
            game.ChooseContract(bidder, ContractMode.Trump, Suit.Clubs);
        }
        return game;
    }
    private static void Finish(GameEngine game)
    {
        var guard = 80;
        while (game.Phase == GamePhase.Playing && guard-- > 0)
        {
            var seat = game.CurrentSeat!.Value;
            if (game.IsCampaign) game.ExecuteCampaignAction(seat, game.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First());
            else
            {
                var card = game.CreateView(seat).LegalActions.Cards.OrderByDescending(c => c.Rank == Rank.Jack && GameRules.EffectiveSuit(c, game.Contract!) == Suit.Clubs ? c.Suit == Suit.Clubs ? 100 : 99 : (int)c.Rank).First();
                game.PlayCard(seat, card);
            }
        }
        Assert.True(guard > 0);
    }
    // Missing public history must fail at the protocol boundary before any new API exists.
    private static JsonObject Result(GameEngine game)
    {
        var json = JsonSerializer.SerializeToNode(game.CreateView())!.AsObject();
        Assert.True(json["HandHistory"] is JsonArray { Count: > 0 }, "A public game view must include completed hands and their structured score explanations.");
        return json["HandHistory"]!.AsArray().Last()!.AsObject();
    }
    private static JsonObject Team(JsonObject result, int team)
    {
        Assert.True(result["ScoreBreakdowns"] is JsonArray { Count: 2 }, "A hand must explain both team deltas without parsing prose.");
        return result["ScoreBreakdowns"]!.AsArray().Single(n => n!["Team"]!.GetValue<int>() == team)!.AsObject();
    }
    private static int Value(JsonObject json, string name) => json[name]!.GetValue<int>();
    private static void CheckFormula(GameEngine game)
    {
        var result = Result(game);
        for (var team = 0; team < 2; team++)
        {
            var part = Team(result, team);
            var expected = (Value(part, "ContractPoints") + Value(part, "CardPoints") + Value(part, "JokerPoints") + Value(part, "TrickModifierPoints")) * Value(part, "HandMultiplier")
                + Value(part, "OpponentBidBonus") + Value(part, "PokerPoints") + part["GumballBonuses"]!.AsArray().Sum(b => b!["Points"]!.GetValue<int>());
            Assert.Equal(team == 0 ? game.HandHistory.Last().TeamZeroDelta : game.HandHistory.Last().TeamOneDelta, expected);
            Assert.Equal(expected, Value(part, "FinalDelta"));
        }
    }
    public static void PlainAndPenaltyResults()
    {
        var game = Begin(campaign: false); Finish(game);
        var result = Result(game);
        Assert.True(result["MadeContract"]!.GetValue<bool>());
        Assert.Equal(6, Value(Team(result, 0), "ContractPoints"));
        Assert.Equal(0, Value(Team(result, 1), "ContractPoints"));
        CheckFormula(game);
        foreach (var offendingSeat in new[] { 0, 1 })
        {
            game = Begin(campaign: false, bid: BidLevel.Alone);
            game.ApplyIllegalPlayPenalty(offendingSeat);
            result = Result(game);
            Assert.Equal(offendingSeat == 1, result["MadeContract"]!.GetValue<bool>());
            Assert.Equal(offendingSeat == 1 ? 24 : -24, Value(Team(result, 0), "ContractPoints"));
            Assert.Equal(offendingSeat == 1 ? 0 : 6, Value(Team(result, 1), "ContractPoints"));
            CheckFormula(game);
        }
    }
    public static void CampaignFactorsAndFixedBonuses()
    {
        var layered = Begin(bid: BidLevel.Four, variant: GumballId.Chaos,
            jokers: [JokerId.LastLaugh, JokerId.Clubhouse], enhancement: EnhancementId.Stone, sigil: SigilId.Fish, duplicates: true);
        var seat = layered.CurrentSeat!.Value;
        layered.ExecuteCampaignAction(seat, layered.CreateCampaignView(seat).LegalActions.OfType<CampaignCommand.Play>().First());
        Assert.Equal(3, layered.Scores[0], "Chaos doubling is applied at settlement, after the live effect points show.");
        Finish(layered);
        var result = Result(layered); var us = Team(result, 0); var them = Team(result, 1);
        Assert.True(result["MadeContract"]!.GetValue<bool>());
        Assert.Equal(6, Value(us, "ContractPoints")); Assert.Equal(42, Value(us, "CardPoints"));
        Assert.Equal(7, Value(us, "JokerPoints")); Assert.Equal(1, Value(us, "TrickModifierPoints"));
        Assert.Equal(2, Value(us, "HandMultiplier")); Assert.Equal(115, layered.Scores[0]);
        Assert.Equal(42, Value(them, "CardPoints")); Assert.Equal(84, layered.Scores[1]);
        Assert.Equal(1, us["GumballBonuses"]!.AsArray().Count);
        Assert.Equal(3, us["GumballBonuses"]!.AsArray().Single(b => b!["Id"]!.GetValue<int>() == (int)GumballId.Chaos)!["Points"]!.GetValue<int>());
        CheckFormula(layered);
        var poker = Begin(bid: BidLevel.Four, variant: GumballId.Poker, duplicates: true);
        var pokerUs = Team(Result(poker), 0);
        Assert.Equal((int)GumballId.Poker, Value(pokerUs["GumballBonuses"]!.AsArray().Single()!.AsObject(), "Id"));
        Assert.Equal(4, Value(pokerUs, "PokerPoints"));
        Assert.Equal(7, poker.Scores[0]);
        CheckFormula(poker);
        var opponent = Begin(bidder: 1, enhancement: EnhancementId.Cursed); Finish(opponent);
        result = Result(opponent); them = Team(result, 1);
        Assert.True(result["MadeContract"]!.GetValue<bool>(), "A made contract remains made even when effects make its total negative.");
        Assert.Equal(6, Value(them, "ContractPoints")); Assert.Equal(-24, Value(them, "CardPoints"));
        Assert.Equal(4, Value(them, "OpponentBidBonus")); Assert.Equal(-14, opponent.Scores[1]); CheckFormula(opponent);
    }
    public static void FailedProtectionAndDuelist()
    {
        foreach (var (jokers, variant, contract) in new[] {
            (Array.Empty<JokerId>(), (GumballId?)null, -4),
            (new[] { JokerId.ProtectiveBubble }, (GumballId?)null, 0),
            (Array.Empty<JokerId>(), (GumballId?)GumballId.Duelist, -6),
            (new[] { JokerId.ProtectiveBubble }, (GumballId?)GumballId.Duelist, 0),
            (Array.Empty<JokerId>(), (GumballId?)GumballId.Chaos, -4) })
        {
            var game = Begin(success: false, bid: BidLevel.Four, variant: variant, jokers: jokers, duplicates: true); Finish(game);
            var result = Result(game); Assert.True(!result["MadeContract"]!.GetValue<bool>());
            Assert.Equal(contract, Value(Team(result, 0), "ContractPoints"));
            Assert.Equal(0, Team(result, 0)["GumballBonuses"]!.AsArray().Count);
            Assert.Equal(0, Value(Team(result, 1), "OpponentBidBonus")); CheckFormula(game);
        }
    }
    public static void HistoryAndPublicEvents()
    {
        var game = Begin(enhancement: EnhancementId.Golden); Finish(game);
        var before = game.CreateView(); var json = JsonSerializer.SerializeToNode(before)!;
        Assert.True(json["PublicEvents"] is JsonArray, "The public log must be available in the view.");
        Assert.True(json["PublicEvents"]!.AsArray().Any(n => n!.GetValue<string>().Contains("+$1")), "Public money effects must be visible.");
        var history = Result(game).ToJsonString(); var events = json["PublicEvents"]!.AsArray().Count;
        game.StartNextHand(); var next = JsonSerializer.SerializeToNode(game.CreateView())!;
        Assert.Equal(history, next["HandHistory"]![0]!.ToJsonString());
        Assert.True(next["PublicEvents"]!.AsArray().Count > events);
        Assert.Equal(events, JsonSerializer.SerializeToNode(before)!["PublicEvents"]!.AsArray().Count);
        Assert.Equal(next["PublicEvents"]!.AsArray().Count, JsonSerializer.SerializeToNode(game.CreateView())!["PublicEvents"]!.AsArray().Count);
        var plain = Begin(campaign: false, bid: BidLevel.PartnersBest);
        var sent = plain.CreateView(0).LegalActions.Cards.First(); plain.ExchangeCard(0, sent); plain.ExchangeCard(2, sent);
        var publicText = string.Join(" ", plain.CreateView().Events);
        Assert.True(publicText.Contains("exchanged privately"));
        Assert.True(!publicText.Contains(sent.Code), "Public exchange events must not identify the private card.");
    }
    public static void HistoryCopiesAndAdditiveProtocol()
    {
        var game = Begin(campaign: false); Finish(game);
        var engineHistory = game.HandHistory; var view = game.CreateView();
        var serialized = JsonSerializer.Serialize(view);
        var roundTrip = JsonSerializer.Deserialize<GameView>(serialized)!;
        Assert.Equal(serialized, JsonSerializer.Serialize(roundTrip));
        var legacy = JsonNode.Parse(serialized)!.AsObject();
        legacy.Remove("HandHistory"); legacy.Remove("PublicEvents"); legacy.Remove("CompletionReason");
        Assert.Equal(0, JsonSerializer.Deserialize<GameView>(legacy.ToJsonString())!.HandHistory.Count);
        var part = view.HandHistory[0].ScoreBreakdowns[0];
        Assert.Throws<NotSupportedException>(() => ((IList<TeamScoreBreakdown>)view.HandHistory[0].ScoreBreakdowns)[0] = part with { CardPoints = 99 });
        game.StartNextHand(); var bidder = game.CurrentSeat!.Value;
        game.PlaceBid(bidder, BidLevel.Three);
        while (game.Phase == GamePhase.Bidding) game.PlaceBid(game.CurrentSeat!.Value, null);
        game.ChooseContract(bidder, ContractMode.Trump, Suit.Clubs); game.ApplyIllegalPlayPenalty(bidder);
        Assert.Equal(1, engineHistory.Count, "Previously exposed hand history must remain a copied snapshot.");
        Assert.Equal(1, view.HandHistory.Count); Assert.Equal(2, game.CreateView().HandHistory.Count);
        var input = new[] { new GumballBidBonus(0, GumballId.Chaos, 3) };
        var copied = new TeamScoreBreakdown(0, 6, 0, 0, 0, 2, 0) { GumballBonuses = input };
        input[0] = input[0] with { Points = 99 }; Assert.Equal(15, copied.FinalDelta);
        Assert.Throws<NotSupportedException>(() => ((IList<GumballBidBonus>)copied.GumballBonuses)[0] = input[0]);
    }

    public static void DeveloperCompletionHasNoFabricatedContract()
    {
        var resources = CampaignSnapshot.Create(5);
        var game = new GameEngine(Names, 5, new MatchOptions(8) { NegativeThreshold = -8 }, new CampaignContext(resources));
        game.StartGame(3); game.DeveloperSetScores(-8, 0); game.DeveloperEndMatch(1);
        var json = JsonSerializer.SerializeToNode(game.CreateView())!;
        Assert.Equal(GamePhase.GameComplete, game.Phase); Assert.True(game.Contract is null);
        Assert.True(json["CompletionReason"]?.GetValue<string>().Contains("developer console") == true, "Terminal noncontract actions need an explicit readable reason.");
        Assert.Equal(0, json["HandHistory"]!.AsArray().Count); Assert.Equal(-8, game.Scores[0]);
    }
}
