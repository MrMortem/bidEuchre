using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignShopUiTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Button Find(Window window, string name)
    {
        Button? button = null;
        WaitFor(() => (button = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)) is not null);
        return button!;
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    internal static void WaitFor(Func<bool> ready)
    {
        if (ready()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (ready()) timeout.Cancel();
        };
        timer.Start();
        try { Dispatcher.UIThread.MainLoop(timeout.Token); }
        finally { timer.Stop(); }
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

    public static void BuySoda()
    {
        VendingPurchase(false);
        VendingPurchase(true);
    }

    private static void VendingPurchase(bool animate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vending-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1200, Height = 850 };
        try
        {
            var initial = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
                { Resources = CampaignSnapshot.Create(124) with { Wallets = [30, 0, 0, 0] } };
            Require(CampaignStore.Save(initial, path), "The vending fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, animate ? new PixelMotion() : Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SodaMachine"));
            Click(Find(window, "SodaMachine"));
            var buy = Find(window, "BuySoda");
            Require(buy.IsEnabled && Text(buy).Contains("$8"), "The vending machine must quote the base price.");
            Capture(window, "vending-machine");
            Click(buy);
            WaitFor(() => window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "OwnedSodas"));
            var saved = CampaignStore.Load(path)!;
            var soda = SodaCatalog.Get(saved.Resources.Inventory.Sodas.Single());
            var tray = Find(window, "SodaTrayCan");
            var face = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "SodaCabinet");
            Require(face.Width == GumballMachine.FaceWidth && face.Height == GumballMachine.FaceHeight, "Both machines must use the same shop footprint.");
            if (animate)
            {
                Require(!tray.IsVisible, "The tray must wait for the can to land.");
                var falling = window.GetVisualDescendants().OfType<Image>().Single(c => c.Name == "DispensingSoda");
                var cabinet = (Canvas)falling.Parent!.Parent!;
                WaitFor(() => Math.Abs(((Avalonia.Media.TranslateTransform)cabinet.RenderTransform!).X) > 1);
                WaitFor(() => Canvas.GetTop(falling) > 180);
            }
            WaitFor(() => tray.IsVisible);
            tray.BringIntoView(); Dispatcher.UIThread.RunJobs();
            var center = tray.TranslatePoint(new Point(tray.Bounds.Width / 2, tray.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Require(saved.Money == 22 && saved.Shop!.SodaPurchased && Text(window).Contains(soda.Description),
                "The shop must save and explain the specific permanent upgrade.");
            var inspect = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "SodaInspection");
            var effect = inspect.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SodaEffect");
            var pour = inspect.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SodaPour");
            var lid = inspect.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "OpenedSodaLid");
            if (animate)
            {
                Require(!lid.IsVisible, "The can must arrive with its original closed lid.");
                Require(!effect.IsVisible, "The effect should wait for the first opening.");
                WaitFor(() => inspect.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "SodaPopOut" && i.Height > 50 && i.Height < 340));
                WaitFor(() => pour.IsVisible);
                Require(lid.IsVisible && !effect.IsVisible, "The opened lid must be visible during the pour, before the effect.");
                if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not null)
                {
                    var captureAt = AnimationClock.Now + .26;
                    WaitFor(() => AnimationClock.Now >= captureAt);
                    Capture(window, "soda-pouring");
                }
                WaitFor(() => effect.IsVisible);
            }
            Require(lid.IsVisible && effect.IsVisible && !pour.IsVisible, "The finished opening must show the effect with the stream stopped.");
            Require(inspect.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "InspectedSoda").Height >= 350
                && Text(inspect).Contains(soda.Name) && Text(inspect).Contains(soda.Description), "Inspection must enlarge and explain the dispensed can.");
            Capture(window, "soda-purchased");
            Click(Find(window, "CloseSodaInspection"));
            Require(tray.IsEffectivelyEnabled && CampaignStore.Load(path)!.Money == 22, "Closing inspection must restore the tray without charging again.");
            Require(CampaignStore.Load(path)!.Shop!.SodaOpened, "The save must remember opening the can.");
            Click(tray);
            Require(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SodaEffect").IsVisible,
                "Reopening the same soda should show its effect immediately.");
            Click(Find(window, "CloseSodaInspection"));
            Click(buy);
            Click(Find(window, "SodaMachine"));
            Require(!Find(window, "BuySoda").IsEnabled && CampaignStore.Load(path)!.Money == 22, "A second click cannot buy another soda.");
            var oldMachine = Find(window, "SodaMachine");
            Click(Find(window, "ShopReroll"));
            WaitFor(() => !oldMachine.IsAttachedToVisualTree() && window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SodaMachine"));
            Click(Find(window, "SodaMachine"));
            Require(!Find(window, "BuySoda").IsEnabled, "Reroll must keep the can sold.");
            Click(Find(window, "CampaignNextMatch"));
            Require(window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "OwnedSodas"), "The new soda must remain visible before the match.");
        }
        finally { window.Close(); File.Delete(path); }
        Console.WriteLine("PASS  The vending machine dispenses and saves one $8 soda, explains the effect, and stays sold on reroll.");
    }

    public static void Run()
    {
        BuySoda();
        GumballDuplicateUiTests.Run("Machine");
        GumballDuplicateUiTests.Run("Astronomer");
        GumballDuplicateUiTests.Run("Retired");
        Console.WriteLine("PASS  Retired gumball purchases remain sold and render without restoring the removed bid");
        Console.WriteLine("PASS  Duplicate gumball pulls save their count and identify the reward after all active unlocks");
        Console.WriteLine("PASS  Newly printed and inventory Sweet Tooth tickets share a committed purchase opportunity with all unlocks and full inventory");
        SpectralTicketChoice();
        PrintedTicketTargets();
        CampaignShopUseUiTests.Run();
        HeldItemsWaitForMatch();
        Availability();
        RerollAndResume();
        BoosterAndResume();
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
                var targets = offered.Take(required).ToArray();
                foreach (var target in targets) Click(Find(window, $"DeckCard{target.Value}"));
                if (chosen == TarotId.Gambler)
                    Require(offered.All(id => !Find(window, $"DeckCard{id.Value}").IsEnabled), "Gambler must choose its own target.");
                var confirm = Find(window, "ConfirmDeckChoice");
                Require(confirm.IsEnabled, "The offered target count must enable ticket confirmation.");
                Click(confirm);
                var after = File.ReadAllText(path);
                Click(confirm);
                Require(File.ReadAllText(path) == after, "A repeated ticket callback must not reapply its effect.");
                var saved = CampaignStore.Load(path)!;
                Require(saved.Resources.Inventory.ItemCount == 0 && Find(window, "FinishShopUse").IsEnabled, "Using the ticket must consume the purchase without storing a card and enable Done.");
                var changed = saved.Resources.Deck.Where(c => c.Enhancement is not null).ToArray();
                var expected = chosen switch { TarotId.Gambler => 1, TarotId.DevilishDeal => 3, _ => required };
                Require(changed.Length == expected && changed.All(c => offered.Contains(c.Id)), "Ticket effects must modify only the six offered cards.");
                Require(saved.Money == (chosen == TarotId.TaxCollector ? 44 : 24), "Paid tickets must apply without charging again.");
                if (chosen == TarotId.Prospector) Require(changed.Single().Id.Value == 1 && changed.Single().Enhancement == EnhancementId.Golden, "Prospector must enhance the exact selected offered card.");
                if (chosen == TarotId.Gambler) Require(changed.Single().Enhancement == EnhancementId.Lucky, "Gambler must make one offered card Lucky.");
                if (chosen == TarotId.DevilishDeal) Require(changed.Single(c => !targets.Contains(c.Id)).Enhancement == EnhancementId.Cursed, "Devilish Deal must curse a third card outside the selection.");
                Click(Find(window, "FinishShopUse"));
                Require(Find(window, "CampaignNextMatch").IsEnabled, "After Done, the normal shop can be left.");
            }
            finally { window.Close(); File.Delete(path); }
        }
        Console.WriteLine("PASS  Printed tickets commit before use, resume the same six cards, require exact selections, and apply only once before Done");
    }

    private static void SpectralTicketChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mixed-teller-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1280, Height = 1000 };
        try
        {
            var run = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(19) });
            var teller = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
            var six = run.Resources.Deck.Take(6).Select(card => card.Id).ToArray();
            run = run with {
                Resources = run.Resources with { Inventory = run.Resources.Inventory with { Sodas = [SodaId.SpectralSprite] } },
                Shop = run.Shop with { ArcadeRoll = 5, Offers = run.Shop.Offers.Select(o => o == teller
                    ? o with { Printed = ["sigil:chameleon", "Chameleon", "Gambler"], TargetCards = six } : o).ToArray() }
            };
            Require(CampaignStore.Save(run, path), "The mixed spread must be saved.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "FortuneTeller"));
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ShopArcade"), "Closed visits should hide the Arcade.");
            Click(Find(window, "FortuneTeller"));
            Click(Find(window, "TicketChameleon"));
            Click(Find(window, "Ticketsigilchameleon"));
            Require(Text(window).Contains("SPECTRAL"), "The two Chameleon cards must show their respective families.");
            Click(Find(window, "TellerKeep"));
            Require(CampaignStore.Load(path)!.Resources.Inventory.Sigils.SequenceEqual([SigilId.Chameleon]), "The chosen spectral must enter Sigil storage.");
        }
        finally { window.Close(); File.Delete(path); }
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
            Shop = new(0, [new("j", FeatureCategory.Joker, jokerIds[6].ToString(), 4), new("t", FeatureCategory.Tarot, "Prospector", 3), new("g", FeatureCategory.Gumball, "", 3)]) { Arcade = new(), Booster = new("comic") }
        };
        run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
        { Jokers = jokerIds.Take(5).Select(j => new JokerState(j)).ToArray(), Tarot = [TarotId.Prospector, TarotId.Gambler], Sigils = [SigilId.Bear, SigilId.Fish] } } };
        var arcadeOpens = 0;
        var purchasedPack = -1;
        var actions = new ShopActions(_ => throw new Exception("Blocked storage purchase must not run."), _ => { }, _ => { }, (_, _) => { }, () => { }, () => { })
        { Pull = _ => null, Refresh = () => { }, Reroll = () => { }, OpenBooster = index => purchasedPack = index, Arcade = () => arcadeOpens++ };
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
            Click(Find(window, "ShopBooster"));
            Require(Find(window, "OpenBooster").IsEnabled, "A full Joker shelf must still allow buying a booster.");
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
            var upgraded = CampaignShop.Open(run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
            { Sodas = [SodaId.PackAPunch, SodaId.PeachPlus, SodaId.CitrusSale, SodaId.RerollRaspberry] } } });
            window.Content = CampaignShopScreen.Build(upgraded, actions, Still()); Dispatcher.UIThread.RunJobs();
            Click(Find(window, "ShopBonusBooster"));
            var buyBonus = Find(window, "OpenBooster");
            Require(Text(buyBonus).Contains("$5"), "The displayed bonus booster price should include Citrus Sale.");
            Click(buyBonus);
            Require(purchasedPack == 1, "The bonus booster button should buy the second pack.");
            var printed = run.Shop!.Offers.Select(o => o.Category == FeatureCategory.Tarot ? o with { Printed = ["Prospector"] } : o).ToArray();
            window.Content = CampaignShopScreen.Build(run with { Shop = run.Shop with { Offers = printed } }, actions, Still()); Dispatcher.UIThread.RunJobs();
            Click(Find(window, "ShopBooster"));
            Require(!Find(window, "OpenBooster").IsEnabled && Text(Find(window, "ShopBooster")).Contains("Choose a printed ticket first"),
                "A paid ticket must be resolved before the shop can open a booster.");
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
                    WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "FinishShopUse" && b.IsEnabled));
                    Click(Find(window, "FinishShopUse"));
                }
                var after = CampaignStore.Load(path)!.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
                Require(after is { Sold: true, Printed: null } && after.FeatureId == chosen.ToString(), "The choice must be saved and the others discarded.");
                WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CampaignNextMatch"));
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "FortuneTeller"), "She prints once a visit.");
                Click(Find(window, "CampaignNextMatch"));
                Require(flow.Run!.Phase == CampaignPhase.Intro, "Once a ticket is taken the shop can be left.");
            }
            finally { window.Close(); File.Delete(path); }
        }
    }

    public static void BoosterAndResume()
    {
        foreach (var skip in new[] { false, true })
        {
            var path = Path.Combine(Path.GetTempPath(), $"booster-ui-{Guid.NewGuid():N}.json");
            var window = new Window { Width = 980, Height = 680 };
            try
            {
                var initial = CampaignShop.Open(new CampaignRun(Money: 12, Phase: CampaignPhase.Shop));
                var owned = Enum.GetValues<JokerId>().Take(CampaignTableControls.JokerSlots).ToArray();
                initial = initial with { Resources = initial.Resources with { Inventory = initial.Resources.Inventory with
                    { Jokers = owned.Select(id => new JokerState(id)).ToArray() } } };
                Require(CampaignStore.Save(initial, path), "Could not prepare booster.");
                var openings = 0;
                void Continue(bool reduced = false)
                {
                    var motion = new PixelMotion();
                    motion.SetReduced(reduced);
                    var flow = new CampaignFlow(path, screen =>
                    {
                        window.Content = screen;
                        if (Visible("BoosterOpening")) openings++;
                    }, _ => Task.CompletedTask, () => { }, motion);
                    flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
                }
                bool Visible(string name) => window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name && c.IsEffectivelyVisible);
                Continue(reduced: skip);
                WaitFor(() => Visible("ShopBooster"));
                Point Anchor(string title) => window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Text == title).TranslatePoint(default, window)!.Value;
                var jokerAnchor = Anchor("JOKERS");
                var itemAnchor = Anchor("TAROTS / SIGILS");
                Click(Find(window, "ShopBooster")); Click(Find(window, "OpenBooster"));
                var paid = CampaignStore.Load(path)!;
                var choices = paid.Shop!.Booster!.Choices!;
                Require(paid.Money == 6 && choices.Distinct().Count() == 4, "Opening the booster must save its four choices and charge $6.");
                WaitFor(() => Visible("TearBooster"));
                Require(paid.Shop.Booster.AwaitingOpen && !Visible("SkipBooster") && !Visible("BoosterOpening"), "Payment must leave a sealed pack waiting for a click.");
                Continue(reduced: skip);
                WaitFor(() => Visible("TearBooster"));
                if (!skip) Capture(window, "shop-joker-booster-sealed");
                var seal = Find(window, "TearBooster");
                seal.Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Dispatcher.UIThread.RunJobs();
                Require(CampaignStore.Load(path)!.Shop!.Booster!.AwaitingOpen == false, "Keyboard activation must save the tear.");
                if (!skip)
                {
                    WaitFor(() => Visible("BoosterWrapperTop"));
                    var top = window.GetVisualDescendants().OfType<Image>().Single(c => c.Name == "BoosterWrapperTop");
                    var body = window.GetVisualDescendants().OfType<Image>().Single(c => c.Name == "BoosterWrapperBody");
                    Require(top.Clip is not null && body.Clip is not null && ReferenceEquals(top.Source, body.Source), "Both pieces must use the actual wrapper.");
                    var portraits = choices.Select(id => window.GetVisualDescendants().OfType<Image>().Single(c => c.Name == $"BoosterReveal{id}")).ToArray();
                    WaitFor(() => top.Opacity == 1 && Math.Abs(Canvas.GetLeft(body) - (520 - 192) / 2) > 2 && Canvas.GetLeft(body) == Canvas.GetLeft(top));
                    WaitFor(() => top.Opacity is > 0.15 and < 0.65);
                    Capture(window, "shop-joker-booster-tear");
                    WaitFor(() => portraits[0].Opacity > 0 && portraits[1].Opacity == 0 && Canvas.GetTop(top) < Canvas.GetTop(body));
                    WaitFor(() => portraits[2].Opacity > 0 && portraits[3].Opacity == 0);
                    Capture(window, "shop-joker-booster-opening");
                    WaitFor(() => portraits[3].Opacity > 0 && portraits.Take(3).All(p => p.Opacity == 1));
                    var picker = Find(window, "SkipBooster").GetVisualAncestors().OfType<ScrollViewer>().First();
                    WaitFor(() => picker.Opacity is > .2 and < .8);
                    Require(!Find(window, "SkipBooster").IsEffectivelyEnabled, "The chooser must stay locked during the crossfade.");
                    Capture(window, "shop-joker-booster-landing");
                }
                WaitFor(() => Visible("SkipBooster") && Find(window, "SkipBooster").IsEffectivelyEnabled);
                Require(Anchor("JOKERS") == jokerAnchor && Anchor("TAROTS / SIGILS") == itemAnchor,
                    "Shop and booster rewards must keep the same inventory anchors.");
                Require(!Visible("BoosterOpening") && !Visible("ShopReroll"), "Only the booster decision should be available once paid.");
                Continue();
                WaitFor(() => Visible("SkipBooster"));
                Require(!Visible("BoosterOpening") && choices.All(id => !Find(window, $"BoosterPick{id}").IsEnabled),
                    "Continue must show the four original choices immediately, with picking blocked until there is room.");
                Require(owned.All(id => Find(window, $"Owned{id}").IsEnabled), "Every owned Joker must be available to sell.");
                var blockedCard = Find(window, $"BoosterPick{choices[0]}");
                window.MouseMove(blockedCard.TranslatePoint(new Point(blockedCard.Bounds.Width / 2, blockedCard.Bounds.Height / 2), window)!.Value);
                var blockedEffect = FeatureCatalog.Get(FeatureCategory.Joker, choices[0].ToString()).Description;
                WaitFor(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "BoosterEffect" && t.Text?.Contains(blockedEffect) == true));
                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                if (!skip) Capture(window, "shop-joker-booster");
                if (!skip)
                {
                    Click(Find(window, $"Owned{owned[2]}"));
                    Click(Find(window, $"BoosterSell{owned[2]}"));
                    WaitFor(() => Find(window, $"BoosterPick{choices[2]}").IsEnabled);
                    Require(!Visible("BoosterOpening") && CampaignStore.Load(path)!.Money == 8, "Selling pays $2 without replaying the opening.");
                    Continue();
                    WaitFor(() => Visible("SkipBooster"));
                    Require(choices.All(id => Find(window, $"BoosterPick{id}").IsEnabled), "Reloading after a sale keeps every original choice.");
                }
                var pick = Find(window, $"BoosterPick{choices[2]}");
                if (skip) Click(Find(window, "SkipBooster"));
                else
                {
                    window.Width = 740;
                    Dispatcher.UIThread.RunJobs();
                    var origins = choices.Select(id => Find(window, $"BoosterPick{id}").TranslatePoint(default, window)!.Value).ToArray();
                    Require(origins.All(p => Math.Abs(p.Y - origins[0].Y) < 1) && origins[0].X >= 0
                        && origins[^1].X + origins[1].X - origins[0].X <= window.Width + 10,
                        "The four Joker choices should fit across a single row.");
                    var art = pick.GetVisualDescendants().OfType<Image>().Single();
                    art.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    var point = art.TranslatePoint(new Point(art.Bounds.Width / 2, art.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point);
                    WaitFor(() => art.TranslatePoint(new Point(art.Bounds.Width / 2, art.Bounds.Height / 2), window)!.Value.Y < point.Y - 5);
                    var effect = FeatureCatalog.Get(FeatureCategory.Joker, choices[2].ToString()).Description;
                    Require(art.Width >= 168 && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "BoosterEffect" && t.Text?.Contains(effect) == true),
                        "Hovering the enlarged Joker must show its effect.");
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                }
                WaitFor(() => Visible("ShopReroll"));
                var saved = CampaignStore.Load(path)!;
                Require(openings == (skip ? 0 : 1), "Only tearing a sealed pack with full motion may present the opening animation.");
                var expected = skip ? owned : owned.Where(id => id != owned[2]).Append(choices[2]);
                Require(saved.Money == (skip ? 6 : 8) && saved.Shop!.Booster is { Choices: null, Sold: true }
                    && saved.Resources.Inventory.Jokers.Select(j => j.Id).SequenceEqual(expected),
                    "Skipping preserves a full inventory, and keeping replaces only the Joker sold.");
                var after = File.ReadAllText(path);
                Click(pick);
                Require(File.ReadAllText(path) == after, "A stale pack choice must leave the save untouched.");
            }
            finally { window.Close(); File.Delete(path); }
        }
        Console.WriteLine("PASS  Paid Joker boosters restore all four choices and allow picking one or skipping");
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
