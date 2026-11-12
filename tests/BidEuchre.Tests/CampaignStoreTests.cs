using BidEuchre.App;

internal static class CampaignStoreTests
{
    public static void RoundTrip() => InTempFolder((folder, path) =>
    {
        Assert.True(CampaignStore.Load(path) is null, "Missing file is no save");
        foreach (var phase in Enum.GetValues<CampaignPhase>())
        {
            var run = new CampaignRun(2, 7, 31, 4, phase, VictoryShown: phase is CampaignPhase.Victory);
            Assert.True(CampaignStore.Save(run, path));
            Assert.Equal(run, CampaignStore.Load(path)!);
        }
        Assert.True(CampaignStore.Delete(path));
        Assert.True(CampaignStore.Load(path) is null);
        Assert.True(CampaignStore.Delete(path), "Deleting a missing save succeeds");
        Assert.False(CampaignStore.Save(new CampaignRun(), folder), "A directory path fails gracefully");
    });

    public static void RejectsInvalidSaves() => InTempFolder((_, path) =>
    {
        foreach (var text in new[]
        {
            "not json", "null", "[]", "{\"Lives\":0}", "{\"Lives\":3,\"Match\":0}",
            "{\"Lives\":3,\"Match\":1,\"Money\":-5}", "{\"Lives\":3,\"Match\":1,\"MatchesWon\":-1}",
            "{\"Lives\":3,\"Match\":1,\"Phase\":99}", "{\"Lives\":\"many\"}"
        })
        {
            File.WriteAllText(path, text);
            Assert.True(CampaignStore.Load(path) is null, text);
        }
        File.WriteAllText(path, "{\"Lives\":2,\"Match\":4}");
        Assert.Equal(new CampaignRun(2, 4), CampaignStore.Load(path)!);
    });

    public static void AbandonedMatchForfeits() => InTempFolder((_, path) =>
    {
        CampaignStore.Save(new CampaignRun(3, 3, 10).StartMatch(), path);
        var (run, outcome) = CampaignStore.Load(path)!.Forfeit();
        Assert.True(outcome.Forfeited);
        Assert.Equal(2, outcome.LivesLost);
        Assert.Equal(1, run.Lives);
        Assert.Equal(12, run.Money);
        Assert.Equal(CampaignPhase.Shop, run.Phase);
    });

    private static void InTempFolder(Action<string, string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"euchre-campaign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try { test(folder, Path.Combine(folder, "campaign.json")); }
        finally { Directory.Delete(folder, true); }
    }
}
