using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class GumballDuplicateUiTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception($"Missing {name}.");
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static string Text(Window window) => string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Require(frame is not null, "The duplicate gumball shop must render.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run(string which)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gumball-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1280, Height = 1100 };
        try
        {
            var inventory = new CampaignInventory([], [TarotId.Astronomer, TarotId.Prospector], Enum.GetValues<GumballId>()) { Sigils = [SigilId.Bear, SigilId.Fish] };
            var initial = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(17) with { Wallets = [30, 0, 0, 0], Inventory = inventory }, Shop = new(0, [new("g", FeatureCategory.Gumball, "", 3, which == "Retired"), new("a", FeatureCategory.Tarot, "", 3) { Printed = ["Astronomer"] }]) };
            Require(CampaignStore.Save(initial, path), "Prepare duplicate shop save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            if (which == "Retired")
            {
                Require(Text(window).Contains("SOLD"), "A migrated retired gumball purchase must remain sold.");
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "GumballMachine" or "PullGumball"), "The retired purchase must not give another pull.");
                Require(!Text(window).Contains("Panic Button"), "The retired bid must not appear in the current shop.");
                var saved = CampaignStore.Load(path)!;
                Require(saved.Money == 30 && saved.Shop!.Offers.Single(o => o.Id == "g") is { Sold: true, FeatureId: "" }, "Showing a retired sold machine must preserve its payment and stock.");
                Capture(window, "retired-gumball-sold");
            }
            else if (which == "Machine")
            {
                Click(Find(window, "GumballMachine"));
                Click(Find(window, "PullGumball"));
                var saved = CampaignStore.Load(path)!;
                Require(saved.Money == 27 && saved.Resources.Inventory.Unlocks.Length == CampaignInventory.GumballCount, "A duplicate pull must save once without changing unique unlocks.");
                Require(Text(window).Contains("copy 2") && Text(window).Contains("×2"), "Duplicate feedback and owned inventory must display its count.");
                Require(Text(window).Contains($"GUMBALLS {CampaignInventory.GumballCount}/{CampaignInventory.GumballCount}"), "Unique collection count must include every active bid.");
                var note = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampaignNote");
                Require(Equals(note.Foreground, MainWindow.Green), "Successful duplicate grants must use the positive reward tone.");
                Require(Text(window).Contains("+1 points on your successful matching bid"), "Every active duplicate must explain its successful-bid reward.");
                Require(!Text(window).Contains("Panic Button"), "Retired bids must not appear in the collection or pull feedback.");
                Capture(window, "duplicate-gumball-" + which.ToLowerInvariant());
            }
            else
            {
                Click(Find(window, "FortuneTeller"));
                Click(Find(window, "TicketAstronomer"));
                Require(!Find(window, "TellerKeep").IsEnabled, "A printed ticket cannot be kept with full card slots.");
                Require(Find(window, "TellerUse").IsEnabled, "A printed Astronomer must be usable with every bid unlocked and full inventory.");
                Capture(window, "astronomer-full-inventory");
                Click(Find(window, "TellerUse"));
                Require(Find(window, "ConfirmDeckChoice").IsEnabled && !Find(window, "FinishShopUse").IsEnabled, "A zero-target purchase still requires explicit use before Done.");
                Click(Find(window, "ConfirmDeckChoice"));
                Require(flow.Run!.Resources.Inventory.ItemCount == 4 && flow.Run.Money == 30, "Immediate Astronomer use must not need a slot or charge again.");
                Require(Text(window).Contains("copy 2"), "Astronomer must identify its duplicate reward.");
                Click(Find(window, "ShopHeldTarot0"));
                Click(Find(window, "ConfirmDeckChoice"));
                Require(flow.Run.Resources.Inventory.ItemCount == 3 && flow.Run.Resources.Inventory.GumballExtraCopies.Values.Sum() == 2, "An inventory Astronomer must remain usable and preserve both grants within the same committed session.");
                Click(Find(window, "FinishShopUse"));
                Click(Find(window, "OwnedTarot0"));
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "UseTarot0"), "Stored tickets must wait for a match or a new purchase opportunity outside an application session.");
                Require(flow.Run.Resources.Inventory.ItemCount == 3, "Inspecting a stored ticket must preserve it.");
                window.Content = CampaignAnteScreen.Build(initial with { Phase = CampaignPhase.Intro }, () => { }, () => { }, null, Still());
                Dispatcher.UIThread.RunJobs();
                var item = Find(window, "AnteItemTarot0");
                var flyout = (Flyout)item.Flyout!;
                var controls = ((Panel)flyout.Content!).Children.OfType<Button>().ToArray();
                Require(!controls.Any(b => b.Name == "AnteUseTarot0"), "A stored Astronomer must not be usable before the next match.");
                Require(controls.Any(b => b.Name == "AnteDiscardTarot0"), "Pre-match cards must remain inspectable and discardable.");
            }
        }
        finally { window.Close(); File.Delete(path); }
    }
}
