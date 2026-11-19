using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// The lid and can share a transform. Fluid leaves the mouth wherever the can was when it was poured,
// then falls in stage coordinates, so the stream trails behind the can as it tips back.
internal sealed class SodaOpening : Canvas
{
    public const double Duration = 2.3;
    private const double PopAt = .2, PourAt = .74;
    private const double FallTime = .32, Shutoff = .64, SplashFade = .30;
    private static readonly Point Impact = new(263, 390);
    private readonly Point _mouth;
    private readonly Image _lid;
    private readonly Pour _pour;
    private readonly Fizz _fizz;
    private readonly Splash _splash;
    private readonly MatrixTransform _pose = new();

    public SodaOpening(Image art, SodaId id)
    {
        Width = 300;
        Height = 416;
        ClipToBounds = true;
        // Register the full cap and its mouth in the same source-pixel space as the can art.
        var source = art.Source!.Size;
        var scale = Math.Min(art.Width / source.Width, art.Height / source.Height);
        var lidOffset = id is SodaId.PackAPunch or SodaId.PeachPlus ? -4 : 0;
        var lidCenter = new Point(art.Width / 2 + lidOffset * scale, (art.Height - source.Height * scale) / 2);
        var lid = PixelArt.SodaTopFrame(0);
        _lid = new Image { Name = "OpenedSodaLid", Source = lid, Width = lid.Size.Width * scale,
            Height = lid.Size.Height * scale, IsHitTestVisible = false };
        RenderOptions.SetBitmapInterpolationMode(_lid, BitmapInterpolationMode.HighQuality);
        Canvas.SetLeft(_lid, lidCenter.X - _lid.Width / 2);
        Canvas.SetTop(_lid, lidCenter.Y - 13 * scale);
        _mouth = lidCenter + new Point(24 * scale, 16 * scale);
        var liquid = Liquid(id);
        _pour = new Pour(liquid, t => Pose(PourAt + t).Transform(_mouth)) { Width = Width, Height = Height, Name = "SodaPour" };
        _fizz = new Fizz(liquid) { Width = Width, Height = Height };
        _splash = new Splash(liquid) { Width = Width, Height = Height, Name = "SodaSplash" };
        var can = new Canvas { Width = art.Width, Height = art.Height, RenderTransform = _pose,
            RenderTransformOrigin = RelativePoint.TopLeft, Children = { art, _lid } };
        Children.Add(can);
        Children.Add(_pour);
        Children.Add(_fizz);
        Children.Add(_splash);
        SetTime(Duration);
    }

    public void SetTime(double seconds)
    {
        var pose = Pose(seconds);
        _pose.Matrix = pose;
        _lid.IsVisible = seconds >= PopAt;
        var elapsed = seconds - PopAt;
        var lift = Ease(elapsed / .09) * (1 - Ease((elapsed - .12) / .24));
        if (_lid.IsVisible)
            _lid.Source = PixelArt.SodaTopFrame((int)Math.Round(lift * (PixelArt.SodaTopFrames - 1)));
        _fizz.Origin = pose.Transform(_mouth);
        _fizz.Time = elapsed;
        _fizz.IsVisible = _fizz.Time is > 0 and < .8;
        _fizz.InvalidateVisual();
        _pour.Time = seconds - PourAt;
        _pour.IsVisible = _pour.Time is > 0 and < FallTime + Shutoff;
        _pour.InvalidateVisual();
        _splash.Time = _pour.Time - FallTime;
        _splash.IsVisible = _splash.Time is >= 0 and < Shutoff + SplashFade;
        _splash.InvalidateVisual();
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
            * Matrix.CreateRotation(tilt * 1.77 + rattle + settle) * Matrix.CreateTranslation(150 + 65 * tilt + swing, 67 + 105 * tilt);
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

        // Bend every part of the stream toward the same point on the stage floor.
        private static Point Along(Point mouth, double t) =>
            new(mouth.X + (Impact.X - mouth.X) * t * (1.5 - .5 * t),
                mouth.Y + (Impact.Y - mouth.Y) * t * t);

        public override void Render(DrawingContext context)
        {
            var now = Time;
            var frame = (int)Math.Floor(now * 20);
            var head = Math.Min(1, now / FallTime);
            var tail = Math.Max(0, (now - Shutoff) / FallTime);
            for (var i = 0; i <= 160; i++)
            {
                var t = i / 160d;
                if (t < tail || t > head) continue;
                var point = Along(mouthAt(now - FallTime * t), t);
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
                var from = Along(mouthAt(Math.Max(0, born - FallTime * t)), t);
                var x = Cell(from.X + (Noise(j, 5) - .4) * 100 * age);
                var y = Cell(from.Y - 30 * age + 450 * age * age);
                if (y + 2 < Cell(Impact.Y)) Drop(context, ink, x, y);
            }
        }
    }

    private sealed class Splash(Palette ink) : Control
    {
        public double Time;

        public override void Render(DrawingContext context)
        {
            var x = Cell(Impact.X);
            var y = Cell(Impact.Y) + 1;
            var width = 4 + (int)(7 * Ease(Time / .25));
            using (context.PushOpacity(1 - Ease((Time - Shutoff) / SplashFade)))
            {
                Block(context, ink.Shade, x - width / 2 - 1, y, width + 2, 2);
                Block(context, ink.Body, x - width / 2, y, width, 1);
                Block(context, ink.Glint, x - width / 4, y, width / 2, 1);
            }

            for (var i = 0; i < 12; i++)
            {
                var age = Time - i * .055;
                var life = .15 + .08 * Noise(i, 8);
                if (age is < 0 || age >= life) continue;
                var spread = 42 + 43 * Noise(i, 9);
                var dx = (i % 2 == 0 ? -1 : 1) * spread * age;
                var dy = (145 + 50 * Noise(i, 10)) * age * (1 - age / life);
                Drop(context, ink, x + Cell(dx), y - 2 - Cell(dy));
            }
        }
    }

    private static void Drop(DrawingContext ctx, Palette ink, int x, int y)
    {
        Block(ctx, ink.Shade, x, y + 1, 3, 2);
        Block(ctx, ink.Body, x, y, 2, 2);
        Block(ctx, ink.Glint, x, y, 1, 1);
    }
}
