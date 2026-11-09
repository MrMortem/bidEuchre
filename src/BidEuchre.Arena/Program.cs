using BidEuchre.Arena;

if (args.Length == 3 && args[0] == "export-fixtures" && args[1] == "--output")
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await FixtureExporter.ExportAsync(args[2], cancellation.Token);
    Console.WriteLine($"Wrote fixture-v1 corpus to {Path.GetFullPath(args[2])}");
}
else
{
    Console.Error.WriteLine("Usage: BidEuchre.Arena export-fixtures --output PATH");
    Environment.ExitCode = 2;
}
