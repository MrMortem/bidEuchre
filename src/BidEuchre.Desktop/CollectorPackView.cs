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

/// <summary>An opened Spectral Pack: three saved cards, each a button that picks it.</summary>
internal static class CollectorPackView
{
    private const double CardWidth = 136, CardHeight = 204;
    private static readonly IBrush LegendaryGlow = Brush.Parse("#F4C95D");

    public static string LegendaryText(int freeSlots) => $"fills {freeSlots} empty Joker slot{(freeSlots == 1 ? "" : "s")}";
    public static string AccessibleName(string key, int freeSlots) => key == CampaignArcade.LegendaryKey
        ? "Legendary — " + LegendaryText(freeSlots)
        : $"{FeatureCatalog.CollectorTitle(Enum.Parse<SigilId>(key))} — {key} Sigil";

    public static Control Build(ArcadeResult result, Action<int> pick, int freeJokerSlots, PixelMotion motion, TextBlock caption, out Image[] targets)
    {
        var row = new StackPanel { Name = "CollectorPack", Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var width = result.PackCards!.Length > CampaignArcade.PackSize ? 102 : CardWidth;
        targets = new Image[result.PackCards!.Length];
        for (var index = 0; index < result.PackCards!.Length; index++)
        {
            var key = result.PackCards[index]; var chosen = index;
            var art = new Image { Name = $"SpectralTarget{index}", Width = width, Height = width * CardHeight / CardWidth, Stretch = Stretch.Uniform, Source = PixelArt.CollectorImage(key) };
            targets[index] = art;
            var legendary = key == CampaignArcade.LegendaryKey;
            var button = new Button
            {
                Name = $"CollectorCard{index}", Padding = new Thickness(3), Background = Brushes.Transparent,
                BorderBrush = legendary ? LegendaryGlow : MainWindow.Line, BorderThickness = new Thickness(legendary ? 3 : 2),
                CornerRadius = new CornerRadius(6), Cursor = new Cursor(StandardCursorType.Hand),
                Content = art,
                Effect = legendary ? new DropShadowEffect { Color = Color.Parse("#F4C95D"), BlurRadius = 18, OffsetX = 0, OffsetY = 0 } : null
            };
            AutomationProperties.SetName(button, AccessibleName(key, freeJokerSlots));
            var effect = legendary
                ? $"Legendary — {LegendaryText(freeJokerSlots)} with random Jokers."
                : $"{AccessibleName(key, freeJokerSlots)} — {FeatureCatalog.Get(FeatureCategory.Sigil, key).Description}";
            BoosterOpening.ShowEffectOnHover(button, button, caption, effect);
            button.Click += (_, _) => { if (button.IsEffectivelyEnabled) pick(chosen); };
            BoosterOpening.EnableHoverLift(button, motion);
            row.Children.Add(button);
        }
        return row;
    }

}
