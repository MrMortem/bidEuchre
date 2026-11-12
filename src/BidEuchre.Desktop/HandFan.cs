using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>Overlapping cards facing the table, with the portrait behind the hand.</summary>
internal static class HandFan
{
    public static double Angle(int seat, int index, int count) =>
        (index - (count - 1) / 2d) * 6 + (seat switch { 1 => 90, 2 => 180, 3 => -90, _ => 0 });

    public static Control Arrange(int seat, Control portrait, IReadOnlyList<Control> cards, PixelMotion motion)
    {
        var side = seat is 1 or 3;
        var panel = new Canvas { Width = side ? 190 : 410, Height = side ? 250 : 176 };
        portrait.Width = side ? 76 : 260;
        if (side && portrait is Border { Child: StackPanel content })
            content.Orientation = Avalonia.Layout.Orientation.Vertical;
        Canvas.SetLeft(portrait, seat switch { 1 => 0, 3 => 114, _ => 75 });
        Canvas.SetTop(portrait, side ? 80 : seat == 0 ? 104 : 0);
        panel.Children.Add(portrait);

        var fan = new Canvas { Width = side ? 250 : 360, Height = 110 };
        var step = side ? 29d : 43d;
        var midpoint = (cards.Count - 1) / 2d;
        for (var index = 0; index < cards.Count; index++)
        {
            var layer = index;
            var offset = index - midpoint;
            // Rotation belongs to its own wrapper so hover and deal transforms survive.
            var card = new Border
            {
                Width = 66, Height = 92, Child = motion.Float(cards[index], seat * 7 + index),
                RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative),
                RenderTransform = new RotateTransform(offset * 6), ZIndex = index
            };
            Canvas.SetLeft(card, fan.Width / 2 - 33 + offset * step);
            Canvas.SetTop(card, 4 + offset * offset * 2);
            void UpdateLayer() => card.ZIndex = card.IsPointerOver || card.IsKeyboardFocusWithin ? 20 : layer;
            card.PointerEntered += (_, _) => UpdateLayer();
            card.PointerExited += (_, _) => UpdateLayer();
            card.GotFocus += (_, _) => UpdateLayer();
            card.LostFocus += (_, _) => UpdateLayer();
            fan.Children.Add(card);
        }
        if (side)
        {
            fan.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
            fan.RenderTransform = new RotateTransform(seat == 1 ? 90 : -90);
            Canvas.SetLeft(fan, seat == 1 ? 5 : -65);
            Canvas.SetTop(fan, 70);
        }
        else
        {
            if (seat == 2)
            {
                fan.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
                fan.RenderTransform = new RotateTransform(180);
            }
            Canvas.SetLeft(fan, 25);
            Canvas.SetTop(fan, seat == 0 ? 0 : 66);
        }
        panel.Children.Add(fan);
        return panel;
    }
}
