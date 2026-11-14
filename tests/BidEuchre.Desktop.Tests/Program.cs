using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

AppBuilder.Configure<Application>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .AfterSetup(builder => builder.Instance!.Styles.Add(new FluentTheme()))
    .SetupWithoutStarting();

EnhancementBackgroundTests.Run();

var motion = new PixelMotion();
motion.SetEnabled(false);
CardId[]? selected = null;
var deck = CampaignCards.CreateDeck().ToArray();
var window = new Window
{
    Width = 1280, Height = 1100,
    Content = CampaignDeckPicker.Build(deck, 1, ids => selected = ids, () => { }, motion, "Choose a Sigil target")
};
try
{
    window.Show();
    Dispatcher.UIThread.RunJobs();
    Button Find(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    void Click(Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
    var confirm = Find("ConfirmDeckChoice");
    if (confirm.IsEnabled) throw new Exception("Confirmation must require a card.");
    Click(Find("DeckCard5"));
    if (!confirm.IsEnabled) throw new Exception("Clicking the card face must enable confirmation.");
    Click(Find("DeckCard7"));
    Click(confirm);
    if (selected is not { Length: 1 } || selected[0] != deck[7].Id)
        throw new Exception("Confirmation must return the most recently selected physical card.");
    Console.WriteLine("PASS  Deck picker accepts card-face clicks and confirms the selected card");
}
finally { window.Close(); }

foreach (var storeFirst in new[] { false, true })
{
    var path = Path.Combine(Path.GetTempPath(), $"sigil-ui-{Guid.NewGuid():N}.json");
    var shop = new Window { Width = 1280, Height = 1100 };
    try
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        { Shop = new(0, [new("s", FeatureCategory.Sigil, "Fish", 3)]) };
        if (!CampaignStore.Save(run, path)) throw new Exception("Could not prepare disposable save.");
        var flow = new CampaignFlow(path, screen => shop.Content = screen, _ => Task.CompletedTask, () => { }, motion);
        flow.Open();
        shop.Show();
        void Click(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var button = shop.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            button.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shop)!.Value;
            shop.MouseDown(point, MouseButton.Left);
            shop.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Click("CampaignContinue");
        Click("Offers");
        if (storeFirst)
        {
            Click("Buys");
            if (flow.Run!.Resources.Inventory.Sigils.Length != 1 || flow.Run.Money != 27)
                throw new Exception("Buy must store the Sigil and charge once.");
            Click("OwnedSigil0");
            Click("UseSigil0");
        }
        else Click("BuyUses");
        var revision = flow.Run!.Resources.Revision;
        Click("DeckCard5");
        Click("CancelDeckChoice");
        if (flow.Run.Resources.Revision != revision || flow.Run.Money != (storeFirst ? 27 : 30))
            throw new Exception("Canceling card selection must leave the run unchanged.");
        Click(storeFirst ? "OwnedSigil0" : "Offers");
        Click(storeFirst ? "UseSigil0" : "BuyUses");
        Click("DeckCard7");
        Click("ConfirmDeckChoice");
        var saved = CampaignStore.Load(path)!;
        if (saved.Money != 27 || saved.Resources.Deck[7].Sigil != SigilId.Fish || saved.Resources.Inventory.Sigils.Length != 0)
            throw new Exception("The chosen card must receive the Sigil, saved with exactly one charge.");
        var note = shop.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CampaignNote").Text;
        if (note is null || !note.Contains("now carries Fish")) throw new Exception("Shop must report the applied Sigil.");
        Console.WriteLine($"PASS  Sigil {(storeFirst ? "Buy then Use" : "Buy & Use")} supports cancel, target selection, confirmation and saving");
    }
    finally { shop.Close(); File.Delete(path); }
}

// Stored Tarots and Sigils share one shelf, and are played from the shop and from the Ante screen.
{
    var path = Path.Combine(Path.GetTempPath(), $"items-ui-{Guid.NewGuid():N}.json");
    var screen = new Window { Width = 1280, Height = 1100 };
    try
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop);
        run = run with { Resources = run.Resources with { Wallets = [30, 0, 0, 0], Inventory = run.Resources.Inventory with { Tarot = [TarotId.Prospector], Sigils = [SigilId.Fish] } } };
        if (!CampaignStore.Save(run, path)) throw new Exception("Could not prepare disposable save.");
        var flow = new CampaignFlow(path, content => screen.Content = content, _ => Task.CompletedTask, () => { }, motion);
        flow.Open();
        screen.Show();
        Button Find(string name) { Dispatcher.UIThread.RunJobs(); return screen.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name); }
        void Click(string name)
        {
            var button = Find(name);
            button.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), screen)!.Value;
            screen.MouseDown(point, MouseButton.Left);
            screen.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        bool Shows(string text) => screen.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
        Click("CampaignContinue");
        if (!Shows("YOUR CARDS 2/4")) throw new Exception("The shop must show Tarots and Sigils on one shelf.");
        Click("OwnedTarot0");
        Click("UseTarot0");
        Click("DeckCard5");
        Click("ConfirmDeckChoice");
        if (flow.Run!.Resources.Deck[5].Enhancement != EnhancementId.Golden || flow.Run.Money != 30 || flow.Run.Resources.Inventory.Tarot.Length != 0)
            throw new Exception("A stored Tarot must be playable from the shop at no charge.");
        if (!Shows("YOUR CARDS 1/4")) throw new Exception("The shelf must show the card as used.");
        Click("CampaignNextMatch");
        if (flow.Run.Phase != CampaignPhase.Intro || !Shows("YOUR CARDS 1/4")) throw new Exception("The Ante screen must show the stored cards.");
        // The card's buttons live in a flyout, outside the window's own tree.
        var menu = (Panel)((Avalonia.Controls.Flyout)Find("AnteItemSigil0").Flyout!).Content!;
        menu.Children.OfType<Button>().Single(b => b.Name == "AnteUseSigil0").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Click("DeckCard7");
        Click("ConfirmDeckChoice");
        var saved = CampaignStore.Load(path)!;
        if (saved.Phase != CampaignPhase.Intro || saved.Resources.Deck[7].Sigil != SigilId.Fish || saved.Resources.Inventory.ItemCount != 0)
            throw new Exception("A stored Sigil must be playable from the Ante screen, and saved.");
        if (!Shows("YOUR CARDS 0/4") || Find("CampaignStartMatch") is null) throw new Exception("Using a card must return to the Ante screen.");
        Console.WriteLine("PASS  Stored Tarots and Sigils share a shelf and are played from the shop and the Ante screen");
    }
    finally { screen.Close(); File.Delete(path); }
}

