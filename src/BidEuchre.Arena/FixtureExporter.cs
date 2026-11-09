using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.Arena;

/// <summary>
/// fixture-v1 is JSONL, one independent trace per line. Cards are suit*6+rank-9
/// (CDHS, 9TJQKA); actions use the shared 0..60 mapping. Sets are ascending.
/// initial contains four six-card hands, dealer, scores, handNumber, dealSerial.
/// expectedInitial and steps[].expected contain state (all hands) and privateViews
/// (seat order 0..3). Each step is action {seat,id}, penalty {seat,illegalAction},
/// or redeal {deal: explicit initial-format replacement}. Redeal's deal is an
/// input, never an expected random shuffle. before/after hand identity is retained.
/// deliveries are separate from parity: {afterStep,kind,seat,view}; afterStep=-1
/// means initial, otherwise the zero-based step just completed. decision delivers
/// only the next actor's private view, terminal delivers all four private views.
/// Parity snapshots are never delivered to a driver. Names/events/reasons are absent.
/// </summary>
public static class FixtureExporter
{
    private static readonly string[] Names = ["Seat 0", "Seat 1", "Seat 2", "Seat 3"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int CardId(Card card) => checked((int)card.Suit * 6 + (int)card.Rank - 9);
    public static Card CardFromId(int id) => id is >= 0 and < 24
        ? new Card((Suit)(id / 6), (Rank)(9 + id % 6)) : throw new ArgumentOutOfRangeException(nameof(id));

    public static int ActionId(BotAction action) => action switch
    {
        BotAction.Pass => 0,
        BotAction.Bid bid when Enum.IsDefined(bid.Level) => (int)bid.Level - 2,
        BotAction.ChooseContract { Mode: ContractMode.High, Trump: null } => 7,
        BotAction.ChooseContract { Mode: ContractMode.Low, Trump: null } => 8,
        BotAction.ChooseContract { Mode: ContractMode.Trump, Trump: { } suit } when Enum.IsDefined(suit) => 9 + (int)suit,
        BotAction.Exchange exchange => ValidCardAction(exchange.Card, 13),
        BotAction.Play play => ValidCardAction(play.Card, 37),
        _ => throw new ProtocolException("Action has no valid normalized ID.")
    };

    private static int ValidCardAction(Card card, int offset) => Enum.IsDefined(card.Suit) && Enum.IsDefined(card.Rank)
        ? offset + CardId(card) : throw new ProtocolException("Invalid card action.");

    public static BotAction ActionFromId(int id) => id switch
    {
        0 => new BotAction.Pass(),
        >= 1 and <= 6 => new BotAction.Bid((BidLevel)(id + 2)),
        7 => new BotAction.ChooseContract(ContractMode.High, null),
        8 => new BotAction.ChooseContract(ContractMode.Low, null),
        >= 9 and <= 12 => new BotAction.ChooseContract(ContractMode.Trump, (Suit)(id - 9)),
        >= 13 and <= 36 => new BotAction.Exchange(CardFromId(id - 13)),
        >= 37 and <= 60 => new BotAction.Play(CardFromId(id - 37)),
        _ => throw new ProtocolException("Invalid normalized action ID.")
    };

    public static int[] LegalActionIds(GameView view)
    {
        var legal = view.LegalActions;
        var result = new List<int>();
        if (legal.CanPass) result.Add(0);
        result.AddRange(legal.Bids.Select(bid => (int)bid - 2));
        foreach (var mode in legal.ContractModes)
        {
            if (mode == ContractMode.High) result.Add(7);
            if (mode == ContractMode.Low) result.Add(8);
            if (mode == ContractMode.Trump) result.AddRange(legal.TrumpSuits.Select(suit => 9 + (int)suit));
        }
        if (view.Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard)
            result.AddRange(legal.Cards.Select(card => 13 + CardId(card)));
        if (view.Phase == GamePhase.Playing) result.AddRange(legal.Cards.Select(card => 37 + CardId(card)));
        return result.Order().ToArray();
    }

    private static object? ContractProjection(Contract? contract) => contract is null ? null : new
    { bid = (int)contract.Bid, mode = contract.Mode.ToString(), trump = (int?)contract.Trump };
    private static object[] Plays(IReadOnlyList<CardPlay> plays) => plays.Select(play => (object)new
    { seat = play.Seat, card = CardId(play.Card) }).ToArray();

    public static object Project(GameEngine game, int? seat, bool revealAll = false)
    {
        var view = game.CreateView(seat, revealAll);
        return new
        {
            phase = view.Phase.ToString(), handNumber = view.HandNumber, dealSerial = view.DealSerial,
            dealer = view.Dealer, currentSeat = view.CurrentSeat, scores = view.Scores,
            players = view.Players.Select(player => new
            {
                seat = player.Seat, team = player.Team, cardCount = player.CardCount,
                cards = player.Cards?.Select(CardId).Order().ToArray(), isSittingOut = player.IsSittingOut
            }).ToArray(),
            auction = view.Auction.Select(action => new { seat = action.Seat, bid = (int?)action.Bid }).ToArray(),
            highBid = (int?)view.HighBid, bidder = view.Bidder, contract = ContractProjection(view.Contract),
            currentTrick = Plays(view.CurrentTrick),
            completedTricks = view.CompletedTricks.Select(trick => new
            { number = trick.Number, leader = trick.Leader, winner = trick.Winner, plays = Plays(trick.Plays) }).ToArray(),
            tricksByTeam = view.TricksByTeam, gameWinner = view.GameWinner, legal = LegalActionIds(view),
            handResults = game.HandHistory.Select(hand => new
            {
                handNumber = hand.HandNumber, bidder = hand.Bidder, contract = ContractProjection(hand.Contract),
                biddingTeamTricks = hand.BiddingTeamTricks, defendingTeamTricks = hand.DefendingTeamTricks,
                teamZeroDelta = hand.TeamZeroDelta, teamOneDelta = hand.TeamOneDelta
            }).ToArray()
        };
    }

    private static object Snapshot(GameEngine game) => new
    { state = Project(game, null, true), privateViews = Enumerable.Range(0, 4).Select(seat => Project(game, seat)).ToArray() };
    private static object Deal(GameEngine game) => new
    {
        hands = game.CreateView(null, true).Players.Select(player => player.Cards!.Select(CardId).Order().ToArray()).ToArray(),
        dealer = game.Dealer, scores = game.Scores.ToArray(), handNumber = game.HandNumber, dealSerial = game.DealSerial
    };
    private static FixtureStart ReadDeal(JsonElement deal) => new(Names,
        deal.GetProperty("hands").EnumerateArray().Select(hand => hand.EnumerateArray().Select(card => CardFromId(card.GetInt32())).ToArray()).ToArray(),
        deal.GetProperty("dealer").GetInt32(), deal.GetProperty("scores").EnumerateArray().Select(score => score.GetInt32()).ToArray(),
        deal.GetProperty("handNumber").GetInt32(), deal.GetProperty("dealSerial").GetInt64());

    public static async Task ExportAsync(string path, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path);
        var fixtures = new List<object>();
        foreach (var suit in Enum.GetValues<Suit>())
            fixtures.Add(Build($"trump-{suit.ToString().ToLowerInvariant()}", (BidLevel)(3 + (int)suit), ContractMode.Trump, suit));
        var contestedHands = Enumerable.Range(0, 4).Select(seat => GameRules.CreateDeck()
            .Where((_, index) => index % 4 == seat).ToArray()).ToArray();
        fixtures.Add(Build("high", BidLevel.Four, ContractMode.High, hands: contestedHands));
        fixtures.Add(Build("low", BidLevel.Four, ContractMode.Low, hands: contestedHands));
        fixtures.Add(Build("partners-best-same-card", BidLevel.PartnersBest, ContractMode.Trump, Suit.Clubs));
        fixtures.Add(Build("partners-best-return-other", BidLevel.PartnersBest, ContractMode.Trump, Suit.Hearts, returnSame: false));
        foreach (var mode in Enum.GetValues<ContractMode>())
            fixtures.Add(Build($"alone-{mode.ToString().ToLowerInvariant()}", BidLevel.Alone, mode, mode == ContractMode.Trump ? Suit.Spades : null));
        fixtures.Add(Build("negative-scores", BidLevel.Six, ContractMode.Low, scores: [-24, -12]));
        fixtures.Add(Build("near-forty", BidLevel.Three, ContractMode.Trump, Suit.Clubs, scores: [39, -12], firstPlay: Card.Parse("JC")));
        // Search only the production authority for a three-trick split; no independently calculated expected state.
        Card[][]? tiedHands = null;
        for (var seed = 1; seed <= 1000 && tiedHands is null; seed++)
        {
            var dealt = new GameEngine(Names, seed); dealt.StartGame(3);
            var hands = dealt.CreateView(null, true).Players.Select(player => player.Cards!.ToArray()).ToArray();
            var candidate = JsonSerializer.SerializeToElement(Build("candidate", BidLevel.Three, ContractMode.Trump, Suit.Clubs, hands: hands), Json);
            var candidateSteps = candidate.GetProperty("steps");
            var scores = candidateSteps[candidateSteps.GetArrayLength() - 1].GetProperty("expected").GetProperty("state").GetProperty("scores");
            if (scores[0].GetInt32() == 3 && scores[1].GetInt32() == 3) tiedHands = hands;
        }
        if (tiedHands is null) throw new InvalidOperationException("Could not generate tied-threshold fixture.");
        fixtures.Add(Build("both-over-forty-zero", BidLevel.Three, ContractMode.Trump, Suit.Clubs, scores: [39, 38], hands: tiedHands));
        fixtures.Add(Build("both-over-forty-one", BidLevel.Three, ContractMode.Trump, Suit.Clubs, scores: [38, 39], hands: tiedHands));
        var sweepHands = SuitHands();
        (sweepHands[0][0], sweepHands[3][2]) = (sweepHands[3][2], sweepHands[0][0]);
        fixtures.Add(Build("partners-best-success", BidLevel.PartnersBest, ContractMode.Trump, Suit.Clubs, hands: sweepHands, firstPlay: Card.Parse("JC")));
        fixtures.Add(Build("alone-sitting-lead", BidLevel.Alone, ContractMode.High, bidderTurn: 2));
        fixtures.Add(Build("tied-threshold", BidLevel.Three, ContractMode.Trump, Suit.Clubs, scores: [37, 37], hands: tiedHands));
        fixtures.Add(Build("tied-threshold-continues", BidLevel.Three, ContractMode.Trump, Suit.Clubs, scores: [40, 40], hands: tiedHands));
        fixtures.Add(Build("redeal-auction", BidLevel.Three, ContractMode.Trump, Suit.Clubs, redealPhase: GamePhase.Bidding));
        fixtures.Add(Build("redeal-exchange", BidLevel.PartnersBest, ContractMode.Trump, Suit.Clubs, redealPhase: GamePhase.ExchangingPartnerCard));
        foreach (var bid in new[] { BidLevel.Three, BidLevel.PartnersBest, BidLevel.Alone })
        foreach (var team in new[] { 0, 1 })
            fixtures.Add(Build($"penalty-{(int)bid}-team-{team}", bid, ContractMode.Trump, Suit.Clubs, penaltyTeam: team));
        var bowerHands = SuitHands();
        (bowerHands[0][0], bowerHands[3][0]) = (bowerHands[3][0], bowerHands[0][0]);
        (bowerHands[1][0], bowerHands[3][2]) = (bowerHands[3][2], bowerHands[1][0]);
        fixtures.Add(Build("bower-printed-suit-void", BidLevel.Three, ContractMode.Trump, Suit.Clubs, hands: bowerHands, firstPlay: Card.Parse("9S")));
        fixtures.Add(Build("bower-draw-left", BidLevel.Three, ContractMode.Trump, Suit.Clubs, firstPlay: Card.Parse("JC")));
        fixtures.Add(Build("forced-dealer", BidLevel.Three, ContractMode.Trump, Suit.Diamonds, forcedDealer: true));
        // Bounded, reproducible legal-action fuzzing; expected states always come from Core.
        for (var index = 0; index < 32; index++) fixtures.Add(BuildRandom(7100 + index));
        foreach (var fixture in fixtures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = JsonSerializer.SerializeToElement(fixture, Json);
            Verify(element);
            await writer.WriteLineAsync(element.GetRawText().AsMemory(), cancellationToken);
        }
    }

