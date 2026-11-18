using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

/// <summary>Names only the artwork a pending screen can actually use.</summary>
internal static class ScreenArtwork
{
    public static IEnumerable<string> Collection(FeatureCategory? category)
    {
        if (category is null or FeatureCategory.Enhancement) yield return "card-faces";
        if (category is null) yield return PixelArt.CardBackgroundName(null);
        foreach (var item in FeatureCatalog.All.Where(item => item.Category == category))
        {
            if (category == FeatureCategory.Enhancement)
                yield return PixelArt.CardBackgroundName(Enum.Parse<EnhancementId>(item.Key));
            else if (category == FeatureCategory.Gumball)
            {
                var sprite = PixelArt.CampaignImageName(item);
                if (PixelArt.HasImage(sprite)) yield return sprite;
            }
            else yield return PixelArt.CampaignIconName(item);
        }
    }

    public static IEnumerable<string> Shop(CampaignRun run)
    {
        if (run.Shop is not { } shop) yield break;
        if (shop.UseSession is { } use)
        {
            yield return PixelArt.CampaignIconName(FeatureCatalog.Get(use.Category, use.FeatureId));
            foreach (var tarot in run.Resources.Inventory.Tarot)
                yield return PixelArt.CampaignIconName(FeatureCatalog.Get(FeatureCategory.Tarot, tarot.ToString()));
            yield return "card-faces";
            foreach (var name in Cards(CampaignShop.UseSessionCards(run).Select(card => (card.Enhancement, card.Sigil))))
                yield return name;
            yield break;
        }

        if (shop.Arcade?.Pending is { } pending)
        {
            if (pending.Game == ArcadeGame.Plinko)
                foreach (var name in Plinko()) yield return name;
            foreach (var name in Inventory(run.Resources.Inventory)) yield return name;
            if (CampaignArcade.IsPack(pending))
            {
                yield return PixelArt.CollectorImageName("Pack");
                foreach (var key in pending.PackCards!) yield return PixelArt.CollectorImageName(key);
            }
            else if (pending.PrizeCategory is { } category)
                yield return PixelArt.CampaignIconName(FeatureCatalog.Get(category, pending.FeatureId!));
            yield break;
        }

        foreach (var booster in new[] { shop.Booster, shop.BonusBooster }.OfType<JokerBooster>())
        {
            yield return "card-pack-" + booster.Theme;
            if (booster.Choices is { } choices)
            {
                foreach (var id in choices) yield return PixelArt.CampaignIconName(FeatureCatalog.Get(FeatureCategory.Joker, id.ToString()));
                yield break;
            }
        }
        foreach (var name in Inventory(run.Resources.Inventory)) yield return name;
        yield return "machine-soda";
        if (shop.SodaPurchased) yield return "open-soda-lid";
        foreach (var soda in CampaignShop.EligibleSodas(run.Resources.Inventory).Take(9)) yield return "soda-" + SodaCatalog.Get(soda).Art;
        foreach (var offer in shop.Offers.Where(offer => !offer.IsArcadePrize))
        {
            if (offer.Category == FeatureCategory.Joker)
                yield return PixelArt.CampaignIconName(FeatureCatalog.Get(offer.Category, offer.FeatureId));
            if (offer.Category == FeatureCategory.Tarot || offer.IsTellerSigil)
                yield return "machine-fortune-teller-ticket-dispenser";
            else if (offer.Category == FeatureCategory.Sigil)
                yield return PixelArt.CollectorImageName(offer.FeatureId);
        }

        if (shop.Offers.Any(offer => offer.Category == FeatureCategory.Gumball))
        {
            yield return "machine-gumball";
            var remaining = Enum.GetValues<GumballId>().Except(run.Resources.Inventory.Unlocks).ToArray();
            foreach (var id in remaining.Length > 0 ? remaining : Enum.GetValues<GumballId>())
            {
                var sprite = PixelArt.CampaignImageName(FeatureCatalog.Get(FeatureCategory.Gumball, id.ToString()));
                if (PixelArt.HasImage(sprite)) yield return sprite;
            }
        }

        if (CampaignArcade.Available(run))
            foreach (var name in Plinko()) yield return name;
    }

    private static IEnumerable<string> Plinko()
    {
        foreach (var name in new[] { "arcade-plinko-board", "arcade-plinko-dropper" })
            if (PixelArt.HasImage(name)) yield return name;
    }

    public static IEnumerable<string> Match(CampaignRun run)
    {
        if (PixelArt.BossArenaName(run) is { } arena) yield return arena;
        yield return "portraits";
        yield return "back";
        yield return "card-faces";
        foreach (var name in Inventory(run.Resources.Inventory)) yield return name;
        var art = run.Resources.Deck.Select(card => (card.Enhancement, card.Sigil));
        if (run.LiveMatch is { } match)
            art = art.Concat(match.Overlays.Select(card => (card.Enhancement, card.Sigil)));
        foreach (var name in Cards(art)) yield return name;
    }

    private static IEnumerable<string> Cards(IEnumerable<(EnhancementId? Enhancement, SigilId? Sigil)> art)
    {
        foreach (var (enhancement, sigil) in art.Distinct())
        {
            yield return PixelArt.CardBackgroundName(enhancement);
            if (sigil is { } seal)
                yield return PixelArt.CampaignImageName(FeatureCatalog.Get(FeatureCategory.Sigil, seal.ToString()));
        }
    }

    private static IEnumerable<string> Inventory(CampaignInventory inventory)
    {
        foreach (var soda in inventory.Sodas) yield return "soda-" + SodaCatalog.Get(soda).Art;
        foreach (var joker in inventory.Jokers)
            yield return PixelArt.CampaignIconName(FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString()));
        foreach (var (_, _, definition) in CampaignTableControls.Items(inventory))
            yield return PixelArt.CampaignIconName(definition);
    }
}
