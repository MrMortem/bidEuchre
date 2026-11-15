using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public interface IBotDriver : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task NewGameAsync(CancellationToken cancellationToken = default);
    Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default);
    Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ProcessBotDriver(EngineDescriptor descriptor) : IBotDriver
{
    private readonly EngineProcessClient _client = new(
        descriptor.Executable ?? throw new ArgumentException("External engine has no executable."),
        descriptor.Arguments);

    public Task StartAsync(CancellationToken cancellationToken = default) => _client.StartAsync(cancellationToken);

    public Task NewGameAsync(CancellationToken cancellationToken = default) => _client.NewGameAsync(cancellationToken);

    public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default) =>
        _client.ChooseActionAsync(view, seat, cancellationToken);

    public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) =>
        _client.ObserveAsync(view, seat, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

public sealed class BuiltInBotDriver : IBotDriver
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FreePlayBotPolicy.Choose(view, seat));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
