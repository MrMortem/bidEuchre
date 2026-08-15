using BidEuchre.Core;
using BidEuchre.Protocol;
using BidEuchre.App;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Deck contains 24 unique cards", () => Run(GameRuleTests.DeckIsCorrect)),
    ("Card notation round-trips", () => Run(GameRuleTests.CardNotationRoundTrips)),
    ("Card display ranks use familiar labels", () => Run(GameRuleTests.CardDisplayRanksAreCorrect)),
    ("Left Bower has the effective trump suit", () => Run(GameRuleTests.LeftBowerChangesSuit)),
    ("Follow-suit legality honors the Left Bower", () => Run(GameRuleTests.FollowSuitUsesEffectiveSuit)),
    ("High, Low, and Trump tricks resolve correctly", () => Run(GameRuleTests.TrickWinnersAreCorrect)),
    ("Dealer is forced after three passes", () => Run(GameEngineTests.DealerIsForcedToBid)),
    ("Auction hides and defers the contract", () => Run(GameEngineTests.ContractIsChosenAfterAuction)),
    ("Partners Best exchange sits the partner out", () => Run(GameEngineTests.PartnersBestExchangeWorks)),
    ("Alone skips the sitting partner for every trick", () => Run(GameEngineTests.AloneSkipsPartner)),
    ("A complete normal hand scores both teams", () => Run(GameEngineTests.CompleteHandScores)),
    ("A complete hand log retains every card", () => Run(GameEngineTests.CompleteHandLogContainsEveryCard)),
    ("Illegal defender play awards the maximum", () => Run(GameEngineTests.IllegalDefenderAwardsMaximum)),
    ("Illegal bidder play sets the contract", () => Run(GameEngineTests.IllegalBidderIsSet)),
    ("Action notation round-trips", () => Run(ProtocolTests.ActionNotationRoundTrips)),
    ("Position payload round-trips", () => Run(ProtocolTests.PositionRoundTrips)),
    ("Spectator view is valid between hands", () => Run(ProtocolTests.SpectatorViewBetweenHands)),
    ("Command tokenizer handles quoting", () => Run(ProtocolTests.TokenizerHandlesQuotes)),
    ("Engine host completes a BEUCI exchange", ProtocolTests.EngineHostExchange),
    ("Disposed engine clients reject new work", ProtocolTests.DisposedClientRejectsWork),
    ("Session layer completes a human-controlled hand", SessionTests.HumanSessionCompletesHand),
    ("Session layer advances a four-bot hand", SessionTests.BotSessionCompletesHand),
    ("Bot turns are paced asynchronously", SessionTests.BotTurnsArePaced),
    ("Every bot observes a normally completed hand", SessionTests.BotsObserveCompletedHand),
    ("Bots observe a human illegal-play penalty", SessionTests.BotsObserveHumanPenalty),
    ("Bots observe a bot illegal-play penalty", SessionTests.BotsObserveBotPenalty),
    ("An active table can be disposed and replaced safely", SessionTests.ActiveTableCanBeReplaced),
    ("Mixed human and bot turns resume reliably", SessionTests.MixedTurnsResume),
    ("Mixed Partners Best session skips the bot partner", SessionTests.MixedPartnersBestSkipsBotPartner)
};

var prologEngine = Environment.GetEnvironmentVariable("BIDEUCHRE_PROLOG_ENGINE");
if (!string.IsNullOrWhiteSpace(prologEngine))
{
    tests.Add(("Basic Prolog engine completes normal and solo-contract hands",
        () => ExternalEngineTests.CompletesHands(
            prologEngine,
            new EngineIdentity("Basic Prolog Bot", "Bid Euchre Project", "1"))));
}

var pythonCfrEngine = Environment.GetEnvironmentVariable("BIDEUCHRE_PYTHON_CFR_ENGINE");
if (!string.IsNullOrWhiteSpace(pythonCfrEngine))
{
    tests.Add(("Python CFR engine completes normal and solo-contract hands",
        () => ExternalEngineTests.CompletesHands(
            pythonCfrEngine,
            new EngineIdentity("PyTorch CFR Bot", "Bid Euchre Project", "1"))));
}

var cppHeuristicEngine = Environment.GetEnvironmentVariable("BIDEUCHRE_CPP_HEURISTIC_ENGINE");
if (!string.IsNullOrWhiteSpace(cppHeuristicEngine))
{
    tests.Add(("C++ heuristic engine completes normal and solo-contract hands",
        () => ExternalEngineTests.CompletesHands(
            cppHeuristicEngine,
            new EngineIdentity("C++ Heuristic Bot", "Bid Euchre Project", "1"))));
}

var cppStrengthEngine = Environment.GetEnvironmentVariable("BIDEUCHRE_CPP_STRENGTH_ENGINE");
if (!string.IsNullOrWhiteSpace(cppStrengthEngine))
{
    tests.Add(("C++ heuristic defeats TableBot over the fixed strength corpus",
        () => CppStrengthTests.BeatsTableBot(cppStrengthEngine)));
}

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}\n      {exception}");
    }
}

Console.WriteLine($"\n{tests.Count - failures.Count}/{tests.Count} tests passed.");
if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

static Task Run(Action action)
{
    action();
    return Task.CompletedTask;
}

internal static class Assert
{
    public static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void False(bool value, string? message = null) => True(!value, message ?? "Expected false.");

