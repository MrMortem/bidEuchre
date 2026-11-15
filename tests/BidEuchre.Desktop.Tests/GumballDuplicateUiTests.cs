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
            { Resources = CampaignSnapshot.Create(which == "Panic" ? 40UL : 17UL) with { Wallets = [30, 0, 0, 0], Inventory = inventory }, Shop = new(0, [new("g", FeatureCategory.Gumball, "", 3), new("a", FeatureCategory.Tarot, "Astronomer", 3)]) };
            Require(CampaignStore.Save(initial, path), "Prepare duplicate shop save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            if (which is "Machine" or "Panic")
            {
                Click(Find(window, "GumballMachine"));
                Click(Find(window, "PullGumball"));
                var saved = CampaignStore.Load(path)!;
                Require(saved.Money == 27 && saved.Resources.Inventory.Unlocks.Length == 9, "A duplicate pull must save once without changing unique unlocks.");
                Require(Text(window).Contains("copy 2") && Text(window).Contains("×2"), "Duplicate feedback and owned inventory must display its count.");
                Require(Text(window).Contains("GUMBALLS 9/9"), "Unique collection count must stay nine.");
                var note = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampaignNote");
                Require(Equals(note.Foreground, MainWindow.Green), "Successful duplicate grants must use the positive reward tone.");
                if (which == "Panic")
                {
                    Require(saved.Resources.Inventory.GumballExtraCopies.GetValueOrDefault(GumballId.PanicButton) == 1, "Seed40 must award a Panic Button duplicate.");
                    Require(Text(window).Contains("No successful contract reward"), "Panic Button duplicate feedback must not promise a successful-bid bonus.");
                    var tip = window.GetVisualDescendants().OfType<Border>().Select(ToolTip.GetTip).OfType<string>().Single(t => t.StartsWith("Panic Button:"));
                    Require(tip.Contains("Copies: 2") && tip.Contains("extra copies award no success points"), "Panic Button inventory must show count and no success payout.");
                }
                Capture(window, "duplicate-gumball-" + which.ToLowerInvariant());
            }
            else
            {
                Click(Find(window, "Offera"));
                Require(Find(window, "BuyUsea").IsEnabled, "Astronomer Buy & Use must work with every bid unlocked and full inventory.");
                Capture(window, "astronomer-full-inventory");
                Click(Find(window, "BuyUsea"));
                Require(flow.Run!.Resources.Inventory.ItemCount == 4 && flow.Run.Money == 27, "Immediate Astronomer use must not need a slot.");
                Require(Text(window).Contains("copy 2"), "Astronomer must identify its duplicate reward.");
                Click(Find(window, "OwnedTarot0"));
                Require(Find(window, "UseTarot0").IsEnabled, "Stored Astronomer remains usable after every unlock.");
                Click(Find(window, "UseTarot0"));
                Require(flow.Run.Resources.Inventory.ItemCount == 3, "Stored Astronomer must be consumed once.");
                var used = 0;
                window.Content = CampaignAnteScreen.Build(initial with { Phase = CampaignPhase.Intro }, () => { }, () => { }, null, Still(), (_, _) => used++);
                Dispatcher.UIThread.RunJobs();
                var item = Find(window, "AnteItemTarot0");
                var flyout = (Flyout)item.Flyout!;
                var use = ((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "AnteUseTarot0")
                    ?? ((Panel)flyout.Content!).Children.OfType<Button>().Single(b => b.Name == "AnteUseTarot0");
                Require(use.IsEnabled, "Astronomer remains usable before the next match after every unlock.");
                Click(use); Require(used == 1, "Pre-match Astronomer must invoke one use.");
            }
        }
        finally { window.Close(); File.Delete(path); }
    }
}
