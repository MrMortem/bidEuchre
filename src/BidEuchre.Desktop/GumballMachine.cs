using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;
namespace BidEuchre.Desktop;

/// <summary>The shop's gumball machine, drawn as tall as the shop's two rows of cards. Its globe holds the bids that are
/// still locked; <see cref="Dispense"/> plays the pull and shows the gumball that came out.</summary>
internal sealed class GumballMachine
{
    public const double FaceWidth = 206, FaceHeight = 406;
    private const double Width = FaceWidth - 6, Height = FaceHeight - 6, BallSize = 40, GlobeX = 100, GlobeY = 126, GlobeRadius = 90;
    private const double CoinY = 186, ChuteY = 290, TrayY = 344, RestX = 162, ShowY = 150;

    // Where gumballs lie in the globe, from the bottom up, as offsets from its centre.
    private static readonly (double X, double Y)[] Slots =
    [
        (-38, 60), (0, 62), (38, 60),
        (-60, 30), (-21, 28), (20, 30), (60, 28),
        (-64, -4), (-23, -6), (22, -4), (64, -3),
        (-45, -36), (-4, -38), (39, -35),
        (-19, -66), (22, -67)
    ];

    private readonly Canvas _canvas = new() { Width = Width, Height = Height, ClipToBounds = true };
    private readonly List<(GumballId Id, Control Ball, double X, double Y)> _pile = [];
    private readonly RotateTransform _crank = new();
    private readonly Ellipse _coin;
    private readonly CancellationTokenSource _stop = new();

    public Border Face { get; }

    public static IBrush Colour(GumballId id) => Brush.Parse(id switch
    {
        GumballId.Lowballer => "#5BC8F5", GumballId.Chaos => "#FF5A5F", GumballId.Poker => "#3DDC84", GumballId.Duelist => "#FF9F1C",
        GumballId.Telepath => "#C77DFF", GumballId.PartnersBest => "#FF7EB6", GumballId.Alone => "#F4F4F8", GumballId.HighLow => "#1FA84A", _ => "#FFD23F"
    });

    /// <summary>One gumball: its sprite when it has one, otherwise a ball in its bid's colour with a
    /// shine so it can be seen to roll.</summary>
    public static Control Ball(GumballId id, double size) => PixelArt.Gumball(id, size) ?? Plain(id, size);

    private static Control Plain(GumballId id, double size) => new Grid
    {
        Width = size, Height = size, IsHitTestVisible = false,
        Children =
        {
            // Sprites carry a glow around the ball, so a plain one is drawn a little inside its box to match.
            new Ellipse { Fill = Colour(id), Stroke = Brush.Parse("#66100B28"), StrokeThickness = Math.Max(1, size / 14), Margin = new Thickness(size * .07) },
            new Ellipse
            {
                Width = size * .3, Height = size * .22, Fill = Brushes.White, Opacity = .75,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(size * .26, size * .22, 0, 0)
            }
        }
    };

