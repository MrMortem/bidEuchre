using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class PanicRetirementTests
{
    private static readonly GumballId Retired = (GumballId)7;

    public static void ActiveRosterAndPulls()
    {
        Assert.False(Enum.IsDefined(Retired), "The retired numeric identity must not remain playable.");
        Assert.Equal(8, (int)GumballId.HighLow);
        Assert.Equal(10, CampaignInventory.GumballCount);
        Assert.False(FeatureCatalog.All.Any(f => f.Id == "gumball:panic-button"));
        Assert.False(typeof(CampaignCommand).GetNestedTypes().Any(type => type.Name == "PanicButton"));
        foreach (var full in new[] { false, true })
        {
            var inventory = new CampaignInventory([], [], full ? Enum.GetValues<GumballId>() : []);
            ulong random = 17;
            var seen = new HashSet<GumballId>();
            for (var i = 0; i < 256; i++)
            {
                var grant = GumballGrants.Pull(inventory, random);
                Assert.True(grant.Id != Retired);
                inventory = grant.Inventory; random = grant.RandomState; seen.Add(grant.Id);
            }
            Assert.True(seen.SetEquals(Enum.GetValues<GumballId>()));
        }
    }

    public static void LegacyShopMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"panic-retired-{Guid.NewGuid():N}.json");
        try
        {
            var run = new CampaignRun(Money: 31, Match: 2, Phase: CampaignPhase.Shop)
            {
                Resources = CampaignSnapshot.Create(43) with { Wallets = [31, 0, 0, 0], Inventory = new([], [], [GumballId.HighLow]) },
                Shop = new(1, [new("machine", FeatureCategory.Gumball, nameof(GumballId.HighLow), 3, true)])
            };
            Assert.True(CampaignStore.Save(run, path));
            var original = JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var version in new[] { 2, 3 })
            foreach (var prefix in new[] { "gumball:", "planet:" })
            {
                var node = original.DeepClone(); node["Version"] = version;
                var inventory = node["Run"]!["Resources"]!["Inventory"]!;
                inventory["Unlocks"]!.AsArray().Add(prefix + "panic-button");
                inventory["GumballExtraCopies"]![prefix + "panic-button"] = 5;
                inventory["GumballExtraCopies"]!["gumball:high-low"] = 2;
                node["Run"]!["Shop"]!["Offers"]![0]!["FeatureId"] = "PanicButton";
                File.WriteAllText(path, node.ToJsonString());
                var migrated = CampaignStore.Load(path)!;
                Assert.True(migrated is not null, "A known retired unlock must migrate instead of corrupting the run.");
                Assert.True(migrated!.Resources.Inventory.Unlocks.SequenceEqual([GumballId.HighLow]));
                Assert.False(migrated.Resources.Inventory.GumballExtraCopies.ContainsKey(Retired));
                Assert.Equal(2, migrated.Resources.Inventory.GumballExtraCopies[GumballId.HighLow]);
                Assert.Equal(31, migrated.Money);
                Assert.True(migrated.Shop!.Offers.Single().Sold, "The spent machine cannot become a free new pull.");
                Assert.Equal("", migrated.Shop.Offers.Single().FeatureId);
                Assert.Equal(node.ToJsonString(), File.ReadAllText(path), "Migration reads must not rewrite the source file.");
                Assert.True(CampaignStore.Save(migrated, path));
                Assert.False(File.ReadAllText(path).Contains("panic-button"));
            }
        }
        finally { File.Delete(path); }
    }

    public static void LiveCheckpointMigrationPreservesTheHandAndHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"panic-live-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var phase in new[] { GamePhase.Bidding, GamePhase.Playing, GamePhase.HandComplete, GamePhase.GameComplete })
            foreach (var prefix in new[] { "gumball:", "planet:" })
            {
                var resources = CampaignSnapshot.Create(73) with
                {
                    Wallets = [17, 2, 3, 4],
                    Inventory = new([new(JokerId.DoubleDown)], [TarotId.Gambler], [GumballId.HighLow])
                    { GumballExtraCopies = new() { [GumballId.HighLow] = 2 } }
                };
                resources.Deck[3] = resources.Deck[3] with { Enhancement = EnhancementId.Lucky, Sigil = SigilId.Fish };
                var game = new GameEngine(["You", "W", "P", "E"], 73, new MatchOptions(100) { NegativeThreshold = -100 }, new(resources));
                game.StartGame(3);
                if (phase == GamePhase.Playing)
                {
                    game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four));
                    while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
                    game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.High));
                    game.ExecuteCampaignAction(0, game.CreateCampaignView(0).LegalActions[0]);
                }
                var snapshot = game.ExportCampaignMatchSnapshot();
                if (phase is GamePhase.HandComplete or GamePhase.GameComplete)
                {
                    const string reason = "Panic Button: your team -8; hand consumed.";
                    snapshot = snapshot with { Phase = phase, CurrentSeat = null, GameWinner = phase == GamePhase.GameComplete ? 1 : null,
                        Scores = [-8, 0], CompletionReason = reason, Events = [.. snapshot.Events, reason] };
                }
                var expected = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(snapshot));
                Assert.True(CampaignStore.Save(expected, path));
                var node = JsonNode.Parse(File.ReadAllText(path))!;
                foreach (var savedResources in new[] { node["Run"]!["Resources"]!, node["Run"]!["LiveMatch"]!["Resources"]! })
                {
                    savedResources["Inventory"]!["Unlocks"]!.AsArray().Add(prefix + "panic-button");
                    savedResources["Inventory"]!["GumballExtraCopies"]![prefix + "panic-button"] = 4;
                }
                File.WriteAllText(path, node.ToJsonString());
                var loaded = CampaignStore.Load(path)!;
                Assert.True(loaded is not null, $"Retired unlock migration must retain a {phase} checkpoint.");
                Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(loaded));
                Assert.Equal(node.ToJsonString(), File.ReadAllText(path));
                Assert.False(loaded!.LegacyMatchRestart);
                var restored = GameEngine.RestoreCampaignMatch(loaded.LiveMatch!);
                Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(restored.ExportCampaignMatchSnapshot()));
                if (phase == GamePhase.HandComplete)
                {
                    restored.StartNextHand();
                    Assert.Equal(2, restored.HandNumber);
                    Assert.Equal(-8, restored.Scores[0]);
                }
                if (phase == GamePhase.GameComplete)
                {
                    Assert.Equal(2, loaded.ApplyResult(false, -8, 0).Run.Lives);
                    Assert.True(restored.CompletionReason!.Contains("Panic Button"), "Past events must stay readable.");
                }
                Assert.True(CampaignStore.Save(loaded, path));
                Assert.Equal(JsonSerializer.Serialize(loaded), JsonSerializer.Serialize(CampaignStore.Load(path)));
            }
        }
        finally { File.Delete(path); }
    }

    public static void OnlyKnownRetiredIdentitiesAreMigrated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"panic-strict-{Guid.NewGuid():N}.json");
        try
        {
            var run = new CampaignRun(Money: 13, Match: 2, Phase: CampaignPhase.Shop)
            {
                Resources = CampaignSnapshot.Create(9) with { Wallets = [13, 0, 0, 0], Inventory = new([], [], [GumballId.HighLow]) },
                Shop = new(1, [new("old", FeatureCategory.Gumball, nameof(GumballId.HighLow), 3, true)])
            };
            Assert.True(CampaignStore.Save(run, path));
            var original = JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var unknown in new JsonNode[] { JsonValue.Create("gumball:unknown")!, JsonValue.Create("planet:unknown")!, JsonValue.Create(7)! })
            {
                var node = original.DeepClone();
                node["Run"]!["Resources"]!["Inventory"]!["Unlocks"]!.AsArray().Add(unknown.DeepClone());
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
            foreach (var unknown in new[] { "gumball:unknown", "planet:unknown", "7", "PanicButton" })
            {
                var node = original.DeepClone();
                node["Run"]!["Resources"]!["Inventory"]!["GumballExtraCopies"]![unknown] = 1;
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
            // Bare saves used numeric enums and never held a live checkpoint. Their
            // established resource reset stays unchanged, while a paid retired machine stays spent.
            var bare = JsonSerializer.SerializeToNode(run)!;
            bare["Resources"]!["Inventory"]!["Unlocks"]!.AsArray().Add(7);
            bare["Resources"]!["Inventory"]!["GumballExtraCopies"]!["PanicButton"] = 2;
            bare["Shop"]!["Offers"]![0]!["FeatureId"] = "PanicButton";
            File.WriteAllText(path, bare.ToJsonString());
            var legacy = CampaignStore.Load(path)!;
            Assert.True(legacy is not null);
            Assert.Equal(13, legacy!.Money);
            Assert.Equal(0, legacy.Resources.Inventory.Unlocks.Length);
            Assert.True(legacy.Shop!.Offers[0].Sold);
            Assert.Equal("", legacy.Shop.Offers[0].FeatureId);
        }
        finally { File.Delete(path); }
    }

    public static void MalformedRetiredDataIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"panic-invalid-{Guid.NewGuid():N}.json");
        try
        {
            var resources = CampaignSnapshot.Create(13) with { Inventory = new([], [], [GumballId.HighLow]) };
            var game = new GameEngine(["You", "W", "P", "E"], 13, new MatchOptions(12), new(resources));
            game.StartGame(3);
            Assert.True(CampaignStore.Save(new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot())), path));
            var valid = JsonNode.Parse(File.ReadAllText(path))!;
            IEnumerable<JsonNode> Inventories(JsonNode node) =>
                [node["Run"]!["Resources"]!["Inventory"]!, node["Run"]!["LiveMatch"]!["Resources"]!["Inventory"]!];
            foreach (var inventory in Inventories(valid))
            {
                inventory["Unlocks"]!.AsArray().Add("gumball:panic-button");
                inventory["GumballExtraCopies"]!["gumball:panic-button"] = 2;
            }
            void Reject(Action<JsonNode> change)
            {
                var node = valid.DeepClone(); change(node);
                var text = node.ToJsonString(); File.WriteAllText(path, text);
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
                Assert.Equal(text, File.ReadAllText(path));
            }
            foreach (var invalid in new JsonNode?[] { JsonValue.Create(-1), JsonValue.Create((long)int.MaxValue + 1),
                JsonValue.Create("broken"), new JsonObject(), new JsonArray(), null })
                Reject(node =>
                {
                    foreach (var inventory in Inventories(node))
                        inventory["GumballExtraCopies"]!["gumball:panic-button"] = invalid?.DeepClone();
                });
            foreach (var alias in new[] { "gumball:panic-button", "planet:panic-button" })
                Reject(node =>
                {
                    foreach (var inventory in Inventories(node)) inventory["Unlocks"]!.AsArray().Add(alias);
                });
            Reject(node =>
            {
                foreach (var inventory in Inventories(node)) inventory["Unlocks"]!.AsArray().RemoveAt(1);
            });
            Reject(node =>
            {
                foreach (var inventory in Inventories(node)) inventory["GumballExtraCopies"]!["gumball:high-low"] = int.MaxValue - 1;
            });
            Reject(node =>
            {
                foreach (var inventory in Inventories(node)) inventory["GumballExtraCopies"]!["planet:panic-button"] = 2;
            });
            // Neither a different count nor a missing retired entry may be erased into
            // apparently equal run and checkpoint resources by the migration.
            Reject(node => node["Run"]!["Resources"]!["Inventory"]!["GumballExtraCopies"]!["gumball:panic-button"] = 3);
            Reject(node => node["Run"]!["Resources"]!["Inventory"]!["GumballExtraCopies"]!.AsObject().Remove("gumball:panic-button"));
            Reject(node => node["Run"]!["Resources"]!["Inventory"]!["Unlocks"] = new JsonArray("gumball:panic-button", "gumball:high-low"));
            Reject(node =>
            {
                var inventory = node["Run"]!["Resources"]!["Inventory"]!;
                inventory["Unlocks"]!.AsArray().RemoveAt(1);
                inventory["GumballExtraCopies"]!.AsObject().Remove("gumball:panic-button");
            });
        }
        finally { File.Delete(path); }
    }
}
