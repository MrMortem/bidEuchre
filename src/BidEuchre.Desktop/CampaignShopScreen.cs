using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;
namespace BidEuchre.Desktop;

/// <summary>What the shop screen can ask of the run. Each one redraws the shop when it is done.</summary>
internal sealed record ShopActions(Action<string> Buy, Action<string> BuyAndUse, Action<JokerId> Sell,
    Action<int> Discard, Action<int> UseSigil, Action Inspect, Action Next);

/// <summary>The shop between matches. Offers are drawn as cards: Jokers on the top row, then a Sigil,
/// a Tarot and a Planet. Pressing a card selects it and puts its text and its buttons in the side panel.</summary>
internal static class CampaignShopScreen
{
    private const double CardWidth = 148, CardHeight = 196, Art = 128, SmallCard = 76, Lift = 8;
    private static readonly IBrush Danger = Brush.Parse("#FF617B");
    private static readonly IBrush TarotInk = Brush.Parse("#C792FF");
    private static readonly IBrush SigilInk = Brush.Parse("#7BE0A0");

    public static Control Build(CampaignRun run, ShopActions actions, PixelMotion motion, string? note = null)
    {
        var inventory = run.Resources.Inventory;
        var detail = new Border
        {
            Width = 300, MinHeight = CardHeight * 2 + 14, Padding = new Thickness(16), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Lift, 0, 0),
            Background = Brush.Parse("#CC100B28"), BorderBrush = MainWindow.Line, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(2)
        };
        var cards = new List<(Button Button, Border Face, IBrush Idle)>();

