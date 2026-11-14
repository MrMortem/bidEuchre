using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class ProtocolBoundaryTests
{
    public static Task ContractValues()
    {
        var game = new GameEngine(["a", "b", "c", "d"], 1);
        game.StartGame(0);
        game.PlaceBid(1, BidLevel.Four);
        game.PlaceBid(2, null); game.PlaceBid(3, null); game.PlaceBid(0, null);
        var before = PositionCodec.Encode(new BotPosition(1, game.CreateView(1)));
        foreach (var (mode, suit) in new[] { ((ContractMode)99, (Suit?)null), (ContractMode.Trump, (Suit?)(Suit)99) })
        {
            Assert.Throws<GameRuleException>(() => game.ChooseContract(1, mode, suit));
            Assert.Equal(GamePhase.ChoosingContract, game.Phase);
            Assert.True(game.CreateView(1).Contract is null);
            Assert.Equal(1, game.CurrentSeat!.Value);
            Assert.Equal(before, PositionCodec.Encode(new BotPosition(1, game.CreateView(1))));
        }
        var valid = new[] { (ContractMode.High, (Suit?)null), (ContractMode.Low, (Suit?)null) }
            .Concat(Enum.GetValues<Suit>().Select(suit => (ContractMode.Trump, (Suit?)suit)));
        foreach (var (mode, suit) in valid)
        {
            var fresh = new GameEngine(["a", "b", "c", "d"], 1);
            fresh.StartGame(0);
            fresh.PlaceBid(1, BidLevel.Four);
            fresh.PlaceBid(2, null); fresh.PlaceBid(3, null); fresh.PlaceBid(0, null);
            fresh.ChooseContract(1, mode, suit);
            Assert.Equal(GamePhase.Playing, fresh.Phase);
            Assert.Equal(Contract.Create(BidLevel.Four, mode, suit), fresh.Contract!);
            var action = new BotAction.ChooseContract(mode, suit);
            Assert.Equal<BotAction>(action, BotActionNotation.Parse(BotActionNotation.Format(action)));
        }
        foreach (var line in new[] { "bestaction contract 99", "bestaction contract trump 99" })
            Assert.Throws<ProtocolException>(() => BotActionNotation.Parse(line));
        return Task.CompletedTask;
    }

    public static Task MalformedCardNotation()
    {
        foreach (var action in new[] { "play", "exchange" })
            foreach (var code in new[] { "ZZ", "AX", "SSS", "\" \"", "\"\t\"" })
                Assert.Throws<ProtocolException>(() => BotActionNotation.Parse($"bestaction {action} {code}"));
        return Task.CompletedTask;
    }

    public static async Task MalformedPlayRecovers()
    {
        await MalformedSession("malformed-play");
        await MalformedSession("malformed-play-whitespace");
    }

    public static async Task MalformedExchangeRecovers()
    {
        await MalformedSession("malformed-exchange");
        await MalformedSession("malformed-exchange-whitespace");
    }

    private static async Task MalformedSession(string mode)
    {
        var drivers = new List<ClientDriver>();
        await using var session = new GameSession(mode, mode, Enumerable.Range(0, 4)
            .Select(i => new SeatConfiguration(i, $"Bot {i}", PlayerKind.Bot, null)).ToArray(),
            new EngineCatalog(), 21, botActionDelay: TimeSpan.Zero,
            botDriverFactory: (_, _) => { var driver = new ClientDriver(mode); drivers.Add(driver); return driver; });
        await session.StartAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await session.WaitForBotsAsync(deadline.Token);
        Assert.True(session.BotLoopFault is null);
        Assert.Equal(GamePhase.HandComplete, session.Game!.Phase);
        Assert.True(drivers.Sum(d => d.MalformedResponses) > 0, "Engine never exercised malformed response");
        if (mode.StartsWith("malformed-play"))
        {
            Assert.True(!string.IsNullOrWhiteSpace(session.LastError));
            Assert.True(session.LastError!.Contains(mode.EndsWith("whitespace") ? "whitespace" : "Card 'ZZ'"));
            Assert.True(session.Game.HandHistory.Single().Reason.Contains("Illegal", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Equal(2, drivers.Sum(d => d.MalformedResponses), "Both exchange decisions must use fallback");
            Assert.True(session.Game.CompletedTricks.Count == 6, "Exchange fallback did not finish the hand");
            Assert.True(session.Game.HandHistory.Single().Contract.IsPartnersBest);
        }
    }

    public static Task DecisionTimeoutClosesChannel() => InterruptedDecision(false);
    public static Task DecisionCancellationClosesChannel() => InterruptedDecision(true);

    private static async Task InterruptedDecision(bool cancel)
    {
        await using var client = Client("delayed-decision", cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(150));
        await client.StartAsync();
        var game = new GameEngine(["a", "b", "c", "d"], 1);
        game.StartGame(0);
        using var cancellation = new CancellationTokenSource();
        var decision = client.ChooseActionAsync(game.CreateView(1), 1, cancellation.Token);
        if (cancel)
        {
            cancellation.CancelAfter(150);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await decision);
        }
        else await Assert.ThrowsAsync<ProtocolException>(async () => await decision);
        Assert.True(!client.IsRunning, "Interrupted decision left its process reusable");
        await Assert.ThrowsAsync<ProtocolException>(() => client.ChooseActionAsync(game.CreateView(1), 1));
        // Cross a real hand boundary and exercise both reset and position requests.
        game.PlaceBid(1, BidLevel.Four); game.PlaceBid(2, null); game.PlaceBid(3, null); game.PlaceBid(0, null);
        game.ChooseContract(1, ContractMode.High, null); game.ApplyIllegalPlayPenalty(1); game.StartNextHand();
        await Assert.ThrowsAsync<ProtocolException>(() => client.NewGameAsync());
        await Assert.ThrowsAsync<ProtocolException>(() => client.ObserveAsync(game.CreateView(2), 2));
        await Assert.ThrowsAsync<ProtocolException>(() => client.ChooseActionAsync(game.CreateView(2), 2));
        await Assert.ThrowsAsync<ProtocolException>(() => client.StartAsync());
    }

    private static EngineProcessClient Client(string mode, TimeSpan? timeout = null) =>
        new(Environment.ProcessPath!, $"--arena-test-child {mode}", timeout ?? TimeSpan.FromSeconds(3));

    private sealed class ClientDriver(string mode) : IBotDriver
    {
        private readonly EngineProcessClient _client = Client(mode);
        public int MalformedResponses { get; private set; }
        public Task StartAsync(CancellationToken token = default) => _client.StartAsync(token);
        public Task NewGameAsync(CancellationToken token = default) => _client.NewGameAsync(token);
        public Task ObserveAsync(GameView view, int seat, CancellationToken token = default) => _client.ObserveAsync(view, seat, token);
        public async Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken token = default)
        {
            try { return await _client.ChooseActionAsync(view, seat, token); }
            catch (Exception) when (view.Phase is GamePhase.Playing or GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard)
            {
                MalformedResponses++;
                throw;
            }
        }
        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}
