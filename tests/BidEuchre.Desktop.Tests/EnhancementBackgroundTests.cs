using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class EnhancementBackgroundTests
{
    private static readonly Card Face = Card.Parse("AH");
    private static object Background(EnhancementId? enhancement) => ((Image)((Grid)PixelArt.CardContent(Face,
        enhancement is null ? "card-background-white" : enhancement is EnhancementId.Emerald or EnhancementId.Stinky
            ? $"card-background-{enhancement.Value.ToString().ToLowerInvariant()}"
            : $"curated-enhancement-{enhancement.Value.ToString().ToLowerInvariant()}")).Children[0]).Source!;

    private static void Contains(Control control, EnhancementId? enhancement)
    {
        var expected = Background(enhancement);
        if (!control.GetLogicalDescendants().OfType<Image>().Any(image => ReferenceEquals(image.Source, expected)))
            throw new Exception($"Card must render its {enhancement?.ToString() ?? "white"} background.");
    }

    public static void Run()
    {
        foreach (var enhancement in Enum.GetValues<EnhancementId>().Select(e => (EnhancementId?)e).Append(null))
        {
            var content = (Grid)PixelArt.CardContent(Face, enhancement);
            if (!ReferenceEquals(((Image)content.Children[0]).Source, Background(enhancement)))
                throw new Exception($"Renderer selected the wrong background for {enhancement}.");
            var plain = (Grid)PixelArt.CardContent(Face);
            if (!ReferenceEquals(((Image)content.Children[1]).Source, ((Image)plain.Children[1]).Source))
                throw new Exception("Enhancements must not change the printed card face.");
        }
        Console.WriteLine("PASS  All ten enhancement backgrounds and white fallback preserve printed face sprites");

        var deck = Enum.GetValues<EnhancementId>().Select((e, index) => new CardState(new(index), Face, e)).ToArray();
        var motion = new PixelMotion(); motion.SetEnabled(false);
        var picker = CampaignDeckPicker.Build(deck, 0, _ => { }, () => { }, motion, "Enhancement test");
        foreach (var enhancement in Enum.GetValues<EnhancementId>()) Contains(picker, enhancement);
        Console.WriteLine("PASS  Deck/shop cards automatically select their individual enhancement substrates");

        var window = new MainWindow();
        try
        {
            var first = new CampaignCardView(new(0), Face, EnhancementId.Golden, SigilId.Fish);
            var second = new CampaignCardView(new(1), Face, EnhancementId.Glass, SigilId.Frog);
            var players = Enumerable.Range(0, 4).Select(seat => new CampaignPlayerView(seat, seat < 2 ? 1 : 0,
                seat == 0 ? [first] : seat == 1 ? [second] : [], false)).ToArray();
            CampaignTrickPlay[] trick = [new(0, first, Suit.Hearts), new(1, second, Suit.Hearts)];
            var campaign = new CampaignGameView(0, 0, 0, players, [], trick, [], CampaignInventory.Empty, 0, 1, 20, null);
            var plain = new GameEngine(["A", "B", "C", "D"], 1).CreateView(0);
            var view = plain with { Campaign = campaign, CurrentTrick = [new(0, Face), new(1, Face)] };
            Control Invoke(string name, params object[] args) => (Control)typeof(MainWindow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args)!;
            Contains(Invoke("PlayerPanel", plain.Players[0] with { CardCount = 1 }, view), EnhancementId.Golden);
            Contains(Invoke("PlayerPanel", plain.Players[1] with { CardCount = 1 }, view), EnhancementId.Glass);
            var current = Invoke("TrickPanel", view);
            Contains(current, EnhancementId.Golden); Contains(current, EnhancementId.Glass);
            CuratedArtTests.ContainsSigil(current, SigilId.Fish); CuratedArtTests.ContainsSigil(current, SigilId.Frog);
            var completed = Invoke("TrickPanel", view with
            {
                CurrentTrick = [], CompletedTricks = [new(1, 0, 0, view.CurrentTrick)],
                Campaign = campaign with { Trick = [], LastTrick = trick }
            });
            Contains(completed, EnhancementId.Golden); Contains(completed, EnhancementId.Glass);
            CuratedArtTests.ContainsSigil(completed, SigilId.Fish); CuratedArtTests.ContainsSigil(completed, SigilId.Frog);
            Contains(Invoke("TrickPanel", view with { Campaign = null }), null);
            Console.WriteLine("PASS  Campaign hands and current/completed tricks preserve enhancements for duplicate printed cards; ordinary Table stays white");
        }
        finally { window.Close(); }
    }
}
