using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>The screen before each match: the Ante's three matches side by side. The match about to be
/// played is lit and holds the Start button; the ones before and after it are dimmed.</summary>
internal static class CampaignAnteScreen
{
    private const double PanelWidth = 250, ButtonWidth = 214;
    private static readonly IBrush Danger = Brush.Parse("#FF617B");
    private static readonly (string Name, IBrush Ink)[] Slots = [("SMALL", Green), ("MEDIUM", Gold), ("BOSS", Danger)];

    public static Control Build(CampaignRun run, Action start, Action back, string? note, PixelMotion motion,
        Action<FeatureCategory, int>? discardItem = null)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, $"ANTE {run.Ante}", 38), Centered($"♥ {run.Lives}", Danger, 26), Centered($"${run.Money}", Gold, 26) }
        };

        var inventory = run.Resources.Inventory;
        // Stored Tarots and Sigils can be inspected or discarded while waiting for the match.
        var items = new List<Control>();
        foreach (var (category, slot, definition) in CampaignTableControls.Items(inventory))
        {
            var flyout = new Flyout { Placement = PlacementMode.Bottom };
            flyout.Content = new StackPanel
            {
                Spacing = 8, Width = ButtonWidth,
                Children =
                {
                    CampaignShopScreen.Label(definition.Name, Gold, 14),
                    CampaignShopScreen.Label("Use during a match on cards in your hand.", Muted, 10),
                    MenuParts.Button("Discard", false, motion, $"AnteDiscard{category}{slot}", () => { flyout.Hide(); discardItem?.Invoke(category, slot); }, true, ButtonWidth)
                }
            };
            var face = CampaignTableControls.CardFace(definition);
            var card = new Button
            {
                Name = $"AnteItem{category}{slot}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), Flyout = flyout
            };
            CampaignCardFocus.Apply(card, face);
            Avalonia.Automation.AutomationProperties.SetName(card, $"Your {category}: {definition.Name}");
            items.Add(CampaignCardExplanation.Attach(card, definition));
        }
        var matches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center };
        var options = run.NextMatch();
        for (var slot = 1; slot <= CampaignRun.MatchesPerAnte; slot++)
        {
            var (name, ink) = Slots[slot - 1];
            var current = slot == run.MatchInAnte;
            // The Start button and the status line take the same room, so the panels' rows line up.
            Control top;
            if (current) top = MenuParts.Button("Start Match", true, motion, "CampaignStartMatch", start, stay: true, width: ButtonWidth);
            else
            {
                var status = Centered(slot < run.MatchInAnte ? "PLAYED" : "UPCOMING", Muted, 14);
                status.Name = $"AnteStatus{slot}";
                top = new Border { Height = 58, Child = status };
            }
            var tag = Centered(name, Paper, 16);
            tag.FontWeight = FontWeight.Bold;
            var panel = new Border
            {
                Name = $"AntePanel{slot}", Width = PanelWidth, Padding = new Thickness(15, 15, 15, 20), Opacity = current ? 1 : .55,
                Background = current ? PanelRaised : MainWindow.Panel, BorderBrush = current ? Gold : MainWindow.Line,
                BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        top,
                        new Border { Background = ink, CornerRadius = new CornerRadius(2), Padding = new Thickness(0, 6), Child = tag },
                        Centered($"±{run.Threshold} POINTS", Ink, 16),
                        Centered($"{options.HandLimit} HANDS · TIES CONTINUE", Muted, 12),
                        Centered($"OPPONENT MADE BID +{run.OpponentSuccessfulBidBonus}", Muted, 11),
                        Centered($"REWARD ${CampaignRun.BaseReward(slot)}+", Gold, 14),
                        Centered($"LOSE ♥{CampaignRun.LivesAtStake(slot, run.Ante)}", Danger, 14)
                    }
                }
            };
            matches.Children.Add(motion.Enter(panel, 160 + slot * 80, 0, 16));
        }

        var content = new StackPanel { Spacing = 16, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(header);
        if (note is not null)
        {
            var line = Centered(note, note.EndsWith('!') ? Green : Danger, 12);
            line.Name = "CampaignNote";
            content.Children.Add(line);
        }
        if (inventory.Sodas.Length > 0) content.Children.Add(SodaArtwork.Shelf(inventory));
        content.Children.Add(matches);
        var page = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Content = content,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                },
                Place(new Border
                {
                    Padding = new Thickness(8), Background = Paper,
                    Child = MenuParts.Button("Main Menu", false, motion, "CampaignMainMenu", back, stay: true, width: ButtonWidth)
                }, row: 1)
            }
        };
        return CampaignInventoryScreen.Build(MenuParts.Scene(motion, page), inventory, motion, itemCards: items);
    }

    private static TextBlock Centered(string text, IBrush brush, double size)
    {
        var label = CampaignShopScreen.Label(text, brush, size);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        return label;
    }
}
