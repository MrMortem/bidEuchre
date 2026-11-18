using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
namespace BidEuchre.Desktop;

/// <summary>Small effects shared by the shop's machines: a ray burst behind a prize, a glow, and a twinkling star.</summary>
internal static class MachineFx
{
    public static double Part(double t, double from, double to) => Math.Clamp((t - from) / (to - from), 0, 1);
    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    /// <summary>Overshoots a little before settling, for things that pop into place.</summary>
    public static double EaseBack(double t) { const double c = 1.7; var u = t - 1; return 1 + (c + 1) * u * u * u + c * u * u; }

    /// <summary>Rays fanning out from the centre of a square of <paramref name="size"/>; turn it with its RotateTransform.</summary>
    public static Canvas Burst(double size, IBrush ray, int rays = 12)
    {
        var canvas = new Canvas
        {
            Width = size, Height = size, IsHitTestVisible = false, Opacity = 0,
            RenderTransform = new TransformGroup { Children = { new RotateTransform(), new ScaleTransform(1, 1) } },
            RenderTransformOrigin = RelativePoint.Center
        };
        var c = size / 2;
        for (var i = 0; i < rays; i++)
        {
            var a = Math.PI * 2 * i / rays;
            var w = Math.PI / rays * .55;
            canvas.Children.Add(new Polygon
            {
                Fill = ray, Opacity = i % 2 == 0 ? .85 : .45,
                Points = [new Point(c, c), new Point(c + Math.Cos(a - w) * c, c + Math.Sin(a - w) * c), new Point(c + Math.Cos(a + w) * c, c + Math.Sin(a + w) * c)]
            });
        }
        return canvas;
    }

    public static void Spin(Canvas burst, double angle, double scale, double opacity)
    {
        var group = (TransformGroup)burst.RenderTransform!;
        ((RotateTransform)group.Children[0]).Angle = angle;
        var grow = (ScaleTransform)group.Children[1];
        grow.ScaleX = grow.ScaleY = scale;
        burst.Opacity = opacity;
    }

    /// <summary>A soft round glow in <paramref name="colour"/>, fading to nothing at its edge.</summary>
    public static Ellipse Glow(double size, Color colour) => new()
    {
        Width = size, Height = size, IsHitTestVisible = false, Opacity = 0,
        Fill = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(colour, 0), new GradientStop(Color.FromArgb((byte)(colour.A * .45), colour.R, colour.G, colour.B), .45), new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1) }
        }
    };

    /// <summary>A four-pointed twinkle.</summary>
    public static Polygon Star(double size, IBrush fill)
    {
        var c = size / 2; var n = size * .14;
        return new Polygon
        {
            Width = size, Height = size, Fill = fill, IsHitTestVisible = false, Opacity = 0,
            RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = RelativePoint.Center,
            Points = [new Point(c, 0), new Point(c + n, c - n), new Point(size, c), new Point(c + n, c + n), new Point(c, size), new Point(c - n, c + n), new Point(0, c), new Point(c - n, c - n)]
        };
    }
}
