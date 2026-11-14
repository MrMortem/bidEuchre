using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.Core;
using BidEuchre.App;

internal static class CampaignResumePersistenceTests
{
    public static void StoreRejectsIncompleteAndMigratesLegacy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resume-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "run.json");
        try
        {
            // A newly saved InMatch cannot silently become a legacy restart after missing live data.
            Assert.False(CampaignStore.Save(new CampaignRun(Phase: CampaignPhase.InMatch), path));
            File.WriteAllText(path, "{\"Lives\":2,\"Match\":7,\"Money\":23,\"Phase\":1}");
            var legacy = CampaignStore.Load(path)!;
            Assert.True(legacy.LegacyMatchRestart); Assert.Equal(2, legacy.Lives); Assert.Equal(7, legacy.Match); Assert.Equal(23, legacy.Resources.Wallets[0]);
            Assert.False(CampaignStore.Save(legacy, path)); // Only the first complete live checkpoint upgrades it.
            Assert.True(CampaignStore.Save(new CampaignRun(Lives: 2, Match: 7, Money: 23), path));
            var v2 = JsonNode.Parse(File.ReadAllText(path))!; v2["Version"] = 2; v2["Run"]!["Phase"] = 1;
            File.WriteAllText(path, v2.ToJsonString()); legacy = CampaignStore.Load(path)!;
            Assert.True(legacy.LegacyMatchRestart); Assert.Equal(2, legacy.Lives); Assert.Equal(7, legacy.Match); Assert.Equal(23, legacy.Resources.Wallets[0]);
            v2["Version"] = 3; v2["Run"]!["LegacyMatchRestart"] = true;
            File.WriteAllText(path, v2.ToJsonString()); Assert.True(CampaignStore.Load(path) is null);
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void AtomicSaveAndTerminal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resume-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "run.json");
        try
        {
            var game = new GameEngine(["You", "West", "Partner", "East"], 7, new MatchOptions(12) { HandLimit = 1 },
                new CampaignContext(CampaignSnapshot.Create(7) with { Inventory = new([], [], [GumballId.PanicButton]) }));
            game.StartGame(3); game.ExecuteCampaignAction(0, new CampaignCommand.PanicButton());
            var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot()));
            Assert.True(CampaignStore.Save(run, path)); var loaded = CampaignStore.Load(path)!;
            Assert.Equal(GamePhase.GameComplete, loaded.LiveMatch!.Phase); Assert.Equal(3, loaded.Lives);
            var terminal = GameEngine.RestoreCampaignMatch(loaded.LiveMatch);
            Assert.Equal(JsonSerializer.Serialize(game.CreateView(0)), JsonSerializer.Serialize(terminal.CreateView(0)));
            var finished = loaded.ApplyResult(false, terminal.Scores[0], terminal.Scores[1]).Run;
            Assert.True(finished.LiveMatch is null); Assert.Equal(2, finished.Lives);
            Assert.Throws<InvalidOperationException>(() => finished.Forfeit());
            Assert.True(loaded.Forfeit().Run.LiveMatch is null);
            var original = File.ReadAllText(path); var node = JsonNode.Parse(original)!;
            ((JsonObject)node["Run"]!["LiveMatch"]!["Options"]!).Remove("TargetScore");
            File.WriteAllText(path, node.ToJsonString()); Assert.True(CampaignStore.Load(path) is null, "Missing live options must reject instead of defaulting.");
            node = JsonNode.Parse(original)!;
            node["Run"]!["Resources"]!["Wallets"]![0] = 999;
            File.WriteAllText(path, node.ToJsonString()); Assert.True(CampaignStore.Load(path) is null); Assert.Equal(node.ToJsonString(), File.ReadAllText(path));
            File.WriteAllText(path, original); node = JsonNode.Parse(original)!; node["Run"]!["LiveMatch"] = null;
            File.WriteAllText(path, node.ToJsonString()); Assert.True(CampaignStore.Load(path) is null);
            File.WriteAllText(path, original); node = JsonNode.Parse(original)!; node["Run"]!["LiveMatch"]!["Hands"]![0] = null;
            File.WriteAllText(path, node.ToJsonString()); Assert.True(CampaignStore.Load(path) is null);
        }
        finally { Directory.Delete(directory, true); }
    }

    public static async Task SessionReorderRestoreAndGates()
    {
        var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"P{s}", PlayerKind.Human, null)).ToArray();
        CampaignCheckpoint? checkpoint = null; var saves = 0; var saveSucceeds = true;
        await using var session = new GameSession("original", "Campaign", seats, new EngineCatalog(), 7, TimeSpan.Zero,
            campaign: new(CampaignSnapshot.Create(7)), liveCheckpoint: c => { saves++; checkpoint = c; return saveSucceeds; });
        await session.StartAsync(); var revision = checkpoint!.Match.Resources.Revision;
        var order = checkpoint.Match.Hands[0].Reverse().ToArray();
        saveSucceeds = false; await session.ArrangeCampaignHandAsync(0, order);
        Assert.Equal(2, saves); Assert.True(session.LastError!.Contains("save")); Assert.True(checkpoint!.Arrangement!.ManuallyArranged);
        Assert.Equal(revision, checkpoint.Match.Resources.Revision); Assert.True(checkpoint.Match.Hands[0].SequenceEqual(order));
        await using var restored = new GameSession("restored", "Campaign", seats, new EngineCatalog(), 123, TimeSpan.Zero, waitForContractPresentation: true);
        await restored.RestoreCampaignAsync(checkpoint);
        Assert.Equal(JsonSerializer.Serialize(await session.GetViewAsync(0)), JsonSerializer.Serialize(await restored.GetViewAsync(0)));
        Assert.False(restored.WaitingForDeal); Assert.False(restored.WaitingForContractPresentation);
        var coherent = await restored.ReadCampaignAsync((v,c,e) => (v!.Campaign!.Revision,c!.Match.Resources.Revision));
        Assert.Equal(coherent.Item1, coherent.Item2);
        var before = JsonSerializer.Serialize(await restored.GetCampaignCheckpointAsync());
        await Assert.ThrowsAsync<GameRuleException>(() => restored.ArrangeCampaignHandAsync(0, order, precondition: () => throw new GameRuleException("stale")));
        Assert.Equal(before, JsonSerializer.Serialize(await restored.GetCampaignCheckpointAsync()));
        saveSucceeds = true; await session.ArrangeCampaignHandAsync(0, order); Assert.True(session.LastError is null);
    }
}
