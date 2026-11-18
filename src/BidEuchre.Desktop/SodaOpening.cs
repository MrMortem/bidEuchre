using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// The lid and can share a transform. Fluid leaves the mouth wherever the can was when it was poured,
// then falls in stage coordinates, so the stream trails behind the can as it tips back.
internal sealed class SodaOpening : Canvas
{
    public const double Duration = 2.3;
    private const double PopAt = .2, PourAt = .74;
    private readonly Point _mouth;
    private readonly Image _lid;
    private readonly Pour _pour;
    private readonly Fizz _fizz;
    private readonly MatrixTransform _pose = new();

    public SodaOpening(Image art, SodaId id)
    {
        Width = 300;
        Height = 396;
        ClipToBounds = true;
        // Draw only the puncture beside the original diagonal tab. Register it in source pixels
        // so the lid's foreshortening, painted highlights and rim stay with each can.
        var source = art.Source!.Size;
        var scale = Math.Min(art.Width / source.Width, art.Height / source.Height);
        var left = art.Width / 2 + 11 * scale;
        var top = (art.Height - source.Height * scale) / 2 + 10 * scale;
        _lid = PixelArt.Machine("open-soda-lid", 19 * scale, 8 * scale);
        _lid.Name = "OpenedSodaLid";
        Canvas.SetLeft(_lid, left);
        Canvas.SetTop(_lid, top);
        _mouth = new Point(left + 12 * scale, top + 4 * scale);
        var liquid = Liquid(id);
        _pour = new Pour(liquid, t => Pose(PourAt + t).Transform(_mouth)) { Width = Width, Height = Height, Name = "SodaPour" };
        _fizz = new Fizz(liquid) { Width = Width, Height = Height };
        var can = new Canvas { Width = art.Width, Height = art.Height, RenderTransform = _pose,
            RenderTransformOrigin = RelativePoint.TopLeft, Children = { art, _lid } };
        Children.Add(can);
        Children.Add(_pour);
        Children.Add(_fizz);
        SetTime(Duration);
    }

    public void SetTime(double seconds)
    {
        var pose = Pose(seconds);
        _pose.Matrix = pose;
        _lid.IsVisible = seconds >= PopAt;
        _fizz.Origin = pose.Transform(_mouth);
        _fizz.Time = seconds - PopAt;
        _fizz.IsVisible = _fizz.Time is > 0 and < .8;
        _fizz.InvalidateVisual();
        _pour.Time = seconds - PourAt;
        _pour.IsVisible = _pour.Time is > 0 and < 1.4;
        _pour.InvalidateVisual();
    }

    private static Matrix Pose(double seconds)
    {
        // Pressure rattles the can before the tab, it leans back, tips past level, then rights itself with a wobble.
        var rattle = seconds is > 0 and < PopAt ? .035 * Math.Sin(seconds * 95) * seconds / PopAt : 0;
        var lean = -.08 * Math.Sin(Math.PI * Math.Clamp((seconds - .28) / .2, 0, 1));
        var tip = BackOut((seconds - .42) / .5);
        var back = Ease((seconds - 1.66) / .44);
        var tilt = tip * (1 - back) + lean;
        var settle = seconds is > 2.04 and < Duration ? .08 * Math.Sin((seconds - 2.04) * 26) * (Duration - seconds) / (Duration - 2.04) : 0;
        var scale = 1 - .43 * Math.Clamp(tilt, 0, 1.1);
        // Mid-swing the can is longest sideways; carry the pivot right so its base stays on stage.
        var swing = 50 * Math.Sin(Math.PI * Math.Clamp(tilt, 0, 1));
        return Matrix.CreateTranslation(-89, -20) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateRotation(tilt * 1.77 + rattle + settle) * Matrix.CreateTranslation(150 + 65 * tilt + swing, 47 + 105 * tilt);
    }