// The gumball machine sells one blind pull, with and without its animation.
foreach (var animated in new[] { false, true })
{
    var path = Path.Combine(Path.GetTempPath(), $"gumball-ui-{Guid.NewGuid():N}.json");
    var shop = new Window { Width = 1280, Height = 1100 };
    var moving = new PixelMotion();
    moving.SetEnabled(animated);
    try
    {
        if (!CampaignStore.Save(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop), path)) throw new Exception("Could not prepare disposable save.");
        var flow = new CampaignFlow(path, screen => shop.Content = screen, _ => Task.CompletedTask, () => { }, moving);
        flow.Open();
        shop.Show();
        void Click(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var button = shop.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            button.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shop)!.Value;
            shop.MouseDown(point, MouseButton.Left);
            shop.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        string? Text(string name) => shop.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == name)?.Text;
        // With motion on, screens and the machine take real time to finish.
        long Await(Func<bool> done)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var limit = new CancellationTokenSource(8000);
            // Timers only fire inside a running loop, so run one until the screen has caught up.
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            poll.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
            poll.Start();
            Dispatcher.UIThread.MainLoop(limit.Token);
            poll.Stop();
            return clock.ElapsedMilliseconds;
        }
        Click("CampaignContinue");
        Await(() => Text("GumballTag") is not null);
        if (Text("GumballTag") != "$3") throw new Exception("The machine must show its price.");
        if (shop.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text is "Chaos" or "Poker" or "Duelist" or "Telepath"))
            throw new Exception("The machine must not name a bid before the pull.");
        Click("GumballMachine");
        Click("PullGumball");
        // Paid and saved at once, before any gumball has rolled out.
        var saved = CampaignStore.Load(path)!;
        if (saved.Money != 27 || saved.Resources.Inventory.Unlocks.Length != 1) throw new Exception("A pull must charge once and save its bid immediately.");
        if (animated)
        {
            if (Text("CampaignNote") is not null) throw new Exception("The result must wait for the animation.");
            if (Await(() => Text("CampaignNote") is not null) < 1500) throw new Exception("The animation must play before the result.");
        }
        Dispatcher.UIThread.RunJobs();
        var name = FeatureCatalog.Get(FeatureCategory.Gumball, saved.Resources.Inventory.Unlocks[0].ToString()).Name;
        if (Text("CampaignNote") != $"The gumball unlocked the {name} bid!") throw new Exception("The shop must report the bid that came out.");
        if (Text("GumballTag") != "SOLD") throw new Exception("The machine must read SOLD after its pull.");
        if (shop.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "GumballMachine")) throw new Exception("A sold machine cannot be pressed.");
        if (!shop.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "GUMBALLS 1/9")) throw new Exception("The shelf must count the gumball.");
        if (flow.Run!.Money != 27 || flow.Run.Resources.Inventory.Unlocks.Length != 1) throw new Exception("A pull must not repeat.");
        Console.WriteLine($"PASS  Gumball machine sells one blind pull and reports its bid ({(animated ? "animated" : "motion off")})");
    }
    finally { shop.Close(); File.Delete(path); }
}

