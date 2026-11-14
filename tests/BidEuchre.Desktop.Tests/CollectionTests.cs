using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CollectionTests
{
    private static readonly (string Button, FeatureCategory Category)[] Categories =
    [
        ("CollectionTarot", FeatureCategory.Tarot),
        ("CollectionGumballs", FeatureCategory.Gumball),
        ("CollectionEnhancements", FeatureCategory.Enhancement),
        ("CollectionSigils", FeatureCategory.Sigil),
        ("CollectionJokers", FeatureCategory.Joker)
    ];

    public static void Run()
    {
        MainMenuOpensCollectionAndBackReturns(new Size(1280, 1100));
        foreach (var size in new[] { new Size(1280, 900), new Size(653, 453) })
            AllCategoriesRemainReadableAndReachable(size);
        MainMenuOpensCollectionAndBackReturns(new Size(653, 453));
    }

    // Catches a missing menu route or a back button that leaves the user in the collection.
    private static void MainMenuOpensCollectionAndBackReturns(Size size)
    {
        var motion = StillMotion();
        var window = new Window { Width = size.Width, Height = size.Height };
        var returned = false;
        var settingsOpened = false;
        window.Content = MainMenu.Build(() => { }, () => { },
            () => window.Content = CollectionPage.Build(() => returned = true, motion),
            () => { }, () => { }, () => settingsOpened = true, motion);
        try
        {
            window.Show();
            if (size.Height < 500)
            {
                // Adding Collection must not push the menu's existing last action out of reach.
                Flush();
                var settings = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "MenuSettings");
                for (var attempt = 0; attempt < 20 && BoundsInWindow(settings, window).Bottom > window.Bounds.Height; attempt++)
                {
                    window.MouseWheel(window.Bounds.Center, new Vector(0, -3));
                    Flush();
                }
                Require(BoundsInWindow(settings, window).Bottom <= window.Bounds.Height,
                    "Mouse-wheel scrolling must reveal the last main-menu action in a compact window.");
                Click(window, "MenuSettings");
                Require(settingsOpened, "Settings must remain clickable after adding Collection to the main menu.");
            }
            Click(window, "MenuCollection");
            CheckFeatures(window, FeatureCategory.Joker);
            Click(window, "CollectionBack");
            Require(returned, "Collection Back must invoke the supplied return route.");
            Console.WriteLine($"PASS  Main menu opens the collection on Jokers and Back returns at {size}");
        }
        finally { window.Close(); }
    }

    // Catches catalog omissions, ownership filtering, wrong category wiring, missing art,
    // stale scroll positions, and navigation clipped at the minimum scaled window size.
    private static void AllCategoriesRemainReadableAndReachable(Size size)
    {
        var returned = false;
        var window = new Window
        {
            Width = size.Width, Height = size.Height,
            Content = CollectionPage.Build(() => returned = true, StillMotion())
        };
        try
        {
            window.Show();
            Flush();
            foreach (var name in Categories.Select(category => category.Button).Append("CollectionPlayingCards"))
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
                var labels = button.GetVisualDescendants().OfType<TextBlock>().Where(label => !string.IsNullOrWhiteSpace(label.Text)).ToArray();
                Require(labels.Length > 0 && labels.All(label => label.Foreground is not null && label.Opacity > 0),
                    $"{name} must render readable category labels with a foreground brush.");
            }
            CheckFeatures(window, FeatureCategory.Joker);
            CheckGalleryKeyboard(window);
            ScrollToLastEntry(window, mustScroll: true);
            foreach (var (button, category) in Categories)
            {
                Click(window, button);
                Require(Scroll(window).Offset.Y < 1, $"Opening {category} must start at the top.");
                CheckFeatures(window, category);
                ScrollToLastEntry(window, mustScroll: category == FeatureCategory.Joker);
            }

            Click(window, "CollectionPlayingCards");
            Require(Scroll(window).Offset.Y < 1, "Opening Playing Cards must start at the top.");
            var cards = GameRules.CreateDeck();
            var entries = Entries(window);
            RequireSameIds(entries, cards.Select(card => card.Code), "Playing Cards");
            foreach (var card in cards)
            {
                var entry = entries.Single(control => Equals(control.Tag, card.Code));
                Require(Text(entry).Any(text => text.Contains(card.Suit.ToString(), StringComparison.OrdinalIgnoreCase)),
                    $"Playing card {card.Code} must have a readable suit label.");
                RequireArt(entry, card.Code, allowDrawnBall: false);
            }
            ScrollToLastEntry(window, mustScroll: true);
            Click(window, "CollectionJokers");
            Require(Scroll(window).Offset.Y < 1, "Returning to Jokers must reset its previous scroll position.");
            CheckFeatures(window, FeatureCategory.Joker);
            Click(window, "CollectionBack");
            Require(returned, $"Back must remain reachable at {size}.");
            Console.WriteLine($"PASS  All six collection categories expose complete art and rules, scroll by pointer and keyboard, and remain reachable at {size}");
        }
        finally { window.Close(); }
    }

    // Catches menu navigation stealing the gallery's scroll keys or losing its focus.
    private static void CheckGalleryKeyboard(Window window)
    {
        var scroll = Scroll(window);
        Require(scroll.Focus(NavigationMethod.Tab), "The collection gallery must accept keyboard focus.");
        void Press(Key key)
        {
            var physical = key == Key.Down ? PhysicalKey.ArrowDown : Enum.Parse<PhysicalKey>(key.ToString());
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Flush();
            Require(scroll.IsFocused, $"{key} must keep keyboard focus in the collection gallery.");
        }
        Press(Key.PageDown);
        Require(scroll.Offset.Y > 0, "Page Down must advance the collection gallery.");
        Press(Key.End);
        Require(Math.Abs(scroll.Offset.Y - (scroll.Extent.Height - scroll.Viewport.Height)) < 1,
            "End must reveal the end of the collection gallery.");
        Press(Key.Home);
        Require(scroll.Offset.Y < 1, "Home must return to the start of the collection gallery.");
        Press(Key.Down);
        Require(scroll.Offset.Y > 0, "Down must scroll the gallery rather than changing the selected category.");
        Press(Key.Home);
    }

    private static void CheckFeatures(Window window, FeatureCategory category)
    {
        var expected = FeatureCatalog.All.Where(feature => feature.Category == category).ToArray();
        var entries = Entries(window);
        RequireSameIds(entries, expected.Select(feature => feature.Id), category.ToString());
        foreach (var feature in expected)
        {
            var entry = entries.Single(control => Equals(control.Tag, feature.Id));
            var text = Text(entry);
            Require(text.Any(value => value.Contains(feature.Name, StringComparison.OrdinalIgnoreCase)),
                $"{feature.Id} must display its catalog name.");
            Require(text.Any(value => value.Contains(feature.Description, StringComparison.Ordinal)),
                $"{feature.Id} must display its full effect description.");
            RequireArt(entry, feature.Id, allowDrawnBall: category == FeatureCategory.Gumball);
        }
    }

    private static void RequireSameIds(Control[] entries, IEnumerable<string> expected, string category)
    {
        var ids = entries.Select(entry => entry.Tag as string).ToArray();
        Require(ids.Length == ids.Distinct().Count() && ids.ToHashSet().SetEquals(expected),
            $"{category} must show every catalog entry exactly once, with no ownership filtering or other-category entries.");
    }

    private static void RequireArt(Control entry, string id, bool allowDrawnBall)
    {
        var descendants = entry.GetVisualDescendants().OfType<Control>().ToArray();
        var hasImage = descendants.OfType<Image>().Any(image => image.IsEffectivelyVisible &&
            image.Source is { Size.Width: > 0, Size.Height: > 0 } && image.Bounds.Width > 0 && image.Bounds.Height > 0);
        var hasDrawnBall = allowDrawnBall && descendants.OfType<Ellipse>().Any(ellipse =>
            ellipse.IsEffectivelyVisible && ellipse.Fill is not null && ellipse.Bounds.Width > 0 && ellipse.Bounds.Height > 0);
        Require(hasImage || hasDrawnBall, $"{id} must display its artwork.");
    }

    private static void ScrollToLastEntry(Window window, bool mustScroll)
    {
        var scroll = Scroll(window);
        var viewport = BoundsInWindow(scroll, window);
        Require(viewport.Width > 100 && viewport.Height > 100 &&
            viewport.Left >= 0 && viewport.Top >= 0 && viewport.Right <= window.Bounds.Width + 1 &&
            viewport.Bottom <= window.Bounds.Height + 1, "The collection gallery must fit inside the window.");
        foreach (var entry in Entries(window))
        {
            var bounds = BoundsInWindow(entry, window);
            Require(bounds.Left >= viewport.Left - 1 && bounds.Right <= viewport.Right + 1,
                $"{entry.Tag} must fit horizontally inside the gallery.");
        }
        var before = scroll.Offset.Y;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var previous = scroll.Offset.Y;
            window.MouseWheel(viewport.Center, new Vector(0, -5));
            Flush();
            if (Math.Abs(scroll.Offset.Y - previous) < .1) break;
        }
        Require(!mustScroll || scroll.Offset.Y > before, "The mouse wheel must scroll the long collection gallery.");
        var last = BoundsInWindow(Entries(window)[^1], window);
        Require(last.Bottom <= viewport.Bottom + 1 && last.Bottom > viewport.Top,
            "Mouse-wheel scrolling must reveal the final entry, including its description.");
    }

    private static void Click(Window window, string name)
    {
        Flush();
        var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == name);
        button.BringIntoView();
        Flush();
        var bounds = BoundsInWindow(button, window);
        Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0 &&
            bounds.Right <= window.Bounds.Width + 1 && bounds.Bottom <= window.Bounds.Height + 1,
            $"{name} must be reachable inside the window.");
        // Headless pointer dispatch flushes compositor hit-test data after BringIntoView.
        // Assert the resulting route/category in the caller, not a stale pre-dispatch hit test.
        window.MouseDown(bounds.Center, MouseButton.Left);
        window.MouseUp(bounds.Center, MouseButton.Left);
        Flush();
    }

    private static Control[] Entries(Window window) => window.GetVisualDescendants().OfType<Control>()
        .Where(control => control.Name == "CollectionEntry").ToArray();
    private static string[] Text(Control entry) => entry.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToArray();
    private static ScrollViewer Scroll(Window window) => window.GetVisualDescendants().OfType<ScrollViewer>()
        .Single(control => control.Name == "CollectionScroll");
    private static Rect BoundsInWindow(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    private static PixelMotion StillMotion() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
