using BidEuchre.Arena;

if (args.Length == 3 && args[0] == "export-fixtures" && args[1] == "--output")
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await FixtureExporter.ExportAsync(args[2], cancellation.Token);
    Console.WriteLine($"Wrote fixture-v1 corpus to {Path.GetFullPath(args[2])}");
}
else if (args.Length >= 5 && (args[0] == "tournament" || args[0] == "audit") && args[1] == "--manifest" && args[3] == "--output"
    && (args.Length == 5 || (args.Length == 7 && args[5] == "--max-duration-seconds")))
{
    var manifest = System.Text.Json.JsonSerializer.Deserialize<ArenaManifest>(File.ReadAllText(args[2]))
        ?? throw new InvalidDataException("Missing arena manifest.");
    var duration = args.Length == 7 ? TimeSpan.FromSeconds(double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture)) : TimeSpan.FromMinutes(30);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var report = args[0] == "audit" ? await TournamentRunner.AuditAsync(manifest, args[4])
        : await TournamentRunner.RunAsync(manifest, args[4], duration, cancellation.Token);
    Console.WriteLine($"{report.Status}: {report.CompletedLegs}/{report.RegisteredLegs} legs; can promote: {report.CanPromote}; reports: {Path.GetFullPath(args[4])}");
}
else
{
    Console.Error.WriteLine("Usage: BidEuchre.Arena export-fixtures --output PATH | tournament --manifest PATH --output DIRECTORY [--max-duration-seconds 1800]");
    Environment.ExitCode = 2;
}
