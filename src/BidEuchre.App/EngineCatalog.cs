using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public sealed record EngineAvailability(bool Available, bool Verified, string? Error = null);

public sealed class EngineCatalog
{
    public const string BuiltInEngineId = "builtin-tablebot";
    private readonly ConcurrentDictionary<string, EngineDescriptor> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EngineAvailability> _availability = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredDiscoveries = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly string? _registryPath;
    public string? PersistenceError { get; private set; }
    private sealed record Registry(EngineDescriptor[]? Engines = null, string[]? IgnoredDiscoveries = null);
    private sealed record Manifest(string? Executable, string? Arguments = null, string? Name = null, string? Author = null);

    // Servers retain a transient catalogue unless a persistence location is explicitly provided.
    public EngineCatalog(string? registryPath = null, string? discoveryDirectory = null)
    {
        _registryPath = registryPath;
        _engines[BuiltInEngineId] = new(BuiltInEngineId, "TableBot", "Bid Euchre Project", true, null, null);
        _availability[BuiltInEngineId] = new(true, true);
        if (registryPath is not null && LocalJsonFile.Load<Registry>(registryPath) is { } registry)
        {
            foreach (var ignored in registry.IgnoredDiscoveries ?? [])
                if (!string.IsNullOrWhiteSpace(ignored)) _ignoredDiscoveries.Add(ignored);
            foreach (var engine in registry.Engines ?? [])
            {
                if (engine is null || engine.IsBuiltIn || string.IsNullOrWhiteSpace(engine.Id) ||
                    engine.Id.Equals(BuiltInEngineId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(engine.Executable) ||
                    engine.Executable.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                    _engines.ContainsKey(engine.Id) || FindCommand(engine.Executable, engine.Arguments) is not null) continue;
                var normalized = engine with { Name = string.IsNullOrWhiteSpace(engine.Name) ? Path.GetFileName(engine.Executable) : engine.Name,
                    Author = engine.Author ?? "Unknown", Executable = engine.Executable.Trim(), Arguments = NormalizeArguments(engine.Arguments) };
                _engines[engine.Id] = normalized;
                _availability[engine.Id] = InitialAvailability(normalized);
            }
        }
        if (discoveryDirectory is not null) Discover(discoveryDirectory);
    }

    public IReadOnlyList<EngineDescriptor> List() =>
        _engines.Values.OrderByDescending(engine => engine.IsBuiltIn).ThenBy(engine => engine.Name).ToArray();
    public EngineDescriptor Get(string id) => _engines.TryGetValue(id, out var engine)
        ? engine : throw new KeyNotFoundException($"Bot engine '{id}' was not found.");
    public EngineAvailability Availability(string id)
    {
        var descriptor = Get(id);
        var state = _availability[id];
        return !descriptor.IsBuiltIn && !ExecutableExists(descriptor.Executable!)
            ? new(false, false, "Executable is missing. Repair its path or remove this engine.") : state;
    }

    public async Task<EngineDescriptor> LoadAsync(string executable, string? arguments, CancellationToken cancellationToken = default) =>
        await HandshakeAsync(null, executable, arguments, cancellationToken);
    public async Task<EngineDescriptor> RepairAsync(string id, string executable, string? arguments, CancellationToken cancellationToken = default)
    {
        if (Get(id).IsBuiltIn) throw new ArgumentException("The built-in engine cannot be replaced.", nameof(id));
        return await HandshakeAsync(id, executable, arguments, cancellationToken);
    }

    private async Task<EngineDescriptor> HandshakeAsync(string? repairId, string executable, string? arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        executable = executable.Trim(); arguments = NormalizeArguments(arguments);
        await using var client = new EngineProcessClient(executable, arguments);
        var identity = await client.StartAsync(cancellationToken);
        lock (_sync)
        {
            var existing = FindCommand(executable, arguments);
            if (repairId is not null && existing is not null && existing.Id != repairId)
                throw new ArgumentException("That command already belongs to another engine. Remove the duplicate first.");
            var id = repairId ?? existing?.Id ?? StableId(executable, arguments);
            if (repairId is not null) _ignoredDiscoveries.Add(CommandKey(Get(repairId).Executable!, Get(repairId).Arguments));
            var descriptor = new EngineDescriptor(id, identity.Name, identity.Author, false, executable, arguments);
            _engines[id] = descriptor;
            _availability[id] = new(true, true);
            _ignoredDiscoveries.Remove(CommandKey(executable, arguments));
            Persist();
            return descriptor;
        }
    }

    public bool Remove(string id)
    {
        if (id.Equals(BuiltInEngineId, StringComparison.OrdinalIgnoreCase)) return false;
        lock (_sync)
        {
            if (!_engines.TryRemove(id, out var removed)) return false;
            _availability.TryRemove(id, out _);
            _ignoredDiscoveries.Add(CommandKey(removed.Executable!, removed.Arguments));
            Persist(); return true;
        }
    }

    public IBotDriver CreateDriver(EngineDescriptor descriptor) => descriptor.IsBuiltIn
        ? new BuiltInBotDriver() : new TrackedDriver(this, descriptor);

    private sealed class TrackedDriver(EngineCatalog catalog, EngineDescriptor descriptor) : IBotDriver
    {
        private readonly ProcessBotDriver _driver = new(descriptor);
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try { await _driver.StartAsync(cancellationToken); catalog._availability[descriptor.Id] = new(true, true); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { catalog._availability[descriptor.Id] = new(false, false, error.Message); throw; }
        }
        public Task NewGameAsync(CancellationToken cancellationToken = default) => _driver.NewGameAsync(cancellationToken);
        public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default) => _driver.ChooseActionAsync(view, seat, cancellationToken);
        public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => _driver.ObserveAsync(view, seat, cancellationToken);
        public ValueTask DisposeAsync() => _driver.DisposeAsync();
    }

