namespace BidEuchre.Arena;

public sealed record EngineSpec(string Id, string Executable, string[] Arguments, string WorkingDirectory,
    string ExecutableSha256, string? ModelSha256)
{
    public string? ExpectedBackend { get; init; }
    public string? ManifestSha256 { get; init; }
}
public sealed record BudgetSpec(int DecisionMs = 3000, int StopMs = 3500, int ProcessMs = 5000, int StartupMs = 5000);
public sealed record MatchSpec(string BlockId, int LegId, int Seed, int Dealer, EngineSpec[] Seats, BudgetSpec Budget);
public enum MatchStatus { Complete, Interrupted, Failed }
public sealed record TraceSeal(string Sha256, int Actions, string SpecSha256, string TerminalSha256);
public sealed record MatchResult(MatchSpec Spec, MatchStatus Status, int? Winner, int[] Scores,
    int Hands, string? Error, string TracePath)
{
    public TraceSeal? TraceSeal { get; init; }
    public string[] EffectiveBackends { get; init; } = [];
}
