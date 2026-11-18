using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>A card's shadow or glow drawn by its own unclipped layer. Card faces clip to their rounded
/// bounds, and a clipped border draws no box shadow, so the shadow cannot live on the card itself.</summary>
internal sealed class CardShadow
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _turn = new();
    private readonly TranslateTransform _shift = new();

    public Border Visual { get; }

    /// <param name="card">The card this follows: its opacity is mirrored so the shadow hides with it.</param>
    public CardShadow(Control card, double width, double height, double radius)
    {
        Visual = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius), Background = Brushes.Transparent,
            IsHitTestVisible = false, RenderTransformOrigin = card.RenderTransformOrigin,
            RenderTransform = new TransformGroup { Children = { _scale, _turn, _shift } }
        };
        Visual[!Avalonia.Visual.OpacityProperty] = card[!Avalonia.Visual.OpacityProperty];
    }

    /// <summary>Copies the card's transform and lifts the shadow to <paramref name="height"/> (0 resting, 1 highest).</summary>
    public void Follow(double scaleX, double scaleY, double angle, double x, double y, double height)
    {
        _scale.ScaleX = scaleX;
        _scale.ScaleY = scaleY;
        _turn.Angle = angle;
        _shift.X = x;
        _shift.Y = y;
        Visual.BoxShadow = Drop(height);
    }

    /// <summary>A card's shadow on the felt: tight at rest and softer and further away as it rises.</summary>
    public static BoxShadows Drop(double height) => new(new BoxShadow
    {
        OffsetX = 2 + 6 * height, OffsetY = 3 + 12 * height, Blur = 4 + 14 * height,
        Color = Color.FromArgb((byte)(110 - 40 * height), 0, 0, 0)
    });

    /// <summary>A glow sized and aligned like <paramref name="card"/>, to place just beneath it in the same panel.</summary>
    public static Border Glow(Control card, BoxShadows glow, double radius = 10)
    {
        var halo = new Border
        {
            Width = card.Width, Height = card.Height, Margin = card.Margin, CornerRadius = new CornerRadius(radius),
            HorizontalAlignment = card.HorizontalAlignment, VerticalAlignment = card.VerticalAlignment,
            Background = Brushes.Transparent, BoxShadow = glow, IsHitTestVisible = false, ZIndex = card.ZIndex - 1
        };
        halo[!Avalonia.Visual.OpacityProperty] = card[!Avalonia.Visual.OpacityProperty];
        return halo;
    }
}
