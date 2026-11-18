using BidEuchre.Core;

namespace BidEuchre.App;

public static partial class CampaignShop
{
    public const int BoosterPrice = 6;
    public static int BoosterChoices(CampaignInventory inventory) => inventory.Has(SodaId.PeachPlus) ? 5 : 4;

    internal static ShopState WithBonusBooster(ShopState shop, CampaignInventory inventory, CampaignRandom random) =>
        inventory.Has(SodaId.PackAPunch) && shop.BonusBooster is null ? shop with { BonusBooster = RollBooster(random) } : shop;

    private static ShopState ReplaceBooster(ShopState shop, JokerBooster current, JokerBooster updated) =>
        ReferenceEquals(shop.Booster, current) ? shop with { Booster = updated } : shop with { BonusBooster = updated };
    private static readonly string[] BoosterThemes = ["arcade", "comic", "horror", "retro-synth", "tech"];

    private static JokerBooster RollBooster(CampaignRandom random) =>
        new(BoosterThemes[random.NextInt(BoosterThemes.Length)]);

    private static void RequireNoBoosterChoice(CampaignRun run)
    {
        if (run.Shop?.PendingBooster is { } pack)
            throw new GameRuleException(pack.AwaitingOpen ? "Tear open your Joker booster first." : "Choose a Joker from your booster or skip it first.");
    }

    public static CampaignRun OpenBooster(CampaignRun run, long? expectedRevision = null, int packIndex = 0)
    {
        RequireCurrentShop(run, expectedRevision);
        RequireNoPrintedTickets(run);
        var stock = packIndex switch { 0 => run.Shop!.Booster, 1 => run.Shop!.BonusBooster, _ => null };
        if (run.IsOver || stock is not { Sold: false } booster)
            throw new GameRuleException("There is no available Joker booster.");
        var price = Discount(run, BoosterPrice);
        if (run.Money < price) throw new GameRuleException("Not enough money.");
        var inventory = run.Resources.Inventory;

        var random = new CampaignRandom(run.Resources.RandomState);
        var eligible = FeatureCatalog.All.Where(f => f.Category == FeatureCategory.Joker)
            .Select(f => Enum.Parse<JokerId>(f.Key)).Where(id => !inventory.Has(id)).ToList();
        random.Shuffle(eligible);
        var choices = eligible.Take(BoosterChoices(inventory)).ToArray();
        if (choices.Length != BoosterChoices(inventory)) throw new GameRuleException("Too few Jokers available for a booster.");
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money - price;
        return run with
        {
            Money = resources.Wallets[0],
            Resources = resources with { RandomState = random.State, Revision = resources.Revision + 1 },
            Shop = ReplaceBooster(run.Shop!, booster, booster with { Sold = true, Choices = choices, AwaitingOpen = true })
        };
    }

    public static CampaignRun TearBooster(CampaignRun run, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision, allowBoosterChoice: true);
        if (run.Shop!.PendingBooster is not { Sold: true, Choices: not null, AwaitingOpen: true } booster)
            throw new GameRuleException("There is no sealed Joker booster.");
        return run with
        {
            Shop = ReplaceBooster(run.Shop, booster, booster with { AwaitingOpen = false }),
            Resources = run.Resources with { Revision = run.Resources.Revision + 1 }
        };
    }

    public static CampaignRun ResolveBooster(CampaignRun run, JokerId? pick, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision, allowBoosterChoice: true);
        if (run.Shop!.PendingBooster is not { Sold: true, Choices: { } choices, AwaitingOpen: false } booster)
            throw new GameRuleException("There is no opened Joker booster.");
        var inventory = run.Resources.Inventory;
        if (pick is { } joker)
        {
            if (!choices.Contains(joker)) throw new GameRuleException("Choose one of the offered Jokers.");
            if (inventory.Jokers.Length >= inventory.JokerCapacity)
                throw new GameRuleException("Sell an owned Joker to keep this one, or skip the pack.");
            if (inventory.Has(joker)) throw new GameRuleException("You already own that Joker.");
            inventory = inventory with { Jokers = [.. inventory.Jokers, new(joker)] };
        }
        return run with
        {
            Shop = ReplaceBooster(run.Shop, booster, booster with { Choices = null }),
            Resources = run.Resources with { Inventory = inventory, Revision = run.Resources.Revision + 1 }
        };
    }

    internal static bool IsValidBooster(ShopState shop, CampaignInventory inventory)
    {
        if (shop.BonusBooster is not null && (shop.Booster is null || !inventory.Has(SodaId.PackAPunch))) return false;
        if (shop.Booster?.Choices is not null && shop.BonusBooster?.Choices is not null) return false;
        return Valid(shop.Booster) && Valid(shop.BonusBooster);

        bool Valid(JokerBooster? booster)
        {
            if (booster is null) return true;
            if (!BoosterThemes.Contains(booster.Theme)) return false;
            if (booster.Choices is not { } choices) return !booster.AwaitingOpen;
            return booster.Sold && choices.Length == BoosterChoices(inventory) && choices.Distinct().Count() == choices.Length
                && choices.All(id => Enum.IsDefined(id) && !inventory.Has(id))
                && shop.UseSession is null && shop.Arcade?.Pending is null && shop.Offers.All(o => o.Printed is null);
        }
    }
}
