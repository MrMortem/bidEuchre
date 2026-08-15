using System.Collections.Concurrent;

namespace BidEuchre.App;

public sealed class SessionManager(EngineCatalog catalog) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SessionSummary> List() =>
        _sessions.Values.Select(session => session.Summary()).OrderByDescending(session => session.HandNumber).ToArray();

    public GameSession Get(string id) =>
        _sessions.TryGetValue(id, out var session)
            ? session
            : throw new KeyNotFoundException($"Session '{id}' was not found.");

    public GameSession Create(CreateSessionRequest request)
    {
        var requestedSeats = request.Seats ?? [];
        if (requestedSeats.Count != 4)
        {
            throw new ArgumentException("Exactly four seats are required.");
        }

        var seats = requestedSeats.Select((seat, index) =>
        {
            var name = string.IsNullOrWhiteSpace(seat.Name) ? $"Player {index + 1}" : seat.Name.Trim();
            string? engineId = null;
            if (seat.Kind is PlayerKind.Bot)
            {
                engineId = string.IsNullOrWhiteSpace(seat.EngineId)
                    ? EngineCatalog.BuiltInEngineId
                    : seat.EngineId;
                catalog.Get(engineId);
            }

            return new SeatConfiguration(index, name, seat.Kind, engineId);
        }).ToArray();

        var id = Guid.NewGuid().ToString("N")[..10];
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"Table {id[..4].ToUpperInvariant()}" : request.Name.Trim();
        var botDelay = request.BotDelayMilliseconds ?? 1000;
        if (botDelay is < 250 or > 3000)
        {
            throw new ArgumentException("Bot delay must be between 250 and 3000 milliseconds.");
        }

        var session = new GameSession(
            id,
            name,
            seats,
            catalog,
            request.Seed,
            TimeSpan.FromMilliseconds(botDelay));
        if (!_sessions.TryAdd(id, session))
        {
            throw new InvalidOperationException("Could not allocate a unique session identifier.");
        }

        return session;
    }

    public async Task<bool> RemoveAsync(string id)
    {
        if (!_sessions.TryRemove(id, out var session))
        {
            return false;
        }

        await session.DisposeAsync();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }
}
