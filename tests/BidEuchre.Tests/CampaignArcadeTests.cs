using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignArcadeTests
{
    private static CampaignRun Tear(CampaignRun run) =>
        CampaignArcade.TearSpectralPack(run, run.Shop!.Arcade!.Pending!.PlayId, run.Resources.Revision);
    private static string Snapshot(CampaignRun run) => JsonSerializer.Serialize(run);
    private static CampaignRun GeneratedShop(ulong seed) => CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
    { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0] } });
    private static CampaignRun Shop(ulong seed = 41)
    {
        var run = GeneratedShop(seed);
        return run with { Shop = run.Shop! with { ArcadeRoll = 0 } };
    }
    private static CampaignRun Outcome(ArcadeGame game, int score)
    {
        for (ulong seed = 1; seed < 1000; seed++)
        {
            var run = Shop(seed);
            var played = game == ArcadeGame.Plinko ? CampaignArcade.PlayPlinko(run, score == 6 ? 0 : 4) : CampaignArcade.PlayRoulette(run, 7);
            if (played.Shop!.Arcade!.Pending!.Score == score) return played;
        }
        throw new Exception("No deterministic fixture found.");
    }
    // An older save's pending Sigil prize: one drawn Sigil and no pack.
    private static CampaignRun Legacy(CampaignRun run, string sigil = "Fish") => run with { Shop = run.Shop! with
    { Arcade = run.Shop.Arcade! with { Pending = run.Shop.Arcade.Pending! with { FeatureId = sigil, PackCards = null, AwaitingOpen = false } } } };
    private static IEnumerable<CampaignRun> Packs(int seeds, Func<CampaignRun, CampaignRun>? prepare = null)
    {
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            var run = Shop(seed); if (prepare is not null) run = prepare(run);
            var played = CampaignArcade.PlayPlinko(run, 0);
            if (played.Shop!.Arcade!.Pending!.PrizeCategory == FeatureCategory.Sigil) yield return played;
        }
    }
    private static void Reject(CampaignRun run, Action action)
    {
        var original = Snapshot(run);
        Assert.Throws<GameRuleException>(action);
        Assert.Equal(original, Snapshot(run));
    }
    private static CampaignRun RoundTrip(CampaignRun run)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run, path));
            var loaded = CampaignStore.LoadSave(path);
            Assert.Equal(CampaignLoadStatus.Loaded, loaded.Status);
            Assert.Equal(Snapshot(run), Snapshot(loaded.Save!.Run));
            return loaded.Save.Run;
        }
        finally { File.Delete(path); }
    }

    public static void SodaArcadeCadence()
    {
        var open = 0;
        var halfOpen = 0;
        var alwaysOpen = 0;
        for (ulong seed = 1; seed <= 600; seed++)
        {
            var run = GeneratedShop(seed);
            var midnight = WithSodas(run, SodaId.MidnightGrape);
            var twilight = WithSodas(run, SodaId.MidnightGrape, SodaId.TwilightTangerine);
            if (CampaignArcade.Available(midnight)) halfOpen++;
            if (CampaignArcade.Available(twilight)) alwaysOpen++;
            Assert.Equal(CampaignArcade.Available(run), CampaignArcade.Available(CampaignShop.Reroll(run)));
            try { CampaignArcade.PlayRoulette(run, 1); open++; }
            catch (GameRuleException error) when (error.Message.Contains("closed")) { }
        }
        Assert.True(halfOpen is > 250 and < 350);
        Assert.Equal(600, alwaysOpen);
        Assert.True(open is > 150 and < 250, $"Expected about a third of shops to open the Arcade, got {open}/600.");
    }

    private static CampaignRun WithSodas(CampaignRun run, params SodaId[] sodas) => run with
    { Resources = run.Resources with { Inventory = run.Resources.Inventory with { Sodas = sodas } } };

    public static void SodaSpectralRewards()
    {
        var single = 0;
        var stocked = 0;
        CampaignRun? printed = null;
        CampaignRun? direct = null;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var source = WithSodas(Shop(seed), SodaId.SpectralSprite, SodaId.GhostlyGuava, SodaId.RerollRaspberry, SodaId.CitrusSale);
            var run = CampaignShop.Open(source with { Shop = null });
            var teller = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
            var stock = run.Shop.Offers.SingleOrDefault(o => o.Category == FeatureCategory.Sigil);
            if (stock is not null) { stocked++; direct ??= run; }
            var result = CampaignShop.PrintTickets(run, teller.Id, 1);
            Assert.Equal(28, result.Money);
            if (CampaignShop.PrintedCard(result.Shop!.Offers.Single(o => o.Id == teller.Id).Printed![0])!.Category == FeatureCategory.Sigil)
                single++;
            var spread = CampaignShop.PrintTickets(run, teller.Id, 3);
            Assert.Equal(25, spread.Money);
            var choices = spread.Shop!.Offers.Single(o => o.Id == teller.Id).Printed!;
            Assert.Equal(3, choices.Distinct().Count());
            if (choices.Any(key => CampaignShop.PrintedCard(key)!.Category == FeatureCategory.Sigil)
                && choices.Any(key => CampaignShop.PrintedCard(key)!.Category == FeatureCategory.Tarot))
                printed ??= spread;
        }
        Assert.True(single is > 40 and < 110 && stocked is > 40 and < 110);
        var paid = RoundTrip(printed!);
        var offer = paid.Shop!.Offers.Single(o => o.Printed is not null);
        var choice = offer.Printed!.First(key => CampaignShop.PrintedCard(key)!.Category == FeatureCategory.Sigil);
        var sigil = Enum.Parse<SigilId>(CampaignShop.PrintedCard(choice)!.Key);
        var kept = RoundTrip(CampaignShop.KeepTicket(paid, offer.Id, choice));
        Assert.True(kept.Resources.Inventory.Sigils.Contains(sigil));
        Assert.Equal(paid.Money, kept.Money);
        Reject(kept, () => CampaignShop.KeepTicket(kept, offer.Id, choice));
        var target = offer.TargetCards![0];
        var begun = RoundTrip(CampaignShop.BeginTicketUse(paid, offer.Id, choice));
        var applied = CampaignShop.UsePurchased(begun, [target]);
        Assert.Equal(sigil, applied.Resources.Deck.Single(c => c.Id == target).Sigil!.Value);
        var instant = CampaignShop.UseTicket(paid, offer.Id, choice, [target]);
        Assert.Equal(sigil, instant.Resources.Deck.Single(c => c.Id == target).Sigil!.Value);
        Assert.Equal(paid.Money, instant.Money);
        var restored = CampaignShop.Open(RoundTrip(direct!));
        var shelf = restored.Shop!.Offers.Single(o => o.Category == FeatureCategory.Sigil);
        Assert.Equal(6, CampaignShop.TargetCards(restored, shelf.Id).Length);
        Assert.Equal(28, CampaignShop.BeginSigilUse(restored, shelf.Id).Money);

        var jackpots = Packs(150, run => WithSodas(run, SodaId.PackAPunch, SodaId.PeachPlus, SodaId.RerollRaspberry, SodaId.CitrusSale));
        var jackpot = RoundTrip(jackpots.First());
        Assert.Equal(19, jackpot.Money);
        Assert.Equal(4, jackpot.Shop!.Arcade!.Pending!.PackCards!.Distinct().Count());
        var torn = Tear(jackpot);
        var selected = CampaignArcade.PickPackCard(torn, torn.Shop!.Arcade!.Pending!.PlayId, 3);
        Assert.Equal(torn.Money, selected.Money);
    }

    public static void StockAndLegacyMigration()
    {
        var run = Shop();
        Assert.True(run.Shop!.Offers.All(o => o.Category != FeatureCategory.Sigil));
        Assert.Equal(2, run.Shop.Offers.Count(o => o.Category == FeatureCategory.Joker));
        Assert.Equal(new ArcadeState(), run.Shop.Arcade!);
        Assert.True(ReferenceEquals(run, CampaignShop.Open(run)));
        var legacy = run with { Shop = run.Shop with { Arcade = null, Offers = [.. run.Shop.Offers,
            new ShopOffer("old-sigil", FeatureCategory.Sigil, "Fish", 3)] } };
        var original = Snapshot(legacy);
        var migrated = CampaignShop.Open(RoundTrip(legacy));
        Assert.Equal(original, Snapshot(legacy));
        Assert.True(migrated.Shop!.Offers.All(o => o.Category != FeatureCategory.Sigil));
        Assert.Equal(run.Resources.RandomState, migrated.Resources.RandomState);
        Assert.Equal(legacy.Resources.Revision + 1, migrated.Resources.Revision);
        Assert.Equal(legacy.Money, migrated.Money);
        Assert.True(ReferenceEquals(migrated, CampaignShop.Open(migrated)));

        var six = legacy.Resources.Deck.Take(6).Select(c => c.Id).ToArray();
        var paid = legacy with { Shop = legacy.Shop! with
        {
            Offers = [new ShopOffer("old-sigil", FeatureCategory.Sigil, "Fish", 3, true) { TargetCards = six }],
            UseSession = new("old-sigil", FeatureCategory.Sigil, "Fish", false)
        } };
        Assert.True(ReferenceEquals(paid, CampaignShop.Open(paid)));
        var used = CampaignShop.FinishUse(CampaignShop.UsePurchased(RoundTrip(paid), [six[0]]));
        Assert.Equal(SigilId.Fish, used.Resources.Deck.Single(c => c.Id == six[0]).Sigil!.Value);
        Assert.True(CampaignShop.Open(used).Shop!.Arcade is not null);
    }

    public static void OutcomesCommitBeforeAnimation()
    {
        Assert.True(CampaignArcade.PlinkoScores.SequenceEqual([6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6]));
        Assert.Equal(100, CampaignArcade.RouletteScore(7, 7));
        Assert.Equal(50, CampaignArcade.RouletteScore(7, 1));
        Assert.Equal(0, CampaignArcade.RouletteScore(7, 2));
        foreach (var game in new[] { ArcadeGame.Plinko, ArcadeGame.Roulette })
        foreach (var score in game == ArcadeGame.Plinko ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 0, 50, 100 })
        {
            var run = Outcome(game, score);
            var result = run.Shop!.Arcade!.Pending!;
            Assert.Equal(18, run.Money);
            Assert.Equal(18, run.Resources.Wallets[0]);
            Assert.Equal(2L, run.Resources.Revision);
            Assert.Equal(0, run.Resources.Inventory.ItemCount);
            Assert.Equal(game == ArcadeGame.Plinko, run.Shop.Arcade.PlinkoPlayed);
            Assert.Equal(game == ArcadeGame.Roulette, run.Shop.Arcade.RoulettePlayed);
            Assert.Equal(result.PrizeCategory is null ? 0 : 6, result.TargetCards.Distinct().Count());
            Assert.True(result.TargetCards.All(id => run.Resources.Deck.Any(c => c.Id == id)));
            Assert.True(ReferenceEquals(run, CampaignShop.Open(run)));
            Assert.Equal(Snapshot(run), Snapshot(Outcome(game, score)));
            RoundTrip(run);
            if (game == ArcadeGame.Plinko)
            {
                Assert.Equal(15, result.Path.Length);
                Assert.Equal(6 + result.Choice * 2, result.Path[0]);
                Assert.Equal(result.Landing, Math.Clamp((78 + 40 * result.Path[^1] - 64) / 98, 0, 10));
                Assert.True(result.Path.All(p => p is >= 0 and <= 26));
                var minimums = new[] { 6, 5, 4, 3, 4, 3, 2, 3, 2, 1, 2, 1, 0, 1 };
                Assert.True(result.Path.Take(14).Select((p, row) => p >= minimums[row] && p <= 26 - minimums[row]).All(valid => valid));
                Assert.True(result.Path.Skip(1).Select((p, i) => Math.Abs(p - result.Path[i])).All(d => d == 1));
            }
            else Assert.Equal(0, result.Path.Length);
        }
        for (var lane = 0; lane < 8; lane++)
        {
            var original = Shop(); var before = Snapshot(original);
            var played = CampaignArcade.PlayPlinko(original, lane, original.Resources.Revision);
            Assert.Equal(before, Snapshot(original));
            Assert.True(played.Shop!.Arcade!.Pending!.Path.All(p => p is >= 0 and <= 26));
        }
    }

    public static void EntryCoversBothGamesThroughReload()
    {
        var open = Shop();
        var poor = open with { Money = 11, Resources = open.Resources with { Wallets = [11, 0, 0, 0] } };
        Reject(poor, () => CampaignArcade.PlayPlinko(poor, 4));
        var discounted = poor with { Resources = poor.Resources with
        { Inventory = poor.Resources.Inventory with { Sodas = [SodaId.RerollRaspberry, SodaId.CitrusSale] } } };
        Assert.Equal(0, CampaignArcade.PlayPlinko(discounted, 4).Money);

        foreach (var first in new[] { ArcadeGame.Plinko, ArcadeGame.Roulette })
        {
            var paidLoss = Outcome(first, first == ArcadeGame.Plinko ? 1 : 0);
            var pending = paidLoss.Shop!.Arcade!.Pending!;
            // The played flags also cover older saves that paid the historical per-game fee.
            var paid = paidLoss with { Money = 0, Resources = paidLoss.Resources with { Wallets = [0, 0, 0, 0] } };
            var resumed = RoundTrip(CampaignArcade.DismissArcadeLoss(RoundTrip(paid), pending.PlayId));
            var second = first == ArcadeGame.Plinko ? CampaignArcade.PlayRoulette(resumed, 7) : CampaignArcade.PlayPlinko(resumed, 4);
            Assert.Equal(0, second.Money);
            Assert.Equal(0, second.Resources.Wallets[0]);
            Assert.True(second.Shop!.Arcade is { PlinkoPlayed: true, RoulettePlayed: true });
        }
    }

    public static void PrizesKeepOrUseWithoutExtraPayment()
    {
        foreach (var game in new[] { ArcadeGame.Plinko, ArcadeGame.Roulette })
        foreach (var score in game == ArcadeGame.Plinko ? new[] { 3, 5 } : new[] { 50, 100 })
        {
            var won = Outcome(game, score); if (won.Shop!.Arcade!.Pending!.PrizeCategory == FeatureCategory.Sigil) won = Legacy(won);
            var prize = won.Shop!.Arcade!.Pending!;
            Reject(won, () => CampaignArcade.DismissArcadeLoss(won, prize.PlayId));
            Reject(won, () => CampaignArcade.KeepArcadePrize(won, "other"));
            Reject(won, () => CampaignArcade.KeepArcadePrize(won, prize.PlayId, won.Resources.Revision - 1));
            var kept = CampaignArcade.KeepArcadePrize(RoundTrip(won), prize.PlayId, won.Resources.Revision);
            Assert.Equal(18, kept.Money); Assert.Equal(1, kept.Resources.Inventory.ItemCount);
            Assert.Equal(prize.PrizeCategory == FeatureCategory.Sigil ? 1 : 0, kept.Resources.Inventory.Sigils.Length);
            Assert.Equal(prize.PrizeCategory == FeatureCategory.Tarot ? 1 : 0, kept.Resources.Inventory.Tarot.Length);
            Assert.Equal(won.Resources.RandomState, kept.Resources.RandomState);
            Assert.True(kept.Shop!.Arcade!.Pending is null);
            Reject(kept, () => CampaignArcade.KeepArcadePrize(kept, prize.PlayId));
            RoundTrip(kept);
            // Immediate use also works for each reward category without spending again.
            var begun = CampaignArcade.BeginArcadePrizeUse(won, prize.PlayId);
            var targetCount = prize.PrizeCategory == FeatureCategory.Sigil ? 1 : Tarot.TargetCount(Enum.Parse<TarotId>(prize.FeatureId!));
            var used = CampaignShop.UsePurchased(RoundTrip(begun), prize.TargetCards.Take(targetCount).ToArray());
            var finished = CampaignShop.FinishUse(used);
            Assert.Equal(used.Money, finished.Money);
            Assert.Equal(0, finished.Resources.Inventory.ItemCount);
            RoundTrip(finished);
        }
        var full = Legacy(Outcome(ArcadeGame.Plinko, 5));
        full = full with { Resources = full.Resources with { Inventory = new([], [TarotId.Gambler, TarotId.Prospector], []) { Sigils = [SigilId.Fish, SigilId.Bear] } } };
        var pending = full.Shop!.Arcade!.Pending!;
        Reject(full, () => CampaignArcade.KeepArcadePrize(full, pending.PlayId));
        var discarded = CampaignShop.DiscardSigil(full, 0, full.Resources.Revision);
        Assert.Equal(4, CampaignArcade.KeepArcadePrize(discarded, pending.PlayId).Resources.Inventory.ItemCount);
        var session = CampaignArcade.BeginArcadePrizeUse(full, pending.PlayId, full.Resources.Revision);
        Assert.Equal(18, session.Money); Assert.Equal(4, session.Resources.Inventory.ItemCount);
        Assert.Equal(full.Resources.RandomState, session.Resources.RandomState);
        Assert.True(session.Shop!.Arcade!.Pending is null);
        Assert.True(session.Shop.Offers.Single(o => o.Id == session.Shop.UseSession!.OfferId) is { IsArcadePrize: true, Sold: true, Price: 0 });
        Assert.True(CampaignShop.UseSessionCards(session).Select(c => c.Id).SequenceEqual(pending.TargetCards));
        Reject(session, () => CampaignShop.FinishUse(session));
        Reject(session, () => session.LeaveShop());
        Reject(session, () => CampaignArcade.PlayRoulette(session, 7));
        session = CampaignShop.UseHeldTarot(RoundTrip(session), 0, []);
        Assert.Contains(session.Resources.Deck.Single(c => c.Enhancement == EnhancementId.Lucky).Id, pending.TargetCards);
        session = CampaignShop.UsePurchased(session, [pending.TargetCards[0]], session.Resources.Revision);
        session = CampaignShop.UseHeldTarot(session, 0, [pending.TargetCards[2]], session.Resources.Revision);
        session = CampaignShop.FinishUse(RoundTrip(session), session.Resources.Revision);
        Assert.Equal(18, session.Money);
        Assert.Equal(2, session.Resources.Inventory.ItemCount);
        Assert.Equal(Enum.Parse<SigilId>(pending.FeatureId!), session.Resources.Deck.Single(c => c.Id == pending.TargetCards[0]).Sigil!.Value);
        Assert.Equal(CampaignPhase.Intro, RoundTrip(session).LeaveShop().Phase);
    }

    public static void PendingAndVisitGuards()
    {
        var open = Shop(); var rev = open.Resources.Revision;
        Reject(open, () => CampaignArcade.PlayPlinko(open, -1));
        Reject(open, () => CampaignArcade.PlayPlinko(open, 8));
        Reject(open, () => CampaignArcade.PlayRoulette(open, 0));
        Reject(open, () => CampaignArcade.PlayRoulette(open, 13));
        Reject(open, () => CampaignArcade.PlayRoulette(open, 1, rev - 1));
        Reject(open, () => CampaignArcade.PlayPlinko(open with { Phase = CampaignPhase.Intro }, 4));
        Reject(open, () => CampaignArcade.PlayPlinko(open with { Money = 2 }, 4));
        Reject(open, () => CampaignArcade.PlayPlinko(open with { Match = 2 }, 4));
        var printed = CampaignShop.PrintTickets(open, open.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id, 1);
        Reject(printed, () => CampaignArcade.PlayPlinko(printed, 4));
        var loss = Outcome(ArcadeGame.Plinko, 1); var result = loss.Shop!.Arcade!.Pending!;
        Reject(loss, () => CampaignArcade.PlayPlinko(loss, 4));
        Reject(loss, () => CampaignArcade.PlayRoulette(loss, 7));
        Reject(loss, () => CampaignArcade.KeepArcadePrize(loss, result.PlayId));
        Reject(loss, () => CampaignArcade.BeginArcadePrizeUse(loss, result.PlayId));
        Reject(loss, () => CampaignShop.Reroll(loss));
        Reject(loss, () => loss.LeaveShop());
        Reject(loss, () => CampaignShop.Buy(loss, loss.Shop.Offers.First(o => o.Category == FeatureCategory.Joker).Id));
        Reject(loss, () => CampaignShop.PrintTickets(loss, loss.Shop.Offers.Single(o => o.Category == FeatureCategory.Tarot).Id, 1));
        var done = CampaignArcade.DismissArcadeLoss(RoundTrip(loss), result.PlayId, loss.Resources.Revision);
        Assert.Equal(loss.Money, done.Money); Assert.Equal(loss.Resources.RandomState, done.Resources.RandomState);
        Reject(done, () => CampaignArcade.PlayPlinko(done, 4));
        Reject(done, () => CampaignArcade.DismissArcadeLoss(done, result.PlayId));
        var rerolled = CampaignShop.Reroll(done);
        Assert.True(rerolled.Shop!.Arcade!.PlinkoPlayed);
        Reject(rerolled, () => CampaignArcade.PlayPlinko(rerolled, 4));
        Assert.True(CampaignArcade.PlayRoulette(rerolled, 7).Shop!.Arcade!.RoulettePlayed);
        var next = CampaignShop.Open(rerolled with { Match = 2 });
        Assert.Equal(new ArcadeState(), next.Shop!.Arcade!);
        if (CampaignArcade.Available(next))
            Assert.Equal(next.Money - 12, CampaignArcade.PlayPlinko(next, 4).Money);
        else Reject(next, () => CampaignArcade.PlayPlinko(next, 4));
    }

    public static void StrictPersistence()
    {
        var run = Legacy(Outcome(ArcadeGame.Plinko, 5));
        var path = Path.Combine(Path.GetTempPath(), $"bad-arcade-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run, path)); var original = File.ReadAllText(path);
            void Bad(Action<JsonNode> mutate)
            {
                var node = JsonNode.Parse(original)!; mutate(node); var text = node.ToJsonString();
                File.WriteAllText(path, text);
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
                Assert.Equal(text, File.ReadAllText(path));
            }
            JsonNode Arcade(JsonNode n) => n["Run"]!["Shop"]!["Arcade"]!;
            JsonNode Pending(JsonNode n) => Arcade(n)["Pending"]!;
            foreach (var name in new[] { "PlinkoPlayed", "RoulettePlayed", "Pending" }) Bad(n => Arcade(n).AsObject().Remove(name));
            foreach (var name in new[] { "PlayId", "Game", "Choice", "Landing", "Score", "Path", "PrizeCategory", "FeatureId", "TargetCards" })
                Bad(n => Pending(n).AsObject().Remove(name));
            Bad(n => Arcade(n)["PlinkoPlayed"] = false);
            Bad(n => Arcade(n)["RoulettePlayed"] = "false");
            Bad(n => Pending(n)["PlayId"] = "arcade:99:plinko");
            Bad(n => Pending(n)["Game"] = 22);
            Bad(n => Pending(n)["Choice"] = -1);
            Bad(n => Pending(n)["Landing"] = 11);
            Bad(n => Pending(n)["Score"] = 50);
            Bad(n => Pending(n)["Path"] = new JsonArray(8, 9, 10));
            Bad(n => Pending(n)["Path"]![1] = Pending(n)["Path"]![0]!.DeepClone());
            Bad(n => Pending(n)["PrizeCategory"] = (int)FeatureCategory.Tarot);
            Bad(n => Pending(n)["FeatureId"] = "Unknown");
            Bad(n => Pending(n)["FeatureId"] = null);
            Bad(n => Pending(n)["TargetCards"]![0] = new JsonObject());
            Bad(n => Pending(n)["TargetCards"]![0]!["Value"] = 999);
            Bad(n => Pending(n)["TargetCards"]![0] = Pending(n)["TargetCards"]![1]!.DeepClone());
            Bad(n => Pending(n)["TargetCards"]!.AsArray().RemoveAt(0));
            Bad(n => n["Run"]!["Phase"] = (int)CampaignPhase.Intro);
            Bad(n => n["Run"]!["Match"] = 2);
            Bad(n => n["Run"]!["Money"] = 28);
            Bad(n => n["Run"]!["LegacyMatchRestart"] = true);
            Bad(n => n["Run"]!["Resources"]!.AsObject().Remove("RandomState"));
            Bad(n => n["Version"] = 2);
            Bad(n => n["Run"]!["Shop"]!["Offers"]![3]!["Printed"] = new JsonArray("Prospector"));
            Bad(n => n["Run"]!["Shop"]!["UseSession"] = new JsonObject
            { ["OfferId"] = "other", ["Category"] = (int)FeatureCategory.Sigil, ["FeatureId"] = "Fish", ["PurchasedItemUsed"] = false });
            var bare = JsonSerializer.Serialize(run);
            File.WriteAllText(path, bare);
            Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);

            // A new-format prize offer cannot survive without its durable played flag.
            var usingPrize = CampaignArcade.BeginArcadePrizeUse(run, run.Shop!.Arcade!.Pending!.PlayId);
            Assert.True(CampaignStore.Save(usingPrize, path)); var begun = JsonNode.Parse(File.ReadAllText(path))!;
            begun["Run"]!["Shop"]!["Arcade"]!["PlinkoPlayed"] = false;
            File.WriteAllText(path, begun.ToJsonString());
            Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
        }
        finally { File.Delete(path); }
    }

    public static void SigilWinOpensPack()
    {
        var sigils = Enum.GetNames<SigilId>();
        foreach (var (game, score) in new[] { (ArcadeGame.Plinko, 6), (ArcadeGame.Plinko, 5), (ArcadeGame.Roulette, 100) })
        {
            var run = Outcome(game, score); var result = run.Shop!.Arcade!.Pending!;
            Assert.Equal(FeatureCategory.Sigil, result.PrizeCategory!.Value);
            Assert.True(result.FeatureId is null);
            Assert.Equal(CampaignArcade.PackSize, result.PackCards!.Length);
            Assert.Equal(3, result.PackCards.Distinct().Count());
            Assert.True(result.PackCards.All(k => sigils.Contains(k) || k == CampaignArcade.LegendaryKey));
            Assert.Equal(6, result.TargetCards.Length);
            Assert.True(CampaignArcade.IsPack(result));
            Assert.Equal(18, run.Money);
            Assert.True(result.AwaitingOpen);
            var loaded = RoundTrip(run);
            Reject(loaded, () => CampaignArcade.PickPackCard(loaded, result.PlayId, 0));
            Reject(loaded, () => CampaignArcade.TearSpectralPack(loaded, "wrong-play"));
            Reject(loaded, () => CampaignArcade.TearSpectralPack(loaded, result.PlayId, run.Resources.Revision - 1));
            var torn = RoundTrip(Tear(loaded));
            Assert.False(torn.Shop!.Arcade!.Pending!.AwaitingOpen);
            Assert.Equal(loaded.Money, torn.Money);
            Assert.Equal(loaded.Resources.RandomState, torn.Resources.RandomState);
            Assert.Equal(loaded.Resources.Revision + 1, torn.Resources.Revision);
            Assert.True(result.PackCards.SequenceEqual(torn.Shop.Arcade.Pending.PackCards!));
            Assert.True(result.TargetCards.SequenceEqual(torn.Shop.Arcade.Pending.TargetCards));
            Reject(torn, () => Tear(torn));
            Assert.True(CampaignArcade.PickPackCard(torn, result.PlayId, SigilIndex(result)).Shop!.UseSession is not null);
        }
        foreach (var score in new[] { 3, 4 })
        {
            var tarot = Outcome(ArcadeGame.Plinko, score).Shop!.Arcade!.Pending!;
            Assert.True(tarot.PackCards is null); Assert.False(CampaignArcade.IsPack(tarot));
        }
    }

    public static void LegendaryRateAndLimit()
    {
        var packs = Packs(4000).Select(r => r.Shop!.Arcade!.Pending!.PackCards!).ToArray();
        Assert.True(packs.Length >= 500, $"Only {packs.Length} packs sampled.");
        Assert.True(packs.All(p => p.Count(k => k == CampaignArcade.LegendaryKey) <= 1));
        var share = packs.Count(p => p.Contains(CampaignArcade.LegendaryKey)) / (double)packs.Length;
        Assert.True(share is > 0.03 and < 0.09, $"Legendary share {share:P1} is outside 3-9%.");
        JokerState[] five = Enum.GetValues<JokerId>().Take(5).Select(id => new JokerState(id)).ToArray();
        var full = Packs(1500, r => r with { Resources = r.Resources with { Inventory = r.Resources.Inventory with { Jokers = five } } }).ToArray();
        Assert.True(full.Length > 100);
        Assert.True(full.All(r => !r.Shop!.Arcade!.Pending!.PackCards!.Contains(CampaignArcade.LegendaryKey)));
    }

    public static void PackCannotBeKeptOrUsedDirectly()
    {
        var run = Outcome(ArcadeGame.Plinko, 6); var result = run.Shop!.Arcade!.Pending!;
        Reject(run, () => CampaignArcade.KeepArcadePrize(run, result.PlayId));
        Reject(run, () => CampaignArcade.BeginArcadePrizeUse(run, result.PlayId));
        Reject(run, () => CampaignArcade.DismissArcadeLoss(run, result.PlayId));
        Reject(run, () => run.LeaveShop());
        Reject(run, () => CampaignShop.Reroll(run));
    }

    public static void LegacySigilPrizeStillKeeps()
    {
        var legacy = Legacy(Outcome(ArcadeGame.Plinko, 6));
        Assert.False(CampaignArcade.IsPack(legacy.Shop!.Arcade!.Pending!));
        var loaded = RoundTrip(legacy);
        var kept = CampaignArcade.KeepArcadePrize(loaded, loaded.Shop!.Arcade!.Pending!.PlayId);
        Assert.True(kept.Resources.Inventory.Sigils.SequenceEqual([SigilId.Fish]));
    }

    public static void PackPersistence()
    {
        var run = Outcome(ArcadeGame.Plinko, 6);
        var path = Path.Combine(Path.GetTempPath(), $"bad-pack-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run, path)); var original = File.ReadAllText(path);
            void Bad(Action<JsonNode> mutate)
            {
                var node = JsonNode.Parse(original)!; mutate(node);
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
            JsonNode Pending(JsonNode n) => n["Run"]!["Shop"]!["Arcade"]!["Pending"]!;
            Bad(n => Pending(n).AsObject().Remove("PackCards"));
            Bad(n => Pending(n)["PackCards"]!.AsArray().RemoveAt(0));
            Bad(n => Pending(n)["PackCards"]![1] = Pending(n)["PackCards"]![0]!.DeepClone());
            Bad(n => Pending(n)["PackCards"]![0] = "Unknown");
            Bad(n => Pending(n)["PackCards"]![0] = 3);
            Bad(n => { Pending(n)["PackCards"]![0] = "Legendary"; Pending(n)["PackCards"]![1] = "Legendary"; });
            Bad(n => Pending(n)["FeatureId"] = "Fish");
            Bad(n => Pending(n)["PrizeCategory"] = (int)FeatureCategory.Tarot);
            var old = JsonNode.Parse(original)!;
            Pending(old).AsObject().Remove("AwaitingOpen");
            File.WriteAllText(path, old.ToJsonString());
            var loaded = CampaignStore.Load(path)!;
            var result = loaded.Shop!.Arcade!.Pending!;
            Assert.False(result.AwaitingOpen);
            Assert.True(CampaignArcade.PickPackCard(loaded, result.PlayId, SigilIndex(result)).Shop!.UseSession is not null);
        }
        finally { File.Delete(path); }
    }

    private static CampaignRun LegendaryPack(int jokers)
    {
        var owned = Enum.GetValues<JokerId>().Take(jokers).Select(id => new JokerState(id)).ToArray();
        return Packs(4000, r => r with { Resources = r.Resources with { Inventory = r.Resources.Inventory with { Jokers = owned } } })
            .First(r => r.Shop!.Arcade!.Pending!.PackCards!.Contains(CampaignArcade.LegendaryKey));
    }
    private static int SigilIndex(ArcadeResult result) => Array.FindIndex(result.PackCards!, k => k != CampaignArcade.LegendaryKey);

    public static void PickSigilForcesUse()
    {
        var run = Tear(Outcome(ArcadeGame.Plinko, 6)); var result = run.Shop!.Arcade!.Pending!;
        var index = SigilIndex(result); var sigil = result.PackCards![index];
        var picked = CampaignArcade.PickPackCard(run, result.PlayId, index, run.Resources.Revision);
        Assert.Equal(run.Resources.Revision + 1, picked.Resources.Revision);
        Assert.Equal(18, picked.Money);
        Assert.True(picked.Shop!.Arcade!.Pending is null);
        Assert.Equal(sigil, picked.Shop.UseSession!.FeatureId);
        Assert.Equal(FeatureCategory.Sigil, picked.Shop.UseSession.Category);
        Assert.True(CampaignShop.UseSessionCards(picked).Select(c => c.Id).SequenceEqual(result.TargetCards));
        Assert.True(picked.Shop.Offers.Single(o => o.Id == picked.Shop.UseSession.OfferId) is { IsArcadePrize: true, Sold: true, Price: 0 });
        Reject(picked, () => CampaignShop.FinishUse(picked));
        Reject(picked, () => picked.LeaveShop());
        var used = CampaignShop.UsePurchased(picked, [result.TargetCards[0]]);
        Assert.Equal(Enum.Parse<SigilId>(sigil), used.Resources.Deck.Single(c => c.Id == result.TargetCards[0]).Sigil!.Value);
        var finished = CampaignShop.FinishUse(used);
        Assert.Equal(0, finished.Resources.Inventory.Sigils.Length);
        Assert.Equal(CampaignPhase.Intro, RoundTrip(finished).LeaveShop().Phase);
    }

    public static void PickLegendaryFillsJokers()
    {
        foreach (var owned in new[] { 0, 2, 4 })
        {
            var run = Tear(LegendaryPack(owned)); var result = run.Shop!.Arcade!.Pending!;
            var before = run.Resources.Inventory.Jokers.Select(j => j.Id).ToArray();
            var picked = CampaignArcade.PickPackCard(RoundTrip(run), result.PlayId, Array.IndexOf(result.PackCards!, CampaignArcade.LegendaryKey));
            var after = picked.Resources.Inventory.Jokers.Select(j => j.Id).ToArray();
            Assert.Equal(CampaignArcade.JokerSlots, after.Length);
            Assert.Equal(after.Length, after.Distinct().Count());
            Assert.True(before.All(after.Contains));
            Assert.True(picked.Shop!.Arcade!.Pending is null);
            Assert.True(picked.Shop.UseSession is null);
            Assert.Equal(run.Money, picked.Money);
            Assert.True(picked.Shop.Offers.All(o => !o.IsArcadePrize));
            Assert.Equal(CampaignPhase.Intro, RoundTrip(picked).LeaveShop().Phase);
        }
    }

    public static void PickRejectsReplayAndStale()
    {
        var run = Tear(Outcome(ArcadeGame.Plinko, 6)); var result = run.Shop!.Arcade!.Pending!;
        Reject(run, () => CampaignArcade.PickPackCard(run, result.PlayId, -1));
        Reject(run, () => CampaignArcade.PickPackCard(run, result.PlayId, 3));
        Reject(run, () => CampaignArcade.PickPackCard(run, "other", 0));
        Reject(run, () => CampaignArcade.PickPackCard(run, result.PlayId, 0, run.Resources.Revision - 1));
        var picked = CampaignArcade.PickPackCard(run, result.PlayId, SigilIndex(result));
        Reject(picked, () => CampaignArcade.PickPackCard(picked, result.PlayId, SigilIndex(result)));
        var tarot = Outcome(ArcadeGame.Plinko, 3);
        Reject(tarot, () => CampaignArcade.PickPackCard(tarot, tarot.Shop!.Arcade!.Pending!.PlayId, 0));
        var legacy = Legacy(run);
        Reject(legacy, () => CampaignArcade.PickPackCard(legacy, result.PlayId, 0));
    }

    public static void PendingPackBlocksJokerChanges()
    {
        var run = LegendaryPack(2);
        Reject(run, () => CampaignShop.SellJoker(run, run.Resources.Inventory.Jokers[0].Id));
        var joker = run.Shop!.Offers.First(o => o.Category == FeatureCategory.Joker && !o.Sold);
        Reject(run with { Money = 30 }, () => CampaignShop.Buy(run with { Money = 30 }, joker.Id));
    }

    public static void PickedSigilSurvivesRestart()
    {
        var run = Tear(Outcome(ArcadeGame.Roulette, 100)); var result = run.Shop!.Arcade!.Pending!;
        var picked = CampaignArcade.PickPackCard(run, result.PlayId, SigilIndex(result));
        var loaded = RoundTrip(picked);
        Assert.Equal(picked.Shop!.UseSession, loaded.Shop!.UseSession);
        Assert.True(CampaignShop.UseSessionCards(loaded).Select(c => c.Id).SequenceEqual(result.TargetCards));
        Reject(loaded, () => CampaignShop.FinishUse(loaded));
    }

    public static void LegendaryWithFullSlotsIsNotADeadPick()
    {
        var run = Tear(LegendaryPack(2)); var result = run.Shop!.Arcade!.Pending!;
        var full = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
            { Jokers = Enum.GetValues<JokerId>().Take(5).Select(id => new JokerState(id)).ToArray() } } };
        Reject(full, () => CampaignArcade.PickPackCard(full, result.PlayId, Array.IndexOf(result.PackCards!, CampaignArcade.LegendaryKey)));
        Assert.True(CampaignArcade.PickPackCard(full, result.PlayId, SigilIndex(result)).Shop!.UseSession is not null);
    }
}
