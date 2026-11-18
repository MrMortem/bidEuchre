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
    private static void WaitFor(Func<bool> ready)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && timeout.ElapsedMilliseconds < 3000)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        Require(ready(), "The updated shop did not finish loading.");
    }

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
        GumballDuplicateUiTests.Run("Machine");
        GumballDuplicateUiTests.Run("Astronomer");
        GumballDuplicateUiTests.Run("Retired");
        Console.WriteLine("PASS  Retired gumball purchases remain sold and render without restoring the removed bid");
        Console.WriteLine("PASS  Duplicate gumball pulls save their count and identify the reward after all active unlocks");
        Console.WriteLine("PASS  Newly printed and inventory Astronomers share a committed purchase opportunity with all unlocks and full inventory");
        PrintedTicketTargets();
        CampaignShopUseUiTests.Run();
        HeldItemsWaitForMatch();
        Availability();
        RerollAndResume();
        FortuneTeller();
        Console.WriteLine("PASS  Fortune Teller prints 1 for $3 or 3 for $6, saves the print, and keeps or uses the chosen ticket");
        Console.WriteLine("PASS  Shop badges explain full slots and funds, preserve Arcade access, and show Joker sell values");
        Console.WriteLine("PASS  Shop reroll charges once, rejects stale controls, persists and resumes escalating price");
    }

    private static void PrintedTicketTargets()
    {
        foreach (var chosen in new[] { TarotId.Prospector, TarotId.Gambler, TarotId.DevilishDeal, TarotId.TaxCollector })
        {
            var path = Path.Combine(Path.GetTempPath(), $"ticket-targets-ui-{Guid.NewGuid():N}.json");
            var window = new Window { Width = 980, Height = 680 };
            try
            {
                CardId[] offered = [new(1), new(4), new(7), new(10), new(13), new(16)];
                var printed = chosen == TarotId.TaxCollector ? new[] { "TaxCollector" } : new[] { "Prospector", "Gambler", "DevilishDeal" };
                var initial = new CampaignRun(Money: 24, Phase: CampaignPhase.Shop)
                {
                    Shop = new(0, [new("t", FeatureCategory.Tarot, "", 3) { Printed = printed, TargetCards = offered }])
                };
                Require(CampaignStore.Save(initial, path), "Could not prepare paid ticket spread.");
                var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
                flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
                Click(Find(window, "FortuneTeller")); Click(Find(window, $"Ticket{chosen}")); Click(Find(window, "TellerUse"));
                var committed = CampaignStore.Load(path)!;
                Require(committed.Shop!.Offers.Single() is { Sold: true, Printed: null } && committed.Money == 24,
                    "Opening the application screen must commit the chosen ticket without charging again.");
                Require(!Find(window, "FinishShopUse").IsEnabled, "Even a zero-target ticket must be applied before Done.");
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "CancelDeckChoice" or "TellerBack"), "A committed ticket must not offer Back or Cancel.");
                flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
                flow.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
                Require(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("DeckCard") is true)
                    .Select(b => int.Parse(b.Name![8..])).SequenceEqual(new[] { 1, 4, 7, 10, 13, 16 }), "Reloading a committed ticket must retain the same six physical cards.");
                var required = CampaignShop.TarotTargets(chosen);
                Require(Find(window, "ConfirmDeckChoice").IsEnabled == (required == 0), "Confirmation must require the appropriate number of fresh card choices.");
                var targets = chosen == TarotId.Gambler ? new[] { offered[1], offered[4] } : offered.Take(required).ToArray();
                foreach (var target in targets) Click(Find(window, $"DeckCard{target.Value}"));
                if (chosen == TarotId.Gambler)
                {
                    Require(offered.All(id => Find(window, $"DeckCard{id.Value}").IsEnabled == (id.Value is 4 or 13)), "Gambler must enable only the selected cards for deselection at two targets.");
                    Capture(window, "shop-gambler-two-selected");
                }
                var confirm = Find(window, "ConfirmDeckChoice");
                Require(confirm.IsEnabled, "The offered target count must enable ticket confirmation.");
                Click(confirm);
                var after = File.ReadAllText(path);
                Click(confirm);
                Require(File.ReadAllText(path) == after, "A repeated ticket callback must not reapply its effect.");
                var saved = CampaignStore.Load(path)!;
                Require(saved.Resources.Inventory.ItemCount == 0 && Find(window, "FinishShopUse").IsEnabled, "Using the ticket must consume the purchase without storing a card and enable Done.");
                var changed = saved.Resources.Deck.Where(c => c.Enhancement is not null).ToArray();
                var expected = chosen == TarotId.TaxCollector ? 0 : chosen == TarotId.Gambler ? 2 : CampaignShop.TarotTargets(chosen);
                Require(changed.Length == expected && changed.All(c => offered.Contains(c.Id)), "Ticket effects must modify only the six offered cards.");
                Require(saved.Money == (chosen == TarotId.TaxCollector ? 44 : 24), "Paid tickets must apply without charging again.");
                if (chosen == TarotId.Prospector) Require(changed.Single().Id.Value == 1 && changed.Single().Enhancement == EnhancementId.Golden, "Prospector must enhance the exact selected offered card.");
                if (chosen == TarotId.Gambler) Require(changed.Select(c => c.Id.Value).SequenceEqual(new[] { 4, 13 }) && changed.All(c => c.Enhancement == EnhancementId.Lucky), "Gambler must make exactly the selected two physical cards Lucky.");
                if (chosen == TarotId.DevilishDeal) Require(saved.Resources.Deck.Single(c => c.Id.Value == 7).Enhancement == EnhancementId.Cursed, "Devilish Deal must preserve target order for its cursed third card.");
                Click(Find(window, "FinishShopUse"));
                Require(Find(window, "CampaignNextMatch").IsEnabled, "After Done, the normal shop can be left.");
            }
            finally { window.Close(); File.Delete(path); }
        }
        Console.WriteLine("PASS  Printed tickets commit before use, resume the same six cards, require exact selections, and apply only once before Done");
    }

    private static void HeldItemsWaitForMatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"held-items-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1280, Height = 1100 };
        try
        {
            var initial = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop);
            initial = initial with { Resources = initial.Resources with { Wallets = [30, 0, 0, 0], Inventory = initial.Resources.Inventory with { Tarot = [TarotId.Prospector], Sigils = [SigilId.Fish] } } };
            Require(CampaignStore.Save(initial, path), "Could not prepare held-card save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            foreach (var category in new[] { FeatureCategory.Tarot, FeatureCategory.Sigil })
            {
                Click(Find(window, $"Owned{category}0"));
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == $"Use{category}0"), "Held items must not have a shop-use action.");
                Require(Find(window, $"Discard{category}0").IsEnabled, "Held cards must remain discardable in the shop.");
            }
            Require(flow.Run!.Resources.Inventory.ItemCount == 2, "Inspecting held items must preserve both cards.");
            Click(Find(window, "CampaignNextMatch"));
            foreach (var category in new[] { FeatureCategory.Tarot, FeatureCategory.Sigil })
            {
                var item = Find(window, $"AnteItem{category}0");
                var menu = (Panel)((Flyout)item.Flyout!).Content!;
                Require(!menu.Children.OfType<Button>().Any(b => b.Name == $"AnteUse{category}0"), "Held items must not have a pre-match use action.");
                Click(menu.Children.OfType<Button>().Single(b => b.Name == $"AnteDiscard{category}0"));
                Require(flow.Run!.Phase == CampaignPhase.Intro, "Discarding a held item must return to the pre-match screen.");
            }
            var saved = CampaignStore.Load(path)!;
            Require(saved.Resources.Inventory.ItemCount == 0 && saved.Resources.Deck.SequenceEqual(initial.Resources.Deck), "Discarding held cards must save the inventory without modifying the deck.");
        }
        finally { window.Close(); File.Delete(path); }
        Console.WriteLine("PASS  Held Tarots and Sigils are inspectable and discardable outside a match, with no use action");
    }

    private static void Availability()
    {
        var jokerIds = Enum.GetValues<JokerId>();
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        {
            Shop = new(0, [new("j", FeatureCategory.Joker, jokerIds[6].ToString(), 4), new("t", FeatureCategory.Tarot, "Prospector", 3), new("g", FeatureCategory.Gumball, "", 3)]) { Arcade = new() }
        };
        run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
        { Jokers = jokerIds.Take(5).Select(j => new JokerState(j)).ToArray(), Tarot = [TarotId.Prospector, TarotId.Gambler], Sigils = [SigilId.Bear, SigilId.Fish] } } };
        var arcadeOpens = 0;
        var actions = new ShopActions(_ => throw new Exception("Blocked storage purchase must not run."), _ => { }, _ => { }, (_, _) => { }, () => { }, () => { })
        { Pull = _ => null, Refresh = () => { }, Reroll = () => { }, Arcade = () => arcadeOpens++ };
        var window = new Window { Width = 1280, Height = 1100, Content = CampaignShopScreen.Build(run, actions, Still()) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Require(Find(window, "ShopReroll").IsEnabled, "Full shelves must still allow refreshing shop stock.");
            Require(Text(Find(window, "Offerj")).Contains("Sell a Joker first"), "Full Joker slots must be explained on the offer before selecting it.");
            Require(!Text(Find(window, "FortuneTeller")).Contains("Card slots full"), "Full slots must not block the Fortune Teller: her tickets can be used at once.");
            Require(!Text(Find(window, "ShopArcade")).Contains("Card slots full"), "Full card slots must preserve Arcade access because prizes can be used immediately.");
            Require(Text(Find(window, $"Owned{jokerIds[0]}")).Contains("Sell +$2"), "Owned Jokers must show sell proceeds before selection.");
            Click(Find(window, "Offerj"));
            Require(!Find(window, "Buyj").IsEnabled, "A full Joker shelf must disable Buy while keeping its detail reachable.");
            Click(Find(window, "ShopArcade"));
            Require(arcadeOpens == 1, "Full inventory must allow opening the Arcade.");
            Capture(window, "shop-full-slots");
            window.Content = CampaignShopScreen.Build(run with { Money = 0 }, actions, Still()); Dispatcher.UIThread.RunJobs();
            Require(Text(Find(window, "FortuneTeller")).Contains("Not enough money"), "An unaffordable Fortune Teller must show her reason.");
            foreach (var id in new[] { "j" })
            {
                Require(Text(Find(window, "Offer" + id)).Contains("Not enough money"), "Unaffordable offers must show their reason before selecting.");
                Click(Find(window, "Offer" + id));
                Require(!Find(window, "Buy" + id).IsEnabled, "Insufficient money must disable purchase.");
            }
            Require(Text(Find(window, "GumballMachine")).Contains("Not enough money"), "The unaffordable gumball machine must show its reason before selecting.");
            Require(!Find(window, "ShopReroll").IsEnabled, "Reroll must be visibly unavailable without enough money.");
            Capture(window, "shop-insufficient-funds");
        }
        finally { window.Close(); }
    }

    private static void FortuneTeller()
    {
        foreach (var keep in new[] { true, false })
        {
            var path = Path.Combine(Path.GetTempPath(), $"teller-ui-{Guid.NewGuid():N}.json");
            var window = new Window { Width = 1280, Height = 1100 };
            try
            {
                Require(CampaignStore.Save(CampaignShop.Open(new CampaignRun(Money: 10, Phase: CampaignPhase.Shop)), path), "Could not prepare teller save.");
                var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
                flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
                Require(Text(Find(window, "FortuneTeller")).Contains("$3+"), "The shop card must show the Fortune Teller's starting price.");
                Click(Find(window, "FortuneTeller"));
                Require(Text(Find(window, "TellerPrintOne")).Contains("$3") && Text(Find(window, "TellerPrintThree")).Contains("$6"), "Her menu must price one ticket at $3 and three at $6.");
                Click(Find(window, keep ? "TellerPrintThree" : "TellerPrintOne"));
                var saved = CampaignStore.Load(path)!;
                var printed = saved.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot).Printed!;
                Require(saved.Money == (keep ? 4 : 7) && printed.Length == (keep ? 3 : 1), "A print must charge and save its tickets at once.");
                var tickets = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Ticket") is true).ToArray();
                Require(tickets.Length == printed.Length, "Every printed ticket must be shown to choose from.");

                // Going back keeps the paid tickets waiting; the shop cannot be left until one is taken.
                Click(Find(window, "TellerBack"));
                Require(Text(Find(window, "FortuneTeller")).Contains("PICK"), "The shop must show that printed tickets are waiting.");
                Click(Find(window, "CampaignNextMatch"));
                Require(flow.Run!.Phase == CampaignPhase.Shop && Text(window).Contains("Choose one of your printed tickets first."), "Next Match must wait for the printed ticket choice.");
                Click(Find(window, "FortuneTeller"));
                Require(window.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("Ticket") is true) == printed.Length, "Waiting tickets must still be offered.");

                var chosen = keep ? Enum.Parse<TarotId>(printed[1]) : Enum.Parse<TarotId>(printed[0]);
                Click(Find(window, $"Ticket{chosen}"));
                if (keep)
                {
                    Click(Find(window, "TellerKeep"));
                    Require(flow.Run!.Resources.Inventory.Tarot.SequenceEqual([chosen]) && flow.Run.Money == 4, "Keep must store the chosen ticket without charging again.");
                }
                else
                {
                    Click(Find(window, "TellerUse"));
                    if (CampaignShop.TarotTargets(chosen) > 0)
                    {
                        foreach (var card in CampaignShop.UseSessionCards(flow.Run!).Take(CampaignShop.TarotTargets(chosen))) Click(Find(window, $"DeckCard{card.Id.Value}"));
                    }
                    Click(Find(window, "ConfirmDeckChoice"));
                    Require(flow.Run!.Resources.Inventory.ItemCount == 0, "Using a printed ticket must not take a slot.");
                    Click(Find(window, "FinishShopUse"));
                }
                var after = CampaignStore.Load(path)!.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
                Require(after is { Sold: true, Printed: null } && after.FeatureId == chosen.ToString(), "The choice must be saved and the others discarded.");
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "FortuneTeller"), "She prints once a visit.");
                Click(Find(window, "CampaignNextMatch"));
                Require(flow.Run!.Phase == CampaignPhase.Intro, "Once a ticket is taken the shop can be left.");
            }
            finally { window.Close(); File.Delete(path); }
        }
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
            WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ShopReroll"));
            var old = Find(window, "ShopReroll");
            var oldNext = Find(window, "CampaignNextMatch");
            Require(Text(old).Contains("$2"), "First reroll must display its $2 price.");
            Click(old);
            Require(flow.Run!.Money == 8 && CampaignStore.Load(path)!.Shop!.RerollCount == 1, "Reroll must save its payment and counter immediately.");
            WaitFor(() => !ReferenceEquals(old, Find(window, "ShopReroll")));
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
            WaitFor(() => Text(Find(window, "ShopReroll")).Contains("$4"));
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
