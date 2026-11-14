using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CuratedArtTests
{
    public static void ContainsSigil(Control control, SigilId sigil)
    {
        var icon = PixelArt.CampaignIcon(FeatureCatalog.Get(FeatureCategory.Sigil, sigil.ToString()));
        if (!control.GetLogicalDescendants().OfType<Image>().Any(image => image.Name == "CardSigil" && ReferenceEquals(image.Source, icon.Source)))
            throw new Exception($"Card must carry its {sigil} Sigil artwork.");
    }

    public static void Run()
    {
        var face = Card.Parse("9H");
        foreach (var sigil in new[] { SigilId.Fish, SigilId.Vulture, SigilId.Frog, SigilId.Spider, SigilId.Eagle, SigilId.Duck, SigilId.Bear, SigilId.Chameleon })
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Sigil, sigil.ToString());
            var expected = ((Image)((Grid)PixelArt.CardContent(face, "curated-" + definition.Id.Replace(':', '-'))).Children[0]).Source;
            var shop = PixelArt.CampaignIcon(definition);
            if (!ReferenceEquals(shop.Source, expected)) throw new Exception($"Shop must use curated {sigil} art.");
            var content = (Grid)PixelArt.CardContent(face, EnhancementId.Golden, sigil);
            ContainsSigil(content, sigil);
            if (((Image)content.Children[1]).Effect is not DropShadowEffect)
                throw new Exception("Curated darker substrates need a contrasting halo behind the original face sprite.");
        }
        foreach (var gumball in new[] { GumballId.HighLow, GumballId.Chaos, GumballId.Alone })
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Gumball, gumball.ToString());
            var expected = ((Image)((Grid)PixelArt.CardContent(face, "curated-" + definition.Id.Replace(':', '-'))).Children[0]).Source;
            if (!ReferenceEquals(PixelArt.Gumball(gumball, 64)?.Source, expected) || !ReferenceEquals(PixelArt.CampaignIcon(definition).Source, expected))
                throw new Exception($"Machine, inventory and shop must share curated {gumball} art.");
        }
        ContainsSigil(PixelArt.CardContent(face, null, SigilId.Coin), SigilId.Coin);
        var plain = (Grid)PixelArt.CardContent(face);
        if (plain.Children.Count != 2 || ((Image)plain.Children[1]).Effect is not null)
            throw new Exception("Ordinary unenhanced cards must retain their two art layers.");
        var motion = new PixelMotion(); motion.SetEnabled(false);
        var picker = CampaignDeckPicker.Build([new(new(0), face, EnhancementId.Glass, SigilId.Fish)], 0, _ => { }, () => { }, motion, "Curated picker");
        ContainsSigil(picker, SigilId.Fish);
        Console.WriteLine("PASS  Curated Sigils, Gumballs and enhanced card badges use supplied art with readable face layers and fallback art");
    }
}
