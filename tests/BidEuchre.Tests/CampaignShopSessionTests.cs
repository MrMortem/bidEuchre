using System.Text.Json;
using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignShopSessionTests
{
    private static CardId[] Six() => [new(0), new(1), new(2), new(3), new(4), new(5)];
    private static string Snapshot(CampaignRun run) => JsonSerializer.Serialize(run);
    private static CampaignRun Stock(params TarotId[] held) => new(Money: 10, Phase: CampaignPhase.Shop)
    {
        Resources = CampaignSnapshot.Create(41) with
        {
            Wallets = [10, 0, 0, 0],
            Inventory = new([new(JokerId.HighRoller)], held, []) { Sigils = [SigilId.Bear] }
        },
        Shop = new(0, [
            new ShopOffer("sigil", FeatureCategory.Sigil, "Fish", 3) { TargetCards = Six() },
            new("ticket", FeatureCategory.Tarot, "", 3),
            new("joker", FeatureCategory.Joker, nameof(JokerId.Alpha), 3),
            new("gumball", FeatureCategory.Gumball, "", 3)])
    };

    private static CampaignRun Waiting(CampaignRun run, params TarotId[] tickets) => run with
    {
        Shop = run.Shop! with { Offers = run.Shop.Offers.Select(o => o.Id == "ticket"
            ? o with { Printed = tickets.Select(t => t.ToString()).ToArray(), TargetCards = Six() } : o).ToArray() }
    };

    private static void RejectedUnchanged(CampaignRun run, Action action)
    {
        var before = Snapshot(run);
        Assert.Throws<GameRuleException>(action);
        Assert.Equal(before, Snapshot(run));
    }

    // Deserialize the public save representation so this test detects an old reader that silently drops the obligation.
    public static void HeldTarotUsesCommittedCards()
    {
        var original = Stock(TarotId.Prospector);
        var node = JsonSerializer.SerializeToNode(original)!;
        node["Shop"]!["Offers"]![0]!["Sold"] = true;
        node["Shop"]!["UseSession"] = JsonNode.Parse("{\"OfferId\":\"sigil\",\"Category\":3,\"FeatureId\":\"Fish\",\"PurchasedItemUsed\":false}");
        node["Shop"]!["UseSession"]!["Category"] = (int)FeatureCategory.Sigil;
        var waiting = node.Deserialize<CampaignRun>()!;
        var used = CampaignShop.UseTarot(waiting, 0, [new(2)], waiting.Resources.Revision);
        Assert.Equal(EnhancementId.Golden, used.Resources.Deck[2].Enhancement!.Value);
        Assert.Equal(0, used.Resources.Inventory.Tarot.Length);
        Assert.True(used.Resources.Deck.Where(c => c.Id.Value != 2).SequenceEqual(waiting.Resources.Deck.Where(c => c.Id.Value != 2)));
        Assert.Throws<GameRuleException>(() => used.LeaveShop());
    }

    public static void MalformedSessionsAreNotIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shop-session-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(Stock(), path));
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            node["Run"]!["Shop"]!["UseSession"] = JsonNode.Parse("{\"OfferId\":\"sigil\",\"Category\":3,\"FeatureId\":\"Fish\"}");
            File.WriteAllText(path, node.ToJsonString());
            Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
        }
        finally { File.Delete(path); }
    }

    public static void SigilPaymentAndHeldTarotsSurviveBothOrders()
    {
        var initial = Stock(TarotId.Prospector, TarotId.Gambler, TarotId.TaxCollector);
        var original = Snapshot(initial);
        var begun = CampaignShop.BeginSigilUse(initial, "sigil", initial.Resources.Revision);
        Assert.Equal(7, begun.Money);
        Assert.Equal(7, begun.Resources.Wallets[0]);
        Assert.Equal(initial.Resources.Revision + 1, begun.Resources.Revision);
        Assert.Equal(initial.Resources.RandomState, begun.Resources.RandomState);
        Assert.Equal(4, begun.Resources.Inventory.ItemCount);
        Assert.True(begun.Shop!.Offers.Single(o => o.Id == "sigil").Sold);
        Assert.True(!begun.Shop.UseSession!.PurchasedItemUsed);
        Assert.True(begun.Resources.Deck.SequenceEqual(initial.Resources.Deck));
        Assert.True(CampaignShop.UseSessionCards(begun).Select(c => c.Id).SequenceEqual(Six()));
        Assert.True(ReferenceEquals(begun, CampaignShop.Open(begun)));
        RejectedUnchanged(begun, () => CampaignShop.FinishUse(begun));

        var enhanced = CampaignShop.UseHeldTarot(begun, 0, [new(1)], begun.Resources.Revision);
        Assert.Equal(EnhancementId.Golden, enhanced.Resources.Deck[1].Enhancement!.Value);
        Assert.Equal(2, enhanced.Resources.Inventory.Tarot.Length);
        Assert.Equal(begun.Resources.Revision + 1, enhanced.Resources.Revision);
        Assert.False(enhanced.Shop!.UseSession!.PurchasedItemUsed);
        var purchased = CampaignShop.UsePurchased(enhanced, [new(1)], enhanced.Resources.Revision);
        Assert.Equal(SigilId.Fish, purchased.Resources.Deck[1].Sigil!.Value);
        Assert.Equal(EnhancementId.Golden, purchased.Resources.Deck[1].Enhancement!.Value);
        Assert.Equal(enhanced.Resources.Revision + 1, purchased.Resources.Revision);
        Assert.True(purchased.Shop!.UseSession!.PurchasedItemUsed);
        Assert.Equal(7, purchased.Money);
        RejectedUnchanged(purchased, () => CampaignShop.UsePurchased(purchased, [new(0)]));

        var gambled = CampaignShop.UseHeldTarot(purchased, 0, [new(4), new(2)], purchased.Resources.Revision);
        Assert.True(gambled.Resources.Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id.Value).ToHashSet().SetEquals([2, 4]));
        Assert.Equal(purchased.Resources.Revision + 1, gambled.Resources.Revision);
        // The held Tax Collector sees the seven dollars left after payment, not the old ten-dollar wallet.
        var taxed = CampaignShop.UseTarot(gambled, 0, [], gambled.Resources.Revision);
        Assert.Equal(14, taxed.Money);
        Assert.Equal(14, taxed.Resources.Wallets[0]);
        Assert.Equal(0, taxed.Resources.Inventory.Tarot.Length);
        Assert.Equal(gambled.Resources.Revision + 1, taxed.Resources.Revision);
        Assert.Equal(initial.Resources.RandomState, taxed.Resources.RandomState);
        var finished = CampaignShop.FinishUse(taxed, taxed.Resources.Revision);
        Assert.True(finished.Shop!.UseSession is null);
        Assert.Equal(taxed.Resources.Revision + 1, finished.Resources.Revision);
        Assert.Equal(14, finished.Money);
        Assert.Equal(0, CampaignShop.UseSessionCards(finished).Length);
        Assert.Equal(CampaignPhase.Intro, finished.LeaveShop().Phase);
        RejectedUnchanged(finished, () => CampaignShop.UseHeldTarot(finished, 0, []));
        Assert.Equal(original, Snapshot(initial));
    }

    public static void TicketChoiceCommitsOnceWithoutAnotherCharge()
    {
        var initial = Stock(TarotId.Prospector);
        var printed = CampaignShop.PrintTickets(initial, "ticket", 3, initial.Resources.Revision);
        var ticket = Enum.Parse<TarotId>(printed.Shop!.Offers.Single(o => o.Id == "ticket").Printed![0]);
        var begun = CampaignShop.BeginTicketUse(printed, "ticket", ticket, printed.Resources.Revision);
        Assert.Equal(4, begun.Money);
        Assert.Equal(4, begun.Resources.Wallets[0]);
        Assert.Equal(printed.Resources.Revision + 1, begun.Resources.Revision);
        Assert.Equal(printed.Resources.RandomState, begun.Resources.RandomState);
        Assert.True(begun.Shop!.Offers.Single(o => o.Id == "ticket") is { Sold: true, Printed: null });
        Assert.Equal(ticket.ToString(), begun.Shop.UseSession!.FeatureId);
        Assert.True(CampaignShop.UseSessionCards(begun).Select(c => c.Id).SequenceEqual(printed.Shop.Offers.Single(o => o.Id == "ticket").TargetCards!));
        RejectedUnchanged(begun, () => CampaignShop.BeginTicketUse(begun, "ticket", ticket));
        RejectedUnchanged(begun, () => CampaignShop.KeepTicket(begun, "ticket", ticket));

        var gambler = CampaignShop.BeginTicketUse(Waiting(Stock(), TarotId.Gambler), "ticket", TarotId.Gambler);
        foreach (var invalid in new CardId[][] { [], [new(1)], [new(1), new(2), new(3)], [new(1), new(1)], [new(1), new(6)] })
            RejectedUnchanged(gambler, () => CampaignShop.UsePurchased(gambler, invalid));
        var used = CampaignShop.UsePurchased(gambler, [new(5), new(0)], gambler.Resources.Revision);
        Assert.True(used.Resources.Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id.Value).ToHashSet().SetEquals([0, 5]));
        Assert.Equal(10, used.Money);
        RejectedUnchanged(used, () => CampaignShop.UsePurchased(used, [new(5), new(0)]));
        Assert.Equal(CampaignPhase.Intro, CampaignShop.FinishUse(used).LeaveShop().Phase);
    }

    public static void EveryHeldTarotUsesTheCurrentSixCardOpportunity()
    {
        foreach (var tarot in Enum.GetValues<TarotId>())
        {
            var initial = Stock(tarot);
            initial = initial with { Resources = initial.Resources with { Inventory = initial.Resources.Inventory with { Unlocks = Enum.GetValues<GumballId>() } } };
            var begun = CampaignShop.BeginSigilUse(initial, "sigil");
            CardId[] targets = tarot switch
            {
                TarotId.Gambler => [new(3), new(1)], TarotId.DevilishDeal => [new(4), new(2), new(0)],
                TarotId.TaxCollector or TarotId.Astronomer => [], _ => [new(5)]
            };
            var used = CampaignShop.UseHeldTarot(begun, 0, targets);
            Assert.Equal(0, used.Resources.Inventory.Tarot.Length);
            Assert.True(used.Resources.Inventory.Sigils.SequenceEqual([SigilId.Bear]));
            Assert.True(used.Resources.Inventory.Jokers.SequenceEqual(initial.Resources.Inventory.Jokers));
            Assert.True(used.Resources.Deck.Where(c => !targets.Contains(c.Id)).SequenceEqual(initial.Resources.Deck.Where(c => !targets.Contains(c.Id))));
            Assert.False(used.Shop!.UseSession!.PurchasedItemUsed);
            switch (tarot)
            {
                case TarotId.Astronomer:
                    Assert.Equal(1, used.Resources.Inventory.GumballExtraCopies.Values.Sum());
                    Assert.Equal(Enum.GetValues<GumballId>().Length, used.Resources.Inventory.Unlocks.Length);
                    Assert.True(used.Resources.RandomState != begun.Resources.RandomState);
                    break;
                case TarotId.TaxCollector: Assert.Equal(14, used.Money); break;
                case TarotId.DevilishDeal:
                    Assert.Equal(EnhancementId.Cursed, used.Resources.Deck[0].Enhancement!.Value);
                    Assert.True(used.Resources.Deck[4].Enhancement is not null && used.Resources.Deck[2].Enhancement is not null);
                    Assert.Equal(Snapshot(used), Snapshot(CampaignShop.UseHeldTarot(begun, 0, targets)));
                    break;
                case TarotId.Gambler:
                    Assert.True(used.Resources.Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id.Value).ToHashSet().SetEquals([1, 3]));
                    break;
                default: Assert.True(used.Resources.Deck[5].Enhancement is not null); break;
            }
        }
    }

    public static void SessionRejectsEscapesAndStaleOrInvalidChoices()
    {
        var initial = Waiting(Stock(TarotId.Gambler), TarotId.Prospector);
        RejectedUnchanged(initial, () => CampaignShop.BeginSigilUse(initial, "joker"));
        RejectedUnchanged(initial, () => CampaignShop.BeginSigilUse(initial, "missing"));
        RejectedUnchanged(initial, () => CampaignShop.BeginSigilUse(initial, "sigil", initial.Resources.Revision - 1));
        RejectedUnchanged(initial, () => CampaignShop.BeginTicketUse(initial, "ticket", TarotId.Gambler));
        RejectedUnchanged(initial, () => CampaignShop.BeginTicketUse(initial, "ticket", TarotId.Prospector, initial.Resources.Revision - 1));
        var poor = initial with { Money = 2 };
        RejectedUnchanged(poor, () => CampaignShop.BeginSigilUse(poor, "sigil"));
        var begun = CampaignShop.BeginSigilUse(initial, "sigil");
        foreach (var state in new[] { begun, CampaignShop.UsePurchased(begun, [new(0)]) })
        {
            RejectedUnchanged(state, () => state.LeaveShop());
            RejectedUnchanged(state, () => CampaignShop.Reroll(state));
            RejectedUnchanged(state, () => CampaignShop.Buy(state, "joker"));
            RejectedUnchanged(state, () => CampaignShop.Buy(state, "sigil", new(0)));
            RejectedUnchanged(state, () => CampaignShop.BuyAndUse(state, "sigil", [new(0)]));
            RejectedUnchanged(state, () => CampaignShop.BeginSigilUse(state, "sigil"));
            RejectedUnchanged(state, () => CampaignShop.BeginTicketUse(state, "ticket", TarotId.Prospector));
            RejectedUnchanged(state, () => CampaignShop.PrintTickets(state, "ticket", 1));
            RejectedUnchanged(state, () => CampaignShop.KeepTicket(state, "ticket", TarotId.Prospector));
            RejectedUnchanged(state, () => CampaignShop.UseTicket(state, "ticket", TarotId.Prospector, [new(0)]));
            RejectedUnchanged(state, () => CampaignShop.SellJoker(state, JokerId.HighRoller));
            RejectedUnchanged(state, () => CampaignShop.DiscardTarot(state, 0));
            RejectedUnchanged(state, () => CampaignShop.DiscardSigil(state, 0));
            RejectedUnchanged(state, () => CampaignShop.UseSigil(state, 0, new(0)));
            RejectedUnchanged(state, () => CampaignShop.UseHeldTarot(state, 0, [new(1), new(2)], state.Resources.Revision - 1));
            RejectedUnchanged(state, () => CampaignShop.UsePurchased(state, [new(0)], state.Resources.Revision - 1));
            RejectedUnchanged(state, () => CampaignShop.FinishUse(state, state.Resources.Revision - 1));
        }
        foreach (var targets in new CardId[][] { [], [new(0), new(1)], [new(6)], [new(99)] })
            RejectedUnchanged(begun, () => CampaignShop.UsePurchased(begun, targets));
        foreach (var targets in new CardId[][] { [], [new(0)], [new(0), new(0)], [new(0), new(6)], [new(0), new(99)] })
            RejectedUnchanged(begun, () => CampaignShop.UseHeldTarot(begun, 0, targets));
        RejectedUnchanged(begun, () => CampaignShop.UseHeldTarot(begun, -1, [new(0), new(1)]));
        RejectedUnchanged(begun, () => CampaignShop.UseHeldTarot(begun, 1, [new(0), new(1)]));
        RejectedUnchanged(initial, () => CampaignShop.UseHeldTarot(initial, 0, [new(0), new(1)]));
        RejectedUnchanged(initial, () => CampaignShop.UsePurchased(initial, [new(0)]));
        RejectedUnchanged(initial, () => CampaignShop.FinishUse(initial));
    }

    public static void SaveReloadKeepsPaymentEffectsAndFinishObligation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shop-session-resume-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var ticket in new[] { false, true })
            {
                var initial = Stock(TarotId.Prospector, TarotId.Gambler);
                var begun = ticket ? CampaignShop.BeginTicketUse(Waiting(initial, TarotId.TaxCollector), "ticket", TarotId.TaxCollector)
                    : CampaignShop.BeginSigilUse(initial, "sigil");
                var enhanced = CampaignShop.UseHeldTarot(begun, 0, [new(3)]);
                var purchased = CampaignShop.UsePurchased(enhanced, ticket ? [] : [new(3)]);
                foreach (var state in new[] { begun, enhanced, purchased })
                {
                    Assert.True(CampaignStore.Save(state, path));
                    var loaded = CampaignStore.LoadSave(path).Save!.Run;
                    Assert.Equal(Snapshot(state), Snapshot(loaded));
                    Assert.True(ReferenceEquals(loaded, CampaignShop.Open(loaded)));
                    Assert.True(CampaignShop.UseSessionCards(loaded).Select(c => c.Id).SequenceEqual(Six()));
                    RejectedUnchanged(loaded, () => loaded.LeaveShop());
                    if (!loaded.Shop!.UseSession!.PurchasedItemUsed) RejectedUnchanged(loaded, () => CampaignShop.FinishUse(loaded));
                    else
                    {
                        RejectedUnchanged(loaded, () => CampaignShop.UsePurchased(loaded, ticket ? [] : [new(3)]));
                        var finish = CampaignShop.FinishUse(loaded);
                        Assert.True(CampaignStore.Save(finish, path));
                        Assert.True(CampaignStore.Load(path)!.Shop!.UseSession is null);
                    }
                }
            }
        }
        finally { File.Delete(path); }
    }

    public static void SaveValidationRejectsIncompleteAndImpossibleCommitments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shop-session-invalid-{Guid.NewGuid():N}.json");
        try
        {
            var begun = CampaignShop.BeginSigilUse(Stock(TarotId.Prospector), "sigil");
            Assert.True(CampaignStore.Save(begun, path));
            var original = File.ReadAllText(path);
            void Reject(Action<JsonNode> change)
            {
                var node = JsonNode.Parse(original)!;
                change(node);
                File.WriteAllText(path, node.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);
            }
            foreach (var field in new[] { "OfferId", "Category", "FeatureId", "PurchasedItemUsed" })
            {
                Reject(n => n["Run"]!["Shop"]!["UseSession"]!.AsObject().Remove(field));
                Reject(n => n["Run"]!["Shop"]!["UseSession"]![field] = null);
            }
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["PurchasedItemUsed"] = "false");
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["PurchasedItemUsed"] = 0);
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["OfferId"] = "missing");
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["OfferId"] = 3);
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["FeatureId"] = "Bear");
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["FeatureId"] = "Coin");
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["Category"] = (int)FeatureCategory.Tarot);
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["Category"] = "Sigil");
            Reject(n => n["Run"]!["Shop"]!["UseSession"]!["Category"] = 1.5);
            Reject(n => n["Run"]!["Shop"]!["UseSession"] = new JsonArray());
            Reject(n => n["Run"]!["Shop"]!["Offers"]![0]!["Sold"] = false);
            Reject(n => n["Run"]!["Shop"]!["Offers"]![0]!["TargetCards"] = null);
            Reject(n => n["Run"]!["Shop"]!["Offers"]![0]!["TargetCards"]![0] = new JsonObject());
            Reject(n => n["Run"]!["Shop"]!["Offers"]![0]!["TargetCards"]![0]!["Value"] = 5);
            Reject(n => n["Run"]!["Shop"]!["Offers"]![0]!["TargetCards"]![0]!["Value"] = 99);
            Reject(n => n["Run"]!["Shop"]!["MatchJustFinished"] = 1);
            Reject(n => n["Run"]!["Phase"] = (int)CampaignPhase.Intro);
            Reject(n => n["Run"]!["Phase"] = (int)CampaignPhase.InMatch);
            Reject(n => n["Run"]!["Phase"] = (int)CampaignPhase.Victory);
            Reject(n => n["Run"]!["Lives"] = 0);
            Reject(n => n["Run"]!["LegacyMatchRestart"] = true);
            Reject(n => n["Run"]!["Money"] = 10);
            Reject(n => n["Run"]!["Resources"]!.AsObject().Remove("Inventory"));
            Reject(n => n["Run"]!["Resources"]!["Inventory"]!.AsObject().Remove("Tarot"));
            Reject(n => n["Version"] = 2);
            // A bare legacy save rebuilds resources, so it cannot carry a new-format paid session.
            File.WriteAllText(path, JsonSerializer.Serialize(begun));
            Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status);

            var impossible = begun with { Phase = CampaignPhase.Intro };
            Assert.False(CampaignStore.Save(impossible, path));
            var withResult = new CampaignSave(3, begun)
            {
                Metadata = new("test", 1, new(new(false, false, 0, 0, 0, begun.Money, false), []))
            };
            Assert.False(CampaignStore.Save(withResult, path));
            // Legacy saves with no commitment, and old sold purchases, never acquire a new obligation.
            var legacy = CampaignShop.Buy(Stock(), "sigil");
            Assert.True(CampaignStore.Save(legacy, path));
            var old = JsonNode.Parse(File.ReadAllText(path))!;
            old["Run"]!["Shop"]!.AsObject().Remove("UseSession");
            foreach (var version in new[] { 2, 3 })
            {
                old["Version"] = version;
                File.WriteAllText(path, old.ToJsonString());
                var loaded = CampaignStore.LoadSave(path);
                Assert.Equal(CampaignLoadStatus.Loaded, loaded.Status);
                Assert.True(loaded.Save!.Run.Shop!.UseSession is null);
                Assert.Equal(CampaignPhase.Intro, loaded.Save.Run.LeaveShop().Phase);
            }
        }
        finally { File.Delete(path); }
    }
}
