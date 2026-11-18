using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignShopUseUiTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception($"Missing committed purchase control {name}.");
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }
    private static CardId[] Cards(Window window) => window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("DeckCard") is true)
        .Select(b => new CardId(int.Parse(b.Name![8..]))).ToArray();
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Require(frame is not null, "The committed purchase screen must render.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"committed-shop-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 980, Height = 680 };
        try
        {
            var initial = CampaignArcadeUiTests.PlinkoFixture(5,
                new([], [TarotId.Gambler, TarotId.Prospector, TarotId.TaxCollector], []) { Sigils = [SigilId.Bear] });
            var prize = CampaignArcade.PlayPlinko(initial, 4).Shop!.Arcade!.Pending!;
            Require(CampaignStore.Save(initial, path), "The committed purchase fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade")); Click(Find(window, "ArcadePlay"));
            Click(Find(window, "TearSpectral"));
            var sigilCard = Array.FindIndex(prize.PackCards!, k => k != CampaignArcade.LegendaryKey);
            Click(Find(window, $"CollectorCard{sigilCard}"));
            Require(CampaignStore.Load(path)!.Money == 30 - CampaignArcade.Price, "Opening the six cards must retain the saved Arcade payment without another charge.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "CancelDeckChoice" or "CampaignNextMatch" or "TellerBack"), "A committed purchase must not expose a way back out.");
            var six = Cards(window);
            Require(six.Length == 6 && six.Distinct().Count() == 6, "The committed purchase must show six distinct cards.");
            Require(!Find(window, "FinishShopUse").IsEnabled, "Done must wait for the purchased Sigil.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name?.StartsWith("ShopHeldSigil") is true), "The extra inventory opportunity only applies to Tarots.");

            Click(Find(window, "ShopHeldTarot0"));
            Require(!Find(window, "ConfirmDeckChoice").IsEnabled, "Held Gambler needs two cards.");
            Click(Find(window, $"DeckCard{six[1].Value}"));
            Require(!Find(window, "ConfirmDeckChoice").IsEnabled, "One target is insufficient for held Gambler.");
            Click(Find(window, $"DeckCard{six[4].Value}"));
            Require(Find(window, "ConfirmDeckChoice").IsEnabled, "Two different targets enable held Gambler.");
            Require(six.All(id => Find(window, $"DeckCard{id.Value}").IsEnabled == (id == six[1] || id == six[4])), "Selected Gambler targets must stay deselectable while a third card is disabled.");
            Capture(window, "committed-shop-held-gambler");
            Click(Find(window, "ShopPurchasedItem"));
            Require(!Find(window, "ConfirmDeckChoice").IsEnabled, "Switching consumables must clear target selection.");
            Click(Find(window, "ShopHeldTarot0"));
            foreach (var id in new[] { six[1], six[4] }) Click(Find(window, $"DeckCard{id.Value}"));
            var heldUse = Find(window, "ConfirmDeckChoice");
            Click(heldUse);
            Require(flow.Run!.Resources.Inventory.Tarot.SequenceEqual([TarotId.Prospector, TarotId.TaxCollector]), "Using inventory Gambler must consume just that slot.");
            Require(flow.Run.Resources.Deck.Where(c => c.Enhancement == EnhancementId.Lucky).Select(c => c.Id).ToHashSet().SetEquals([six[1], six[4]]), "Inventory Gambler must enhance the exact chosen cards before the purchase is applied.");
            Require(!Find(window, "FinishShopUse").IsEnabled, "Using a held Tarot must not fulfill the purchase.");
            var afterHeld = File.ReadAllText(path);
            Click(heldUse);
            Require(File.ReadAllText(path) == afterHeld, "An old use button must not consume the item that shifted into the slot.");

            flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Require(Cards(window).SequenceEqual(six), "Resume must return to the same committed six cards.");
            Require(flow.Run!.Money == 30 - CampaignArcade.Price && !Find(window, "FinishShopUse").IsEnabled, "Resume must preserve payment and the purchase obligation.");
            Click(Find(window, $"DeckCard{six[1].Value}"));
            Click(Find(window, "ConfirmDeckChoice"));
            Require(Find(window, "FinishShopUse").IsEnabled, "The purchase must unlock Done after it is used.");
            Require(flow.Run.Resources.Deck.Single(c => c.Id == six[1]).Sigil == Enum.Parse<SigilId>(prize.PackCards![sigilCard]), "The won Sigil must affect the selected offered card.");
            Require(flow.Run.Resources.Deck.Single(c => c.Id == six[1]).Enhancement == EnhancementId.Lucky, "A Sigil and held Tarot must coexist on the same physical card.");
            Require(!Find(window, "ShopPurchasedItem").IsEnabled, "The purchased item cannot be selected twice.");
            Click(Find(window, "ShopHeldTarot0"));
            Click(Find(window, $"DeckCard{six[2].Value}")); Click(Find(window, "ConfirmDeckChoice"));
            Require(flow.Run.Resources.Deck.Single(c => c.Id == six[2]).Enhancement == EnhancementId.Golden, "Held Tarots must remain usable after the purchased item is applied.");
            Click(Find(window, "ShopHeldTarot0"));
            Require(Find(window, "ConfirmDeckChoice").IsEnabled && Cards(window).Length == 6, "A zero-target inventory Tarot must be usable while all six cards remain visible.");
            Click(Find(window, "ConfirmDeckChoice"));
            Require(flow.Run.Money == 36, "Tax Collector must use the paid current balance.");
            Require(flow.Run.Resources.Inventory.Tarot.Length == 0 && flow.Run.Resources.Inventory.Sigils.Length == 1, "All used Tarots are consumed and the held Sigil is preserved.");
            Capture(window, "committed-shop-complete");

            flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Require(Cards(window).SequenceEqual(six) && Find(window, "FinishShopUse").IsEnabled, "Reload after applying the purchase must retain the six cards until Done.");
            var done = Find(window, "FinishShopUse"); Click(done);
            Require(Find(window, "CampaignNextMatch").IsEnabled, "Done must return to the normal shop.");
            Click(Find(window, "InspectDeck"));
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name?.StartsWith("DeckCard") is true || b.Name == "ConfirmDeckChoice"), "Full deck inspection must remain read-only.");
            Console.WriteLine("PASS  Arcade prize keeps its payment, blocks exit, uses inventory Tarots before and after the prize, and restores the same six cards on resume");
        }
        finally { window.Close(); File.Delete(path); }
    }
}
