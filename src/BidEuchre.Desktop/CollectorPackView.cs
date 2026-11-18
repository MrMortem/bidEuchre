using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>An opened Collector's Pack: three saved cards that flip face-up, each a button that picks it.</summary>
internal static class CollectorPackView
{
    private const double CardWidth = 92, CardHeight = 138;
    private static readonly IBrush LegendaryGlow = Brush.Parse("#F4C95D");

    public static string LegendaryText(int freeSlots) => $"fills {freeSlots} empty Joker slot{(freeSlots == 1 ? "" : "s")}";
    public static string AccessibleName(string key, int freeSlots) => key == CampaignArcade.LegendaryKey
        ? "Legendary — " + LegendaryText(freeSlots)
        : $"{FeatureCatalog.CollectorTitle(Enum.Parse<SigilId>(key))} — {key} Sigil";

    public static Control Build(ArcadeResult result, PixelMotion motion, Action<int> pick, int freeJokerSlots)
    {
        var row = new StackPanel { Name = "CollectorPack", Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var cards = new List<(Button Button, Image Art, ScaleTransform Flip, string Key)>();
        for (var index = 0; index < result.PackCards!.Length; index++)
        {
            var key = result.PackCards[index]; var chosen = index;
            var flip = new ScaleTransform(1, 1);
            var art = new Image { Width = CardWidth, Height = CardHeight, Stretch = Stretch.Uniform, Source = PixelArt.CollectorImage(key) };
            var legendary = key == CampaignArcade.LegendaryKey;
            var button = new Button
            {
                Name = $"CollectorCard{index}", Padding = new Thickness(3), Background = Brushes.Transparent,
                BorderBrush = legendary ? LegendaryGlow : MainWindow.Line, BorderThickness = new Thickness(legendary ? 3 : 2),
                CornerRadius = new CornerRadius(6), Cursor = new Cursor(StandardCursorType.Hand),
                RenderTransform = flip, RenderTransformOrigin = RelativePoint.Center, Content = art,
                Effect = legendary ? new DropShadowEffect { Color = Color.Parse("#F4C95D"), BlurRadius = 18, OffsetX = 0, OffsetY = 0 } : null
            };
            AutomationProperties.SetName(button, AccessibleName(key, freeJokerSlots));
            ToolTip.SetTip(button, AccessibleName(key, freeJokerSlots));
            button.Click += (_, _) => { if (button.IsEnabled) pick(chosen); };
            row.Children.Add(button);
            cards.Add((button, art, flip, key));
        }
        if (motion.Enabled) _ = Reveal(row, cards);
        return row;
    }

    // Cards start as the pack wrapper and flip one by one; the saved cards never change, only when they show.
    private static async Task Reveal(Control owner, List<(Button Button, Image Art, ScaleTransform Flip, string Key)> cards)
    {
        var stop = new CancellationTokenSource();
        void Leave(object? sender, VisualTreeAttachmentEventArgs e) => stop.Cancel();
        owner.DetachedFromVisualTree += Leave;
        foreach (var card in cards) { card.Art.Source = PixelArt.CollectorImage("Pack"); card.Button.IsEnabled = false; }
        try
        {
            foreach (var card in cards)
            {
                await Tween.Run(140, stop.Token, t => card.Flip.ScaleX = 1 - t);
                card.Art.Source = PixelArt.CollectorImage(card.Key);
                await Tween.Run(160, stop.Token, t => card.Flip.ScaleX = t);
            }
        }
        catch (OperationCanceledException) { }
        finally { owner.DetachedFromVisualTree -= Leave; stop.Dispose(); }
        foreach (var card in cards) { card.Art.Source = PixelArt.CollectorImage(card.Key); card.Flip.ScaleX = 1; card.Button.IsEnabled = true; }
    }
}
