using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignShopUiTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception($"The shop must expose {name}.");
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static string Text(Control control) => string.Join(" ", control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Require(frame is not null, "The shop must render its availability badges.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run()
    {
        Availability();
        RerollAndResume();
        Console.WriteLine("PASS  Shop badges explain full slots and funds, preserve Buy & Use, and show Joker sell values");
        Console.WriteLine("PASS  Shop reroll charges once, rejects stale controls, persists and resumes escalating price");
    }

    private static void Availability()
    {
        var jokerIds = Enum.GetValues<JokerId>();
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        {
            Shop = new(0, [new("j", FeatureCategory.Joker, jokerIds[6].ToString(), 4), new("t", FeatureCategory.Tarot, "Prospector", 3), new("s", FeatureCategory.Sigil, "Fish", 3), new("g", FeatureCategory.Gumball, "", 3)])
        };
        run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
        { Jokers = jokerIds.Take(5).Select(j => new JokerState(j)).ToArray(), Tarot = [TarotId.Prospector, TarotId.Gambler], Sigils = [SigilId.Bear, SigilId.Fish] } } };
        var uses = 0;
        var actions = new ShopActions(_ => throw new Exception("Blocked storage purchase must not run."), _ => uses++, _ => { }, (_, _) => { }, (_, _) => { }, () => { }, () => { })
        { Pull = _ => null, Refresh = () => { }, Reroll = () => { } };
        var window = new Window { Width = 1280, Height = 1100, Content = CampaignShopScreen.Build(run, actions, Still()) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Require(Find(window, "ShopReroll").IsEnabled, "Full shelves must still allow refreshing shop stock.");
            Require(Text(Find(window, "Offerj")).Contains("Sell a Joker first"), "Full Joker slots must be explained on the offer before selecting it.");
            Require(Text(Find(window, "Offert")).Contains("Card slots full"), "Full item slots must be visible before selecting an offer.");
            Require(Text(Find(window, "Offers")).Contains("Card slots full"), "Full Sigil slots must be visible before selecting an offer.");
            Require(Text(Find(window, $"Owned{jokerIds[0]}")).Contains("Sell +$2"), "Owned Jokers must show sell proceeds before selection.");
            Click(Find(window, "Offerj"));
            Require(!Find(window, "Buyj").IsEnabled, "A full Joker shelf must disable Buy while keeping its detail reachable.");
            foreach (var id in new[] { "t", "s" })
            {
                Click(Find(window, "Offer" + id));
                Require(!Find(window, "Buy" + id).IsEnabled, "A full item shelf must disable storage purchase.");
                Require(Find(window, "BuyUse" + id).IsEnabled, "Full storage must preserve Buy & Use.");
                Click(Find(window, "BuyUse" + id));
            }
            Require(uses == 2, "Both full-storage cards must remain usable immediately.");
            Capture(window, "shop-full-slots");
            window.Content = CampaignShopScreen.Build(run with { Money = 0 }, actions, Still()); Dispatcher.UIThread.RunJobs();
            foreach (var id in new[] { "j", "t", "s" })
            {
                Require(Text(Find(window, "Offer" + id)).Contains("Not enough money"), "Unaffordable offers must show their reason before selecting.");
                Click(Find(window, "Offer" + id));
                Require(!Find(window, "Buy" + id).IsEnabled, "Insufficient money must disable purchase.");
                if (id != "j") Require(!Find(window, "BuyUse" + id).IsEnabled, "Buy & Use must also require money.");
            }
            Require(Text(Find(window, "GumballMachine")).Contains("Not enough money"), "The unaffordable gumball machine must show its reason before selecting.");
            Require(!Find(window, "ShopReroll").IsEnabled, "Reroll must be visibly unavailable without enough money.");
            Capture(window, "shop-insufficient-funds");
        }
        finally { window.Close(); }
    }

    private static void RerollAndResume()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shop-reroll-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1280, Height = 1100 };
        try
        {
            var initial = CampaignShop.Open(new CampaignRun(Match: 2, Money: 10, Phase: CampaignPhase.Shop));
            Require(CampaignStore.Save(initial, path), "Could not prepare reroll save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            var old = Find(window, "ShopReroll");
            var oldNext = Find(window, "CampaignNextMatch");
            Require(Text(old).Contains("$2"), "First reroll must display its $2 price.");
            Click(old);
            Require(flow.Run!.Money == 8 && CampaignStore.Load(path)!.Shop!.RerollCount == 1, "Reroll must save its payment and counter immediately.");
            Require(Text(Find(window, "ShopReroll")).Contains("$3"), "Next reroll must display its increased price.");
            var savedAfterReroll = File.ReadAllText(path);
            var moneyAfterReroll = flow.Run.Money;
            var walletAfterReroll = flow.Run.Resources.Wallets[0];
            var revisionAfterReroll = flow.Run.Resources.Revision;
            var randomAfterReroll = flow.Run.Resources.RandomState;
            Click(oldNext);
            Require(flow.Run.Phase == CampaignPhase.Shop, "An old Next Match callback must not leave refreshed shop stock.");
            Require(File.ReadAllText(path) == savedAfterReroll, "Stale Next Match must preserve the exact saved campaign state.");
            Require(flow.Run.Money == moneyAfterReroll && flow.Run.Resources.Wallets[0] == walletAfterReroll
                && flow.Run.Resources.Revision == revisionAfterReroll && flow.Run.Resources.RandomState == randomAfterReroll,
                "Stale Next Match must not change money, wallet, revision or RNG.");
            Click(old);
            Require(flow.Run.Money == 8 && flow.Run.Shop!.RerollCount == 1, "A stale reroll button must not charge again.");
            var resumed = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            resumed.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Require(Text(Find(window, "ShopReroll")).Contains("$3"), "Resuming must preserve the reroll price.");
            Click(Find(window, "ShopReroll"));
            Require(resumed.Run!.Money == 5 && resumed.Run.Shop!.RerollCount == 2, "Resumed reroll must charge the escalated price.");
            var beforeNext = resumed.Run;
            Click(Find(window, "CampaignNextMatch"));
            Require(resumed.Run.Phase == CampaignPhase.Intro && CampaignStore.Load(path)!.Phase == CampaignPhase.Intro,
                "The current Next Match control must still advance and save the intro screen.");
            Require(resumed.Run.Money == beforeNext.Money && resumed.Run.Resources.Revision == beforeNext.Resources.Revision
                && resumed.Run.Resources.RandomState == beforeNext.Resources.RandomState,
                "Current Next Match must preserve shop transaction resources.");
        }
        finally { window.Close(); File.Delete(path); }
    }
}
