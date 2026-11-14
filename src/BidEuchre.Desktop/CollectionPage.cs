using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>A read-only reference to the complete game catalog, independent of any campaign.</summary>
internal static class CollectionPage
{
    private sealed record Category(string Key, string Name, string About, FeatureCategory? Feature);

    private static readonly Category[] Categories =
    [
        new("Jokers", "Jokers", "Passive effects that work while you hold the Joker.", FeatureCategory.Joker),
        new("Tarot", "Tarot cards", "One-time effects that change your cards or your run.", FeatureCategory.Tarot),
        new("Gumballs", "Gumballs", "Unlock new ways to bid during a campaign.", FeatureCategory.Gumball),
        new("PlayingCards", "Playing cards", "The 24-card deck: 9 through Ace in all four suits.", null),
        new("Enhancements", "Card enhancements", "Permanent card effects, previewed on the Ace of Spades.", FeatureCategory.Enhancement),
        new("Sigils", "Sigils", "Place a Sigil on a card to give it a lasting effect.", FeatureCategory.Sigil)
    ];

    public static Control Build(Action back, PixelMotion motion)
    {
        var gallery = new WrapPanel();
        var scroll = new ScrollViewer
        {
            Name = "CollectionScroll", Content = gallery, Focusable = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(scroll, "Collection items");
        // Give keyboard scrolling its own focus target, without menu Up/Down taking it over.
        scroll.PointerPressed += (_, _) => scroll.Focus();
        scroll.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            var offset = args.Key switch
            {
                Key.Up => scroll.Offset.Y - 48,
                Key.Down => scroll.Offset.Y + 48,
                Key.PageUp => scroll.Offset.Y - scroll.Viewport.Height,
                Key.PageDown => scroll.Offset.Y + scroll.Viewport.Height,
                Key.Home => 0,
                Key.End => scroll.Extent.Height,
                _ => (double?)null
            };
            if (offset is null) return;
            scroll.Offset = new Vector(0, Math.Clamp(offset.Value, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            args.Handled = true;
        }, RoutingStrategies.Tunnel);

        var heading = Label("", Gold, 24);
        heading.Name = "CollectionCategoryTitle";
        var about = Label("", Muted, 13, body: true);
        var categories = new StackPanel { Spacing = 8 };
        var buttons = new List<Button>();

        void Select(Category category)
        {
            gallery.Children.Clear();
            if (category.Feature is { } feature)
                foreach (var item in FeatureCatalog.All.Where(item => item.Category == feature))
                    gallery.Children.Add(FeatureTile(item));
            else
                foreach (var card in GameRules.CreateDeck())
                    gallery.Children.Add(Tile(card.Code, $"{RankName(card.Rank)} of {card.Suit}",
                        card.Rank == Rank.Jack
                            ? "The Jack of trump is the right bower. The other Jack of the same color is the left bower."
                            : $"{Card.DisplayRank(card.Rank)} · {card.Suit}", CardPreview(card), Green));

            heading.Text = $"{category.Name.ToUpperInvariant()} · {gallery.Children.Count}";
            about.Text = category.About;
            scroll.Offset = default;
            foreach (var button in buttons)
            {
                var selected = Equals(button.Tag, category);
                button.Background = selected ? PanelRaised : MainWindow.Panel;
                button.Foreground = selected ? Gold : Ink;
                AutomationProperties.SetHelpText(button, selected ? "Selected category" : "Show category");
            }
        }

        foreach (var category in Categories)
        {
            var count = category.Feature is { } feature
                ? FeatureCatalog.All.Count(item => item.Category == feature) : GameRules.CreateDeck().Count;
            var button = MenuParts.Button(category.Name, false, motion, $"Collection{category.Key}",
                () => Select(category), stay: true, width: 184);
            button.Tag = category;
            button.Padding = new Thickness(12, 12);
            var label = Label(category.Name, null, 13);
            var badge = Label(count.ToString(), Muted, 12);
            badge.VerticalAlignment = VerticalAlignment.Center;
            button.Content = new Grid
            {
                ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6,
                Children = { label, Place(badge, column: 1) }
            };
            AutomationProperties.SetName(button, $"{category.Name}, {count} items");
            buttons.Add(button);
            categories.Children.Add(button);
        }
        Select(Categories[0]);

        var menu = MenuParts.Button("← Menu", false, motion, "CollectionBack", back, width: 150);
        menu.HorizontalAlignment = HorizontalAlignment.Right;
        var title = MenuParts.Title(motion, "COLLECTION", 38);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid
        {
            ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12,
            Children = { title, Place(menu, column: 1) }
        };
        var details = new Grid
        {
            RowDefinitions = new("Auto,Auto,*"), RowSpacing = 10,
            Children = { heading, Place(about, row: 1), Place(scroll, row: 2) }
        };
        var body = new Grid
        {
            ColumnDefinitions = new("200,*"), ColumnSpacing = 16,
            Children =
            {
                new ScrollViewer
                {
                    Content = categories, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Place(details, column: 1)
            }
        };
        var content = new Grid
        {
            Name = "CollectionPage", Margin = new Thickness(24), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 20,
            Children =
            {
                header, Place(body, row: 1),
                Place(Label("ALL ITEMS · SCROLL TO EXPLORE · ESC TO RETURN", Muted, 10), row: 2)
            }
        };
        content.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            args.Handled = true;
        };
        return MenuParts.Scene(motion, content);
    }

    private static Control FeatureTile(FeatureDefinition item)
    {
        Control art = item.Category switch
        {
            FeatureCategory.Enhancement => CardPreview(new Card(Suit.Spades, Rank.Ace), Enum.Parse<EnhancementId>(item.Key)),
            FeatureCategory.Gumball => GumballMachine.Ball(Enum.Parse<GumballId>(item.Key), 120),
            _ => PixelArt.CampaignIcon(item, 120)
        };
        return Tile(item.Id, item.Name, item.Description, art, CampaignShopScreen.Ink(item.Category));
    }

    private static Border CardPreview(Card card, EnhancementId? enhancement = null) => new()
    {
        Width = 88, Height = 124, ClipToBounds = true, CornerRadius = new CornerRadius(5),
        HorizontalAlignment = HorizontalAlignment.Center, Child = PixelArt.CardContent(card, enhancement)
    };

    private static Border Tile(string id, string name, string description, Control art, IBrush accent)
    {
        var tile = new Border
        {
            Name = "CollectionEntry", Tag = id, Width = 240, MinHeight = 300, Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(16), Background = MainWindow.Panel,
            BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Border { Height = 124, Child = art },
                    Label(name, accent, 16),
                    Label(description, Ink, 14, body: true)
                }
            }
        };
        AutomationProperties.SetName(tile, name);
        return tile;
    }

    private static TextBlock Label(string text, IBrush? brush, double size, bool body = false)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size,
            FontFamily = body ? FontFamily.Default : PixelArt.Font,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = body ? 20 : double.NaN
        };
        // Category labels inherit their button's selected/focused foreground.
        if (brush is not null) label.Foreground = brush;
        return label;
    }

    private static string RankName(Rank rank) => rank switch
    {
        Rank.Nine => "9", Rank.Ten => "10", _ => rank.ToString()
    };
}
