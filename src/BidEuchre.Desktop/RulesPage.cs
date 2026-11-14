using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>The rules file drawn as a page, followed by the campaign items straight from the catalog.</summary>
internal static class RulesPage
{
    private static readonly FontFamily Mono = new("DejaVu Sans Mono, Cascadia Mono, Consolas, Menlo, monospace");

    // Shown in this order; each group gets one line on how its items are used.
    private static readonly (FeatureCategory Category, string Title, string About)[] Campaign =
    [
        (FeatureCategory.Joker, "Jokers", "Bought in the shop. Each Joker works for as long as you hold it."),
        (FeatureCategory.Enhancement, "Enhancements", "Permanent changes to a card in your campaign deck."),
        (FeatureCategory.Sigil, "Sigils", "Placed on a card in your hand on one of your turns. The Sigil stays on that card."),
        (FeatureCategory.Tarot, "Tarots", "Used on one of your turns for a one-time effect, usually on cards in your hand."),
        (FeatureCategory.Gumball, "Gumball bids", "Extra bids you can unlock during a run.")
    ];

    public static Control Build()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "bid-euchre-final-rules.md");
        var content = new StackPanel { Spacing = 10 };
        if (File.Exists(path))
            foreach (var block in MarkdownDocument.Parse(File.ReadAllText(path))) content.Children.Add(Block(block));
        else content.Children.Add(Text("Rules file was not found beside the executable."));

        content.Children.Add(Block(new MarkdownHeading(2, "Campaign items")));
        content.Children.Add(Text("In a campaign, these items change the game while you hold or use them."));
        foreach (var (category, title, about) in Campaign)
        {
            content.Children.Add(Block(new MarkdownHeading(3, title)));
            content.Children.Add(Text(about, Muted));
            var items = new StackPanel { Spacing = 6, Margin = new Thickness(12, 2, 0, 6) };
            foreach (var feature in FeatureCatalog.All.Where(f => f.Category == category))
            {
                var line = Text(null);
                line.Inlines!.Add(new Run(feature.Name) { FontWeight = FontWeight.Bold, Foreground = Gold });
                line.Inlines.Add(new Run(" — " + feature.Description));
                items.Children.Add(line);
            }
            content.Children.Add(items);
        }

        return new ScrollViewer
        {
            Name = "RulesPage",
            Padding = new Thickness(30, 54, 30, 30),
            Content = new Border
            {
                MaxWidth = 960,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(10),
                Background = MainWindow.Panel,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Child = content
            }
        };
    }

    private static Control Block(MarkdownBlock block) => block switch
    {
        MarkdownHeading heading => Heading(heading),
        MarkdownParagraph paragraph => Text(paragraph.Text),
        MarkdownList list => List(list),
        MarkdownCode code => new Border
        {
            Background = PanelRaised, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = code.Text, FontFamily = Mono, FontSize = 13, Foreground = Gold, TextWrapping = TextWrapping.Wrap }
        },
        MarkdownTable table => Table(table),
        _ => throw new ArgumentOutOfRangeException(nameof(block))
    };

    private static Control Heading(MarkdownHeading heading)
    {
        var text = Text(heading.Text, heading.Level switch { 1 => Gold, 2 => Green, _ => Ink });
        text.FontSize = heading.Level switch { 1 => 26, 2 => 18, _ => 15 };
        text.FontWeight = FontWeight.Bold;
        text.LineHeight = double.NaN;
        if (heading.Level <= 2) text.FontFamily = PixelArt.Font;
        text.Margin = new Thickness(0, heading.Level switch { 1 => 0, 2 => 18, _ => 8 }, 0, 0);
        return text;
    }

    private static Control List(MarkdownList list)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(6, 0, 0, 0), RowSpacing = 4 };
        for (var i = 0; i < list.Items.Count; i++)
        {
            grid.RowDefinitions.Add(new(GridLength.Auto));
            var marker = Text(list.Ordered ? $"{i + 1}." : "•", Gold);
            marker.Margin = new Thickness(0, 0, 10, 0);
            marker.HorizontalAlignment = HorizontalAlignment.Right;
            var item = Text(list.Items[i]);
            Grid.SetRow(marker, i); Grid.SetRow(item, i); Grid.SetColumn(item, 1);
            grid.Children.Add(marker); grid.Children.Add(item);
        }
        return grid;
    }

    private static Control Table(MarkdownTable table)
    {
        var columns = table.Header.Count;
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new(GridLength.Auto));
        var rows = new[] { table.Header }.Concat(table.Rows).ToArray();
        for (var r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new(GridLength.Auto));
            for (var c = 0; c < columns; c++)
            {
                var cell = Text(c < rows[r].Count ? rows[r][c] : string.Empty, r == 0 ? Gold : Ink);
                if (r == 0) cell.FontWeight = FontWeight.Bold;
                cell.TextAlignment = c < table.RightAligned.Count && table.RightAligned[c] ? TextAlignment.Right : TextAlignment.Left;
                var border = new Border
                {
                    Child = cell, Padding = new Thickness(12, 6), BorderBrush = Line,
                    BorderThickness = new Thickness(0, 0, 0, r == 0 ? 2 : 1),
                    Background = r == 0 ? PanelRaised : null
                };
                Grid.SetRow(border, r); Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }
        return grid;
    }

    // Body text with **bold** and `code` spans.
    private static TextBlock Text(string? markdown, IBrush? brush = null)
    {
        var text = new TextBlock { Foreground = brush ?? Ink, FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Inlines = [] };
        if (markdown is null) return text;
        foreach (var span in MarkdownDocument.Inline(markdown))
            text.Inlines!.Add(span.Code
                ? new Run(span.Text) { FontFamily = Mono, Foreground = Gold }
                : new Run(span.Text) { FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal });
        return text;
    }
}