    public GumballMachine(IReadOnlyList<GumballId> locked, string tag)
    {
        void Add(Control control, double x, double y) { Canvas.SetLeft(control, x); Canvas.SetTop(control, y); _canvas.Children.Add(control); }
        Rectangle Block(double width, double height, string colour, double radius = 0) =>
            new() { Width = width, Height = height, Fill = Brush.Parse(colour), RadiusX = radius, RadiusY = radius };

        // Two of each locked bid, so a nearly full machine looks full.
        var count = Math.Min(Slots.Length, locked.Count * 2);
        for (var i = 0; i < count; i++)
        {
            var id = locked[i % locked.Count];
            var (x, y) = (GlobeX + Slots[i].X - BallSize / 2, GlobeY + Slots[i].Y - BallSize / 2);
            var ball = Ball(id, BallSize);
            _pile.Add((id, ball, x, y));
            Add(ball, x, y);
        }
        Add(new Ellipse
        {
            Width = GlobeRadius * 2, Height = GlobeRadius * 2, Fill = Brush.Parse("#30BFE8FF"), Stroke = Brush.Parse("#BFE8FF"), StrokeThickness = 4
        }, GlobeX - GlobeRadius, GlobeY - GlobeRadius);
        Add(new Ellipse { Width = 28, Height = 56, Fill = Brushes.White, Opacity = .22, RenderTransform = new RotateTransform(28) }, 44, 58);
        Add(Block(64, 16, "#D8344A", 4), GlobeX - 32, 24);

        Add(Block(160, 118, "#D8344A", 8), 20, 200);
        Add(Block(160, 10, "#F0566A", 5), 20, 200);
        Add(Block(160, 10, "#9E2238"), 20, 308);
        Add(Block(30, 8, "#100B28", 3), 136, 222);
        Add(Block(56, 30, "#100B28", 6), 72, 274);
        // The handle turns about the middle of its own square, which sits on the knob.
        Add(new Border
        {
            Width = 56, Height = 56, RenderTransform = _crank, RenderTransformOrigin = RelativePoint.Center,
            Child = new Rectangle { Width = 9, Height = 28, Fill = Brush.Parse("#F4F4F8"), VerticalAlignment = VerticalAlignment.Top, RadiusX = 3, RadiusY = 3 }
        }, 72, 216);
        Add(new Ellipse { Width = 26, Height = 26, Fill = Brush.Parse("#D9D9E8"), Stroke = Brush.Parse("#7C7C96"), StrokeThickness = 3 }, 87, 231);
        Add(new Rectangle
        {
            Width = 184, Height = 48, Fill = Brush.Parse("#241A4A"), Stroke = MainWindow.Line, StrokeThickness = 2, RadiusX = 4, RadiusY = 4
        }, 8, 320);
        Add(Block(22, 12, "#9E2238", 2), 30, 368);
        Add(Block(22, 12, "#9E2238", 2), 148, 368);
        _coin = new Ellipse { Width = 18, Height = 18, Fill = Gold, Stroke = Brush.Parse("#B8860B"), StrokeThickness = 2, Opacity = 0 };
        Add(_coin, 142, CoinY);

        var kind = CampaignShopScreen.Label("GUMBALLS", Green, 9);
        kind.Margin = new Thickness(7, 5, 0, 0);
        var price = new Border
        {
            Background = Gold, Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Name = "GumballTag", Text = tag, Foreground = Paper, FontSize = 13, FontFamily = PixelArt.Font, FontWeight = FontWeight.Bold }
        };
        Face = new Border
        {
            Width = FaceWidth, Height = FaceHeight, Background = MainWindow.Panel, BorderBrush = Green, BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = new Grid { Children = { _canvas, kind, price } }
        };
        // Leaving the shop mid-pull stops the animation; the purchase is already saved.
        Face.DetachedFromVisualTree += (_, _) => _stop.Cancel();
    }

    private static double Part(double t, double from, double to) => Math.Clamp((t - from) / (to - from), 0, 1);
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>Coin in, crank round, a gumball down the chute and along the tray, then held up with the
    /// name of its bid. False when the machine left the screen before it finished.</summary>
    public async Task<bool> Dispense(GumballId result, string name)
    {
        var spin = new RotateTransform();
        var grow = new ScaleTransform(1, 1);
        var ball = Ball(result, BallSize);
        ball.RenderTransform = new TransformGroup { Children = { spin, grow } };
        ball.RenderTransformOrigin = RelativePoint.Center;
        ball.IsVisible = false;
        var veil = new Rectangle { Width = Width, Height = Height, Fill = Brush.Parse("#100B28"), Opacity = 0 };
        var title = new StackPanel
        {
            Width = Width - 8, Spacing = 4, Opacity = 0,
            Children = { CampaignShopScreen.Label(name.ToUpperInvariant(), Gold, 18), CampaignShopScreen.Label("UNLOCKED!", Green, 11) }
        };
        foreach (var line in title.Children.OfType<TextBlock>()) { line.TextAlignment = TextAlignment.Center; line.HorizontalAlignment = HorizontalAlignment.Center; }
        Canvas.SetLeft(title, 4); Canvas.SetTop(title, 252);
        _canvas.Children.Add(veil); _canvas.Children.Add(ball); _canvas.Children.Add(title);
        void Place(double x, double y) { Canvas.SetLeft(ball, x - BallSize / 2); Canvas.SetTop(ball, y - BallSize / 2); }

        try
        {
            await Tween.Run(1700, _stop.Token, t =>
            {
                var coin = Part(t, 0, .15);
                Canvas.SetTop(_coin, CoinY + 30 * coin * coin);
                _coin.Opacity = t <= 0 ? 0 : 1 - Math.Pow(coin, 4);

                var turn = Part(t, .12, .7);
                _crank.Angle = 360 * (turn < .5 ? 2 * turn * turn : 1 - 2 * (1 - turn) * (1 - turn));
                var shake = Math.Sin(Math.PI * Part(t, .12, .72));
                for (var i = 0; i < _pile.Count; i++)
                {
                    Canvas.SetLeft(_pile[i].Ball, _pile[i].X + Math.Sin(t * 40 + i * 1.9) * 5 * shake);
                    Canvas.SetTop(_pile[i].Ball, _pile[i].Y - Math.Abs(Math.Sin(t * 31 + i * 2.7)) * 7 * shake);
                }

                if (t < .6) return;
                if (!ball.IsVisible)
                {
                    ball.IsVisible = true;
                    // The one that fell is gone from the globe.
                    var taken = _pile.FindIndex(entry => entry.Id == result);
                    if (taken >= 0) _pile[taken].Ball.IsVisible = false;
                }
                var drop = Part(t, .6, .75);
                var roll = Part(t, .75, 1);
                var along = (RestX - GlobeX) * EaseOut(roll);
                Place(GlobeX + along, ChuteY + (TrayY - ChuteY) * drop * drop - Math.Abs(Math.Sin(roll * Math.PI * 2)) * 8 * (1 - roll));
                spin.Angle = along / (BallSize / 2) * 180 / Math.PI;
            });
            var rolled = spin.Angle;
            await Tween.Run(450, _stop.Token, t =>
            {
                var e = EaseOut(t);
                // It finishes the turn it was on, so its shine ends the right way up.
                spin.Angle = rolled + (360 - rolled) * e;
                Place(RestX + (GlobeX - RestX) * e, TrayY + (ShowY - TrayY) * e);
                grow.ScaleX = grow.ScaleY = 1 + 3 * e;
                veil.Opacity = .85 * e;
                title.Opacity = Part(t, .5, 1);
            });
            await Tween.Run(1100, _stop.Token, _ => { });
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }
}