    private static object BuildRandom(int seed)
    {
        var game = new GameEngine(Names, seed);
        game.StartGame(seed % 4);
        var random = new Random(seed ^ 0x5eed);
        var initial = Deal(game);
        var expectedInitial = Snapshot(game);
        var steps = new List<object>();
        var deliveries = new List<object>();
        while (game.Phase is not GamePhase.HandComplete and not GamePhase.GameComplete)
        {
            if (steps.Count >= 31) throw new InvalidOperationException("Random fixture exceeded one-hand bound.");
            var seat = game.CurrentSeat!.Value;
            deliveries.Add(new { afterStep = steps.Count - 1, kind = "decision", seat, view = Project(game, seat) });
            var legal = LegalActionIds(game.CreateView(seat));
            var action = legal[random.Next(legal.Length)];
            ActionApplier.Apply(game, seat, ActionFromId(action));
            steps.Add(new { kind = "action", seat, id = action, expected = Snapshot(game) });
        }
        for (var seat = 0; seat < 4; seat++)
            deliveries.Add(new { afterStep = steps.Count - 1, kind = "terminal", seat, view = Project(game, seat) });
        return new { schema = "fixture-v1", id = $"random-{seed}", seed, initial, expectedInitial, steps, deliveries };
    }

    private static Card[][] SuitHands() => GameRules.CreateDeck().Chunk(6).Select(hand => hand.ToArray()).ToArray();