    private void Discover(string directory)
    {
        try
        {
            if (!Directory.Exists(directory) || IsLink(directory)) return;
            foreach (var bundle in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (IsLink(bundle)) continue;
                var manifestPath = Path.Combine(bundle, "engine.json");
                var manifest = !IsLink(manifestPath) ? LocalJsonFile.Load<Manifest>(manifestPath) : null;
                var launcher = OperatingSystem.IsWindows() ? "run.exe" : "run.sh";
                var executable = Path.GetFullPath(Path.Combine(bundle, manifest?.Executable ?? launcher));
                // A bundled manifest may only name a file inside its own bundle; links are never followed.
                if (!executable.StartsWith(Path.GetFullPath(bundle) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !File.Exists(executable) || HasLinkBetween(bundle, executable)) continue;
                var arguments = NormalizeArguments(manifest?.Arguments);
                if (_ignoredDiscoveries.Contains(CommandKey(executable, arguments)) || FindCommand(executable, arguments) is not null) continue;
                var id = StableId(executable, arguments);
                _engines[id] = new(id, manifest?.Name ?? Path.GetFileName(bundle), manifest?.Author ?? "Unknown", false, executable, arguments);
                _availability[id] = new(true, false);
            }
            Persist();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { PersistenceError = $"Could not discover bundled engines: {error.Message}"; }
    }

    private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool HasLinkBetween(string root, string file)
    {
        for (var current = file; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
            if (IsLink(current)) return true;
        return false;
    }
    private void Persist()
    {
        if (_registryPath is not null) PersistenceError = LocalJsonFile.Save(_registryPath,
            new Registry(List().Where(engine => !engine.IsBuiltIn).ToArray(), _ignoredDiscoveries.ToArray()))
            ? null : "Could not save the engine catalogue. Changes will be lost on restart.";
    }
    private EngineDescriptor? FindCommand(string executable, string? arguments) => _engines.Values.FirstOrDefault(engine =>
        !engine.IsBuiltIn && CommandKey(engine.Executable!, engine.Arguments) == CommandKey(executable, arguments));
    private static string NormalizeArguments(string? arguments) => arguments?.Trim() ?? "";
    private static string CommandKey(string executable, string? arguments)
    {
        var path = executable.Trim();
        if (Path.IsPathRooted(path)) path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return path + "\n" + NormalizeArguments(arguments);
    }
    private static string StableId(string executable, string? arguments) => "external-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CommandKey(executable, arguments))));
    private static bool ExecutableExists(string executable) => !Path.IsPathRooted(executable) && !executable.Contains(Path.DirectorySeparatorChar)
        || File.Exists(executable);
    private static EngineAvailability InitialAvailability(EngineDescriptor engine) => ExecutableExists(engine.Executable!)
        ? new(true, false) : new(false, false, "Executable is missing. Repair its path or remove this engine.");
}
