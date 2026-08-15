using System.Collections.Concurrent;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public sealed class EngineCatalog
{
    public const string BuiltInEngineId = "builtin-tablebot";
    private readonly ConcurrentDictionary<string, EngineDescriptor> _engines = new(StringComparer.OrdinalIgnoreCase);

    public EngineCatalog()
    {
        _engines[BuiltInEngineId] = new EngineDescriptor(
            BuiltInEngineId,
            "TableBot",
            "Bid Euchre Project",
            true,
            null,
            null);
    }

    public IReadOnlyList<EngineDescriptor> List() =>
        _engines.Values.OrderByDescending(engine => engine.IsBuiltIn).ThenBy(engine => engine.Name).ToArray();

    public EngineDescriptor Get(string id) =>
        _engines.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Bot engine '{id}' was not found.");

    public async Task<EngineDescriptor> LoadAsync(
        string executable,
        string? arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        await using var client = new EngineProcessClient(executable.Trim(), arguments?.Trim());
        var identity = await client.StartAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var descriptor = new EngineDescriptor(
            id,
            identity.Name,
            identity.Author,
            false,
            executable.Trim(),
            arguments?.Trim());
        _engines[id] = descriptor;
        return descriptor;
    }

    public bool Remove(string id)
    {
        if (id.Equals(BuiltInEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _engines.TryRemove(id, out _);
    }
}