    private static object Build(string id, BidLevel bid, ContractMode mode, Suit? trump = null,
        int[]? scores = null, Card[][]? hands = null, bool returnSame = true, Card? firstPlay = null,
        GamePhase? redealPhase = null, int? penaltyTeam = null, bool forcedDealer = false, int bidderTurn = 0)
    {
        var game = GameEngine.CreateFixture(new FixtureStart(Names, hands ?? SuitHands(), 3, scores ?? [0, 0]));
        var initial = Deal(game);
        var expectedInitial = Snapshot(game);
        var steps = new List<object>();
        var deliveries = new List<object>();
        var redealt = false;
        Card? sent = null;
        var played = false;
        while (game.Phase is not GamePhase.HandComplete and not GamePhase.GameComplete)
        {
            if (!redealt && game.Phase == redealPhase)
            {
                game.Redeal();
                steps.Add(new { kind = "redeal", deal = Deal(game), expected = Snapshot(game) });
                redealt = true;
                sent = null;
                continue;
            }
            var seat = game.CurrentSeat!.Value;
            deliveries.Add(new { afterStep = steps.Count - 1, kind = "decision", seat, view = Project(game, seat) });
            if (game.Phase == GamePhase.Playing && penaltyTeam == seat % 2)
            {
                var illegal = GameRules.CreateDeck().First(card => !game.GetLegalCards(seat).Contains(card));
                try { game.PlayCard(seat, illegal); throw new InvalidOperationException("Illegal fixture action was accepted."); }
                catch (GameRuleException) { }
                game.ApplyIllegalPlayPenalty(seat);
                steps.Add(new { kind = "penalty", seat, illegalAction = 37 + CardId(illegal), expected = Snapshot(game) });
                break;
            }
            BotAction action = game.Phase switch
            {
                GamePhase.Bidding => game.Auction.Count == (forcedDealer ? 3 : bidderTurn)
                    ? new BotAction.Bid(bid) : new BotAction.Pass(),
                GamePhase.ChoosingContract => new BotAction.ChooseContract(mode, trump),
                GamePhase.ExchangingBidderCard => new BotAction.Exchange(game.GetLegalCards(seat)[0]),
                GamePhase.ExchangingPartnerCard => new BotAction.Exchange(returnSame ? sent!.Value : game.GetLegalCards(seat).First(card => card != sent)),
                GamePhase.Playing => new BotAction.Play(!played && firstPlay is { } first ? first : game.GetLegalCards(seat)[0]),
                _ => throw new InvalidOperationException("Unexpected fixture phase.")
            };
            if (game.Phase == GamePhase.ExchangingBidderCard) sent = ((BotAction.Exchange)action).Card;
            if (game.Phase == GamePhase.Playing) played = true;
            ActionApplier.Apply(game, seat, action);
            steps.Add(new { kind = "action", seat, id = ActionId(action), expected = Snapshot(game) });
        }
        for (var seat = 0; seat < 4; seat++)
            deliveries.Add(new { afterStep = steps.Count - 1, kind = "terminal", seat, view = Project(game, seat) });
        return new { schema = "fixture-v1", id, initial, expectedInitial, steps, deliveries };
    }

