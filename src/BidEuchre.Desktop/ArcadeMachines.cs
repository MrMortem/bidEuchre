using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using BidEuchre.App;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>The board follows the saved peg decisions, including reflected bounces at either edge.
/// Every frame is a pure function of progress, so a restored result draws its final pose directly.</summary>
internal sealed class PlinkoControl : Viewbox
{
    private const double BallSize = 34, NozzleY = 290, DropperY = 185, BinY = 1082;
    // Relative durations: the first fall from the dropper, each peg hop, and rolling to rest in the bin.
    private const double DropWeight = 1.5, HopWeight = 1, SettleWeight = 1.8;
    private readonly Canvas _board = new() { Width = 1199, Height = 1312 };
    private readonly Control _ball;
    private readonly ScaleTransform _squash = new(1, 1);
    private readonly Ellipse[] _trail;
    private readonly Ellipse _pegFlash, _binGlow;
    private readonly Control _dropper;
    private static readonly double[] Rows = [342, 393, 444, 495, 545, 596, 647, 698, 750, 804, 856, 909, 963, 1017, 1082];
    private bool _dragging;
    private int _lane = 4;

    /// <summary>Called with the nearest lane while the player drags the dropper, and when they let go.</summary>
    public Action<int>? LaneDragged { get; set; }

    /// <summary>Whether the dropper can be dragged to choose a lane.</summary>
    public bool Draggable
    {
        get => _draggable;
        set { _draggable = value; _dropper.Cursor = value ? new Cursor(StandardCursorType.SizeWestEast) : null; }
    }
    private bool _draggable;

    public PlinkoControl()
    {
        Name = "PlinkoBoard"; Width = 420; Height = 460; Child = _board;
        if (Sprite("arcade-plinko-board", _board.Width, _board.Height) is { } artwork) _board.Children.Add(artwork);
        else
        {
            _board.Children.Add(new Border
            {
                Width = _board.Width, Height = _board.Height, Background = Brush.Parse("#15112D"), BorderBrush = Gold,
                BorderThickness = new Thickness(8), CornerRadius = new CornerRadius(22)
            });
            for (var row = 0; row < CampaignArcade.PlinkoSteps; row++)
                for (var half = CampaignArcade.PlinkoRowMinimums[row]; half <= 26 - CampaignArcade.PlinkoRowMinimums[row]; half += 2)
                    Place(new Ellipse { Width = 10, Height = 10, Fill = Gold }, X(half) - 5, Rows[row] - 5);
            for (var slot = 0; slot < CampaignArcade.PlinkoScores.Count; slot++)
            {
                var score = CampaignArcade.PlinkoScores[slot];
                var label = CampaignShopScreen.Label(score.ToString(), score >= 5 ? Green : score >= 3 ? Brush.Parse("#C792FF") : Muted, 40);
                label.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                Place(new Border { Width = 98, Height = 160, BorderBrush = Gold, BorderThickness = new Thickness(2), Child = label }, 63 + 98 * slot, 1060);
            }
        }
        _binGlow = MachineFx.Glow(190, Color.Parse("#FFE98A"));
        _pegFlash = MachineFx.Glow(74, Color.Parse("#FFF6D0"));
        _board.Children.Add(_binGlow);
        _board.Children.Add(_pegFlash);
        // The sprite ignores the pointer; its transparent holder is the handle the player drags.
        _dropper = new Border
        {
            Name = "PlinkoDropper", Width = 140, Height = 128, Background = Brushes.Transparent,
            Child = Sprite("arcade-plinko-dropper", 140, 128) ?? new Border
            { BorderBrush = Gold, BorderThickness = new Thickness(8), Background = MainWindow.Panel, CornerRadius = new CornerRadius(8) }
        };
        Place(_dropper, 0, DropperY);
        // A fading trail behind the ball reads its speed and direction at a glance.
        _trail = Enumerable.Range(0, 3).Select(i => new Ellipse
        {
            Width = BallSize - 4 * (i + 1), Height = BallSize - 4 * (i + 1), Fill = Brushes.White, IsHitTestVisible = false, Opacity = 0
        }).ToArray();
        foreach (var ghost in _trail) _board.Children.Add(ghost);
        // A bright silver ball with a dark rim stands out against the gold pegs and tan board.
        _ball = new Ellipse
        {
            Width = BallSize, Height = BallSize, Stroke = Brush.Parse("#1B1030"), StrokeThickness = 4,
            Fill = new RadialGradientBrush
            {
                Center = new RelativePoint(.35, .3, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.35, .3, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.Parse("#D9E1EC"), .45), new GradientStop(Color.Parse("#7F8BA0"), 1) }
            },
            RenderTransform = _squash, RenderTransformOrigin = RelativePoint.Center, IsHitTestVisible = false
        };
        _ball.Name = "PlinkoBall";
        _board.Children.Add(_ball);

