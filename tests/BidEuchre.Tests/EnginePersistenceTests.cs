using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;

internal static class EnginePersistenceTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return; // Real protocol fixtures below use the POSIX shell.
        var root = Path.Combine(Path.GetTempPath(), "engine-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registry = Path.Combine(root, "engines.json");
            var marker = Path.Combine(root, "executed");
            var script = Path.Combine(root, "bot.sh");
            await File.WriteAllTextAsync(script, $"echo launch >> '{marker}'\nwhile read command; do case \"$command\" in beuci) printf 'id name Test Bot\\nid author Test Author\\nbeuciok\\n';; isready) echo readyok;; quit) exit;; esac; done\n");
            var catalog = new EngineCatalog(registry);
            var loaded = await catalog.LoadAsync("/bin/sh", $"\"{script}\"");
            var duplicate = await catalog.LoadAsync("/bin/sh", $"\"{script}\"");
            Require(duplicate.Id == loaded.Id && catalog.List().Count == 2, "Loading the same command must preserve one stable engine ID.");
            File.Delete(marker);
            catalog = new EngineCatalog(registry);
            Require(!File.Exists(marker), "Registry startup must not execute persisted engines.");
            Require(catalog.Get(loaded.Id).Name == "Test Bot" && catalog.Get(loaded.Id).Author == "Test Author", "Names, authors and stable IDs must survive restart.");
            Require(!catalog.Availability(loaded.Id).Verified, "Restored engines must wait for lazy verification.");
            await using (var driver = catalog.CreateDriver(catalog.Get(loaded.Id))) await driver.StartAsync();
            Require(File.Exists(marker) && catalog.Availability(loaded.Id).Verified, "Using an engine must handshake lazily and mark it verified.");
            var bad = Path.Combine(root, "bad.sh");
            await File.WriteAllTextAsync(bad, "echo 'dependency missing' >&2\nexit 1\n");
            File.WriteAllText(registry, JsonSerializer.Serialize(new { Engines = new[] { loaded with { Executable = "/bin/sh", Arguments = $"\"{bad}\"" } } }));
            catalog = new EngineCatalog(registry);
            await using (var driver = catalog.CreateDriver(catalog.Get(loaded.Id)))
            {
                try { await driver.StartAsync(); throw new Exception("A broken engine unexpectedly started."); }
                catch (BidEuchre.Protocol.ProtocolException) { }
            }
            Require(!catalog.Availability(loaded.Id).Available && catalog.Availability(loaded.Id).Error!.Contains("dependency missing"), "A lazy startup failure must become visible engine availability.");
            catalog = new EngineCatalog(registry);
            await using (var session = new GameSession("lazy-failure", "Registry startup", Enumerable.Range(0, 4)
                .Select(seat => new SeatConfiguration(seat, $"Seat {seat}", seat == 1 ? PlayerKind.Bot : PlayerKind.Human, seat == 1 ? loaded.Id : null)).ToArray(), catalog, 1))
            {
                try { await session.StartAsync(); throw new Exception("A session with a broken engine unexpectedly started."); }
                catch (BidEuchre.Protocol.ProtocolException) { }
                Require(!session.Started && !catalog.Availability(loaded.Id).Available, "Real session startup must mark the failing selected engine unavailable and leave the session unstarted.");
            }
            var repaired = await catalog.RepairAsync(loaded.Id, "/bin/sh", $"\"{script}\"");
            Require(repaired.Id == loaded.Id && new EngineCatalog(registry).Get(loaded.Id).Arguments == $"\"{script}\"", "Repair must retain seat references and persist the corrected command.");
            Require(catalog.Remove(loaded.Id) && !catalog.Remove(EngineCatalog.BuiltInEngineId), "External removal must keep the built-in engine.");
            Require(new EngineCatalog(registry).List().Count == 1, "Removal must survive restart.");
            File.WriteAllText(registry, "{broken");
            Require(new EngineCatalog(registry).List().Single().IsBuiltIn, "Malformed registries must recover the built-in engine.");
            File.WriteAllText(registry, JsonSerializer.Serialize(new { Engines = new[] { loaded with { Executable = Path.Combine(root, "missing") } } }));
            catalog = new EngineCatalog(registry);
            Require(!catalog.Availability(loaded.Id).Available && catalog.List().Count == 2, "Missing executables must remain visible for repair.");
            File.WriteAllText(registry, JsonSerializer.Serialize(new { Engines = new EngineDescriptor?[] { null, loaded with { Executable = "/bad\0path" }, loaded } }));
            Require(new EngineCatalog(registry).Get(loaded.Id).Executable == "/bin/sh", "Malformed individual descriptors must be skipped without losing valid engines.");
            Console.WriteLine("PASS  Registry roundtrip, lazy handshake, failure, repair, removal, malformed and missing files");

            var directory = Path.Combine(root, "engines");
            var bundle = Path.Combine(directory, "bundle"); Directory.CreateDirectory(bundle);
            var launcher = Path.Combine(bundle, "run.sh");
            File.WriteAllText(launcher, $"touch '{marker}'\n");
            File.WriteAllText(Path.Combine(bundle, "engine.json"), JsonSerializer.Serialize(new { Name = "Bundled", Author = "Bundle Author", Executable = "run.sh" }));
            var ignored = Path.Combine(directory, "ignored"); Directory.CreateDirectory(ignored);
            File.WriteAllText(Path.Combine(ignored, "random.sh"), $"touch '{marker}'\n");
            var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "run.sh"), $"touch '{marker}'\n");
            Directory.CreateSymbolicLink(Path.Combine(directory, "linked"), outside);
            var escaped = Path.Combine(directory, "escaped"); Directory.CreateDirectory(escaped);
            File.WriteAllText(Path.Combine(escaped, "engine.json"), JsonSerializer.Serialize(new { Executable = Path.Combine(outside, "run.sh") }));
            File.Delete(marker); File.Delete(registry);
            catalog = new EngineCatalog(registry, directory);
            Require(catalog.List().Count == 2 && catalog.List()[1].Name == "Bundled" && !File.Exists(marker), "Discovery must read known manifests/launchers only and never execute them.");
            var bundledId = catalog.List()[1].Id;
            Require(new EngineCatalog(registry, directory).List().Count == 2, "Persisted/discovered engine commands must deduplicate.");
            catalog.Remove(bundledId);
            Require(new EngineCatalog(registry, directory).List().Count == 1, "Removed discovered engines must not reappear at restart.");
            Require(new EngineCatalog(root, directory).PersistenceError is not null, "Catalogue write failure must be visible without breaking discovery.");
            Require(new EngineCatalog().List().Count == 1, "An unconfigured server catalogue must stay transient.");
            Console.WriteLine("PASS  Bounded non-executing engine discovery, deduplication and persistent removal");

            var setupPath = Path.Combine(root, "last-table.json");
            var setup = new LastTableSetup("Saturday", 600, [new(0, "Alice", PlayerKind.Human, null), new(1, "West", PlayerKind.Bot, "stable-west"), new(2, "North", PlayerKind.Human, null), new(3, "East", PlayerKind.Bot, EngineCatalog.BuiltInEngineId)]);
            Require(setup.Save(setupPath), "Last table setup must save.");
            var restored = LastTableSetup.Load(setupPath);
            Require(restored.TableName == "Saturday" && restored.BotDelayMilliseconds == 600 && restored.Seats[1].EngineId == "stable-west" && restored.Seats[2].Kind == PlayerKind.Human && restored.Seats[0].Name == "Alice", "Setup must restore names, seat kinds, engine identities, table name and pace.");
            File.WriteAllText(setupPath, "{\"TableName\":null,\"BotDelayMilliseconds\":-1,\"Seats\":[null]}");
            restored = LastTableSetup.Load(setupPath);
            Require(restored.Seats.Length == 4 && restored.Seats[0].Name == "You" && restored.BotDelayMilliseconds == 1000, "Malformed table fields must normalize into a safe four-seat setup.");
            File.WriteAllText(setupPath, "broken");
            Require(LastTableSetup.Load(setupPath).Seats.Length == 4, "Malformed table JSON must recover defaults.");
            Console.WriteLine("PASS  Last table setup roundtrip and malformed-field recovery");
        }
        finally { Directory.Delete(root, true); }
    }
}