foreach (var (match, title, threshold) in new[] { (5, "ANTE 2", "±28 POINTS"), (16, "ANTE 6", "±44 POINTS") })
{
    var path = Path.Combine(Path.GetTempPath(), $"ante-ui-{Guid.NewGuid():N}.json");
    var ante = new Window { Width = 1280, Height = 1100 };
    try
    {
        if (!CampaignStore.Save(new CampaignRun(Match: match, Money: 14), path)) throw new Exception("Could not prepare disposable save.");
        var started = 0;
        var flow = new CampaignFlow(path, screen => ante.Content = screen, _ => { started++; return Task.CompletedTask; }, () => { }, motion);
        flow.Open();
        ante.Show();
        void Click(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var button = ante.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), ante)!.Value;
            ante.MouseDown(point, MouseButton.Left);
            ante.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Click("CampaignContinue");
        var texts = ante.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        string Status(int slot) => ante.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == $"AnteStatus{slot}").Text!;
        var current = (match - 1) % 3 + 1;
        foreach (var expected in new[] { title, threshold, "8 HANDS", "REWARD $5+", "REWARD $6+", "REWARD $8+", "LOSE ♥1", "LOSE ♥2", "$14", "YOUR JOKERS 0/5" })
            if (!texts.Contains(expected)) throw new Exception($"The Ante screen must show \"{expected}\".");
        if (texts.Count(t => t == threshold) != 3) throw new Exception("Every match panel must show the threshold.");
        if (current == 2 && (Status(1) != "PLAYED" || Status(3) != "UPCOMING")) throw new Exception("Panels around the current match must read PLAYED and UPCOMING.");
        if (current == 1 && (Status(2) != "UPCOMING" || Status(3) != "UPCOMING")) throw new Exception("Later panels must read UPCOMING.");
        var start = ante.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CampaignStartMatch");
        if (start.GetVisualAncestors().OfType<Border>().Count(b => b.Name == $"AntePanel{current}") != 1)
            throw new Exception("Only the current match's panel may hold the Start button.");
        if (!start.IsFocused) throw new Exception("The Start button must be focused on arrival.");
        Click("CampaignStartMatch");
        if (started != 1 || flow.Run!.Phase != CampaignPhase.InMatch) throw new Exception("Start Match must begin the match.");
        Console.WriteLine($"PASS  Ante screen shows three match panels and starts the current one ({title})");
    }
    finally { ante.Close(); File.Delete(path); }
}
