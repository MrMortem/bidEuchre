using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BidEuchre.Arena;

public enum GateType { Screening, Promotion }
public enum BlockKind { Primary, Replacement }
public sealed record ArenaCondition(string Id, string PartnerId, string Opponent0Id, string Opponent1Id);
public sealed record ArenaBlock(string Id, BlockKind Kind, string? ConditionId);

// The serialized manifest is the frozen schedule. No seeds, seats, or conditions are chosen on resume.
public sealed record ArenaManifest(string SchemaFingerprint, string SourceFingerprint, string RuleFingerprint,
    string CandidateId, string IncumbentId, EngineSpec[] Engines, BudgetSpec Budget,
    ArenaCondition[] Conditions, ArenaBlock[] Blocks, MatchSpec[] Legs, GateType Gate, int BootstrapSeed,
    string Format = "arena-manifest-v1")
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, JsonOptions)))).ToLowerInvariant();
    internal static readonly JsonSerializerOptions JsonOptions = new() { IgnoreReadOnlyProperties = true };
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Json(value), JsonOptions)!;

    public static ArenaManifest Create(string schemaFingerprint, string sourceFingerprint, string ruleFingerprint,
        EngineSpec candidate, EngineSpec incumbent, EngineSpec[] engines, BudgetSpec budget,
        ArenaCondition[] conditions, int primaryBlocks, int replacementBlocks, int seedStart, int bootstrapSeed,
        GateType gate = GateType.Screening)
    {
        if (primaryBlocks < 0 || replacementBlocks < 0 || (replacementBlocks > 0 && conditions.Length == 0))
            throw new ArgumentException("Nonnegative block counts and declared replacement conditions are required.");
        var blocks = new List<ArenaBlock>();
        var legs = new List<MatchSpec>();
        for (var i = 0; i < primaryBlocks + replacementBlocks; i++)
        {
            bool primary = i < primaryBlocks;
            var condition = primary ? null : conditions[(i - primaryBlocks) % conditions.Length];
            string id = $"{(primary ? "primary" : "replacement")}-{i:D6}";
            blocks.Add(new(id, primary ? BlockKind.Primary : BlockKind.Replacement, condition?.Id));
            EngineSpec Find(string engineId) => engines.Single(e => e.Id == engineId);
            for (int leg = 0; leg < 2; leg++)
            {
                EngineSpec[] seats = primary
                    ? (leg == 0 ? [candidate, incumbent, candidate, incumbent] : [incumbent, candidate, incumbent, candidate])
                    : [leg == 0 ? candidate : incumbent, Find(condition!.Opponent0Id), Find(condition.PartnerId), Find(condition.Opponent1Id)];
                legs.Add(new(id, leg, checked(seedStart + i), (primary ? i : (i - primaryBlocks) / conditions.Length) % 4, seats, budget));
            }
        }
        var manifest = new ArenaManifest(schemaFingerprint, sourceFingerprint, ruleFingerprint, candidate.Id, incumbent.Id,
            engines, budget, conditions, blocks.ToArray(), legs.ToArray(), gate, bootstrapSeed);
        manifest.Validate();
        return Copy(manifest);
    }

    public void Validate()
    {
        void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Require(Format == "arena-manifest-v1" && Enum.IsDefined(Gate), "Unknown manifest format/gate.");
        Require(!string.IsNullOrWhiteSpace(SchemaFingerprint) && !string.IsNullOrWhiteSpace(SourceFingerprint) && !string.IsNullOrWhiteSpace(RuleFingerprint), "Missing fingerprints.");
        Require(Budget.DecisionMs > 0 && Budget.StopMs >= Budget.DecisionMs && Budget.ProcessMs >= Budget.StopMs && Budget.StartupMs > 0, "Invalid frozen budget.");
        Require(Engines.Select(e => e.Id).Distinct().Count() == Engines.Length && CandidateId != IncumbentId, "Engine identities must be distinct.");
        Require(Engines.Any(e => e.Id == CandidateId) && Engines.Any(e => e.Id == IncumbentId), "Missing candidate/incumbent.");
        Require(Conditions.All(c => !string.IsNullOrWhiteSpace(c.Id)) && Conditions.Select(c => c.Id).Distinct().Count() == Conditions.Length, "Duplicate/empty conditions.");
        Require(Conditions.All(c => new[] { c.PartnerId, c.Opponent0Id, c.Opponent1Id }.All(id => Engines.Any(e => e.Id == id))), "Unknown condition engine.");
        Require(Blocks.All(b => !string.IsNullOrWhiteSpace(b.Id) && Enum.IsDefined(b.Kind)) && Blocks.Select(b => b.Id).Distinct().Count() == Blocks.Length, "Duplicate/invalid blocks.");
        Require(Legs.Length == Blocks.Length * 2 && Legs.Select(l => (l.BlockId, l.LegId)).Distinct().Count() == Legs.Length, "Every block requires exactly two unique legs.");
        var seeds = new HashSet<int>();
        foreach (var block in Blocks)
        {
            var pair = Legs.Where(l => l.BlockId == block.Id).OrderBy(l => l.LegId).ToArray();
            Require(pair.Length == 2 && pair[0].LegId == 0 && pair[1].LegId == 1, "Missing paired legs.");
            Require(pair[0].Seed == pair[1].Seed && pair[0].Dealer == pair[1].Dealer && pair[0].Dealer is >= 0 and < 4 && seeds.Add(pair[0].Seed), "Pairs require common seed/dealer and distinct block seeds.");
            Require(pair.All(l => l.Budget == Budget && l.Seats.Length == 4 && l.Seats.All(s => Engines.Any(e => Json(e) == Json(s)))), "Leg budget/engine differs from frozen definitions.");
            string[] first, second;
            if (block.Kind == BlockKind.Primary)
            {
                Require(block.ConditionId is null, "Primary blocks cannot have replacement conditions.");
                first = [CandidateId, IncumbentId, CandidateId, IncumbentId];
                second = [IncumbentId, CandidateId, IncumbentId, CandidateId];
            }
            else
            {
                var condition = Conditions.SingleOrDefault(c => c.Id == block.ConditionId);
                Require(condition is not null, "Missing replacement condition.");
                first = [CandidateId, condition!.Opponent0Id, condition.PartnerId, condition.Opponent1Id];
                second = [IncumbentId, condition.Opponent0Id, condition.PartnerId, condition.Opponent1Id];
            }
            Require(pair[0].Seats.Select(s => s.Id).SequenceEqual(first) && pair[1].Seats.Select(s => s.Id).SequenceEqual(second), "Invalid paired seat assignments.");
        }
        foreach (var group in Blocks.GroupBy(b => (b.Kind, b.ConditionId)))
        {
            var dealers = Enumerable.Range(0, 4).Select(d => group.Count(b => Legs.First(l => l.BlockId == b.Id).Dealer == d)).ToArray();
            Require(dealers.Max() - dealers.Min() <= 1, "Dealers must rotate evenly within each condition.");
        }
    }
}
