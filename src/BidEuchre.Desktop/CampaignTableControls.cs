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

    /// <summary>A stored Tarot or Sigil. While usable, pressing it starts (or, while <paramref name="active"/>, cancels)
    /// choosing its targets by pressing cards in hand.</summary>
    public static Control Item(CampaignGameView view, FeatureCategory category, int slot, bool usable, bool active, Action toggle)
    {
        var definition = Definition(view.Inventory, category, slot);
        var face = CardFace(definition);
        if (!usable) return Named(face, definition.Name, definition.Description);

        face.BorderBrush = active ? MainWindow.Green : MainWindow.Gold;
        face.BorderThickness = new Thickness(active ? 3 : 2);
        var button = new Button
        {
            Name = $"Item{category}{slot}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
            Template = new FuncControlTemplate<Button>((_, _) => face)
        };
        button.Click += (_, _) => toggle();
        Avalonia.Automation.AutomationProperties.SetName(button, $"{(active ? "Cancel " : "Use ")}{category}: {definition.Name}");
        return Named(button, definition.Name, definition.Description);
    }

    /// <summary>The catalog entry for the Tarot or Sigil held in <paramref name="slot"/>.</summary>
    public static FeatureDefinition Definition(CampaignInventory inventory, FeatureCategory category, int slot) =>
        FeatureCatalog.Get(category, category == FeatureCategory.Sigil ? inventory.Sigils[slot].ToString() : inventory.Tarot[slot].ToString());

    /// <summary>A card's hover text: its face, then each enhancement and Sigil by catalog name and description.</summary>
    public static Control CardTip(CampaignCardView card, Suit? playedAs = null)
    {
        var tip = new StackPanel { Spacing = 4, MaxWidth = 260 };
        tip.Children.Add(Label($"{BidEuchre.Core.Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}", MainWindow.Gold));
        if (playedAs is { } suit && suit != card.Face.Suit) tip.Children.Add(Label($"Played as {suit}", MainWindow.Green));
        void Add(FeatureCategory category, string key)
        { var feature = FeatureCatalog.Get(category, key); tip.Children.Add(Label($"{feature.Name}: {feature.Description}", MainWindow.Ink)); }
        if (card.Enhancement is { } enhancement) Add(FeatureCategory.Enhancement, enhancement.ToString());
        if (card.Sigil is { } sigil) Add(FeatureCategory.Sigil, sigil.ToString());
        if (card.Enhancement is null && card.Sigil is null) tip.Children.Add(Label("No enhancement or Sigil", MainWindow.Muted));
        tip.Children.Add(Label($"Card #{card.Id.Value + 1} of the shared deck", MainWindow.Muted));
        return tip;
    }

    public static string Title(CampaignGameView view) => view.PendingChoice
        ?? (view.LegalActions.Count == 0 ? "Waiting for the next player"
            : view.LegalActions.Any(c => c is CampaignCommand.Bid or CampaignCommand.Pass) ? "Make your bid"
            : view.LegalActions.Any(c => c is CampaignCommand.ChooseContract) ? "Choose the contract"
            : view.LegalActions.All(c => c is CampaignCommand.Play) ? "Play a gold-outlined card"
            : view.LegalActions.All(c => CardOf(c) is not null) ? "Press a gold-outlined card"
            : "Your move");

    /// <summary>The legal actions that need a button. A card with exactly one play or exchange is pressed in hand instead.</summary>
    public static IEnumerable<CampaignCommand> CenterCommands(CampaignGameView view) => view.LegalActions
        .Where(command => CardOf(command) is not { } card || view.LegalActions.Count(other => CardOf(other) == card) != 1);

    /// <summary>The hand card a command is made by pressing: plays, exchanges, One Up's swap and Telepath's reveal.</summary>
    public static CardId? CardOf(CampaignCommand command) => command switch
    {
        CampaignCommand.Play play => play.Id,
        CampaignCommand.Exchange exchange => exchange.Id,
        CampaignCommand.SwapPartnerCard swap => swap.Id,
        CampaignCommand.RevealPartnerCard reveal => reveal.Id,
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