        _dropper.PointerPressed += (_, e) =>
        {
            if (!Draggable || !e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed) return;
            _dragging = true; e.Pointer.Capture(_dropper); e.Handled = true;
            Drag(e.GetPosition(_board).X);
        };
        _dropper.PointerMoved += (_, e) => { if (_dragging) Drag(e.GetPosition(_board).X); };
        _dropper.PointerReleased += (_, e) => { if (_dragging) { e.Pointer.Capture(null); EndDrag(); } };
        _dropper.PointerCaptureLost += (_, _) => { if (_dragging) EndDrag(); };
        // While the dropper swings, a click anywhere on the board lets the ball go.
        _board.Background = Brushes.Transparent;
        _board.PointerPressed += (_, e) =>
        {
            if (!Swinging || !e.GetCurrentPoint(_board).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            DropRequested?.Invoke();
        };
        Choose(4);
    }

    internal static Control? Sprite(string name, double width, double height)
        => AssetLoader.Exists(new Uri($"avares://BidEuchre/Pixel/{name}.png")) ? PixelArt.Machine(name, width, height) : null;

    private void Place(Control control, double x, double y) { _board.Children.Add(control); Canvas.SetLeft(control, x); Canvas.SetTop(control, y); }
    private void Ball(double x, double y) { Canvas.SetLeft(_ball, x - _ball.Width / 2); Canvas.SetTop(_ball, y - _ball.Height / 2); }
    private static void Centre(Control control, double x, double y) { Canvas.SetLeft(control, x - control.Width / 2); Canvas.SetTop(control, y - control.Height / 2); }
    private static double X(int position) => 78 + 40 * position;
    private static double LaneX(int lane) => X(CampaignArcade.PlinkoStartPosition(lane));
    private static double BinX(int landing) => 112 + 98 * landing;

    public void Choose(int lane)
    {
        _lane = lane;
        Hold(LaneX(lane));
    }

    // The dropper and its waiting ball sit at an x position; the effects rest until a drop.
    private void Hold(double x, double dip = 0)
    {
        Canvas.SetLeft(_dropper, x - _dropper.Width / 2);
        Canvas.SetTop(_dropper, DropperY + dip);
        Ball(x, NozzleY + dip);
        _squash.ScaleX = _squash.ScaleY = 1;
        foreach (var ghost in _trail) ghost.Opacity = 0;
        _pegFlash.Opacity = _binGlow.Opacity = 0;
    }

    // The dropper follows the pointer along the rail, and the nearest lane is chosen as it passes.
    private void Drag(double x)
    {
        Hold(Math.Clamp(x, LaneX(0), LaneX(CampaignArcade.PlinkoLaneCount - 1)));
        var nearest = Enumerable.Range(0, CampaignArcade.PlinkoLaneCount).MinBy(lane => Math.Abs(LaneX(lane) - x));
        if (nearest == _lane) return;
        _lane = nearest;
        LaneDragged?.Invoke(nearest);
    }

    /// <summary>Seconds for one sweep of the rail, end to end. Each lane is under the nozzle for about a seventh of it.</summary>
    public const double SwingSeconds = 1.1;
    private double _swingStart;

    /// <summary>True while the dropper sweeps the rail waiting for the player to time the drop.</summary>
    public bool Swinging { get; private set; }

    /// <summary>Called when the player clicks the board to drop while the dropper swings.</summary>
    public Action? DropRequested { get; set; }

