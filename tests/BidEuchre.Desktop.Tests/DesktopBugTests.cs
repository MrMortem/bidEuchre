using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class DesktopBugTests
{
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (var test in new Action[] { SettingsSaveFailure, MenuReachability, ShopReachability, CardFocus })
            try { test(); } catch (Exception error) { Console.WriteLine($"FAIL  {test.Method.Name}: {error.Message}"); failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        if (bitmap is null) throw new Exception("Skia must render the desktop preview.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    private static void Activate(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Rect Bounds(Control control, Window window) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    private static void Visible(Control control, Window window)
    {
        Flush();
        var rect = Bounds(control, window);
        Require(rect.Left >= -.1 && rect.Right <= window.ClientSize.Width + .1 && rect.Top >= -.1 && rect.Bottom <= window.ClientSize.Height + .1,
            $"{control.Name} must be fully reachable; bounds {rect}, client {window.ClientSize}.");
    }

    private static void SettingsSaveFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"settings-ui-{Guid.NewGuid():N}");
        var old = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        Directory.CreateDirectory(root);
        new GamePreferences(MotionEnabled: false).Save(Path.Combine(root, "BidEuchre", "preferences.json"));
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", root);
        var window = new MainWindow();
        try
        {
            window.Show(); Flush(); Activate(Find(window, "MenuSettings")); Flush();
            var directory = Path.Combine(root, "BidEuchre");
            Directory.Delete(directory, true); File.WriteAllText(directory, "block preference directory");
            var scale = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SettingsUiScale");
            scale.SelectedIndex = 1; Flush();
            var error = window.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Text?.Contains("could not be saved") == true);
            Require(error is { IsVisible: true } && error.IsAttachedToVisualTree(), "Failed preference saves must show an error on the active Settings page.");
            Capture(window, "settings-save-failure");
            Visible(error!, window);
            Require(scale.SelectedIndex == 1, "Failed saves must retain the selected scale for this session.");
            File.Delete(directory); Directory.CreateDirectory(directory);
            scale.SelectedIndex = 2; Flush();
            Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text?.Contains("could not be saved") == true), "A subsequent successful save must clear the Settings error.");
            Require(GamePreferences.Load(Path.Combine(directory, "preferences.json")).UiScale == 1.5, "Retry must persist the current selection.");
            Console.WriteLine("PASS  Settings reports failed saves in the active page, retains values and clears errors after retry");
        }
        finally { window.Close(); Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", old); Directory.Delete(root, true); }
    }

    private static IEnumerable<Size> LogicalViewports() => GamePreferences.WindowSizes.Select(s => new Size(s.Width, s.Height)).Append(new Size(980, 680))
        .SelectMany(size => GamePreferences.UiScales.Select(requested =>
        {
            var fit = Math.Min(size.Width / 980, size.Height / 680);
            var effective = GamePreferences.UiScales.Where(s => s <= requested && s <= fit + .001).DefaultIfEmpty(1).Max();
            return new Size(size.Width / effective, size.Height / effective);
        })).Distinct();

    private static void MenuReachability()
    {
        foreach (var size in LogicalViewports())
        {
            var recovered = false;
            var window = new Window { Width = size.Width, Height = size.Height,
                Content = MainMenu.Build(() => { }, () => { }, () => { }, () => { }, () => { }, () => recovered = true, Still()) };
            try
            {
                window.Show(); Flush();
                foreach (var button in window.GetVisualDescendants().OfType<Button>().ToArray())
                {
                    button.Focus(NavigationMethod.Tab); Flush(); Visible(button, window);
                }
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(); scroll.Offset = default; Flush();
                var settings = Find(window, "MenuSettings");
                for (var i = 0; i < 20 && Bounds(settings, window).Bottom > window.ClientSize.Height; i++) { window.MouseWheel(new Rect(window.ClientSize).Center, new Vector(0, -3)); Flush(); }
                Visible(settings, window);
                var p = settings.TranslatePoint(new Rect(settings.Bounds.Size).Center, window)!.Value;
                window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
                Require(recovered, "Pointer must recover Settings at every offered size/scale and minimum.");
            }
            finally { window.Close(); }
        }
        Console.WriteLine("PASS  Menu entries scroll into keyboard and pointer reach at every effective size/scale");
    }

    // Three Joker offers, the machine and a completely filled owned shelf reproduce the widest shop.
    private static CampaignRun RunWithItems()
    {
        var jokerIds = Enum.GetValues<JokerId>();
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        {
            Shop = new(0,
            [
                new("j", FeatureCategory.Joker, jokerIds[0].ToString(), 3),
                new("j2", FeatureCategory.Joker, jokerIds[1].ToString(), 3),
                new("j3", FeatureCategory.Joker, jokerIds[2].ToString(), 3),
                new("g", FeatureCategory.Gumball, Enum.GetNames<GumballId>()[0], 3),
                new("s", FeatureCategory.Sigil, "Fish", 3),
                new("t", FeatureCategory.Tarot, "Prospector", 3)
            ])
        };
        return run with
        {
            Resources = run.Resources with
            {
                Inventory = run.Resources.Inventory with
                {
                    Tarot = [TarotId.Prospector, TarotId.Prospector], Sigils = [SigilId.Fish, SigilId.Fish],
                    Jokers = jokerIds.Take(5).Select(id => new JokerState(id)).ToArray()
                }
            }
        };
    }

    private static ShopActions Actions() => new(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { }, () => { }, () => { })
    { Pull = _ => null, Refresh = () => { } };

    private static void ShopReachability()
    {
        foreach (var size in LogicalViewports())
        {
            var run = RunWithItems();
            var window = new Window { Width = size.Width, Height = size.Height, Content = CampaignShopScreen.Build(run, Actions(), Still()) };
            try
            {
                window.Show(); Flush();
                foreach (var name in new[] { "Offerj", "Offers", "Offert", $"Owned{run.Resources.Inventory.Jokers[0].Id}", "OwnedTarot0", "OwnedSigil0", "OwnedTarot1", "OwnedSigil1" })
                {
                    var card = Find(window, name); card.BringIntoView(); Flush(); Activate(card); Flush();
                    foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Buy") == true || b.Name?.StartsWith("Use") == true || b.Name?.StartsWith("Sell") == true || b.Name?.StartsWith("Discard") == true).ToArray())
                    { button.BringIntoView(); Flush(); Visible(button, window); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("PASS  Shop purchase and owned-card controls are completely reachable at every effective size/scale");
    }

    private static void CardFocus()
    {
        var run = RunWithItems();
        foreach (var content in new[] { CampaignShopScreen.Build(run, Actions(), Still()), CampaignDeckPicker.Build(run.Resources.Deck, 1, _ => { }, () => { }, Still(), "Choose"), CampaignAnteScreen.Build(run, () => { }, () => { }, null, Still()) })
        {
            var window = new Window { Width = 980, Height = 680, Content = content };
            try
            {
                window.Show(); Flush();
                var cards = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name is { } n && (n.StartsWith("Offer") || n.StartsWith("Owned") || n.StartsWith("DeckCard") || n.StartsWith("AnteItem"))).ToArray();
                cards[0].Focus(NavigationMethod.Tab); Flush();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t"); Flush();
                Require(cards[1].IsFocused, "Tab must traverse to the next campaign card.");
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t"); Flush();
                Require(cards[0].IsFocused, "Shift+Tab must traverse back to the previous campaign card.");
                // Start elsewhere so even the first snapshot is of an unfocused card.
                window.GetVisualDescendants().OfType<Button>().Last(b => !cards.Contains(b)).Focus(); Flush();
                foreach (var card in cards.Reverse())
                {
                    var borders = card.GetVisualDescendants().OfType<Border>().ToArray();
                    var before = borders.Select(b => (b.BorderBrush, b.BorderThickness)).ToArray();
                    card.Focus(NavigationMethod.Tab); Flush();
                    Require(card.IsFocused, $"{card.Name} must accept keyboard focus.");
                    Require(borders.Where((b, i) => !Equals(b.BorderBrush, before[i].BorderBrush) || b.BorderThickness != before[i].BorderThickness).Any(), $"{card.Name} needs a visible focus border independently of selection with Motion off.");
                    Visible(card, window);
                    Capture(window, "focus-" + card.Name);
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Flush();
                    if (card.Name!.StartsWith("DeckCard")) Require(Find(window, "ConfirmDeckChoice").IsEnabled, "Enter must select the focused deck target.");
                    if (card.Name.StartsWith("Offer")) Require(window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "Buy" + card.Name[5..]), "Enter must select the focused offer.");
                    if (card.Name.StartsWith("Owned")) Require(window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "Sell" + card.Name[5..] || b.Name == "Use" + card.Name[5..]), "Enter must select the focused owned item.");
                    if (card.Name.StartsWith("AnteItem")) { Require(card.Flyout!.IsOpen, "Enter must open the focused inventory card."); card.Flyout.Hide(); }
                    card.Focus(NavigationMethod.Tab); Flush();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Flush();
                    if (card.Name.StartsWith("DeckCard")) Require(!Find(window, "ConfirmDeckChoice").IsEnabled, "Space must toggle the selected deck target off.");
                    if (card.Name.StartsWith("AnteItem")) { Require(card.Flyout!.IsOpen, "Space must open the focused inventory card."); card.Flyout.Hide(); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("PASS  Campaign cards visibly focus, scroll into view and activate from the keyboard with Motion off");
    }
}
