using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignSodaTests
{
    private static CampaignRun Visit(params SodaId[] sodas) => CampaignShop.Open(new CampaignRun(Money: 100, Phase: CampaignPhase.Shop)
    {
        Resources = CampaignSnapshot.Create(42) with { Wallets = [100, 0, 0, 0], Inventory = CampaignInventory.Empty with { Sodas = sodas } }
    });

    public static void Vending()
    {
        var run = Visit();
        Assert.Equal(7, CampaignShop.EligibleSodas(run.Resources.Inventory).Length);
        Assert.Throws<GameRuleException>(() => CampaignShop.BuySoda(run with { Money = 7 }));
        Assert.Throws<GameRuleException>(() => CampaignShop.BuySoda(run, run.Resources.Revision - 1));
        var owned = new HashSet<SodaId>();
        while (owned.Count < Enum.GetValues<SodaId>().Length)
        {
            var bought = CampaignShop.BuySoda(run, run.Resources.Revision);
            var soda = bought.Resources.Inventory.Sodas[^1];
            Assert.True(SodaRules.Prerequisite(soda) is not { } prerequisite || owned.Contains(prerequisite));
            Assert.True(owned.Add(soda));
            Assert.Equal(run.Money - CampaignShop.SodaPrice(run), bought.Money);
            Assert.Equal(bought.Money, bought.Resources.Wallets[0]);
            Assert.True(bought.Shop!.SodaPurchased);
            Assert.Throws<GameRuleException>(() => CampaignShop.BuySoda(CampaignShop.Reroll(bought)));
            Assert.True(CampaignShop.Open(bought).Shop!.SodaPurchased);
            run = Visit(bought.Resources.Inventory.Sodas);
        }
        Assert.Throws<GameRuleException>(() => CampaignShop.BuySoda(run));
    }

    public static void CapacityAndPrices()
    {
        var run = Visit(SodaId.OvertimeOrange, SodaId.LimitlessLime, SodaId.GalaxAde, SodaId.CosmicCola,
            SodaId.RerollRaspberry, SodaId.CitrusSale);
        Assert.Equal(8, Visit().NextMatch().HandLimit!.Value);
        Assert.Equal(9, Visit(SodaId.OvertimeOrange).NextMatch().HandLimit!.Value);
        Assert.Equal(10, run.NextMatch().HandLimit!.Value);
        var offer = run.Shop!.Offers.First(o => o.Category == FeatureCategory.Joker);
        var id = Enum.Parse<JokerId>(offer.FeatureId);
        var inventory = run.Resources.Inventory with
        {
            Jokers = Enum.GetValues<JokerId>().Where(j => j != id).Take(5).Select(j => new JokerState(j)).ToArray(),
            Tarot = [TarotId.Gambler, TarotId.Gambler, TarotId.Gambler, TarotId.Gambler]
        };
        Assert.True(inventory.HasItemRoom && inventory.ItemCapacity == 5 && inventory.JokerCapacity == 6);
        var bought = CampaignShop.Buy(run with { Resources = run.Resources with { Inventory = inventory } }, offer.Id);
        Assert.Equal(6, bought.Resources.Inventory.Jokers.Length);
        var teller = bought.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
        var printed = CampaignShop.PrintTickets(bought, teller.Id, 1);
        var ticket = Enum.Parse<TarotId>(printed.Shop!.Offers.Single(o => o.Id == teller.Id).Printed!.Single());
        var full = CampaignShop.KeepTicket(printed, teller.Id, ticket);
        Assert.Equal(5, full.Resources.Inventory.ItemCount);
        Assert.True(!full.Resources.Inventory.HasItemRoom);
        var discarded = CampaignShop.DiscardTarot(full, 4);
        Assert.Equal(4, discarded.Resources.Inventory.ItemCount);
        Assert.True(bought.Resources.IsValid());
        Assert.Equal(100 - offer.Price + 1, bought.Money);
        Assert.Equal(7, CampaignShop.SodaPrice(run));
        var rolled = CampaignShop.Reroll(run);
        Assert.Equal(run.Money, rolled.Money);
        Assert.Equal(2, CampaignShop.RerollPrice(rolled));
        Assert.Equal(98, CampaignShop.Reroll(rolled).Money);
    }

    public static void PaidPullsAndSaves()
    {
        var run = Visit(SodaId.DoubleBubble, SodaId.RainbowStripes, SodaId.RerollRaspberry, SodaId.CitrusSale);
        var machine = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
        var first = CampaignShop.Buy(run, machine.Id);
        Assert.Equal(98, first.Money);
        Assert.Equal(2, first.Resources.Inventory.Unlocks.Length);
        var rerolled = CampaignShop.Reroll(first);
        var second = CampaignShop.Buy(rerolled, machine.Id);
        Assert.Equal(95, second.Money);
        Assert.Equal(3, second.Resources.Inventory.Unlocks.Length);
        Assert.Throws<GameRuleException>(() => CampaignShop.Buy(second, machine.Id, expectedRevision: rerolled.Resources.Revision));
        var path = Path.Combine(Path.GetTempPath(), $"campaign-sodas-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(second, path));
            var loaded = CampaignStore.Load(path)!;
            Assert.True(loaded.Resources.Inventory.Sodas.SequenceEqual(run.Resources.Inventory.Sodas));
            Assert.Equal(2, loaded.Shop!.GumballPulls);
            Assert.Equal(91, CampaignShop.Buy(loaded, machine.Id).Money);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var shop = json["Run"]!["Shop"]!.AsObject();
            shop.Remove("GumballPulls");
            File.WriteAllText(path, json.ToJsonString());
            Assert.True(CampaignStore.Load(path) is null, "An owned upgrade cannot lose its current shop pull count.");
            shop["GumballPulls"] = 2;
            var sodas = json["Run"]!["Resources"]!["Inventory"]!["Sodas"]!.AsArray();
            sodas.RemoveAt(0);
            File.WriteAllText(path, json.ToJsonString());
            Assert.True(CampaignStore.Load(path) is null, "An upgrade without its prerequisite cannot be loaded.");
        }
        finally { File.Delete(path); }
    }
}