    public static void Equal<T>(T expected, T actual, string? message = null) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
        }
    }

    public static void Contains<T>(T expected, IEnumerable<T> values)
    {
        if (!values.Contains(expected)) throw new InvalidOperationException($"Expected collection to contain {expected}.");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

internal static class GameRuleTests
{
    public static void DeckIsCorrect()
    {
        var deck = GameRules.CreateDeck();
        Assert.Equal(24, deck.Count);
        Assert.Equal(24, deck.Distinct().Count());
        Assert.Equal(6, deck.Count(card => card.Suit is Suit.Hearts));
    }

    public static void CardNotationRoundTrips()
    {
        foreach (var card in GameRules.CreateDeck()) Assert.Equal(card, Card.Parse(card.Code.ToLowerInvariant()));
        Assert.Throws<FormatException>(() => Card.Parse("1X"));
    }

    public static void CardDisplayRanksAreCorrect()
    {
        var expected = new[] { "9", "10", "J", "Q", "K", "A" };
        var actual = Enum.GetValues<Rank>().Select(Card.DisplayRank).ToArray();
        Assert.Equal(string.Join(',', expected), string.Join(',', actual));
    }

    public static void LeftBowerChangesSuit()
    {
        var hearts = Contract.Create(BidLevel.Three, ContractMode.Trump, Suit.Hearts);
        Assert.Equal(Suit.Hearts, GameRules.EffectiveSuit(Card.Parse("JD"), hearts));
        Assert.Equal(Suit.Diamonds, GameRules.EffectiveSuit(Card.Parse("AD"), hearts));
    }

    public static void FollowSuitUsesEffectiveSuit()
    {
        var hearts = Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Hearts);
        var hand = new[] { Card.Parse("JD"), Card.Parse("AC"), Card.Parse("9S") };
        var legal = GameRules.LegalCards(hand, [new CardPlay(0, Card.Parse("9H"))], hearts);
        Assert.Equal(1, legal.Count);
        Assert.Equal(Card.Parse("JD"), legal[0]);

        var diamondLead = GameRules.LegalCards(hand, [new CardPlay(0, Card.Parse("AD"))], hearts);
        Assert.Equal(3, diamondLead.Count);
    }

    public static void TrickWinnersAreCorrect()
    {
        var plays = new[]
        {
            new CardPlay(0, Card.Parse("9C")),
            new CardPlay(1, Card.Parse("AC")),
            new CardPlay(2, Card.Parse("TC")),
            new CardPlay(3, Card.Parse("KC"))
        };
        Assert.Equal(1, GameRules.DetermineTrickWinner(plays, Contract.Create(BidLevel.Four, ContractMode.High, null)));
        Assert.Equal(0, GameRules.DetermineTrickWinner(plays, Contract.Create(BidLevel.Four, ContractMode.Low, null)));

        var trump = new[]
        {
            new CardPlay(0, Card.Parse("AS")),
            new CardPlay(1, Card.Parse("9H")),
            new CardPlay(2, Card.Parse("JD")),
            new CardPlay(3, Card.Parse("JH"))
        };
        Assert.Equal(3, GameRules.DetermineTrickWinner(trump, Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Hearts)));
    }
}

internal static class GameEngineTests
{
    private static readonly string[] Names = ["South", "West", "North", "East"];

    public static void DealerIsForcedToBid()
    {
        var game = NewGame();
        game.PlaceBid(1, null);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        Assert.Equal(0, game.CurrentSeat!.Value);
        Assert.False(game.CanPass);
        Assert.Throws<GameRuleException>(() => game.PlaceBid(0, null));
        game.PlaceBid(0, BidLevel.Three);
        Assert.Equal(GamePhase.ChoosingContract, game.Phase);
        Assert.Equal(0, game.Bidder!.Value);
    }

    public static void ContractIsChosenAfterAuction()
    {
        var game = NewGame();
        game.PlaceBid(1, BidLevel.Four);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        Assert.Equal(BidLevel.Four, game.HighBid!.Value);
        Assert.True(game.Contract is null);
        game.ChooseContract(1, ContractMode.Low);
        Assert.Equal(ContractMode.Low, game.Contract!.Mode);
        Assert.Equal(GamePhase.Playing, game.Phase);

        var three = NewGame();
        three.PlaceBid(1, BidLevel.Three);
        three.PlaceBid(2, null);
        three.PlaceBid(3, null);
        three.PlaceBid(0, null);
        Assert.Throws<GameRuleException>(() => three.ChooseContract(1, ContractMode.High));
    }