    private static double Ease(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double BackOut(double value)
    {
        var t = Math.Clamp(value, 0, 1) - 1;
        return 1 + 2.7 * t * t * t + 1.7 * t * t;
    }

    // Stable per-particle noise in [0, 1).
    private static double Noise(int i, int k)
    {
        var v = Math.Sin(i * 12.9898 + k * 78.233) * 43758.5453;
        return v - Math.Floor(v);
    }

    private static Palette Liquid(SodaId id) => new(Color.Parse(id switch
    {
        SodaId.OvertimeOrange or SodaId.TwilightTangerine or SodaId.PeachPlus => "#FF994E",
        SodaId.LimitlessLime or SodaId.GhostlyGuava => "#A8E363",
        SodaId.CitrusSale => "#FFCF58",
        SodaId.MidnightGrape or SodaId.GalaxAde => "#B188F2",
        SodaId.CosmicCola => "#C77D50",
        SodaId.SpectralSprite => "#70E2D7",
        _ => "#FA83AA"
    }));

    private sealed class Palette(Color color)
    {
        public readonly IBrush Body = new SolidColorBrush(color);
        public readonly IBrush Shade = new SolidColorBrush(Color.FromRgb(
            (byte)(color.R * .52), (byte)(color.G * .49), (byte)(color.B * .61)));
        public readonly IBrush Glint = new SolidColorBrush(Color.FromRgb(
            (byte)((color.R + 255) / 2), (byte)((color.G + 243) / 2), (byte)((color.B + 223) / 2)));
    }

    // Liquid is drawn on a fixed two-unit pixel grid. Every contour, glint and bead uses whole cells.
    private const int Pixel = 2;
    private static int Cell(double coordinate) => (int)Math.Round(coordinate / Pixel);
    private static void Block(DrawingContext ctx, IBrush ink, int x, int y, int width, int height)
        => ctx.FillRectangle(ink, new Rect(x * Pixel, y * Pixel, width * Pixel, height * Pixel));

    private sealed class Fizz(Palette ink) : Control
    {
        public Point Origin;
        public double Time;

        public override void Render(DrawingContext context)
        {
            var frame = Math.Floor(Time * 18) / 18;
            for (var i = 0; i < 12; i++)
            {
                var life = .35 + .4 * Noise(i, 2);
                if (frame > life) continue;
                var angle = -Math.PI / 2 + (Noise(i, 0) - .5) * 1.7;
                var speed = 150 + 170 * Noise(i, 1);
                var x = Cell(Origin.X + Math.Cos(angle) * speed * frame);
                var y = Cell(Origin.Y + Math.Sin(angle) * speed * frame + 520 * frame * frame);
                var size = frame > life * .65 ? 1 : 2;
                Block(context, ink.Body, x, y, size, size);
                Block(context, ink.Glint, x, y, 1, 1);
            }
        }
    }

    private sealed class Pour(Palette ink, Func<double, Point> mouthAt) : Control
    {
        public double Time;

        private static Point Along(Point mouth, double t) =>
            new(mouth.X + 62 * t - 22 * t * t, mouth.Y + 260 * t * t);

        public override void Render(DrawingContext context)
        {
            var frame = (int)Math.Floor(Time * 20);
            var now = frame / 20d;
            const double fallTime = .32, shutoff = .64;
            var head = Math.Min(1, now / fallTime);
            var tail = Math.Max(0, (now - shutoff) / fallTime);
            for (var i = 0; i <= 160; i++)
            {
                var t = i / 160d;
                if (t < tail || t > head) continue;
                var point = Along(mouthAt(now - fallTime * t), t);
                var x = Cell(point.X);
                var y = Cell(point.Y);
                // Travelling bands broaden the glugs and carry the highlights down the ribbon.
                var band = (i + 192 - frame * 5) % 32;
                var width = (t < .3 ? 5 : 4) + (band < 8 ? 1 : 0);
                Block(context, ink.Shade, x - 2, y, width, 2);
                Block(context, ink.Body, x - 2, y, width - 1, 2);
                if (band is >= 4 and < 13) Block(context, ink.Glint, x - 2, y, 1, 2);
            }

            for (var j = 0; j < 9; j++)
            {
                var born = .1 + j * .065;
                var age = now - born;
                if (age is < 0 or > .35) continue;
                var t = .24 + .45 * Noise(j, 4);
                var from = Along(mouthAt(Math.Max(0, born - fallTime * t)), t);
                var x = Cell(from.X + (Noise(j, 5) - .4) * 100 * age);
                var y = Cell(from.Y - 30 * age + 450 * age * age);
                Drop(context, x, y);
            }

            if (now is >= .88 and < 1.4)
            {
                var mouth = mouthAt(Math.Min(now, 1));
                var fall = Math.Max(0, now - 1);
                Drop(context, Cell(mouth.X), Cell(mouth.Y + 450 * fall * fall));
            }
        }

        private void Drop(DrawingContext ctx, int x, int y)
        {
            Block(ctx, ink.Shade, x, y + 1, 3, 2);
            Block(ctx, ink.Body, x, y, 2, 2);
            Block(ctx, ink.Glint, x, y, 1, 1);
        }
    }
}
