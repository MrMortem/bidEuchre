using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignBoosterTests
{
    private static CampaignRun Shop(ulong seed) => CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
    { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0] } });

    public static void Stock()
    {
        var themes = new[] { "arcade", "comic", "horror", "retro-synth", "tech" };
        const int samples = 1800;
        var counts = new int[themes.Length];
        for (ulong seed = 1; seed <= samples; seed++)
        {
            var initial = Shop(seed);
            var booster = initial.Shop!.Booster;
            Assert.True(booster is { Sold: false, Choices: null });
            var theme = Array.IndexOf(themes, booster!.Theme);
            Assert.True(theme >= 0, $"Unexpected booster wrapper {booster.Theme}.");
            counts[theme]++;
            var reroll = CampaignShop.Reroll(initial);
            Assert.Equal(booster, reroll.Shop!.Booster!);
        }
        for (var theme = 0; theme < themes.Length; theme++)
            Assert.True(counts[theme] is > 275 and < 445, $"{themes[theme]} appeared {counts[theme]} times among {samples} visits.");
    }

    public static void SodaStock()
    {
        var plain = Shop(17);
        Assert.True(plain.Shop!.BonusBooster is null);
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(plain, packIndex: 1));
        var inventory = plain.Resources.Inventory with
        {
            Sodas = [SodaId.PackAPunch, SodaId.PeachPlus, SodaId.RerollRaspberry, SodaId.CitrusSale, SodaId.GalaxAde, SodaId.CosmicCola],
            Jokers = Enum.GetValues<JokerId>().Take(5).Select(id => new JokerState(id)).ToArray()
        };
        var run = CampaignShop.Open(plain with { Shop = null, Resources = plain.Resources with { Inventory = inventory } });
        Assert.True(run.Shop!.BonusBooster is { Sold: false });
        var sealedPack = CampaignShop.OpenBooster(run, packIndex: 1);
        Assert.Equal(25, sealedPack.Money);
        Assert.True(sealedPack.Shop!.Booster is { Sold: false } && sealedPack.Shop.BonusBooster is { Sold: true, AwaitingOpen: true });
        var choices = sealedPack.Shop.PendingBooster!.Choices!;
        Assert.Equal(5, choices.Distinct().Count());
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(sealedPack));
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(sealedPack));
        Assert.Throws<GameRuleException>(() => sealedPack.LeaveShop());

        var path = Path.Combine(Path.GetTempPath(), $"bonus-booster-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(sealedPack, path));
            var loaded = CampaignShop.Open(CampaignStore.Load(path)!);
            Assert.True(choices.SequenceEqual(loaded.Shop!.PendingBooster!.Choices!));
            var picked = CampaignShop.ResolveBooster(CampaignShop.TearBooster(loaded), choices[4]);
            Assert.Equal(6, picked.Resources.Inventory.Jokers.Length);
            var rerolled = CampaignShop.Reroll(picked);
            Assert.True(rerolled.Shop!.BonusBooster is { Sold: true, Choices: null });
            Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(rerolled, packIndex: 1));
            var other = CampaignShop.TearBooster(CampaignShop.OpenBooster(rerolled));
            Assert.Equal(20, other.Money);
            Assert.Equal(5, other.Shop!.PendingBooster!.Choices!.Length);
            Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(other, other.Shop.PendingBooster.Choices[0]));
            var done = CampaignShop.ResolveBooster(other, null);
            Assert.True(done.Shop!.Booster!.Sold && done.Shop.BonusBooster!.Sold && done.Shop.PendingBooster is null);
            var next = CampaignShop.Open(done with { Match = done.Match + 1 });
            Assert.True(next.Shop!.Booster is { Sold: false } && next.Shop.BonusBooster is { Sold: false });
        }
        finally { File.Delete(path); }
    }

    public static void SellWhileFull()
    {
        var run = Shop(19);
        var owned = Enum.GetValues<JokerId>().Take(5).Select(id => new JokerState(id)).ToArray();
        run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Jokers = owned } } };
        var purchased = CampaignShop.OpenBooster(run);
        Assert.Throws<GameRuleException>(() => CampaignShop.SellJoker(purchased, owned[0].Id, purchased.Resources.Revision));
        var opened = CampaignShop.TearBooster(purchased);
        var choices = opened.Shop!.Booster!.Choices!;
        Assert.Equal(24, opened.Money);
        Assert.True(choices.All(id => !run.Resources.Inventory.Has(id)));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(opened, choices[0]));
        Assert.Throws<GameRuleException>(() => CampaignShop.SellJoker(opened, owned[0].Id, run.Resources.Revision));
        var skipped = CampaignShop.ResolveBooster(opened, null);
        Assert.True(skipped.Resources.Inventory.Jokers.SequenceEqual(owned));
        Assert.Equal(24, skipped.Money);

        var path = Path.Combine(Path.GetTempPath(), $"joker-booster-sale-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(opened, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.True(choices.SequenceEqual(loaded.Shop!.Booster!.Choices!));
            var sold = CampaignShop.SellJoker(loaded, owned[0].Id, loaded.Resources.Revision);
            Assert.Equal(26, sold.Money);
            Assert.Equal(26, sold.Resources.Wallets[0]);
            Assert.True(sold.Resources.Inventory.Jokers.SequenceEqual(owned.Skip(1)));
            Assert.True(choices.SequenceEqual(sold.Shop!.Booster!.Choices!));
            Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(sold));
            Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(sold, choices[0], loaded.Resources.Revision));
            Assert.True(CampaignStore.Save(sold, path));
            var resumed = CampaignStore.Load(path)!;
            var picked = CampaignShop.ResolveBooster(resumed, choices[0], resumed.Resources.Revision);
            Assert.True(picked.Resources.Inventory.Jokers.Select(j => j.Id).SequenceEqual(owned.Skip(1).Select(j => j.Id).Append(choices[0])));
            Assert.Equal(26, picked.Money);
        }
        finally { File.Delete(path); }
    }

    public static void OpenAndResolve()
    {
        var run = Shop(12);
        var owned = Enum.GetValues<JokerId>().Take(4).Select(id => new JokerState(id)).ToArray();
        run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Jokers = owned } } };
        var revision = run.Resources.Revision;
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(run with { Money = 5 }));
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(run with { Shop = run.Shop! with { Booster = null } }));
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(run, revision - 1));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(run, null));

        var teller = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(CampaignShop.PrintTickets(run, teller.Id, 1)));
        var arcadeVisit = run with { Shop = run.Shop! with { ArcadeRoll = 0 } };
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(CampaignArcade.PlayRoulette(arcadeVisit, 1)));

        Assert.Throws<GameRuleException>(() => CampaignShop.TearBooster(run));
        var purchased = CampaignShop.OpenBooster(run, revision);
        Assert.True(purchased.Shop!.Booster!.AwaitingOpen);
        var committed = purchased.Shop.Booster.Choices!;
        Assert.Throws<GameRuleException>(() => CampaignShop.TearBooster(purchased, revision));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(purchased, committed[0]));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(purchased, null));
        Assert.Throws<GameRuleException>(() => CampaignShop.SellJoker(purchased, owned[0].Id));
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(purchased));
        Assert.Throws<GameRuleException>(() => purchased.LeaveShop());
        var opened = CampaignShop.TearBooster(purchased, purchased.Resources.Revision);
        Assert.False(opened.Shop!.Booster!.AwaitingOpen);
        Assert.True(committed.SequenceEqual(opened.Shop.Booster.Choices!));
        Assert.Equal(purchased.Money, opened.Money);
        Assert.Equal(purchased.Resources.RandomState, opened.Resources.RandomState);
        Assert.Throws<GameRuleException>(() => CampaignShop.TearBooster(opened));
        Assert.Equal(6, CampaignShop.BoosterPrice);
        Assert.Equal(24, opened.Money);
        Assert.Equal(24, opened.Resources.Wallets[0]);
        Assert.Equal(revision + 2, opened.Resources.Revision);
        Assert.True(opened.Resources.RandomState != run.Resources.RandomState);
        var booster = opened.Shop!.Booster!;
        Assert.True(booster.Sold);
        Assert.Equal(run.Shop.Booster!.Theme, booster.Theme);
        var choices = booster.Choices!;
        Assert.Equal(4, choices.Length);
        Assert.Equal(4, choices.Distinct().Count());
        Assert.True(choices.All(id => !run.Resources.Inventory.Has(id)));
        Assert.True(choices.SequenceEqual(CampaignShop.OpenBooster(run).Shop!.Booster!.Choices!));
        Assert.Throws<GameRuleException>(() => opened.LeaveShop());
        Assert.Throws<GameRuleException>(() => CampaignShop.Reroll(opened));
        Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(opened));
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(opened, run.Shop.Offers.First(o => o.Category == FeatureCategory.Joker).Id));
        Assert.Throws<GameRuleException>(() => CampaignShop.PrintTickets(opened, teller.Id, 1));
        Assert.Throws<GameRuleException>(() => CampaignArcade.PlayPlinko(opened, 1));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(opened, owned[0].Id));
        Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(opened, choices[0], revision));
        foreach (var pick in choices.Select(id => (JokerId?)id).Append(null))
        {
            var resolved = CampaignShop.ResolveBooster(opened, pick, opened.Resources.Revision);
            Assert.Equal(24, resolved.Money);
            Assert.Equal(opened.Resources.RandomState, resolved.Resources.RandomState);
            Assert.Equal(revision + 3, resolved.Resources.Revision);
            Assert.True(resolved.Shop!.Booster is { Sold: true, Choices: null });
            var expected = owned.Select(joker => joker.Id);
            if (pick is { } taken) expected = expected.Append(taken);
            Assert.True(resolved.Resources.Inventory.Jokers.Select(joker => joker.Id).SequenceEqual(expected));
            Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(resolved));
            Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(resolved, pick));
            var rerolled = CampaignShop.Reroll(resolved);
            Assert.Equal(resolved.Shop.Booster!, rerolled.Shop!.Booster!);
            Assert.Throws<GameRuleException>(() => CampaignShop.OpenBooster(rerolled));
            Assert.True(CampaignShop.Open(rerolled with { Match = rerolled.Match + 1 }).Shop!.Booster is { Sold: false, Choices: null });
            Assert.Equal(CampaignPhase.Intro, resolved.LeaveShop().Phase);
        }
    }

    public static void Persistence()
    {
        var run = Shop(12);
        var purchased = CampaignShop.OpenBooster(run);
        var opened = CampaignShop.TearBooster(purchased);
        var choices = opened.Shop!.Booster!.Choices!;
        var path = Path.Combine(Path.GetTempPath(), $"joker-booster-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var state in new[] { run, purchased, opened, CampaignShop.ResolveBooster(opened, choices[2]), CampaignShop.ResolveBooster(opened, null) })
            {
                Assert.True(CampaignStore.Save(state, path));
                var loaded = CampaignStore.Load(path)!;
                Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(CampaignShop.Open(loaded)));
                if (state == purchased)
                {
                    Assert.Throws<GameRuleException>(() => CampaignShop.ResolveBooster(loaded, null));
                    var torn = CampaignShop.TearBooster(loaded);
                    Assert.Equal(JsonSerializer.Serialize(opened), JsonSerializer.Serialize(torn));
                }
                if (state == opened)
                {
                    Assert.Throws<GameRuleException>(() => loaded.LeaveShop());
                    var picked = CampaignShop.ResolveBooster(loaded, choices[3]);
                    Assert.Equal(choices[3], picked.Resources.Inventory.Jokers.Single().Id);
                    Assert.Equal(24, picked.Money);
                }
            }

            Assert.True(CampaignStore.Save(opened, path));
            var original = File.ReadAllText(path);
            void Reject(Action<JsonNode> mutate)
            {
                var node = JsonNode.Parse(original)!;
                mutate(node["Run"]!["Shop"]!["Booster"]!);
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
            Reject(b => b["Theme"] = "spectral");
            Reject(b => b["Choices"]![0] = b["Choices"]![1]!.DeepClone());
            Reject(b => b["Choices"]![0] = 999);
            Reject(b => b["Choices"]!.AsArray().RemoveAt(0));
            Reject(b => b["Sold"] = false);

            var previousFormat = JsonNode.Parse(original)!;
            previousFormat["Run"]!["Shop"]!["Booster"]!.AsObject().Remove("AwaitingOpen");
            File.WriteAllText(path, previousFormat.ToJsonString());
            var alreadyTorn = CampaignStore.Load(path)!;
            Assert.False(alreadyTorn.Shop!.Booster!.AwaitingOpen);
            Assert.Equal(choices[0], CampaignShop.ResolveBooster(alreadyTorn, choices[0]).Resources.Inventory.Jokers.Single().Id);

            Assert.True(CampaignStore.Save(run, path));
            var legacy = JsonNode.Parse(File.ReadAllText(path))!;
            legacy["Run"]!["Shop"]!.AsObject().Remove("Booster");
            File.WriteAllText(path, legacy.ToJsonString());
            var restored = CampaignShop.Open(CampaignStore.Load(path)!);
            Assert.True(restored.Shop!.Booster is { Sold: false, Choices: null });
            Assert.Equal(run.Money, restored.Money);
            Assert.Equal(JsonSerializer.Serialize(run.Shop!.Offers), JsonSerializer.Serialize(restored.Shop.Offers));
            Assert.Equal(JsonSerializer.Serialize(restored), JsonSerializer.Serialize(CampaignShop.Open(restored)));
            Assert.True(CampaignStore.Save(restored, path));
            Assert.Equal(JsonSerializer.Serialize(restored), JsonSerializer.Serialize(CampaignShop.Open(CampaignStore.Load(path)!)));
        }
        finally { File.Delete(path); }
    }
}
