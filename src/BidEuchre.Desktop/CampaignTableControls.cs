using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
namespace BidEuchre.Desktop;

/// <summary>The campaign's share of the table: Jokers and the stored Tarots and Sigils for the top strip, and the
/// choices that are made with buttons rather than by pressing a card in hand.</summary>
internal static class CampaignTableControls
{
    public const int JokerSlots = 5, ItemSlots = CampaignInventory.ItemSlots;
    private const double CardWidth = 72, CardHeight = 78;

    /// <summary>Replaces a strip row with <paramref name="cards"/>, padded to its slot count with empty outlines.</summary>
    public static void Fill(Panel row, int slots, IEnumerable<Control> cards)
    {
        row.Children.Clear();
        row.Children.AddRange(cards);
        while (row.Children.Count < slots) row.Children.Add(EmptySlot());
    }

    public static Control Joker(JokerState joker)
    {
        var definition = FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString());
        return Named(CardFace(definition), definition.Name, joker.Value == 0 ? definition.Description : $"{definition.Description}\nCurrent value: {joker.Value}");
    }

    /// <summary>The Tarots and Sigils held, in the order they are shown: one shared inventory.</summary>
    public static IEnumerable<(FeatureCategory Category, int Slot, FeatureDefinition Definition)> Items(CampaignInventory inventory) =>
        inventory.Tarot.Select((id, slot) => (FeatureCategory.Tarot, slot, FeatureCatalog.Get(FeatureCategory.Tarot, id.ToString())))
            .Concat(inventory.Sigils.Select((id, slot) => (FeatureCategory.Sigil, slot, FeatureCatalog.Get(FeatureCategory.Sigil, id.ToString()))));

    /// <summary>Stored cards are played on any turn of the human's own.</summary>
    public static bool ItemsUsable(CampaignGameView view) => view.ViewerSeat == 0 && view.LegalActions.Count > 0;

    /// <summary>A stored Tarot or Sigil. While usable, pressing it opens its targets and a button to play it.</summary>
    public static Control Item(CampaignGameView view, FeatureCategory category, int slot, bool usable, Func<CampaignCommand, Task> send)
    {
        var sigil = category == FeatureCategory.Sigil;
        var definition = FeatureCatalog.Get(category, sigil ? view.Inventory.Sigils[slot].ToString() : view.Inventory.Tarot[slot].ToString());
        var face = CardFace(definition);
        if (!usable) return Named(face, definition.Name, definition.Description);

        face.BorderBrush = MainWindow.Gold;
        face.BorderThickness = new Thickness(2);
        var selected = new List<CardId>();
        var required = sigil ? 1 : Tarot.TargetCount(view.Inventory.Tarot[slot]);
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var use = new Button { Content = "Use " + definition.Name, IsEnabled = required == 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var body = new StackPanel { Spacing = 4, MaxWidth = 260 };
        body.Children.Add(Label(definition.Name, MainWindow.Gold));
        body.Children.Add(Label(definition.Description, MainWindow.Ink));
        if (required > 0)
        {
            body.Children.Add(Label(required == 1 ? "Choose a card from your hand:" : $"Choose {required} cards from your hand:", MainWindow.Muted));
            foreach (var card in view.Players[0].VisibleCards)
            {
                var choice = new CheckBox { Content = $"{card.Face.Code} · #{card.Id.Value + 1}", Foreground = MainWindow.Ink };
                choice.IsCheckedChanged += (_, _) =>
                {
                    if (choice.IsChecked == true) selected.Add(card.Id); else selected.Remove(card.Id);
                    use.IsEnabled = selected.Count == required;
                };
                body.Children.Add(choice);
            }
        }
        use.Click += async (_, _) =>
        {
            flyout.Hide();
            await send(sigil ? new CampaignCommand.UseSigil(slot, selected[0]) : new CampaignCommand.UseTarot(slot, selected.ToArray()));
        };
        body.Children.Add(use);
        flyout.Content = body;
        var button = new Button
        {
            Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), Flyout = flyout,
            Template = new FuncControlTemplate<Button>((_, _) => face)
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"{category}: {definition.Name}");
        return Named(button, definition.Name, definition.Description);
    }

    public static string Title(CampaignGameView view) => view.PendingChoice
        ?? (view.LegalActions.Count == 0 ? "Waiting for the next player"
            : view.LegalActions.Any(c => c is CampaignCommand.Bid or CampaignCommand.Pass) ? "Make your bid"
            : view.LegalActions.Any(c => c is CampaignCommand.ChooseContract) ? "Choose the contract"
            : view.LegalActions.All(c => CardOf(c) is not null) ? "Play a gold-outlined card"
            : "Your move");

    /// <summary>The legal actions that need a button. A card with exactly one play or exchange is pressed in hand instead.</summary>
    public static IEnumerable<CampaignCommand> CenterCommands(CampaignGameView view) => view.LegalActions
        .Where(command => CardOf(command) is not { } card || view.LegalActions.Count(other => CardOf(other) == card) != 1);

    private static CardId? CardOf(CampaignCommand command) => command switch
    {
        CampaignCommand.Play play => play.Id,
        CampaignCommand.Exchange exchange => exchange.Id,
        _ => null
    };

    private static Border CardFace(FeatureDefinition definition)
    {
        var face = new Border
        {
            Width = CardWidth, Height = CardHeight, CornerRadius = new CornerRadius(6), Background = Brushes.White,
            BorderBrush = Brush.Parse("#D8CDB9"), BorderThickness = new Thickness(1),
            Child = PixelArt.CampaignIcon(definition)
        };
        Avalonia.Automation.AutomationProperties.SetName(face, $"{definition.Category}: {definition.Name}");
        return face;
    }

    private static Border EmptySlot() => new()
    {
        Width = CardWidth, Height = CardHeight, CornerRadius = new CornerRadius(6), Opacity = .55,
        Background = Brush.Parse("#33100B28"), BorderBrush = MainWindow.Line, BorderThickness = new Thickness(1)
    };

    private static T Named<T>(T card, string title, string description) where T : Control
    {
        ToolTip.SetTip(card, new StackPanel
        {
            Spacing = 4, MaxWidth = 260,
            Children = { Label(title, MainWindow.Gold), Label(description, MainWindow.Ink) }
        });
        ToolTip.SetShowDelay(card, 120);
        return card;
    }

    private static TextBlock Label(string text, IBrush brush) => new()
    {
        Text = text, Foreground = brush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        FontFamily = ReferenceEquals(brush, MainWindow.Gold) ? PixelArt.Font : FontFamily.Default
    };

    private static string Card(CardId id, CampaignGameView view)
    { var card = view.Players.SelectMany(p => p.VisibleCards).FirstOrDefault(c => c.Id == id); return card is null ? $"card #{id.Value + 1}" : $"{card.Face.Code} · #{id.Value + 1}"; }
    public static string Describe(CampaignCommand command, CampaignGameView view) => command switch
    {
        CampaignCommand.Bid b => $"Bid {(b.Level <= BidLevel.Six ? ((int)b.Level).ToString() : b.Level.ToString())}{(b.Variant is { } v ? " · " + v : "")}",
        CampaignCommand.Pass => "Pass",
        CampaignCommand.PanicButton => "Panic Button · −8 points · consumes this hand",
        CampaignCommand.ChooseContract c => c.Trump is { } suit ? $"Trump · {suit}" : c.Mode.ToString(),
        CampaignCommand.Play p => $"Play {Card(p.Id, view)}{(p.EffectiveSuit is { } suit ? " as " + suit : "")}",
        CampaignCommand.Exchange e => "Exchange " + Card(e.Id, view),
        CampaignCommand.SwapPartnerCard s => "Swap " + Card(s.Id, view),
        CampaignCommand.ChooseRevealSeat r => $"Reveal a card from seat {r.Seat + 1}",
        CampaignCommand.RevealPartnerCard r => "Reveal " + Card(r.Id, view),
        CampaignCommand.SelectOpponent s => $"Duel seat {s.Seat + 1}",
        _ => command.GetType().Name
    };
}