    public static void PartnersBestExchangeWorks()
    {
        var game = NewGame();
        game.PlaceBid(1, BidLevel.PartnersBest);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        game.ChooseContract(1, ContractMode.Trump, Suit.Spades);
        Assert.Equal(GamePhase.ExchangingBidderCard, game.Phase);
        var bidderCard = game.CreateView(1).Players[1].Cards![0];
        game.ExchangeCard(1, bidderCard);
        Assert.Equal(5, game.CreateView(1).Players[1].CardCount);
        Assert.Equal(7, game.CreateView(3).Players[3].CardCount);
        Assert.True(game.CreateView(0).Players[3].Cards is null);
        var returnCard = game.CreateView(3).Players[3].Cards!.First(card => card != bidderCard);
        Assert.False(game.CreateView(3).Players[3].IsSittingOut);
        game.ExchangeCard(3, returnCard);
        Assert.Equal(6, game.CreateView(1).Players[1].CardCount);
        Assert.Equal(6, game.CreateView(3).Players[3].CardCount);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.True(game.CreateView(3).Players[3].IsSittingOut);
        Assert.Equal(0, game.GetLegalCards(3).Count);

        var playGuard = 18;
        while (game.Phase is GamePhase.Playing && playGuard-- > 0)
        {
            var seat = game.CurrentSeat!.Value;
            Assert.False(seat == 3, "The Partners Best bidder's partner was given a turn.");
            game.PlayCard(seat, game.GetLegalCards(seat)[0]);
        }
        Assert.True(playGuard >= 0, "Partners Best did not complete after 18 active-player cards.");
        Assert.Equal(GamePhase.HandComplete, game.Phase);
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.Count == 3));
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.All(play => play.Seat != 3)));
        Assert.Equal(18, game.CompletedTricks.Sum(trick => trick.Plays.Count));
        Assert.Equal(6, game.CreateView(3).Players[3].CardCount);
        Assert.True(game.CreateView(3).Players[3].IsSittingOut);
    }

    public static void AloneSkipsPartner()
    {
        var game = NewGame();
        game.PlaceBid(1, null);
        game.PlaceBid(2, null);
        game.PlaceBid(3, BidLevel.Alone);
        game.PlaceBid(0, null);
        game.ChooseContract(3, ContractMode.High);
        Assert.Equal(2, game.CurrentSeat!.Value);
        Assert.True(game.CreateView(1).Players[1].IsSittingOut);

        for (var index = 0; index < 3; index++)
        {
            var seat = game.CurrentSeat!.Value;
            Assert.False(seat == 1, "The Alone bidder's partner was given a turn.");
            game.PlayCard(seat, game.GetLegalCards(seat)[0]);
        }
        Assert.Equal(1, game.CompletedTricks.Count);

        PlayOut(game);
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.Count == 3));
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.All(play => play.Seat != 1)));
        Assert.Equal(18, game.CompletedTricks.Sum(trick => trick.Plays.Count));
        Assert.Equal(6, game.CreateView(1).Players[1].CardCount);
    }

    public static void CompleteHandScores()
    {
        var game = NewGame();
        game.PlaceBid(1, BidLevel.Three);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        game.ChooseContract(1, ContractMode.Trump, Suit.Clubs);
        PlayOut(game);

        Assert.Equal(GamePhase.HandComplete, game.Phase);
        Assert.Equal(6, game.TricksByTeam.Sum());
        var result = game.HandHistory.Single();
        Assert.Equal(result.DefendingTeamTricks, result.TeamZeroDelta);
        var expectedBidder = result.BiddingTeamTricks >= 3 ? result.BiddingTeamTricks : -3;
        Assert.Equal(expectedBidder, result.TeamOneDelta);
    }

    public static void CompleteHandLogContainsEveryCard()
    {
        var game = ActiveThreeBid();
        PlayOut(game);

        var view = game.CreateView();
        Assert.Equal(24, view.Events.Count(item => item.Contains(" played ")));
    }

    public static void IllegalDefenderAwardsMaximum()
    {
        var game = ActiveThreeBid();
        game.ApplyIllegalPlayPenalty(2);
        Assert.Equal(0, game.Scores[0]);
        Assert.Equal(6, game.Scores[1]);
        Assert.Equal(GamePhase.HandComplete, game.Phase);
    }

    public static void IllegalBidderIsSet()
    {
        var game = ActiveThreeBid();
        game.ApplyIllegalPlayPenalty(1);
        Assert.Equal(6, game.Scores[0]);
        Assert.Equal(-3, game.Scores[1]);
    }

    private static GameEngine ActiveThreeBid()
    {
        var game = NewGame();
        game.PlaceBid(1, BidLevel.Three);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        game.ChooseContract(1, ContractMode.Trump, Suit.Hearts);
        return game;
    }

    private static GameEngine NewGame()
    {
        var game = new GameEngine(Names, randomSeed: 42);
        game.StartGame(dealer: 0);
        return game;
    }

    private static void PlayOut(GameEngine game)
    {
        var guard = 30;
        while (game.Phase is GamePhase.Playing && guard-- > 0)
        {
            var seat = game.CurrentSeat!.Value;
            game.PlayCard(seat, game.GetLegalCards(seat)[0]);
        }
        Assert.True(guard > 0, "Game did not complete within the card limit.");
    }
}

internal static class ProtocolTests
{
    public static void ActionNotationRoundTrips()
    {
        BotAction[] actions =
        [
            new BotAction.Pass(),
            new BotAction.Bid(BidLevel.PartnersBest),
            new BotAction.ChooseContract(ContractMode.Trump, Suit.Hearts),
            new BotAction.ChooseContract(ContractMode.Low, null),
            new BotAction.Exchange(Card.Parse("AS")),
            new BotAction.Play(Card.Parse("9D"))
        ];
        foreach (var action in actions) Assert.Equal(action, BotActionNotation.Parse(BotActionNotation.Format(action)));
    }

    public static void PositionRoundTrips()
    {
        var game = new GameEngine(["A", "B", "C", "D"], 8);
        game.StartGame(0);
        var original = new BotPosition(1, game.CreateView(1));
        var decoded = PositionCodec.Decode(PositionCodec.Encode(original));
        Assert.Equal(1, decoded.Seat);
        Assert.Equal(6, decoded.Game.Players[1].Cards!.Count);
        Assert.True(decoded.Game.Players[0].Cards is null);
    }

    public static void SpectatorViewBetweenHands()
    {
        var game = new GameEngine(["A", "B", "C", "D"], 4);
        game.StartGame(0);
        game.PlaceBid(1, BidLevel.Three);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        game.ChooseContract(1, ContractMode.Trump, Suit.Spades);
        while (game.Phase is GamePhase.Playing)
        {
            var seat = game.CurrentSeat!.Value;
            game.PlayCard(seat, game.GetLegalCards(seat)[0]);
        }

        var view = game.CreateView();
        Assert.Equal(GamePhase.HandComplete, view.Phase);
        Assert.True(view.Players.All(player => player.Cards is null));
        Assert.Equal(0, view.LegalActions.Cards.Count);
    }

