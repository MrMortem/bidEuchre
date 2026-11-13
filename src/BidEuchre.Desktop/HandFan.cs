using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>Overlapping cards facing the table, with the portrait behind the hand.</summary>
internal static class HandFan
{
    /// <summary>Cards on the table are drawn at this fraction of their layout size.</summary>
    public const double CardScale = .75;

    // Shrinks a fan toward the edge its portrait sits on, then turns it to face the table.
    private static TransformGroup Facing(double height, double angle) => new()
    {
        Children =
        {
            new ScaleTransform(CardScale, CardScale),
            new TranslateTransform(0, height * (1 - CardScale) / 2),
            new RotateTransform(angle)
        }
    };

    public static double Angle(int seat, int index, int count) =>
        (index - (count - 1) / 2d) * 6 + (seat switch { 1 => 90, 2 => 180, 3 => -90, _ => 0 });

    public static Control Arrange(int seat, Control portrait, IReadOnlyList<Control> cards, PixelMotion motion)
    {
        var side = seat is 1 or 3;
        var panel = new Canvas { Width = side ? 230 : 500, Height = side ? 400 : 208 };
        portrait.Width = side ? 76 : 260;
        portrait.ZIndex = 30;
        if (side && portrait is Border { Child: StackPanel content })
            content.Orientation = Avalonia.Layout.Orientation.Vertical;
        Canvas.SetLeft(portrait, seat switch { 1 => 0, 3 => 154, _ => 120 });
        Canvas.SetTop(portrait, side ? 120 : seat == 0 ? 136 : 0);
        panel.Children.Add(portrait);

        var fan = new Canvas { Width = side ? 400 : 500, Height = 180 };
        var step = side ? 42d : 56d;
        var midpoint = (cards.Count - 1) / 2d;
        for (var index = 0; index < cards.Count; index++)
        {
            var layer = index;
            var offset = index - midpoint;
            // Rotation belongs to its own wrapper so hover and deal transforms survive.
            var card = new Border
            {
                Width = 128, Height = 180, Child = motion.Float(cards[index], seat * 7 + index),
                RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative),
                RenderTransform = new RotateTransform(offset * 6), ZIndex = index
            };
            Canvas.SetLeft(card, fan.Width / 2 - 64 + offset * step);
            Canvas.SetTop(card, 4 + offset * offset * 2);
            void UpdateLayer() => card.ZIndex = card.IsPointerOver || card.IsKeyboardFocusWithin ? 20 : layer;
            card.PointerEntered += (_, _) => UpdateLayer();
            card.PointerExited += (_, _) => UpdateLayer();
            card.GotFocus += (_, _) => UpdateLayer();
            card.LostFocus += (_, _) => UpdateLayer();
            fan.Children.Add(card);
        }
        fan.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        fan.RenderTransform = Facing(fan.Height, seat switch { 1 => 90, 2 => 180, 3 => -90, _ => 0 });
        if (side)
        {
            Canvas.SetLeft(fan, seat == 1 ? -40 : -130);
            Canvas.SetTop(fan, 70);
        }
        else
        {
            Canvas.SetLeft(fan, 0);
            Canvas.SetTop(fan, seat == 0 ? 0 : 62);
        }
        panel.Children.Add(fan);
        return panel;
    }
}