        // One card is selected at a time, across the offers and the player's own cards.
        void Select(Button? chosen, Control content)
        {
            foreach (var (button, face, rest) in cards)
            {
                var selected = ReferenceEquals(button, chosen);
                face.BorderBrush = selected ? Gold : rest;
                ((TranslateTransform)face.RenderTransform!).Y = selected && motion.Enabled ? -Lift : 0;
            }
            detail.Child = content;
        }
        Button Selectable(Border face, IBrush rest, string name, string label, Func<Control> describe)
        {
            face.RenderTransform = new TranslateTransform
            {
                Transitions =
                [
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() }
                ]
            };
            var button = new Button
            {
                Name = name, Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), FocusAdorner = null,
                // The room above the card is what it rises into when selected.
                Template = new FuncControlTemplate<Button>((_, _) => new Panel { Children = { face } })
            };
            face.Margin = new Thickness(0, Lift, 0, 0);
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Select(button, describe());
            cards.Add((button, face, rest));
            return button;
        }

        Control Offer(ShopOffer offer)
        {
            var definition = FeatureCatalog.Get(offer.Category, offer.FeatureId);
            var ink = Ink(offer.Category);
            var face = Face(definition, ink, offer.Sold ? "SOLD" : $"${offer.Price}");
            if (offer.Sold) { face.Opacity = .4; face.Margin = new Thickness(0, Lift, 0, 0); return face; }
            return Selectable(face, ink, $"Offer{offer.Id}", $"{offer.Category}: {definition.Name}, ${offer.Price}",
                () => OfferDetail(run, offer, definition, actions, motion));
        }

        var jokers = Row(14);
        var others = Row(14);
        foreach (var offer in run.Shop!.Offers.Where(o => o.Category == FeatureCategory.Joker)) jokers.Children.Add(Offer(offer));
        foreach (var category in new[] { FeatureCategory.Sigil, FeatureCategory.Tarot, FeatureCategory.Planet })
            foreach (var offer in run.Shop.Offers.Where(o => o.Category == category)) others.Children.Add(Offer(offer));
        var offers = new StackPanel { Spacing = 14 - Lift, VerticalAlignment = VerticalAlignment.Top, Children = { jokers, others } };

        var ownedJokers = Row(8);
        foreach (var joker in inventory.Jokers)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString());
            ownedJokers.Children.Add(Selectable(Small(definition, Gold), Gold, $"Owned{joker.Id}", "Your Joker: " + definition.Name, () => Detail(
                "YOUR JOKER", Gold, definition.Name, joker.Value == 0 ? definition.Description : $"{definition.Description}\nCurrent value: {joker.Value}", null,
                MenuParts.Button("Sell +$2", false, motion, $"Sell{joker.Id}", () => actions.Sell(joker.Id), true, 264))));
        }
        while (ownedJokers.Children.Count < CampaignTableControls.JokerSlots) ownedJokers.Children.Add(EmptySlot());
        var ownedTarot = Row(8);
        for (var index = 0; index < inventory.Tarot.Length; index++)
        {
            var slot = index;
            var definition = FeatureCatalog.Get(FeatureCategory.Tarot, inventory.Tarot[index].ToString());
            ownedTarot.Children.Add(Selectable(Small(definition, TarotInk), TarotInk, $"OwnedTarot{slot}", "Your Tarot: " + definition.Name, () => Detail(
                "YOUR TAROT", TarotInk, definition.Name, definition.Description, "Play it before you bid in a match.",
                MenuParts.Button("Discard", false, motion, $"DiscardTarot{slot}", () => actions.Discard(slot), true, 264))));
        }
        while (ownedTarot.Children.Count < CampaignTableControls.TarotSlots) ownedTarot.Children.Add(EmptySlot());

        var unlocked = Label($"PLANET BIDS {inventory.Unlocks.Length}/8", MainWindow.Green, 11);
        unlocked.VerticalAlignment = VerticalAlignment.Bottom;
        if (inventory.Unlocks.Length > 0)
            ToolTip.SetTip(unlocked, string.Join("\n", inventory.Unlocks.Select(id => FeatureCatalog.Get(FeatureCategory.Planet, id.ToString()).Name)));
        var owned = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 28, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Shelf($"YOUR JOKERS {inventory.Jokers.Length}/{CampaignTableControls.JokerSlots}", ownedJokers),
                Shelf($"YOUR TAROT {inventory.Tarot.Length}/{CampaignTableControls.TarotSlots}", ownedTarot),
                unlocked
            }
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, "SHOP", 38), Label($"${run.Money}", Gold, 26) }
        };
        ((TextBlock)header.Children[1]).VerticalAlignment = VerticalAlignment.Center;
        ((TextBlock)header.Children[1]).Name = "ShopMoney";

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(header);
        if (note is not null)
        {
            var line = Label(note, note.EndsWith('!') ? MainWindow.Green : Danger, 12);
            line.Name = "CampaignNote"; line.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(line);
        }
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { offers, detail }
        });
        panel.Children.Add(owned);
        if (inventory.Sigils.Length > 0)
        {
            var ownedSigils = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Center };
            for (var index = 0; index < inventory.Sigils.Length; index++)
            {
                var slot = index;
                var definition = FeatureCatalog.Get(FeatureCategory.Sigil, inventory.Sigils[index].ToString());
                ownedSigils.Children.Add(Selectable(Small(definition, SigilInk), SigilInk, $"OwnedSigil{slot}", "Your Sigil: " + definition.Name, () => Detail(
                    "YOUR SIGIL", SigilInk, definition.Name, definition.Description, "Use it on a card of the shared deck. Replaces that card's current Sigil.",
                    MenuParts.Button("Use", true, motion, $"UseSigil{slot}", () => actions.UseSigil(slot), true, 264))));
            }
            panel.Children.Add(Shelf($"YOUR SIGILS {inventory.Sigils.Length}", ownedSigils));
        }
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                MenuParts.Button("Inspect Deck", false, motion, "InspectDeck", actions.Inspect, true, 260),
                MenuParts.Button("Next Match", true, motion, "CampaignNextMatch", actions.Next, false, 260)
            }
        });
        Select(null, Detail("SHOP", Muted, "Pick a card", "Press a card to see what it does and what it costs.", null));
        return MenuParts.Scene(motion, panel, new ScrollViewer { Content = panel });
    }

    // A Tarot or Sigil can also be played at once on the shared deck, which
    // needs no free slot. A Planet has nothing to hold: buying it unlocks its bid.
    private static Control OfferDetail(CampaignRun run, ShopOffer offer, FeatureDefinition definition, ShopActions actions, PixelMotion motion)
    {
        var inventory = run.Resources.Inventory;
        var afford = run.Money >= offer.Price;
        var room = offer.Category switch
        {
            FeatureCategory.Joker => inventory.Jokers.Length < CampaignTableControls.JokerSlots,
            FeatureCategory.Tarot => inventory.Tarot.Length < CampaignTableControls.TarotSlots,
            _ => true
        };
        var buy = MenuParts.Button($"Buy ${offer.Price}", offer.Category != FeatureCategory.Tarot, motion, $"Buy{offer.Id}", () => actions.Buy(offer.Id), true, 264);
        buy.IsEnabled = afford && room;
        var buttons = new List<Control> { buy };
        var hint = offer.Category switch
        {
            FeatureCategory.Sigil => "Buy it to store for later, or use it now on a card of the shared deck.",
            FeatureCategory.Planet => "Buying it unlocks this bid for the rest of the run.",
            FeatureCategory.Tarot => "Buy it to play in a match, or use it now on the shared deck.",
            _ => null
        };
        if (offer.Category is FeatureCategory.Tarot or FeatureCategory.Sigil)
        {
            var use = MenuParts.Button($"Buy & Use ${offer.Price}", true, motion, $"BuyUse{offer.Id}", () => actions.BuyAndUse(offer.Id), true, 264);
            use.IsEnabled = afford && !(offer.Category == FeatureCategory.Tarot && offer.FeatureId == nameof(TarotId.Astronomer) && inventory.Unlocks.Length == 8);
            buttons.Add(use);
        }
        var problem = !afford ? $"You need ${offer.Price - run.Money} more."
            : room ? null
            : offer.Category == FeatureCategory.Joker ? "Your Joker slots are full. Sell one first."
            : "Your Tarot slots are full. Use it now, or discard one.";
        return Detail(offer.Category.ToString().ToUpperInvariant(), Ink(offer.Category), definition.Name, definition.Description, problem ?? hint,
            [.. buttons], problem is not null);
    }

    private static Control Detail(string kind, IBrush ink, string name, string description, string? hint, params Control[] buttons) =>
        Detail(kind, ink, name, description, hint, buttons, false);

    private static Control Detail(string kind, IBrush ink, string name, string description, string? hint, Control[] buttons, bool warning)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Label(kind, ink, 11));
        stack.Children.Add(Label(name, Gold, 20));
        stack.Children.Add(new TextBlock { Text = description, Foreground = MainWindow.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (hint is not null)
            stack.Children.Add(new TextBlock { Text = hint, Foreground = warning ? Danger : Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        foreach (var button in buttons) { button.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(button); }
        return stack;
    }

    private static IBrush Ink(FeatureCategory category) => category switch
    {
        FeatureCategory.Joker => Gold, FeatureCategory.Tarot => TarotInk, FeatureCategory.Sigil => SigilInk, _ => MainWindow.Green
    };

    // A card: its kind, its picture at twice size, its name, and a price tag on the corner.
    private static Border Face(FeatureDefinition definition, IBrush ink, string tag)
    {
        var name = Label(definition.Name, MainWindow.Ink, 10);
        name.TextAlignment = TextAlignment.Center; name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var kind = Label(definition.Category.ToString().ToUpperInvariant(), ink, 9);
        kind.Margin = new Thickness(7, 5, 0, 0);
        var body = new StackPanel
        {
            Children =
            {
                new Border { Height = 22, Child = kind },
                new Border { BorderBrush = ink, BorderThickness = new Thickness(0, 2), Child = PixelArt.CampaignIcon(definition, Art), HorizontalAlignment = HorizontalAlignment.Center, Width = CardWidth - 6 },
                new Border { Height = CardHeight - Art - 22 - 10, Padding = new Thickness(6, 0), Child = name }
            }
        };
        name.VerticalAlignment = VerticalAlignment.Center;
        var price = new Border
        {
            Background = Gold, Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = tag, Foreground = Paper, FontSize = 11, FontFamily = PixelArt.Font, FontWeight = FontWeight.Bold }
        };
        return new Border
        {
            Width = CardWidth, Height = CardHeight, Background = MainWindow.Panel, BorderBrush = ink, BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = new Grid { Children = { body, price } }
        };
    }

    internal static Border Small(FeatureDefinition definition, IBrush ink) => new()
    {
        Width = SmallCard, Height = SmallCard, Background = MainWindow.Panel, BorderBrush = ink, BorderThickness = new Thickness(3),
        CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = PixelArt.CampaignIcon(definition)
    };

    internal static Border EmptySlot() => new()
    {
        Width = SmallCard, Height = SmallCard, CornerRadius = new CornerRadius(4), Opacity = .55, Margin = new Thickness(0, Lift, 0, 0),
        Background = Brush.Parse("#33100B28"), BorderBrush = MainWindow.Line, BorderThickness = new Thickness(2)
    };

    private static StackPanel Row(double spacing) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, HorizontalAlignment = HorizontalAlignment.Center };

    private static StackPanel Shelf(string title, Control row) => new() { Spacing = 6, Children = { Label(title, Muted, 11), row } };

    internal static TextBlock Label(string text, IBrush brush, double size) => new()
    {
        Text = text, Foreground = brush, FontSize = size, FontFamily = PixelArt.Font, TextWrapping = TextWrapping.Wrap
    };
}

