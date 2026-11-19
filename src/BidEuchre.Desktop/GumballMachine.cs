using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;
namespace BidEuchre.Desktop;

/// <summary>The shop's supplied gumball machine, fitted beside the shop's two rows of cards. Its globe holds the bids that are
/// still locked; <see cref="Dispense"/> plays the pull and shows the gumball that came out.</summary>
internal sealed class GumballMachine
{
    public const double FaceWidth = 206, FaceHeight = 406;
    private const double Width = FaceWidth - 6, Height = FaceHeight - 6, BallSize = 30, GlobeX = 100, GlobeY = 161;
    // The ball comes down inside the machine's door and rests on its sill.
    private const double CoinY = 234, ChuteY = 292, TrayY = 318, ShowY = 150;

    // Where gumballs lie in the globe, from the bottom up, as offsets from its centre.
    private static readonly (double X, double Y)[] Slots =
    [
        (-30, 39), (0, 41), (30, 39),
        (-48, 16), (-17, 14), (16, 16), (48, 14),
        (-49, -10), (-18, -12), (17, -10), (49, -9),
        (-32, -35), (0, -36), (32, -35)
    ];

    private readonly Canvas _canvas = new() { Width = Width, Height = Height, ClipToBounds = true };
    private readonly List<(GumballId Id, Control Ball, double X, double Y)> _pile = [];
    private readonly TranslateTransform _sway = new();
    private readonly RotateTransform _rock = new();
    private readonly Ellipse _coin;
    private readonly CancellationTokenSource _stop = new();

    public Border Face { get; }

