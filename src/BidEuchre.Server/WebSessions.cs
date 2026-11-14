using BidEuchre.App;

namespace BidEuchre.Server;

public sealed class SessionCapacityException() : Exception("Too many tables. Close a table or wait for an idle table to expire.");

// Web-only limits leave native desktop sessions unaffected.
public sealed class WebSessions : IAsyncDisposable
{
    private sealed record Entry(GameSession Session, DateTimeOffset LastAccess);
    private readonly SessionManager _sessions;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly int _maxSessions;
    public TimeSpan IdleTimeout { get; }

    public WebSessions(EngineCatalog catalog, IConfiguration configuration)
    {
        _sessions = new SessionManager(catalog);
        _maxSessions = configuration.GetValue("WebSessions:MaxSessions", 32);
        var seconds = configuration.GetValue("WebSessions:IdleTimeoutSeconds", 1800);
        if (_maxSessions is < 1 or > 128 || seconds is < 1 or > 86400)
            throw new ArgumentException("Web sessions require a cap of 1–128 and an idle timeout of 1–86400 seconds.");
        IdleTimeout = TimeSpan.FromSeconds(seconds);
    }

    public IReadOnlyList<SessionSummary> List()
    {
        lock (_gate)
            return _entries.Values.Where(entry => !Expired(entry))
                .Select(entry => entry.Session.Summary()).OrderByDescending(session => session.HandNumber).ToArray();
    }

    public GameSession Get(string id)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry) || Expired(entry))
                throw new KeyNotFoundException($"Session '{id}' was not found.");
            _entries[id] = entry with { LastAccess = DateTimeOffset.UtcNow };
            return entry.Session;
        }
    }

    public async Task<GameSession> CreateAsync(CreateSessionRequest request)
    {
        await ExpireAsync();
        lock (_gate)
        {
            if (_entries.Count >= _maxSessions) throw new SessionCapacityException();
            var session = _sessions.Create(request);
            _entries.Add(session.Id, new Entry(session, DateTimeOffset.UtcNow));
            return session;
        }
    }

    public async Task<bool> RemoveAsync(string id)
    {
        lock (_gate)
        {
            if (!_entries.Remove(id)) return false;
        }
        return await _sessions.RemoveAsync(id);
    }

    public async Task ExpireAsync()
    {
        string[] expired;
        lock (_gate)
        {
            expired = _entries.Where(pair => Expired(pair.Value)).Select(pair => pair.Key).ToArray();
            foreach (var id in expired) _entries.Remove(id);
        }
        foreach (var id in expired) await _sessions.RemoveAsync(id);
    }

    private bool Expired(Entry entry) => DateTimeOffset.UtcNow - entry.LastAccess >= IdleTimeout;

    public ValueTask DisposeAsync() => _sessions.DisposeAsync();
}

public sealed class WebSessionReaper(WebSessions sessions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Min(60, sessions.IdleTimeout.TotalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await sessions.ExpireAsync();
    }
}