    // Replay checks the serialized contract, including every expected private view and actual delivery cadence.
    public static void Verify(JsonElement fixture)
    {
        if (fixture.GetProperty("schema").GetString() != "fixture-v1") throw new InvalidDataException("Unknown fixture schema.");
        var game = GameEngine.CreateFixture(ReadDeal(fixture.GetProperty("initial")));
        var expectedDeliveries = new List<object>();
        Equal(fixture.GetProperty("expectedInitial"), Snapshot(game));
        var index = -1;
        foreach (var step in fixture.GetProperty("steps").EnumerateArray())
        {
            var kind = step.GetProperty("kind").GetString();
            if (kind == "redeal")
            {
                var replacement = ReadDeal(step.GetProperty("deal"));
                var handNumber = game.HandNumber; var serial = game.DealSerial; var dealer = game.Dealer;
                var scores = game.Scores.ToArray();
                game.Redeal();
                if (replacement.HandNumber != handNumber || replacement.DealSerial != serial + 1 ||
                    replacement.Dealer != dealer || !replacement.Scores.SequenceEqual(scores))
                    throw new InvalidDataException("Invalid redeal identity.");
                game = GameEngine.CreateFixture(replacement);
            }
            else
            {
                var seat = step.GetProperty("seat").GetInt32();
                if (game.CurrentSeat != seat) throw new InvalidDataException("Wrong fixture actor.");
                expectedDeliveries.Add(new { afterStep = index, kind = "decision", seat, view = Project(game, seat) });
                if (kind == "action")
                {
                    var action = step.GetProperty("id").GetInt32();
                    if (!LegalActionIds(game.CreateView(seat)).Contains(action)) throw new InvalidDataException("Illegal fixture action.");
                    ActionApplier.Apply(game, seat, ActionFromId(action));
                }
                else if (kind == "penalty")
                {
                    var rejected = false;
                    try { ActionApplier.Apply(game, seat, ActionFromId(step.GetProperty("illegalAction").GetInt32())); }
                    catch (GameRuleException) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Penalty action was legal.");
                    game.ApplyIllegalPlayPenalty(seat);
                }
                else throw new InvalidDataException("Unknown fixture step.");
            }
            Equal(step.GetProperty("expected"), Snapshot(game));
            index++;
        }
        if (game.Phase is not GamePhase.HandComplete and not GamePhase.GameComplete)
            throw new InvalidDataException("Fixture did not reach a hand boundary.");
        for (var seat = 0; seat < 4; seat++)
            expectedDeliveries.Add(new { afterStep = index, kind = "terminal", seat, view = Project(game, seat) });
        Equal(fixture.GetProperty("deliveries"), expectedDeliveries);
    }

    private static void Equal(JsonElement expected, object actual)
    {
        if (!JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), JsonSerializer.SerializeToNode(actual, Json)))
            throw new InvalidDataException("Fixture projection or observation cadence differs from authority.");
    }
}
