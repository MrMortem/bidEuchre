using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public sealed class GameSession : IAsyncDisposable
{
    private readonly EngineCatalog _catalog;
    private readonly int? _seed;
    private readonly TimeSpan _botActionDelay;
    private readonly TimeSpan _completedTrickDelay;
    private readonly Func<SeatConfiguration, EngineDescriptor, IBotDriver> _botDriverFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, IBotDriver> _bots = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeLock = new();
    private Task? _botLoopTask;
    private Task? _disposeTask;
    private int? _observedTerminalHand;

    public GameSession(
        string id,
        string name,
        IReadOnlyList<SeatConfiguration> seats,
        EngineCatalog catalog,
        int? seed,
        TimeSpan? botActionDelay = null,
        TimeSpan? completedTrickDelay = null,
        Func<SeatConfiguration, EngineDescriptor, IBotDriver>? botDriverFactory = null)
    {
        Id = id;
        Name = name;
        Seats = seats;
        _catalog = catalog;
        _seed = seed;
        _botActionDelay = botActionDelay ?? TimeSpan.FromSeconds(1);
        _completedTrickDelay = completedTrickDelay ?? (_botActionDelay == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Max(1600, _botActionDelay.TotalMilliseconds * 1.6)));
        _botDriverFactory = botDriverFactory ?? ((_, descriptor) => descriptor.IsBuiltIn
            ? new BuiltInBotDriver()
            : new ProcessBotDriver(descriptor));
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<SeatConfiguration> Seats { get; }
    public GameEngine? Game { get; private set; }
    public string? LastError { get; private set; }
    public bool Started => Game is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var startup = CreateStartupCancellation(cancellationToken);
        await _gate.WaitAsync(startup.Token);
        try
        {
            ThrowIfClosing();
            if (Game is not null)
            {
                throw new GameRuleException("This session has already started.");
            }

            foreach (var seat in Seats.Where(seat => seat.Kind is PlayerKind.Bot))
            {
                ThrowIfClosing();
                var descriptor = _catalog.Get(seat.EngineId ?? EngineCatalog.BuiltInEngineId);
                var driver = _botDriverFactory(seat, descriptor);
                _bots[seat.Seat] = driver;
                await driver.StartAsync(startup.Token);
                await driver.NewGameAsync(startup.Token);
            }

            ThrowIfClosing();
            Game = new GameEngine(Seats.OrderBy(seat => seat.Seat).Select(seat => seat.Name).ToArray(), _seed);
            Game.StartGame();
            LastError = null;
            StartBotLoopLocked();
        }
        catch
        {
            await DisposeBotsAsync();
            Game = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameView?> GetViewAsync(int? viewerSeat, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            return Game?.CreateView(viewerSeat);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExecuteAsync(GameActionRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            EnsureHumanTurn(request.Seat);
            var completedTricksBefore = Game!.CompletedTricks.Count;
            try
            {
                ApplyAction(Game, request.Seat, request);
            }
            catch (GameRuleException) when (
                request.Type?.Equals("play", StringComparison.OrdinalIgnoreCase) is true &&
                Game!.Phase is GamePhase.Playing)
            {
                Game!.ApplyIllegalPlayPenalty(request.Seat);
                LastError = $"Seat {request.Seat + 1} made an illegal card play; the hand was scored by penalty.";
                await ObserveTerminalStateLockedAsync(_lifetime.Token);
                return;
            }

            LastError = null;
            await ObserveTerminalStateLockedAsync(_lifetime.Token);
            StartBotLoopLocked(Game.CompletedTricks.Count > completedTricksBefore
                ? _completedTrickDelay
                : _botActionDelay);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartNextHandAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (Game is null)
            {
                throw new GameRuleException("The session has not started.");
            }

            Game.StartNextHand();
            _observedTerminalHand = null;
            StartBotLoopLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public SessionSummary Summary() => new(
        Id,
        Name,
        Started,
        Game?.Phase ?? GamePhase.NotStarted,
        Game?.HandNumber ?? 0,
        Game?.Scores.ToArray() ?? [0, 0],
        Seats);

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        _lifetime.Cancel();
        Exception? failure = null;
        if (_botLoopTask is not null)
        {
            try
            {
                await _botLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during session shutdown.
            }
            catch (Exception exception)
            {
                // A failed bot action must not make normal table teardown crash
                // the UI or prevent the remaining engines from being released.
                failure = exception;
            }
        }

        await _gate.WaitAsync();
        try
        {
            var botDisposalFailure = await DisposeBotsAsync();
            failure ??= botDisposalFailure;

            if (failure is not null)
            {
                LastError ??= $"Session shutdown recovered from an engine failure: {failure.Message}";
            }
        }
        finally
        {
            _gate.Release();
            _lifetime.Dispose();
        }
    }

    public async Task WaitForBotsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        Task? loop;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            loop = _botLoopTask;
        }
        finally
        {
            _gate.Release();
        }

        if (loop is not null)
        {
            await loop.WaitAsync(cancellationToken);
        }
    }

    private void StartBotLoopLocked(TimeSpan? initialDelay = null)
    {
        if (IsClosing)
        {
            return;
        }

        if (_botLoopTask is { IsCompleted: false } runningLoop)
        {
            _botLoopTask = ContinueBotLoopAsync(
                runningLoop,
                initialDelay ?? _botActionDelay,
                _lifetime.Token);
            return;
        }

        _botLoopTask = Task.Run(
            () => DriveBotsAsync(initialDelay ?? _botActionDelay, _lifetime.Token),
            _lifetime.Token);
    }

    private async Task ContinueBotLoopAsync(
        Task runningLoop,
        TimeSpan initialDelay,
        CancellationToken cancellationToken)
    {
        await runningLoop;
        cancellationToken.ThrowIfCancellationRequested();
        await DriveBotsAsync(initialDelay, cancellationToken);
    }

    private async Task DriveBotsAsync(TimeSpan initialDelay, CancellationToken cancellationToken)
    {
        var nextDelay = initialDelay;
        for (var actions = 0; actions < 200; actions++)
        {
            int expectedSeat;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (Game is null ||
                    Game.Phase is GamePhase.HandComplete or GamePhase.GameComplete ||
                    Game.CurrentSeat is null ||
                    !_bots.ContainsKey(Game.CurrentSeat.Value))
                {
                    return;
                }

                expectedSeat = Game.CurrentSeat.Value;
            }
            finally
            {
                _gate.Release();
            }

            if (nextDelay > TimeSpan.Zero)
            {
                await Task.Delay(nextDelay, cancellationToken);
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (Game is null || Game.CurrentSeat != expectedSeat || !_bots.TryGetValue(expectedSeat, out var bot))
                {
                    continue;
                }

                var completedTricksBefore = Game.CompletedTricks.Count;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var view = Game.CreateView(expectedSeat);
                    var action = await bot.ChooseActionAsync(view, expectedSeat, timeout.Token);
                    ApplyBotAction(Game, expectedSeat, action);
                    LastError = null;
                    await ObserveTerminalStateLockedAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is ProtocolException or GameRuleException or OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LastError = $"Bot in seat {expectedSeat + 1} failed: {exception.Message}";
                    if (Game.Phase is GamePhase.Playing)
                    {
                        Game.ApplyIllegalPlayPenalty(expectedSeat);
                        await ObserveTerminalStateLockedAsync(cancellationToken);
                        return;
                    }

                    ApplySafeFallback(Game, expectedSeat);
                }

                nextDelay = Game.CompletedTricks.Count > completedTricksBefore
                    ? _completedTrickDelay
                    : _botActionDelay;
            }
            finally
            {
                _gate.Release();
            }
        }

        throw new InvalidOperationException("Bot action guard exceeded; the session may be stuck.");
    }

    private async Task ObserveTerminalStateLockedAsync(CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null ||
            game.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) ||
            _observedTerminalHand == game.HandNumber)
        {
            return;
        }

        _observedTerminalHand = game.HandNumber;
        var observations = _bots
            .OrderBy(item => item.Key)
            .Select(async item =>
            {
                var (seat, bot) = item;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await bot.ObserveAsync(game.CreateView(seat), seat, timeout.Token);
                    return null;
                }
                catch (Exception exception)
                {
                    return $"Bot in seat {seat + 1} could not observe the completed hand: {exception.Message}";
                }
            })
            .ToArray();
        var firstFailure = (await Task.WhenAll(observations)).FirstOrDefault(message => message is not null);
        if (firstFailure is not null)
        {
            LastError = firstFailure;
        }
    }

    private void EnsureHumanTurn(int seat)
    {
        GameRules.ValidateSeat(seat);
        if (Game is null)
        {
            throw new GameRuleException("The session has not started.");
        }

        if (Seats[seat].Kind is not PlayerKind.Human)
        {
            throw new GameRuleException("Actions for a bot-controlled seat come from its engine.");
        }

        if (Game.CurrentSeat != seat)
        {
            throw new GameRuleException("It is not this player's turn.");
        }
    }

    private static void ApplyAction(GameEngine game, int seat, GameActionRequest request)
    {
        var type = request.Type?.Trim().ToLowerInvariant();
        switch (type)
        {
            case "pass":
                game.PlaceBid(seat, null);
                break;
            case "bid" when request.Bid is not null:
                game.PlaceBid(seat, request.Bid);
                break;
            case "contract" when request.Mode is not null:
                game.ChooseContract(seat, request.Mode.Value, request.Suit);
                break;
            case "exchange" when request.Card is not null:
                game.ExchangeCard(seat, Card.Parse(request.Card));
                break;
            case "play" when request.Card is not null:
                game.PlayCard(seat, Card.Parse(request.Card));
                break;
            default:
                throw new GameRuleException("The action is missing required data or has an unknown type.");
        }
    }

    private static void ApplyBotAction(GameEngine game, int seat, BotAction action)
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
                throw new ProtocolException("The bot returned an unknown action.");
        }
    }

    private static void ApplySafeFallback(GameEngine game, int seat)
    {
        switch (game.Phase)
        {
            case GamePhase.Bidding:
                if (game.CanPass)
                {
                    game.PlaceBid(seat, null);
                }
                else
                {
                    game.PlaceBid(seat, game.GetLegalBids()[0]);
                }

                break;
            case GamePhase.ChoosingContract:
                game.ChooseContract(seat, ContractMode.Trump, Suit.Clubs);
                break;
            case GamePhase.ExchangingBidderCard:
            case GamePhase.ExchangingPartnerCard:
                game.ExchangeCard(seat, game.GetLegalCards(seat)[0]);
                break;
            default:
                throw new ProtocolException("No safe fallback exists for this bot action.");
        }
    }

    private async Task<Exception?> DisposeBotsAsync()
    {
        Exception? failure = null;
        foreach (var bot in _bots.Values.ToArray())
        {
            try
            {
                await bot.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        _bots.Clear();
        return failure;
    }

    private bool IsClosing
    {
        get
        {
            lock (_disposeLock)
            {
                return _disposeTask is not null;
            }
        }
    }

    private CancellationTokenSource CreateStartupCancellation(CancellationToken cancellationToken)
    {
        lock (_disposeLock)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException(nameof(GameSession));
            }

            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
    }

    private void ThrowIfClosing()
    {
        if (IsClosing)
        {
            throw new ObjectDisposedException(nameof(GameSession));
        }
    }
}
