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