/// <summary>The shared deck, drawn as its 24 cards. With <paramref name="required"/> above zero the
/// player marks that many cards and confirms; at zero it only shows the deck.</summary>
internal static class CampaignDeckPicker
{
    private const double CardWidth = 84, CardHeight = 124;

    public static Control Build(IReadOnlyList<CardState> deck, int required, Action<CardId[]> confirm, Action cancel, PixelMotion motion,
        string title, string confirmText = "Confirm")
    {
        var chosen = new List<CardId>();
        var frames = new List<Border>();
        var count = CampaignShopScreen.Label("", Muted, 12);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        var ok = MenuParts.Button(confirmText, true, motion, "ConfirmDeckChoice", () => confirm(chosen.ToArray()), true, 260);
        void Refresh()
        {
            count.Text = required == 1 ? "CHOOSE A CARD" : $"CHOOSE {required} CARDS · {chosen.Count}/{required}";
            ok.IsEnabled = chosen.Count == required;
        }

        // Keep each printed suit on its own row, including after Tarot changes a card's face.
        var cards = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var suitRows = new Dictionary<Suit, StackPanel>();
        foreach (var suit in Enum.GetValues<Suit>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = CardHeight + 47 };
            suitRows.Add(suit, row);
            cards.Children.Add(row);
        }
        foreach (var card in deck.OrderBy(card => card.Face.Suit).ThenBy(card => card.Face.Rank).ThenBy(card => card.Id.Value))
        {
            var row = suitRows[card.Face.Suit];
            var frame = new Border
            {
                Width = CardWidth + 6, Height = CardHeight + 6, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(6), Child = PixelArt.CardContent(card.Face)
            };
            frames.Add(frame);
            var marks = new StackPanel { Spacing = 1, Height = 30 };
            var tips = new StackPanel { Spacing = 4, MaxWidth = 260 };
            void Mark(FeatureCategory category, string key, IBrush brush)
            {
                var definition = FeatureCatalog.Get(category, key);
                var line = CampaignShopScreen.Label(definition.Name.ToUpperInvariant(), brush, 9);
                line.HorizontalAlignment = HorizontalAlignment.Center;
                marks.Children.Add(line);
                tips.Children.Add(new TextBlock { Text = $"{definition.Name}: {definition.Description}", Foreground = MainWindow.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }
            if (card.Enhancement is { } enhancement) Mark(FeatureCategory.Enhancement, enhancement.ToString(), Gold);
            if (card.Sigil is { } sigil) Mark(FeatureCategory.Sigil, sigil.ToString(), Brush.Parse("#7BE0A0"));
            // The art layers ignore hit testing; the template needs a surface across the card face.
            var cell = new StackPanel { Background = Brushes.Transparent, Spacing = 3, Margin = new Thickness(5, 4), Children = { motion.Float(frame, card.Id.Value), marks } };
            if (tips.Children.Count > 0) { ToolTip.SetTip(cell, tips); ToolTip.SetShowDelay(cell, 120); }
            if (required == 0) { row.Children.Add(cell); continue; }

            var button = new Button
            {
                Name = $"DeckCard{card.Id.Value}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), FocusAdorner = null,
                Template = new FuncControlTemplate<Button>((_, _) => cell)
            };
            AutomationProperties.SetName(button, $"{Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}, card {card.Id.Value + 1}");
            button.Click += (_, _) =>
            {
                if (chosen.Remove(card.Id)) frame.BorderBrush = Brushes.Transparent;
                else
                {
                    // With a single target, pressing another card moves the choice.
                    if (required == 1 && chosen.Count == 1) { chosen.Clear(); foreach (var other in frames) other.BorderBrush = Brushes.Transparent; }
                    if (chosen.Count == required) return;
                    chosen.Add(card.Id); frame.BorderBrush = Gold;
                }
                Refresh();
            };
            row.Children.Add(button);
        }

        var heading = CampaignShopScreen.Label(title, Gold, 20);
        heading.HorizontalAlignment = HorizontalAlignment.Center; heading.TextAlignment = TextAlignment.Center;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
        if (required > 0) { panel.Children.Add(count); buttons.Children.Add(ok); Refresh(); }
        panel.Children.Add(cards);
        buttons.Children.Add(MenuParts.Button("Back", false, motion, "CancelDeckChoice", cancel, true, 260));
        panel.Children.Add(buttons);
        return MenuParts.Scene(motion, panel, new ScrollViewer
        {
            Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
    }
}