    /// <summary>The dropper's current centre on the rail.</summary>
    public double DropperX => Canvas.GetLeft(_dropper) + _dropper.Width / 2;

    /// <summary>Sweeps the dropper back and forth at a steady speed, starting from the current lane.
    /// With motion off it holds still on the chosen lane.</summary>
    public void Swing(PixelMotion motion)
    {
        Swinging = true;
        var span = LaneX(CampaignArcade.PlinkoLaneCount - 1) - LaneX(0);
        _swingStart = PixelMotion.Now - (LaneX(_lane) - LaneX(0)) / span * SwingSeconds;
        motion.Ambient(this, () =>
        {
            if (!Swinging) return;
            if (!motion.Enabled) { Hold(LaneX(_lane)); return; }
            // A triangle wave: constant speed with a hard turn at each end, so no lane is easier to hit.
            var sweep = (PixelMotion.Now - _swingStart) / SwingSeconds % 2;
            Hold(LaneX(0) + span * (sweep < 1 ? sweep : 2 - sweep));
        });
    }

    /// <summary>Stops the sweep and returns the lane nearest the nozzle at that instant.</summary>
    public int StopSwing()
    {
        Swinging = false;
        var x = DropperX;
        _lane = Enumerable.Range(0, CampaignArcade.PlinkoLaneCount).MinBy(lane => Math.Abs(LaneX(lane) - x));
        return _lane;
    }

    /// <summary>Slides the stopped dropper from <paramref name="from"/> onto its lane, with t from 0 to 1.</summary>
    public void Settle(double from, double t) => Hold(from + (LaneX(_lane) - from) * t);

    private void EndDrag()
    {
        _dragging = false;
        Choose(_lane);
        LaneDragged?.Invoke(_lane);
    }

    private readonly record struct Pose(double X, double Y, double Squash, int Peg, double Flash, double Glow, double Dip);

    private static Pose At(ArcadeResult result, double progress)
    {
        var path = result.Path;
        var hops = path.Length - 1;
        var total = DropWeight + hops * HopWeight + SettleWeight;
        var time = Math.Clamp(progress, 0, 1) * total;
        if (time < DropWeight)
        {
            // Released from the dropper, the ball falls freely onto the first peg. The dropper bobs as it lets go.
            var t = time / DropWeight;
            var dip = 10 * Math.Sin(Math.PI * Math.Min(1, t / .3));
            return new(X(path[0]), NozzleY + (Rows[0] - 18 - NozzleY) * t * t, 0, -1, 0, 0, dip);
        }
        time -= DropWeight;
        if (time < hops * HopWeight)
        {
            var step = Math.Min(hops - 1, (int)(time / HopWeight)) + 1;
            var t = time / HopWeight - (step - 1);
            var fromX = X(path[step - 1]); var toX = X(path[step]);
            var fromY = Rows[step - 1] - 18; var toY = Rows[step] - (step == hops ? 0 : 18);
            // A projectile: steady sideways travel, a bounce up off the peg, then a fall to the next one.
            var height = 14 + 9 * (step * 37 % 5) / 4d;
            var impact = Math.Max(0, 1 - t / .18);
            return new(fromX + (toX - fromX) * t, fromY + (toY - fromY) * t - height * 4 * t * (1 - t), impact, step - 1, Math.Max(0, 1 - t / .55), 0, 0);
        }
        time -= hops * HopWeight;
        {
            // Into the bin: roll to its centre while the bounces die away.
            var t = Math.Min(1, time / SettleWeight);
            var startX = X(path[^1]);
            var bounce = 30 * Math.Abs(Math.Sin(Math.PI * 2.5 * t)) * Math.Pow(1 - t, 2);
            var land = Math.Max(0, 1 - t / .12);
            return new(startX + (BinX(result.Landing) - startX) * MachineFx.EaseOut(t), BinY - bounce, land * .7, hops, land, MachineFx.EaseOut(Math.Min(1, t / .6)) * .85, 0);
        }
    }