    public static IBrush Colour(GumballId id) => Brush.Parse(id switch
    {
        GumballId.Lowballer => "#5BC8F5", GumballId.Chaos => "#FF5A5F", GumballId.Poker => "#3DDC84", GumballId.Duelist => "#FF9F1C",
        GumballId.Telepath => "#C77DFF", GumballId.PartnersBest => "#FF7EB6", GumballId.Alone => "#F4F4F8", GumballId.HighLow => "#1FA84A",
        GumballId.RollerCoaster => "#E35DB1", _ => "#FFD23F"
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
        // Keep the complete 2:3 machine intact. The spare vertical room belongs to the title and reveal animation.
        var artwork = PixelArt.Machine("machine-gumball", 196, 294);
        artwork.RenderTransform = new TransformGroup { Children = { _rock, _sway } };
        artwork.RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative);
        Add(artwork, 2, 53);

        // Two of each locked bid, so a nearly full machine looks full.
        var count = Math.Min(Slots.Length, locked.Count * 2);
        for (var i = 0; i < count; i++)
        {
            var id = locked[i % locked.Count];
            var (x, y) = (GlobeX + Slots[i].X - BallSize / 2, GlobeY + Slots[i].Y - BallSize / 2);
            var ball = Ball(id, BallSize);
            ball.Opacity = .9;
            _pile.Add((id, ball, x, y));
            Add(ball, x, y);
        }
        _coin = new Ellipse { Width = 18, Height = 18, Fill = Gold, Stroke = Brush.Parse("#B8860B"), StrokeThickness = 2, Opacity = 0 };
        Add(_coin, GlobeX - 9, CoinY);

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

    private static double Part(double t, double from, double to) => MachineFx.Part(t, from, to);
    private static double EaseOut(double t) => MachineFx.EaseOut(t);

    /// <summary>Coin in, the machine rocks and rattles its globe, a gumball drops into the door and bounces,
    /// then rises in a burst of light with the name of its bid. False when the machine left the screen before it finished.</summary>
    public async Task<bool> Dispense(GumballId result, string name, string subtitle = "UNLOCKED!")
    {
        var spin = new RotateTransform();
        var grow = new ScaleTransform(1, 1);
        var ball = Ball(result, BallSize);
        ball.RenderTransform = new TransformGroup { Children = { spin, grow } };
        ball.RenderTransformOrigin = RelativePoint.Center;
        ball.IsVisible = false;
        var veil = new Rectangle { Width = Width, Height = Height, Fill = Brush.Parse("#100B28"), Opacity = 0 };
        var burst = MachineFx.Burst(Width * 1.1, Gold);
        Canvas.SetLeft(burst, GlobeX - burst.Width / 2); Canvas.SetTop(burst, ShowY - burst.Height / 2);
        var title = new StackPanel
        {
            Width = Width - 8, Spacing = 4, Opacity = 0,
            Children = { CampaignShopScreen.Label(name.ToUpperInvariant(), Gold, 18), CampaignShopScreen.Label(subtitle, Green, 11) }
        };
        foreach (var line in title.Children.OfType<TextBlock>()) { line.TextAlignment = TextAlignment.Center; line.HorizontalAlignment = HorizontalAlignment.Center; }
        Canvas.SetLeft(title, 4); Canvas.SetTop(title, 252);
        _canvas.Children.Add(veil); _canvas.Children.Add(burst); _canvas.Children.Add(ball); _canvas.Children.Add(title);
        void Place(double x, double y) { Canvas.SetLeft(ball, x - BallSize / 2); Canvas.SetTop(ball, y - BallSize / 2); }

        try
        {
            await Tween.Run(1800, _stop.Token, t =>
            {
                var coin = Part(t, 0, .15);
                Canvas.SetTop(_coin, CoinY + 30 * coin * coin);
                _coin.Opacity = t <= 0 ? 0 : 1 - Math.Pow(coin, 4);

                // The crank turns: the machine rocks on its base and the globe's gumballs tumble.
                var shake = Math.Sin(Math.PI * Part(t, .12, .62));
                _sway.X = Math.Sin(t * 46) * 3 * shake;
                _sway.Y = -Math.Abs(Math.Sin(t * 23)) * 2 * shake;
                _rock.Angle = Math.Sin(t * 23) * 1.6 * shake;
                for (var i = 0; i < _pile.Count; i++)
                {
                    Canvas.SetLeft(_pile[i].Ball, _pile[i].X + Math.Sin(t * 40 + i * 1.9) * 6 * shake);
                    Canvas.SetTop(_pile[i].Ball, _pile[i].Y - Math.Abs(Math.Sin(t * 31 + i * 2.7)) * 9 * shake);
                }

                if (t < .6) return;
                if (!ball.IsVisible)
                {
                    ball.IsVisible = true;
                    // The one that fell is gone from the globe.
                    var taken = _pile.FindIndex(entry => entry.Id == result);
                    if (taken >= 0) _pile[taken].Ball.IsVisible = false;
                }
                // Down the chute onto the sill, two smaller bounces, then a little settling roll.
                var fall = Part(t, .6, .74);
                var bounce = Part(t, .74, .95);
                var height = fall < 1 ? (TrayY - ChuteY) * (1 - fall * fall)
                    : Math.Abs(Math.Sin(bounce * Math.PI * 2)) * 10 * (1 - bounce) * (bounce < .5 ? 1 : .45);
                var wobble = Math.Sin(Part(t, .74, 1) * Math.PI * 3) * 3 * (1 - Part(t, .74, 1));
                Place(GlobeX + wobble, TrayY - height);
                spin.Angle = wobble / (BallSize / 2) * 180 / Math.PI;
            });
            await Tween.Run(600, _stop.Token, t =>
            {
                var e = EaseOut(t);
                Place(GlobeX, TrayY + (ShowY - TrayY) * e);
                grow.ScaleX = grow.ScaleY = 1 + 3 * MachineFx.EaseBack(t);
                spin.Angle = 360 * e;
                veil.Opacity = .85 * e;
                MachineFx.Spin(burst, 40 * t, .3 + .7 * e, .8 * Part(t, .2, .7));
                title.Opacity = Part(t, .5, 1);
            });
            // The prize is held up while the light keeps turning behind it.
            await Tween.Run(1200, _stop.Token, t =>
            {
                MachineFx.Spin(burst, 40 + 50 * t, 1 + .04 * Math.Sin(t * Math.PI * 4), .8 - .25 * t);
                grow.ScaleX = grow.ScaleY = 4 + .12 * Math.Sin(t * Math.PI * 4);
            });
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }
}
