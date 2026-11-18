using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

/// <summary>A fixed inventory header above the independently laid out campaign scene.</summary>
internal sealed class CampaignInventoryScreen : Grid
{
    internal static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 4 };

    internal static CampaignInventoryScreen Build(Control scene, CampaignInventory inventory, PixelMotion motion,
        IEnumerable<Control>? jokerCards = null, IEnumerable<Control>? itemCards = null)
    {
        var jokers = Row();
        var items = Row();
        CampaignTableControls.Fill(jokers, inventory.JokerCapacity, motion, 0, jokerCards ?? inventory.Jokers.Select(CampaignTableControls.Joker));
        CampaignTableControls.Fill(items, inventory.ItemCapacity, motion, 7, itemCards ?? CampaignTableControls.Items(inventory)
            .Select(item => CampaignTableControls.StoredItem(item.Definition)));
        return new(scene, jokers, items);
    }

    internal CampaignInventoryScreen(Control scene, StackPanel jokers, StackPanel items)
    {
        RowDefinitions = RowDefinitions.Parse("Auto,*");
        // Reserve the maximum upgraded capacity on both sides, even before the upgrades.
        var groups = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("464,380"), Height = 99 };
        groups.Children.Add(Group("JOKERS", jokers));
        var held = Group("TAROTS / SIGILS", items);
        Grid.SetColumn(held, 1);
        groups.Children.Add(held);
        var strip = new Border
        {
            Name = "CampaignInventoryStrip", Background = MainWindow.Panel,
            Padding = new Thickness(12, 5, 164, 5),
            Child = new Viewbox
            {
                HorizontalAlignment = HorizontalAlignment.Left, StretchDirection = StretchDirection.DownOnly,
                Child = groups
            }
        };
        Children.Add(strip);
        Grid.SetRow(scene, 1);
        Children.Add(scene);
    }

    private static StackPanel Group(string title, StackPanel cards)
    {
        var label = MainWindow.Eyebrow(title);
        label.Height = 16;
        label.FontFamily = PixelArt.Font;
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left, Spacing = 3, Children = { label, cards }
        };
    }
}