    public static void TokenizerHandlesQuotes()
    {
        var tokens = CommandTokenizer.Tokenize("setoption name \"Risk Level\" value \"quite high\"");
        Assert.Equal(5, tokens.Count);
        Assert.Equal("Risk Level", tokens[2]);
        Assert.Equal("quite high", tokens[4]);
    }

    public static async Task EngineHostExchange()
    {
        var game = new GameEngine(["A", "B", "C", "D"], 9);
        game.StartGame(0);
        var payload = PositionCodec.Encode(new BotPosition(1, game.CreateView(1)));
        using var input = new StringReader($"beuci\nisready\nposition {payload}\ngo\nquit\n");
        using var output = new StringWriter();
        await new EngineHost(new PassBot()).RunAsync(input, output);
        var transcript = output.ToString();
        Assert.True(transcript.Contains("beuciok"));
        Assert.True(transcript.Contains("readyok"));
        Assert.True(transcript.Contains("bestaction pass"));
    }

    public static async Task DisposedClientRejectsWork()
    {
        var client = new EngineProcessClient("unused-engine-path");
        var firstDisposal = client.DisposeAsync().AsTask();
        var secondDisposal = client.DisposeAsync().AsTask();
        Assert.True(
            ReferenceEquals(firstDisposal, secondDisposal),
            "Concurrent engine teardown callers did not join the same cleanup operation.");
        await Task.WhenAll(firstDisposal, secondDisposal);

        Assert.False(client.IsRunning);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.NewGameAsync());
    }

    private sealed class PassBot : IBidEuchreBot
    {
        public string Name => "Test Bot";
        public string Author => "Tests";
        public ValueTask<BotAction> ChooseActionAsync(BotPosition position, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BotAction>(new BotAction.Pass());
    }
}

internal static class SessionTests
{
    public static async Task HumanSessionCompletesHand()
    {
        var catalog = new EngineCatalog();
        await using var session = new GameSession(
            "human-test",
            "Human Test",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(seat, $"Human {seat + 1}", PlayerKind.Human, null))
                .ToArray(),
            catalog,
            seed: 21);
        await session.StartAsync();

        var firstBid = true;
        var guard = 40;
        while (session.Game!.Phase is not GamePhase.HandComplete && guard-- > 0)
        {
            var seat = session.Game.CurrentSeat!.Value;
            var view = (await session.GetViewAsync(seat))!;
            GameActionRequest action;
            if (view.Phase is GamePhase.Bidding)
            {
                action = firstBid
                    ? new GameActionRequest(seat, "bid", BidLevel.Three, null, null, null)
                    : new GameActionRequest(seat, "pass", null, null, null, null);
                firstBid = false;
            }
            else if (view.Phase is GamePhase.ChoosingContract)
            {
                action = new GameActionRequest(seat, "contract", null, ContractMode.Trump, Suit.Clubs, null);
            }
            else
            {
                action = new GameActionRequest(seat, "play", null, null, null, view.LegalActions.Cards[0].Code);
            }

            await session.ExecuteAsync(action);
        }

