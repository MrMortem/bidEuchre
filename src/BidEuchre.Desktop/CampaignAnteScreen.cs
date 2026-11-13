using Avalonia;
using Avalonia.Controls;
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

    public static Control Build(CampaignRun run, Action start, Action back, string? note, PixelMotion motion)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, $"ANTE {run.Ante}", 38), Centered($"♥ {run.Lives}", Danger, 26), Centered($"${run.Money}", Gold, 26) }
        };

        var jokers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var owned = run.Resources.Inventory.Jokers;
        foreach (var joker in owned)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString());
            var card = CampaignShopScreen.Small(definition, Gold);
            ToolTip.SetTip(card, $"{definition.Name}: {definition.Description}");
            jokers.Children.Add(card);
        }
        while (jokers.Children.Count < CampaignTableControls.JokerSlots)
        {
            var slot = CampaignShopScreen.EmptySlot();
            slot.Margin = default;
            jokers.Children.Add(slot);
        }
        var shelfTitle = Centered($"YOUR JOKERS {owned.Length}/{CampaignTableControls.JokerSlots}", Muted, 11);

        var matches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center };
        var options = run.NextMatch();
        for (var slot = 1; slot <= CampaignRun.MatchesPerAnte; slot++)
        {
            var (name, ink) = Slots[slot - 1];
            var current = slot == run.MatchInAnte;
            // The Start button and the status line take the same room, so the panels' rows line up.
            Control top;
            if (current) top = MenuParts.Button("Start Match", true, motion, "CampaignStartMatch", start, width: ButtonWidth);
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
                        Centered($"{options.HandLimit} HANDS", Muted, 12),
                        Centered($"REWARD ${CampaignRun.BaseReward(slot)}+", Gold, 14),
                        Centered($"LOSE ♥{CampaignRun.LivesAtStake(slot)}", Danger, 14)
                    }
                }
            };
            matches.Children.Add(motion.Enter(panel, 160 + slot * 80, 0, 16));
        }

        var content = new StackPanel { Spacing = 16, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(header);
        if (note is not null)
        {
            var line = Centered(note, Danger, 12);
            line.Name = "CampaignNote";
            content.Children.Add(line);
        }
        content.Children.Add(new StackPanel { Spacing = 6, Children = { shelfTitle, jokers } });
        content.Children.Add(matches);
        content.Children.Add(motion.Enter(MenuParts.Button("Main Menu", false, motion, "CampaignMainMenu", back, width: ButtonWidth), 520, 0, 14));
        return MenuParts.Scene(motion, content, new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
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
