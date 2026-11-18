using System.Runtime.ExceptionServices;
using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public sealed class GameSession : IAsyncDisposable
{
    private readonly EngineCatalog _catalog;
    private readonly CampaignContext? _campaign;
    private readonly Func<CampaignSnapshot, bool>? _checkpoint;
    private readonly Func<CampaignCheckpoint, bool>? _liveCheckpoint;
    private CampaignArrangement? _arrangement;
    private readonly Func<GameView, int, CampaignCommand> _campaignPolicy;
    private readonly int? _seed;
    private readonly MatchOptions? _matchOptions;
    private readonly TimeSpan _botActionDelay;
    private readonly TimeSpan _completedTrickDelay;
    private readonly Func<SeatConfiguration, EngineDescriptor, IBotDriver> _botDriverFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _botIo = new(1, 1);
    private Task _terminalObservation = Task.CompletedTask;
    private long _pauseGeneration;
    private readonly Dictionary<int, IBotDriver> _bots = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeLock = new();
    private Task? _botLoopTask;
    private Task? _disposeTask;
    private int? _observedTerminalHand;
    private TaskCompletionSource? _dealReady;
    private TaskCompletionSource? _resumeReady;
    private readonly bool _waitForContractPresentation;
    private TaskCompletionSource? _contractReady;
    private long? _contractPresentationDeal;
    public bool WaitingForContractPresentation => _contractReady is { Task.IsCompleted: false };
    private readonly bool _waitForTrickPresentation;
    private sealed record TrickPresentation(long Deal, int Trick, TaskCompletionSource Ready);
    private TrickPresentation? _trickPresentation;
    private (long Deal, int Trick)? _lastPresentedTrick;
    public bool WaitingForTrickPresentation => _trickPresentation is { Ready.Task.IsCompleted: false };

    private bool _paused;
    public bool WaitingForDeal => _dealReady is { Task.IsCompleted: false };

    public GameSession(
        string id,
        string name,
        IReadOnlyList<SeatConfiguration> seats,
        EngineCatalog catalog,
        int? seed,
        TimeSpan? botActionDelay = null,
        TimeSpan? completedTrickDelay = null,
        Func<SeatConfiguration, EngineDescriptor, IBotDriver>? botDriverFactory = null,
        MatchOptions? matchOptions = null, CampaignContext? campaign = null, Func<CampaignSnapshot, bool>? checkpoint = null,
        Func<GameView, int, CampaignCommand>? campaignPolicy = null,
        bool waitForContractPresentation = false, Func<CampaignCheckpoint, bool>? liveCheckpoint = null,
        bool waitForTrickPresentation = false)
    {
        _liveCheckpoint = liveCheckpoint;
        _waitForTrickPresentation = waitForTrickPresentation;
        _waitForContractPresentation = waitForContractPresentation;
        _campaignPolicy = campaignPolicy ?? CampaignBotPolicy.Choose;
        _matchOptions = matchOptions;
        _campaign = campaign; _checkpoint = checkpoint;
        Id = id;
        Name = name;
        Seats = seats;
        _catalog = catalog;
        _seed = seed;
        _botActionDelay = botActionDelay ?? TimeSpan.FromSeconds(1);
        _completedTrickDelay = completedTrickDelay ?? (_botActionDelay == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Max(1600, _botActionDelay.TotalMilliseconds * 1.6)));
        _botDriverFactory = botDriverFactory ?? ((_, descriptor) => catalog.CreateDriver(descriptor));
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<SeatConfiguration> Seats { get; }
    public GameEngine? Game { get; private set; }
    public string? LastError { get; private set; }
    public bool Started => Game is not null;
    // Set when the background bot loop stopped on an error nothing else handled.
    public Exception? BotLoopFault => _botLoopTask is { IsFaulted: true } loop ? loop.Exception!.GetBaseException() : null;

    public Task StartAsync(CancellationToken cancellationToken = default, bool waitForDeal = false) => StartCoreAsync(null, cancellationToken, waitForDeal);

    public Task RestoreCampaignAsync(CampaignCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        if (checkpoint is null || !checkpoint.IsValid()) throw new GameRuleException("Invalid campaign checkpoint.");
        // Detach before awaiting startup so the caller cannot mutate the engine being restored.
        var detached = new CampaignCheckpoint(GameEngine.RestoreCampaignMatch(checkpoint.Match).ExportCampaignMatchSnapshot(), checkpoint.Arrangement);
        return StartCoreAsync(detached, cancellationToken, false);
    }

    private async Task StartCoreAsync(CampaignCheckpoint? restored, CancellationToken cancellationToken, bool waitForDeal)
    {
        using var startup = CreateStartupCancellation(cancellationToken);
        await _gate.WaitAsync(startup.Token);
        var initializing = false;
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            if (Game is not null)
            {
                throw new GameRuleException("This session has already started.");
            }

            if (restored is not null && (Seats.Count != 4 || !Seats.OrderBy(s => s.Seat).Select(s => s.Seat).SequenceEqual(new[] { 0, 1, 2, 3 })
                || !Seats.OrderBy(s => s.Seat).Select(s => s.Name).SequenceEqual(restored.Match.PlayerNames)))
                throw new GameRuleException("The saved campaign seats do not match this session.");
            if ((_campaign is not null || restored is not null) && Seats.Any(s => s.Kind == PlayerKind.Bot && s.EngineId is not null && s.EngineId != EngineCatalog.BuiltInEngineId))
                throw new GameRuleException("External engines do not support campaign cards and bidding options.");
            initializing = true;
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
            Game = restored is null ? new GameEngine(Seats.OrderBy(seat => seat.Seat).Select(seat => seat.Name).ToArray(), _seed, _matchOptions, _campaign)
                : GameEngine.RestoreCampaignMatch(restored.Match);
            if (restored is null) Game.StartGame();
            else
            {
                _arrangement = restored.Arrangement;
                // A selected contract was already acknowledged in the saved game. A future
                // contract still arms its usual fresh gate. No abandoned animation can block bots.
                _contractPresentationDeal = Game.Contract is null ? null : Game.DealSerial;
                _contractReady = null;
                _lastPresentedTrick = (Game.DealSerial, Game.CompletedTricks.Count);
            }
            _dealReady = waitForDeal ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            LastError = null;
            CheckpointCampaign();
            StartBotLoopLocked();
        }
        catch
        {
            if (initializing)
            {
                await DisposeBotsAsync();
                Game = null;
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops table mutations after the current locked operation has completed.
    /// A cancelled visual deal is acknowledged here so hiding the table cannot strand its bots.</summary>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (_paused) return;
            _paused = true;
            _pauseGeneration++;
            _resumeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _dealReady?.TrySetResult();
            _contractReady?.TrySetResult();
            _trickPresentation?.Ready.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            _paused = false;
            _resumeReady?.TrySetResult();
            _resumeReady = null;
        }
        finally { _gate.Release(); }
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

    // Desktop polling can reuse a seat-private projection while the authoritative state is unchanged.
    private (GameEngine Game, long Revision, int? Viewer, GameView View)? _tableProjection;

    public async Task<(GameView? View, string? Error)> ReadTableViewAsync(int? viewerSeat, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (Game is not { } game) return (null, LastError);
            if (_tableProjection is not { } cached || !ReferenceEquals(cached.Game, game) ||
                cached.Revision != game.ViewRevision || cached.Viewer != viewerSeat)
            {
                cached = (game, game.ViewRevision, viewerSeat, game.CreateView(viewerSeat));
                _tableProjection = cached;
            }
            return (cached.View, LastError);
        }
        finally { _gate.Release(); }
    }

    public async Task ExecuteAsync(GameActionRequest request, CancellationToken cancellationToken = default, bool waitForBotObservation = true)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        Task observation = Task.CompletedTask;
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            ThrowIfPresentationPending();
            if (WaitingForDeal) throw new GameRuleException("Wait for dealing to finish before bidding.");
            if (Game?.IsCampaign is true) throw new GameRuleException("Campaign tables only accept campaign actions.");
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
                observation = QueueTerminalObservationLocked();
                return;
            }

            LastError = null;
            observation = QueueTerminalObservationLocked();
            StartBotLoopLocked(Game.CompletedTricks.Count > completedTricksBefore
                ? _completedTrickDelay
                : _botActionDelay);
        }
        finally
        {
            _gate.Release();
            if (waitForBotObservation) await observation;
        }
    }

    private void CheckpointCampaign()
    {
        if (Game?.IsCampaign is not true) return;
        var checkpoint = CreateCampaignCheckpointLocked();
        var saved = _liveCheckpoint is not null ? _liveCheckpoint(checkpoint) : _checkpoint?.Invoke(checkpoint.Match.Resources) ?? true;
        if (!saved) LastError = "Could not save progress. Changes are retained in this session only.";
    }

    private CampaignCheckpoint CreateCampaignCheckpointLocked() => new(Game!.ExportCampaignMatchSnapshot(),
        _arrangement?.DealSerial == Game.DealSerial ? _arrangement : null);

    public async Task<CampaignCheckpoint?> GetCampaignCheckpointAsync(CancellationToken cancellationToken = default) =>
        await ReadCampaignAsync((_, checkpoint, _) => checkpoint, cancellationToken);

    /// <summary>Projects a detached seat-zero view, complete checkpoint and error under the action gate.
    /// The synchronous projection may take a short state lock but must not await session operations.</summary>
    public async Task<T> ReadCampaignAsync<T>(Func<GameView?, CampaignCheckpoint?, string?, T> projection, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing(); await _gate.WaitAsync(cancellationToken);
        try { ThrowIfClosing(); return projection(Game?.CreateView(0), Game?.IsCampaign is true ? CreateCampaignCheckpointLocked() : null, LastError); }
        finally { _gate.Release(); }
    }

    public async Task ExecuteCampaignAsync(CampaignActionRequest request, CancellationToken cancellationToken = default, Action? precondition = null,
        bool waitForDeal = false)
    {
        ThrowIfClosing(); await _gate.WaitAsync(cancellationToken);
        Task observation = Task.CompletedTask;
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            precondition?.Invoke();
            ThrowIfPresentationPending();
            if (Game?.IsCampaign is not true || WaitingForDeal) throw new GameRuleException("Wait for the campaign deal to finish.");
            GameRules.ValidateSeat(request.Seat);
            if (Seats[request.Seat].Kind != PlayerKind.Human) throw new GameRuleException("Bot actions come from the campaign policy.");
            var snapshot = Game.ExportCampaignSnapshot();
            if (snapshot.Revision != request.ExpectedRevision || Game.DealSerial != request.ExpectedDealSerial)
                throw new GameRuleException("The table changed; choose an action from the current hand.");
            var count = Game.CompletedTricks.Count;
            var deal = Game.DealSerial;
            Game.ExecuteCampaignAction(request.Seat, request.Command);
            if (Game.DealSerial != deal)
            {
                _dealReady = waitForDeal ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
                _arrangement = null;
                _observedTerminalHand = null;
            }
            LastError = null; CheckpointCampaign(); observation = QueueTerminalObservationLocked();
            StartBotLoopLocked(Game.CompletedTricks.Count > count ? _completedTrickDelay : _botActionDelay);
        }
        finally
        {
            _gate.Release();
            await observation;
        }
    }

    /// <summary>Runs a developer-console change on the live engine, then saves and wakes the table as a real action would.</summary>
    public async Task DeveloperAsync(Action<GameEngine> change, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing(); await _gate.WaitAsync(cancellationToken);
        Task observation = Task.CompletedTask;
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            if (Game is null) throw new GameRuleException("The session has not started.");
            change(Game);
            LastError = null; CheckpointCampaign(); observation = QueueTerminalObservationLocked();
            StartBotLoopLocked(_botActionDelay);
        }
        finally
        {
            _gate.Release();
            await observation;
        }
    }

    /// <summary>Rearranges a human's campaign hand. It is not a game action: it is allowed at any time,
    /// even during the deal or another seat's turn, and does not change the table revision or wake the bots.</summary>
    public async Task ArrangeCampaignHandAsync(int seat, IReadOnlyList<CardId> order, CancellationToken cancellationToken = default, Action? precondition = null, bool manuallyArranged = true, string? autoArrangeStage = null)
    {
        ThrowIfClosing(); await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            precondition?.Invoke();
            if (Game?.IsCampaign is not true) throw new GameRuleException("Only campaign hands are arranged on the table.");
            GameRules.ValidateSeat(seat);
            if (Seats[seat].Kind != PlayerKind.Human) throw new GameRuleException("Only a human seat arranges its hand.");
            Game.ArrangeCampaignHand(seat, order);
            if (seat == 0) _arrangement = new(Game.DealSerial, manuallyArranged, autoArrangeStage);
            LastError = null; CheckpointCampaign();
        }
        finally { _gate.Release(); }
    }

    public async Task StartNextHandAsync(CancellationToken cancellationToken = default, bool waitForDeal = false, Action? precondition = null)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            ThrowIfPaused();
            precondition?.Invoke();
            if (Game is null)
            {
                throw new GameRuleException("The session has not started.");
            }

            ThrowIfTrickPresentationPending();
            Game.StartNextHand();
            CheckpointCampaign();
            _dealReady = waitForDeal ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            _observedTerminalHand = null;
            StartBotLoopLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteDealAsync(int handNumber, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (Game?.HandNumber == handNumber) _dealReady?.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Acknowledges only this deal's optional desktop cue. This never resumes a settings pause.</summary>
    public async Task CompleteContractPresentationAsync(long dealSerial, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (_contractPresentationDeal == dealSerial) _contractReady?.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    // Called while holding the action gate, before a human response or the next bot action.
    // Later Chaos rolls alter the contract within the same deal and need no new start hold.
    private void ArmContractPresentationLocked()
    {
        if (!_waitForContractPresentation || Game is null) return;
        if (_contractPresentationDeal != Game.DealSerial) _contractReady?.TrySetResult();
        if (Game.Contract is null || _contractPresentationDeal == Game.DealSerial) return;
        _contractPresentationDeal = Game.DealSerial;
        _contractReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_paused) _contractReady.TrySetResult();
    }

    private void ThrowIfPresentationPending()
    {
        if (WaitingForContractPresentation) throw new GameRuleException("Wait for the contract announcement to finish.");
        ThrowIfTrickPresentationPending();
    }

    private void ThrowIfTrickPresentationPending()
    {
        if (WaitingForTrickPresentation) throw new GameRuleException("Wait for the completed trick to finish.");
    }

    public async Task CompleteTrickPresentationAsync(long deal, int trick, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            if (_trickPresentation is { } presentation && presentation.Deal == deal && presentation.Trick == trick)
                presentation.Ready.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    private void ArmTrickPresentationLocked()
    {
        if (!_waitForTrickPresentation || Game is not { } game) return;
        var trick = (game.DealSerial, game.CompletedTricks.Count);
        if (_lastPresentedTrick == trick) return;
        _trickPresentation?.Ready.TrySetResult();
        _lastPresentedTrick = trick;
        if (trick.Count > 0 && !_paused)
            _trickPresentation = new(trick.DealSerial, trick.Count, new(TaskCreationOptions.RunContinuationsAsynchronously));
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

        // A mutation already in the gate can still finish publishing its bot task.
        Task? loop;
        Task observation;
        await _gate.WaitAsync();
        try
        {
            loop = _botLoopTask;
            observation = _terminalObservation;
        }
        finally { _gate.Release(); }

        Exception? failure = null;
        try { await Task.WhenAll(loop ?? Task.CompletedTask, observation); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // A failed bot action must not prevent the remaining engines from being released.
            failure = exception;
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
        Task observation;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfClosing();
            loop = _botLoopTask;
            observation = _terminalObservation;
        }
        finally
        {
            _gate.Release();
        }

        await Task.WhenAll(loop ?? Task.CompletedTask, observation).WaitAsync(cancellationToken);
    }

    private void StartBotLoopLocked(TimeSpan? initialDelay = null)
    {
        if (IsClosing)
        {
            return;
        }

        var token = _lifetime.Token;
        var delay = initialDelay ?? _botActionDelay;
        _botLoopTask = _botLoopTask is { IsCompleted: false } runningLoop
            ? Task.Run(() => ContinueBotLoopAsync(runningLoop, delay, token))
            : Task.Run(() => DriveBotsAsync(delay, token), token);
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
            if (_dealReady is { } deal) await deal.Task.WaitAsync(cancellationToken);
            int expectedSeat = -1;
            Task? resume = null;
            Task? trickReady = null;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (Game is not { } current || current.Phase is GamePhase.HandComplete or GamePhase.GameComplete ||
                    (current.IsCampaign ? current.CampaignActor : current.CurrentSeat) is not { } actor || !_bots.ContainsKey(actor))
                    return;
                if (_paused)
                    resume = _resumeReady!.Task;
                else if (WaitingForContractPresentation)
                    resume = _contractReady!.Task;

                if (resume is null)
                {
                    expectedSeat = actor;
                    if (WaitingForTrickPresentation) trickReady = _trickPresentation!.Ready.Task;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (resume is not null)
            {
                await resume.WaitAsync(cancellationToken);
                actions--; // A pause does not consume the bot action guard or an action delay.
                continue;
            }

            var pace = nextDelay > TimeSpan.Zero ? Task.Delay(nextDelay, cancellationToken) : Task.CompletedTask;
            if (trickReady is not null) await Task.WhenAll(pace, trickReady.WaitAsync(cancellationToken));
            else await pace;

            GameEngine game;
            GameView view;
            IBotDriver bot;
            long revision;
            long pauseGeneration;
            Task earlierObservation;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_paused || WaitingForContractPresentation || WaitingForTrickPresentation || WaitingForDeal)
                { actions--; continue; }
                if (Game is not { } live || (live.IsCampaign ? live.CampaignActor : live.CurrentSeat) != expectedSeat ||
                    !_bots.TryGetValue(expectedSeat, out var driver))
                    continue;
                game = live;
                bot = driver;
                view = game.CreateView(expectedSeat);
                revision = game.ViewRevision;
                pauseGeneration = _pauseGeneration;
                earlierObservation = _terminalObservation;
            }
            finally { _gate.Release(); }

            bool DecisionIsCurrent() =>
                !_paused && pauseGeneration == _pauseGeneration && ReferenceEquals(game, Game) &&
                revision == game.ViewRevision && (game.IsCampaign ? game.CampaignActor : game.CurrentSeat) == expectedSeat &&
                !WaitingForDeal && !WaitingForContractPresentation && !WaitingForTrickPresentation;

            CampaignCommand? command = null;
            BotAction? action = null;
            Exception? failure = null;
            await earlierObservation.WaitAsync(cancellationToken);
            await _botIo.WaitAsync(cancellationToken);
            try
            {
                // An end-of-hand observation may have held the driver since this snapshot was taken.
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    if (!DecisionIsCurrent()) { actions--; continue; }
                }
                finally { _gate.Release(); }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                if (view.Campaign is not null) command = _campaignPolicy(view, expectedSeat);
                else action = await bot.ChooseActionAsync(view, expectedSeat, timeout.Token);
            }
            catch (Exception error) { failure = error; }
            finally { _botIo.Release(); }

            Task observation = Task.CompletedTask;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Pause invalidates a decision even if the table was already resumed before the reply.
                if (!DecisionIsCurrent())
                {
                    actions--;
                    continue;
                }

                var completedTricksBefore = game.CompletedTricks.Count;
                try
                {
                    if (failure is not null) ExceptionDispatchInfo.Throw(failure);
                    if (game.IsCampaign) game.ExecuteCampaignAction(expectedSeat, command!);
                    else ApplyBotAction(game, expectedSeat, action!);
                    LastError = null;
                    CheckpointCampaign();
                    observation = QueueTerminalObservationLocked();
                }
                catch (Exception exception) when (exception is ProtocolException or GameRuleException or OperationCanceledException)
                {
                    LastError = $"Bot in seat {expectedSeat + 1} failed: {exception.Message}";
                    if (game.IsCampaign)
                    {
                        if (!TryCampaignFallback(game, expectedSeat, out var reason))
                        {
                            LastError = $"Bot in seat {expectedSeat + 1} cannot act: {reason}";
                            return;
                        }

                        CheckpointCampaign();
                        observation = QueueTerminalObservationLocked();
                    }
                    else if (game.Phase is GamePhase.Playing)
                    {
                        game.ApplyIllegalPlayPenalty(expectedSeat);
                        observation = QueueTerminalObservationLocked();
                        return;
                    }
                    else ApplySafeFallback(game, expectedSeat);
                }

                ArmContractPresentationLocked();
                ArmTrickPresentationLocked();
                nextDelay = game.CompletedTricks.Count > completedTricksBefore ? _completedTrickDelay : _botActionDelay;
            }
            finally
            {
                _gate.Release();
                await observation;
            }
        }

        throw new InvalidOperationException("Bot action guard exceeded; the session may be stuck.");
    }

    private Task QueueTerminalObservationLocked()
    {
        ArmContractPresentationLocked();
        ArmTrickPresentationLocked();
        if (Game is not { } game || game.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete) ||
            _observedTerminalHand == game.HandNumber)
            return Task.CompletedTask;

        _observedTerminalHand = game.HandNumber;
        var observations = _bots.OrderBy(item => item.Key)
            .Select(item => (Seat: item.Key, Driver: item.Value, View: game.CreateView(item.Key))).ToArray();
        if (observations.Length == 0) return Task.CompletedTask;
        var revision = game.ViewRevision;
        var previous = _terminalObservation;
        var token = _lifetime.Token;
        return _terminalObservation = Task.Run(async () =>
        {
            await previous;
            try { await _botIo.WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }

            string? firstFailure;
            try
            {
                var results = await Task.WhenAll(observations.Select(async item =>
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        await item.Driver.ObserveAsync(item.View, item.Seat, timeout.Token);
                        return null;
                    }
                    catch (Exception error)
                    {
                        return $"Bot in seat {item.Seat + 1} could not observe the completed hand: {error.Message}";
                    }
                }));
                firstFailure = results.FirstOrDefault(message => message is not null);
            }
            finally { _botIo.Release(); }

            if (firstFailure is null) return;
            await _gate.WaitAsync();
            try
            {
                if (!token.IsCancellationRequested && ReferenceEquals(Game, game) && game.ViewRevision == revision)
                    LastError = firstFailure;
            }
            finally { _gate.Release(); }
        });
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

    private static bool TryCampaignFallback(GameEngine game, int seat, out string reason)
    {
        reason = "no legal campaign action is available.";
        foreach (var command in game.CreateCampaignView(seat).LegalActions)
        {
            try
            {
                game.ExecuteCampaignAction(seat, command);
                return true;
            }
            catch (GameRuleException exception)
            {
                reason = exception.Message;
            }
        }

        return false;
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

    private void ThrowIfPaused()
    {
        if (_paused) throw new GameRuleException("Match paused. Return to the table to continue.");
    }

    private void ThrowIfClosing()
    {
        if (IsClosing)
        {
            throw new ObjectDisposedException(nameof(GameSession));
        }
    }
}