    public void Frame(ArcadeResult result, double progress)
    {
        var pose = At(result, progress);
        Canvas.SetTop(_dropper, DropperY + pose.Dip);
        Ball(pose.X, pose.Y);
        // Squash flat on impact, stretching sideways, then spring back round.
        _squash.ScaleY = 1 - .28 * pose.Squash;
        _squash.ScaleX = 1 + .22 * pose.Squash;
        if (pose.Peg >= 0 && pose.Flash > 0)
        {
            Centre(_pegFlash, X(result.Path[pose.Peg]), Rows[pose.Peg]);
            _pegFlash.Opacity = pose.Flash;
        }
        else _pegFlash.Opacity = 0;
        Centre(_binGlow, BinX(result.Landing), BinY + 30);
        _binGlow.Opacity = pose.Glow;
        var moving = progress is > 0 and < 1;
        for (var i = 0; i < _trail.Length; i++)
        {
            var behind = At(result, progress - .0045 * (i + 1));
            var ghost = _trail[i];
            Centre(ghost, behind.X, behind.Y);
            var travel = Math.Sqrt(Math.Pow(behind.X - pose.X, 2) + Math.Pow(behind.Y - pose.Y, 2));
            ghost.Opacity = moving && travel > 4 ? .32 - .1 * i : 0;
        }
    }
}

/// <summary>Twelve equal pockets; the fixed pointer and the saved winning pocket meet at the top.
/// A ball rides the rim against the spin, drops, rattles into the winning pocket and travels with it.</summary>
internal sealed class RouletteControl : Canvas
{
    private const double WheelX = 18, WheelY = 35, Radius = 192, RimTrack = 186, PocketTrack = 175, BallSize = 18;
    private readonly RotateTransform _rotation = new();
    private readonly RotateTransform _pointerTilt = new();
    private readonly Polygon _highlight;
    private readonly Ellipse _ball;
    private readonly List<TextBlock> _numbers = [];
    private int _highlighted;