        Assert.True(guard > 0, "Human-controlled session did not finish a hand.");
        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.Equal(6, session.Game.CompletedTricks.Count);
    }

    public static async Task BotSessionCompletesHand()
    {
        var catalog = new EngineCatalog();
        await using var session = new GameSession(
            "bot-test",
            "Bot Test",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    $"Bot {seat + 1}",
                    PlayerKind.Bot,
                    EngineCatalog.BuiltInEngineId))
                .ToArray(),
            catalog,
            seed: 22,
            botActionDelay: TimeSpan.Zero);
        await session.StartAsync();
        await session.WaitForBotsAsync();

        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.Equal(6, session.Game.CompletedTricks.Count);
        Assert.Equal(6, session.Game.TricksByTeam.Sum());
        var spectator = (await session.GetViewAsync(null))!;
        Assert.True(spectator.Players.All(player => player.Cards is null));
    }

    public static async Task BotTurnsArePaced()
    {
        var catalog = new EngineCatalog();
        await using var session = new GameSession(
            "paced-test",
            "Paced Bot Test",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    $"Bot {seat + 1}",
                    PlayerKind.Bot,
                    EngineCatalog.BuiltInEngineId))
                .ToArray(),
            catalog,
            seed: 23,
            botActionDelay: TimeSpan.FromMilliseconds(50),
            completedTrickDelay: TimeSpan.FromMilliseconds(80));

        await session.StartAsync();
        Assert.Equal(GamePhase.Bidding, session.Game!.Phase);
        Assert.Equal(1, session.Game.CreateView().Events.Count);

        await Task.Delay(80);
        Assert.True(session.Game.CreateView().Events.Count > 1, "The first paced bot action did not become visible.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await session.WaitForBotsAsync(timeout.Token);
        Assert.Equal(GamePhase.HandComplete, session.Game.Phase);
        Assert.True(session.Game.CreateView().Events.Any(item => item.Contains(" played ")));
    }

    public static async Task BotsObserveCompletedHand()
    {
        var catalog = new EngineCatalog();
        var drivers = Enumerable.Range(0, 4)
            .ToDictionary(seat => seat, _ => new RecordingBotDriver());
        await using var session = new GameSession(
            "terminal-observation-test",
            "Terminal Observation Test",
            BotSeats(),
            catalog,
            seed: 34,
            botActionDelay: TimeSpan.Zero,
            completedTrickDelay: TimeSpan.Zero,
            botDriverFactory: (seat, _) => drivers[seat.Seat]);

        await session.StartAsync();
        await session.WaitForBotsAsync();

        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        AssertTerminalObservations(drivers);

        await session.StartNextHandAsync();
        await session.WaitForBotsAsync();
        Assert.Equal(GamePhase.HandComplete, session.Game.Phase);
        AssertTerminalObservations(drivers, expectedCount: 2);
    }

    public static async Task BotsObserveHumanPenalty()
    {
        var catalog = new EngineCatalog();
        var drivers = Enumerable.Range(1, 3)
            .ToDictionary(seat => seat, _ => new RecordingBotDriver());
        var seats = Enumerable.Range(0, 4)
            .Select(seat => new SeatConfiguration(
                seat,
                seat == 0 ? "Human" : $"Bot {seat + 1}",
                seat == 0 ? PlayerKind.Human : PlayerKind.Bot,
                seat == 0 ? null : EngineCatalog.BuiltInEngineId))
            .ToArray();
        await using var session = new GameSession(
            "human-penalty-observation-test",
            "Human Penalty Observation Test",
            seats,
            catalog,
            seed: 35,
            botActionDelay: TimeSpan.Zero,
            completedTrickDelay: TimeSpan.Zero,
            botDriverFactory: (seat, _) => drivers[seat.Seat]);

        await session.StartAsync();
        var guard = 20;
        var penalized = false;
        while (guard-- > 0)
        {
            await session.WaitForBotsAsync();
            Assert.Equal(0, session.Game!.CurrentSeat!.Value, "The bot loop did not stop for the human seat.");
            var view = (await session.GetViewAsync(0))!;
            if (view.Phase is GamePhase.Playing)
            {
                var hand = view.Players.Single(player => player.Seat == 0).Cards!;
                var illegalCard = GameRules.CreateDeck().First(card => !hand.Contains(card));
                await session.ExecuteAsync(new GameActionRequest(0, "play", null, null, null, illegalCard.Code));
                penalized = true;
                break;
            }

            var action = view.Phase switch
            {
                GamePhase.Bidding when view.LegalActions.CanPass =>
                    new GameActionRequest(0, "pass", null, null, null, null),
                GamePhase.Bidding =>
                    new GameActionRequest(0, "bid", view.LegalActions.Bids[0], null, null, null),
                GamePhase.ChoosingContract when view.LegalActions.ContractModes[0] is ContractMode.Trump =>
                    new GameActionRequest(0, "contract", null, ContractMode.Trump, Suit.Clubs, null),
                GamePhase.ChoosingContract =>
                    new GameActionRequest(0, "contract", null, view.LegalActions.ContractModes[0], null, null),
                _ => throw new InvalidOperationException($"Unexpected phase before the human penalty: {view.Phase}.")
            };
            await session.ExecuteAsync(action);
        }

        Assert.True(penalized, "The human did not receive a card-play turn.");
        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.True(session.LastError?.Contains("illegal card play") is true);
        AssertTerminalObservations(drivers);
    }

    public static async Task BotsObserveBotPenalty()
    {
        var catalog = new EngineCatalog();
        var drivers = Enumerable.Range(0, 4)
            .ToDictionary(seat => seat, _ => new RecordingBotDriver(failOnPlay: true));
        await using var session = new GameSession(
            "bot-penalty-observation-test",
            "Bot Penalty Observation Test",
            BotSeats(),
            catalog,
            seed: 36,
            botActionDelay: TimeSpan.Zero,
            completedTrickDelay: TimeSpan.Zero,
            botDriverFactory: (seat, _) => drivers[seat.Seat]);

        await session.StartAsync();
        await session.WaitForBotsAsync();

        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.True(session.LastError?.Contains("Bot in seat") is true);
        AssertTerminalObservations(drivers);
    }

    public static async Task ActiveTableCanBeReplaced()
    {
        var catalog = new EngineCatalog();
        var active = new GameSession(
            "active-table",
            "Active Table",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    $"Bot {seat + 1}",
                    PlayerKind.Bot,
                    EngineCatalog.BuiltInEngineId))
                .ToArray(),
            catalog,
            seed: 31,
            botActionDelay: TimeSpan.FromSeconds(3));
        await active.StartAsync();
        Assert.Equal(GamePhase.Bidding, active.Game!.Phase);

        var firstDisposal = active.DisposeAsync().AsTask();
        var secondDisposal = active.DisposeAsync().AsTask();
        Assert.True(
            ReferenceEquals(firstDisposal, secondDisposal),
            "Concurrent table teardown callers did not join the same cleanup operation.");
        await Task.WhenAll(firstDisposal, secondDisposal);

        Assert.Equal(GamePhase.Bidding, active.Game.Phase);
        Assert.Equal(0, active.Game.Scores[0]);
        Assert.Equal(0, active.Game.Scores[1]);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => active.GetViewAsync(null));

        await using var replacement = new GameSession(
            "replacement-table",
            "Replacement Table",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    $"Player {seat + 1}",
                    PlayerKind.Human,
                    null))
                .ToArray(),
            catalog,
            seed: 32,
            botActionDelay: TimeSpan.Zero);
        await replacement.StartAsync();
        var replacementView = await replacement.GetViewAsync(replacement.Game!.CurrentSeat);
        Assert.True(replacementView is not null);
        Assert.Equal(GamePhase.Bidding, replacementView!.Phase);
        Assert.Equal(1, replacementView.HandNumber);
    }

    public static async Task MixedTurnsResume()
    {
        var catalog = new EngineCatalog();
        await using var session = new GameSession(
            "mixed-test",
            "Mixed Test",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    seat == 0 ? "Human" : $"Bot {seat + 1}",
                    seat == 0 ? PlayerKind.Human : PlayerKind.Bot,
                    seat == 0 ? null : EngineCatalog.BuiltInEngineId))
                .ToArray(),
            catalog,
            seed: 24,
            botActionDelay: TimeSpan.FromMilliseconds(5),
            completedTrickDelay: TimeSpan.FromMilliseconds(5));
        await session.StartAsync();

        var guard = 30;
        while (session.Game!.Phase is not GamePhase.HandComplete && guard-- > 0)
        {
            using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await session.WaitForBotsAsync(waitTimeout.Token);
            if (session.Game.Phase is GamePhase.HandComplete)
            {
                break;
            }

            Assert.Equal(0, session.Game.CurrentSeat!.Value, "Bots stopped on a non-human seat.");
            var view = (await session.GetViewAsync(0))!;
            var action = view.Phase switch
            {
                GamePhase.Bidding => view.LegalActions.CanPass
                    ? new GameActionRequest(0, "pass", null, null, null, null)
                    : new GameActionRequest(0, "bid", view.LegalActions.Bids[0], null, null, null),
                GamePhase.ChoosingContract => new GameActionRequest(0, "contract", null, ContractMode.Trump, Suit.Clubs, null),
                GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard =>
                    new GameActionRequest(0, "exchange", null, null, null, view.LegalActions.Cards[0].Code),
                GamePhase.Playing => new GameActionRequest(0, "play", null, null, null, view.LegalActions.Cards[0].Code),
                _ => throw new InvalidOperationException($"Unexpected mixed-session phase {view.Phase}.")
            };
            await session.ExecuteAsync(action);
        }

        Assert.True(guard > 0, "Mixed human/bot hand did not finish.");
        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.Equal(6, session.Game.CompletedTricks.Count);
    }

    public static async Task MixedPartnersBestSkipsBotPartner()
    {
        var catalog = new EngineCatalog();
        await using var session = new GameSession(
            "partners-best-mixed-test",
            "Partners Best Mixed Test",
            Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(
                    seat,
                    seat == 0 ? "Human Bidder" : $"Bot {seat + 1}",
                    seat == 0 ? PlayerKind.Human : PlayerKind.Bot,
                    seat == 0 ? null : EngineCatalog.BuiltInEngineId))
                .ToArray(),
            catalog,
            seed: 24,
            botActionDelay: TimeSpan.Zero,
            completedTrickDelay: TimeSpan.Zero);
        await session.StartAsync();

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            await session.WaitForBotsAsync(timeout.Token);
        }
        Assert.Equal(0, session.Game!.CurrentSeat!.Value);
        var biddingView = (await session.GetViewAsync(0))!;
        Assert.Equal(GamePhase.Bidding, biddingView.Phase);
        Assert.Contains(BidLevel.PartnersBest, biddingView.LegalActions.Bids);
        await session.ExecuteAsync(new GameActionRequest(0, "bid", BidLevel.PartnersBest, null, null, null));

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            await session.WaitForBotsAsync(timeout.Token);
        }
        Assert.Equal(GamePhase.ChoosingContract, session.Game.Phase);
        Assert.Equal(0, session.Game.CurrentSeat!.Value);
        await session.ExecuteAsync(new GameActionRequest(0, "contract", null, ContractMode.Trump, Suit.Clubs, null));

        var exchangeView = (await session.GetViewAsync(0))!;
        Assert.Equal(GamePhase.ExchangingBidderCard, exchangeView.Phase);
        Assert.False(exchangeView.Players[2].IsSittingOut);
        await session.ExecuteAsync(new GameActionRequest(
            0,
            "exchange",
            null,
            null,
            null,
            exchangeView.LegalActions.Cards[0].Code));

        var guard = 8;
        while (session.Game.Phase is not GamePhase.HandComplete && guard-- > 0)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await session.WaitForBotsAsync(timeout.Token);
            if (session.Game.Phase is GamePhase.HandComplete)
            {
                break;
            }

            Assert.Equal(GamePhase.Playing, session.Game.Phase);
            Assert.Equal(0, session.Game.CurrentSeat!.Value, "The bot loop stopped on the sitting partner or an opponent.");
            var view = (await session.GetViewAsync(0))!;
            Assert.True(view.Players[2].IsSittingOut);
            await session.ExecuteAsync(new GameActionRequest(
                0,
                "play",
                null,
                null,
                null,
                view.LegalActions.Cards[0].Code));
        }

        Assert.True(guard > 0, "The mixed Partners Best hand did not finish.");
        Assert.Equal(GamePhase.HandComplete, session.Game.Phase);
        Assert.Equal(6, session.Game.CompletedTricks.Count);
        Assert.True(session.Game.CompletedTricks.All(trick => trick.Plays.Count == 3));
        Assert.True(session.Game.CompletedTricks.All(trick => trick.Plays.All(play => play.Seat != 2)));
        Assert.Equal(18, session.Game.CompletedTricks.Sum(trick => trick.Plays.Count));
        var partnerView = (await session.GetViewAsync(2))!;
        Assert.True(partnerView.Players[2].IsSittingOut);
        Assert.Equal(6, partnerView.Players[2].CardCount);
    }

    private static SeatConfiguration[] BotSeats() => Enumerable.Range(0, 4)
        .Select(seat => new SeatConfiguration(
            seat,
            $"Bot {seat + 1}",
            PlayerKind.Bot,
            EngineCatalog.BuiltInEngineId))
        .ToArray();

    private static void AssertTerminalObservations(
        IReadOnlyDictionary<int, RecordingBotDriver> drivers,
        int expectedCount = 1)
    {
        foreach (var (seat, driver) in drivers)
        {
            Assert.Equal(
                expectedCount,
                driver.Observations.Count,
                $"Seat {seat + 1} did not receive one terminal observation per completed hand.");
            var observation = driver.Observations[^1];
            Assert.Equal(seat, observation.Seat);
            Assert.Equal(expectedCount, observation.Game.HandNumber);
            Assert.True(observation.Game.Phase is GamePhase.HandComplete or GamePhase.GameComplete);
            Assert.True(observation.Game.CurrentSeat is null);
            Assert.True(observation.Game.Players.Single(player => player.Seat == seat).Cards is not null);
            Assert.True(observation.Game.Players
                .Where(player => player.Seat != seat)
                .All(player => player.Cards is null));
            Assert.Equal(0, observation.Game.LegalActions.Cards.Count);
        }
    }

    private sealed class RecordingBotDriver(bool failOnPlay = false) : IBotDriver
    {
        private readonly BuiltInBotDriver _inner = new();

        public List<BotPosition> Observations { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            _inner.StartAsync(cancellationToken);

        public Task NewGameAsync(CancellationToken cancellationToken = default) =>
            _inner.NewGameAsync(cancellationToken);

        public Task<BotAction> ChooseActionAsync(
            GameView view,
            int seat,
            CancellationToken cancellationToken = default)
        {
            if (failOnPlay && view.Phase is GamePhase.Playing)
            {
                var hand = view.Players.Single(player => player.Seat == seat).Cards!;
                var illegalCard = GameRules.CreateDeck().First(card => !hand.Contains(card));
                return Task.FromResult<BotAction>(new BotAction.Play(illegalCard));
            }

            return _inner.ChooseActionAsync(view, seat, cancellationToken);
        }

        public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observations.Add(new BotPosition(seat, view));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}

internal static class CppStrengthTests
{
    private static readonly string[] Names = ["Cpp South", "Table West", "Cpp North", "Table East"];

    public static async Task BeatsTableBot(string launcher)
    {
        const string arguments = "--samples 48 --play-ms 60 --search-depth 8 --search-nodes 8000 --seed 99173";
        IBotDriver[] drivers =
        [
            new ProcessBotDriver(new EngineDescriptor("cpp-0", "C++", "Project", false, launcher, arguments)),
            new BuiltInBotDriver(),
            new ProcessBotDriver(new EngineDescriptor("cpp-2", "C++", "Project", false, launcher, arguments)),
            new BuiltInBotDriver()
        ];
        try
        {
            foreach (var driver in drivers) await driver.StartAsync();
            var differential = 0;
            const int hands = 24;
            for (var index = 0; index < hands; index++)
            {
                foreach (var driver in drivers) await driver.NewGameAsync();
                var game = new GameEngine(Names, 4100 + index);
                game.StartGame(index % 4);
                var guard = 40;
                while (game.Phase is not GamePhase.HandComplete && guard-- > 0)
                {
                    var seat = game.CurrentSeat!.Value;
                    var view = game.CreateView(seat);
                    var action = await drivers[seat].ChooseActionAsync(view, seat);
                    Apply(game, seat, action);
                }
                Assert.True(guard > 0, "Strength-corpus hand did not complete.");
                differential += game.Scores[0] - game.Scores[1];
                for (var seat = 0; seat < 4; seat++)
                {
                    await drivers[seat].ObserveAsync(game.CreateView(seat), seat);
                }
            }
            Console.WriteLine($"INFO  C++ strength corpus differential: {differential:+#;-#;0} over {hands} hands");
            Assert.True(differential > 0,
                $"Expected the C++ team to beat TableBot; score differential was {differential}.");
        }
        finally
        {
            foreach (var driver in drivers) await driver.DisposeAsync();
        }
    }

    private static void Apply(GameEngine game, int seat, BotAction action)
    {
        switch (action)
        {
            case BotAction.Pass:
                game.PlaceBid(seat, null);
                break;
            case BotAction.Bid bid:
                game.PlaceBid(seat, bid.Level);
                break;
            case BotAction.ChooseContract contract:
                game.ChooseContract(seat, contract.Mode, contract.Trump);
                break;
            case BotAction.Exchange exchange:
                game.ExchangeCard(seat, exchange.Card);
                break;
            case BotAction.Play play:
                game.PlayCard(seat, play.Card);
                break;
            default:
                throw new InvalidOperationException("Unknown bot action in strength corpus.");
        }
    }
}

internal static class ExternalEngineTests
{
    private static readonly string[] Names = ["South", "West", "North", "East"];

    public static async Task CompletesHands(string launcher, EngineIdentity expectedIdentity)
    {
        var clients = Enumerable.Range(0, 4)
            .Select(_ => new EngineProcessClient(launcher))
            .ToArray();
        try
        {
            foreach (var client in clients)
            {
                var identity = await client.StartAsync();
                Assert.Equal(expectedIdentity.Name, identity.Name);
                Assert.Equal(expectedIdentity.Author, identity.Author);
                Assert.Equal(expectedIdentity.ProtocolVersion, identity.ProtocolVersion);
                await client.NewGameAsync();
            }

            await CompleteLiveAuctionHand(clients);
            await Reset(clients);
            await CompleteOrdinaryHand(clients);
            await Reset(clients);
            await CompletePartnersBestHand(clients);
            await Reset(clients);
            await CompleteAloneHand(clients);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static async Task CompleteOrdinaryHand(EngineProcessClient[] clients)
    {
        var game = NewGame(101);
        game.PlaceBid(1, BidLevel.Three);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);
        await AskAndApply(game, clients, 1);

        var guard = 40;
        while (game.Phase is not GamePhase.HandComplete && guard-- > 0)
        {
            await AskAndApply(game, clients, game.CurrentSeat!.Value);
        }

        Assert.True(guard > 0, "The external engines did not finish an ordinary hand.");
        Assert.Equal(BidLevel.Three, game.Contract!.Bid);
        Assert.Equal(6, game.CompletedTricks.Count);
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.Count == 4));
        await ObserveCompletedHand(game, clients);
    }

    private static async Task CompleteLiveAuctionHand(EngineProcessClient[] clients)
    {
        var game = NewGame(100);
        var guard = 4;
        while (game.Phase is GamePhase.Bidding && guard-- > 0)
        {
            await AskAndApply(game, clients, game.CurrentSeat!.Value);
        }

        Assert.True(guard >= 0, "The external engines did not complete a bidding round.");
        Assert.Equal(GamePhase.ChoosingContract, game.Phase);
        Assert.Equal(4, game.Auction.Count);

        guard = 30;
        while (game.Phase is not GamePhase.HandComplete && guard-- > 0)
        {
            await AskAndApply(game, clients, game.CurrentSeat!.Value);
        }

        Assert.True(guard > 0, "The external engines did not finish their live-auction hand.");
        Assert.Equal(6, game.CompletedTricks.Count);
        await ObserveCompletedHand(game, clients);
    }

    private static async Task CompletePartnersBestHand(EngineProcessClient[] clients)
    {
        var game = NewGame(202);
        game.PlaceBid(1, BidLevel.PartnersBest);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);

        await AskAndApply(game, clients, 1);
        Assert.Equal(GamePhase.ExchangingBidderCard, game.Phase);
        await AskAndApply(game, clients, 1);
        Assert.Equal(GamePhase.ExchangingPartnerCard, game.Phase);
        Assert.Equal(3, game.CurrentSeat!.Value);
        await AskAndApply(game, clients, 3);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.True(game.CreateView(3).Players[3].IsSittingOut);

        await PlayOutWithoutSeat(game, clients, 3);
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.Count == 3));
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.All(play => play.Seat != 3)));
        Assert.Equal(6, game.CreateView(3).Players[3].CardCount);
        await ObserveCompletedHand(game, clients);
    }

    private static async Task CompleteAloneHand(EngineProcessClient[] clients)
    {
        var game = NewGame(303);
        game.PlaceBid(1, BidLevel.Alone);
        game.PlaceBid(2, null);
        game.PlaceBid(3, null);
        game.PlaceBid(0, null);

        await AskAndApply(game, clients, 1);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.True(game.CreateView(3).Players[3].IsSittingOut);

        await PlayOutWithoutSeat(game, clients, 3);
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.Count == 3));
        Assert.True(game.CompletedTricks.All(trick => trick.Plays.All(play => play.Seat != 3)));
        Assert.Equal(6, game.CreateView(3).Players[3].CardCount);
        await ObserveCompletedHand(game, clients);
    }

    private static async Task PlayOutWithoutSeat(
        GameEngine game,
        EngineProcessClient[] clients,
        int sittingSeat)
    {
        var guard = 24;
        while (game.Phase is GamePhase.Playing && guard-- > 0)
        {
            var seat = game.CurrentSeat!.Value;
            Assert.False(seat == sittingSeat, "A sitting partner received a play request.");
            await AskAndApply(game, clients, seat);
        }

        Assert.True(guard > 0, "The external engines did not finish a three-player hand.");
        Assert.Equal(GamePhase.HandComplete, game.Phase);
        Assert.Equal(6, game.CompletedTricks.Count);
    }

    private static async Task AskAndApply(
        GameEngine game,
        EngineProcessClient[] clients,
        int seat)
    {
        Assert.Equal(seat, game.CurrentSeat!.Value);
        var view = game.CreateView(seat);
        var action = await clients[seat].ChooseActionAsync(view, seat);
        switch (action)
        {
            case BotAction.Pass:
                Assert.Equal(GamePhase.Bidding, view.Phase);
                Assert.True(view.LegalActions.CanPass, "The external engine returned an illegal pass.");
                game.PlaceBid(seat, null);
                break;
            case BotAction.Bid bid:
                Assert.Equal(GamePhase.Bidding, view.Phase);
                Assert.Contains(bid.Level, view.LegalActions.Bids);
                game.PlaceBid(seat, bid.Level);
                break;
            case BotAction.ChooseContract contract:
                Assert.Equal(GamePhase.ChoosingContract, view.Phase);
                Assert.Contains(contract.Mode, view.LegalActions.ContractModes);
                if (contract.Mode is ContractMode.Trump)
                {
                    Assert.True(contract.Trump is not null, "The external engine omitted the trump suit.");
                    Assert.Contains(contract.Trump!.Value, view.LegalActions.TrumpSuits);
                }
                game.ChooseContract(seat, contract.Mode, contract.Trump);
                break;
            case BotAction.Exchange exchange:
                Assert.True(view.Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard);
                Assert.Contains(exchange.Card, view.LegalActions.Cards);
                game.ExchangeCard(seat, exchange.Card);
                break;
            case BotAction.Play play:
                Assert.Equal(GamePhase.Playing, view.Phase);
                Assert.Contains(play.Card, view.LegalActions.Cards);
                game.PlayCard(seat, play.Card);
                break;
            default:
                throw new InvalidOperationException("The external engine returned an unknown action type.");
        }
    }

    private static async Task Reset(IEnumerable<EngineProcessClient> clients)
    {
        foreach (var client in clients)
        {
            await client.NewGameAsync();
        }
    }

    private static async Task ObserveCompletedHand(GameEngine game, IReadOnlyList<EngineProcessClient> clients)
    {
        for (var seat = 0; seat < clients.Count; seat++)
        {
            await clients[seat].ObserveAsync(game.CreateView(seat), seat);
        }
    }

    private static GameEngine NewGame(int seed)
    {
        var game = new GameEngine(Names, seed);
        game.StartGame(0);
        return game;
    }
}