    public RouletteControl()
    {
        Width = 420; Height = 450;
        var wheel = new Canvas
        {
            Name = "RouletteWheel", Width = 384, Height = 384, RenderTransform = _rotation,
            RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
        };
        Children.Add(wheel); SetLeft(wheel, WheelX); SetTop(wheel, WheelY);
        wheel.Children.Add(new Ellipse { Width = 384, Height = 384, Fill = Gold });
        for (var pocket = 1; pocket <= CampaignArcade.RoulettePocketCount; pocket++)
        {
            wheel.Children.Add(new Polygon { Points = Wedge(pocket), Fill = pocket % 2 == 1 ? Brush.Parse("#AD315B") : Brush.Parse("#221A3D"), Stroke = Gold, StrokeThickness = 1 });
            var number = CampaignShopScreen.Label(pocket.ToString(), Brushes.White, 19);
            number.Width = 40; number.Height = 30; number.TextAlignment = TextAlignment.Center;
            var p = At(Center(pocket), 150);
            SetLeft(number, p.X - 20); SetTop(number, p.Y - 15);
            wheel.Children.Add(number);
            _numbers.Add(number);
        }
        // The winning pocket lights up as the wheel comes to rest.
        _highlight = new Polygon { Points = Wedge(1), Fill = Brush.Parse("#55FFF3C4"), Stroke = Brushes.White, StrokeThickness = 5, Opacity = 0, IsHitTestVisible = false };
        wheel.Children.Add(_highlight);
        var centerCap = new Border
        {
            Width = 110, Height = 110, CornerRadius = new CornerRadius(55), Background = Brush.Parse("#100B28"),
            BorderBrush = Gold, BorderThickness = new Thickness(5), Child = new TextBlock
            {
                Text = "EUCHATRO", FontSize = 12, FontFamily = PixelArt.Font, Foreground = Gold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            }
        };
        Children.Add(centerCap); SetLeft(centerCap, 155); SetTop(centerCap, 172);
        _ball = new Ellipse
        {
            Name = "RouletteBall", Width = BallSize, Height = BallSize, Stroke = Brush.Parse("#1B1030"), StrokeThickness = 2, Opacity = 0, IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                Center = new RelativePoint(.35, .3, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.35, .3, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.Parse("#C9D2DF"), 1) }
            }
        };
        Children.Add(_ball);
        // The pointer swings from its top edge as the pocket dividers flick past.
        var pointer = new Polygon
        {
            Name = "RoulettePointer", Points = [new(0, 0), new(30, 0), new(15, 30)], Fill = Gold, Stroke = Brushes.White, StrokeThickness = 2,
            RenderTransform = _pointerTilt, RenderTransformOrigin = new RelativePoint(.5, 0, RelativeUnit.Relative)
        };
        Children.Add(pointer); SetLeft(pointer, 195); SetTop(pointer, 20);
    }

    private static double Center(int pocket) => -90 + (pocket - 1) * 30;
    private static Points Wedge(int pocket)
    {
        var center = Center(pocket);
        var points = new Points { new(192, 192) };
        for (var angle = center - 15; angle <= center + 15; angle += 3) points.Add(At(angle, 182));
        return points;
    }
    private static Point At(double degrees, double radius)
    {
        var angle = degrees * Math.PI / 180;
        return new Point(192 + Math.Cos(angle) * radius, 192 + Math.Sin(angle) * radius);
    }

    // The wheel winds back a touch, spins up, coasts down past the winning pocket's centre and eases back onto it.
    private static double Spin(ArcadeResult result, double t)
    {
        var final = 1800 - (result.Landing - 1) * 30d;
        const double overshoot = 11;
        var windUp = -12 * Math.Sin(Math.PI * MachineFx.Part(t, 0, .08));
        var coast = MachineFx.Part(t, .05, .9);
        var main = (final + overshoot) * (1 - Math.Pow(1 - coast, 5));
        var settle = -overshoot * (1 - Math.Cos(Math.PI * MachineFx.Part(t, .9, 1))) / 2;
        return windUp + main + settle;
    }

    public void Frame(ArcadeResult result, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var angle = Spin(result, t);
        _rotation.Angle = angle;
        var speed = Math.Abs(Spin(result, Math.Min(1, t + .002)) - Spin(result, Math.Max(0, t - .002))) / .004;
        // Fast enough to blur: the printed numbers fade while the wheel races.
        var blur = Math.Clamp(speed / 9000, 0, 1);
        foreach (var number in _numbers) number.Opacity = 1 - .65 * blur;

        // A divider catches the pointer just after it passes the top, drags it over, then lets it spring back.
        var since = ((angle - 15) % 30 + 30) % 30 / 30;
        var amplitude = 20 * Math.Clamp(1.15 - speed / 8000, .3, 1);
        var tilt = since < .2 ? -amplitude * since / .2
            : since < .6 ? -amplitude * Math.Pow(1 - (since - .2) / .4, 2) * Math.Cos(Math.PI * 3 * (since - .2) / .4) : 0;
        _pointerTilt.Angle = t >= 1 ? 0 : tilt;

        if (_highlighted != result.Landing) { _highlight.Points = Wedge(result.Landing); _highlighted = result.Landing; }
        _highlight.Opacity = MachineFx.Part(t, .9, 1);

        // The ball runs the rim against the spin, then drops and rattles into the winning pocket to ride with it.
        var pocket = Center(result.Landing) + angle;
        var against = 4680 - (result.Landing - 1) * 30d;
        var run = MachineFx.Part(t, 0, .78);
        var drop = MachineFx.Part(t, .58, .8);
        var rattle = 9 * Math.Sin(Math.PI * 5 * drop) * Math.Pow(1 - drop, 2) * (drop > 0 ? 1 : 0);
        var ballAngle = pocket + against * Math.Pow(1 - run, 2) + rattle;
        var radius = PocketTrack + (RimTrack - PocketTrack) * Math.Pow(1 - drop, 2) + 7 * Math.Abs(Math.Sin(Math.PI * 4 * drop)) * (1 - drop);
        var ball = At(ballAngle, radius);
        SetLeft(_ball, WheelX + ball.X - BallSize / 2);
        SetTop(_ball, WheelY + ball.Y - BallSize / 2);
        _ball.Opacity = t > 0 ? 1 : 0;
    }
}
