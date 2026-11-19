using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

internal static class SodaArtwork
{
    public static Control Can(SodaId id, double height)
    {
        var soda = SodaCatalog.Get(id);
        var face = new Border { Background = Brushes.Transparent, Child = PixelArt.Machine("soda-" + soda.Art, height * .5, height) };
        ToolTip.SetTip(face, $"{soda.Name} — {soda.Description}");
        AutomationProperties.SetName(face, $"{soda.Name}. {soda.Description}");
        return face;
    }

    // Cabinet coordinates (162 x 290) of the parts of machine-soda.png the animation touches.
    private static readonly Rect Glass = new(14, 55, 99, 130);
    private static readonly Rect TrayOpening = new(34, 231, 94, 31);
    private static readonly Rect Sign = new(12, 16, 138, 26);
    private static readonly Point TrayRest = new(80.5, 247);
    private static readonly Rect TopButton = new(142, 113, 9, 8);
    private const double ShelfWidth = 17, ShelfHeight = 34, TrayWidth = 14, TrayHeight = 26;

    public static Control Machine(IEnumerable<SodaId> stock, SodaId? dispensed, string tag, PixelMotion motion,
        bool animate, Action select, Action<SodaId, Button, Image> inspect)
    {
        var cabinet = new Canvas { Width = 162, Height = 290 };
        var shake = new TranslateTransform();
        cabinet.RenderTransform = shake;
        var front = new Button { Name = "SodaMachine", Width = 162, Height = 290, Padding = new Thickness(0) };
        cabinet.Children.Add(PixelArt.Machine("machine-soda", 162, 290));
        CampaignCardFocus.Apply(front, new Border { Background = Brushes.Transparent });
        AutomationProperties.SetName(front, "Soda vending machine");
        front.Click += (_, _) => select();

        var index = 0;
        var start = new Point(59 + ShelfWidth / 2, 66 + ShelfHeight / 2);
        foreach (var id in stock.Take(9))
        {
            var position = new Point(23 + index % 3 * 29, 66 + index / 3 * 39);
            if (id == dispensed) start = position + new Point(ShelfWidth / 2, ShelfHeight / 2);
            else
            {
                var can = Can(id, 34);
                can.IsHitTestVisible = false;
                Canvas.SetLeft(can, position.X);
                Canvas.SetTop(can, position.Y);
                cabinet.Children.Add(can);
            }
            index++;
        }

        // Idle life: the neon sign hums and flickers, and a glint sweeps the glass now and then.
        var glint = new Border { Width = 12, Height = 230, Background = Brush.Parse("#30FFFFFF"),
            RenderTransform = new RotateTransform(28), IsHitTestVisible = false };
        Canvas.SetTop(glint, -50);
        var glassLayer = new Canvas { Width = Glass.Width, Height = Glass.Height, ClipToBounds = true,
            IsHitTestVisible = false, Children = { glint } };
        Canvas.SetLeft(glassLayer, Glass.X);
        Canvas.SetTop(glassLayer, Glass.Y);
        var glow = Overlay(Sign, "#FF6BE8");
        var dim = Overlay(Sign, "#000000");
        cabinet.Children.Add(glassLayer);
        cabinet.Children.Add(glow);
        cabinet.Children.Add(dim);
        cabinet.Children.Add(front);
        var signBoost = 0d;
        motion.Ambient(cabinet, () =>
        {
            if (!motion.Lively)
            {
                glow.Opacity = dim.Opacity = 0;
                glint.IsVisible = false;
                return;
            }
            var now = PixelMotion.Now;
            glow.Opacity = Math.Clamp(.06 + .04 * Math.Sin(now * 2.3) + signBoost, 0, .5);
            var flicker = now % 9.3;
            dim.Opacity = flicker is < .06 or (> .13 and < .17) or (> .31 and < .34) ? .45 : 0;
            var sweep = (now + 2.5) % 6.5;
            glint.IsVisible = sweep < 1.1;
            Canvas.SetLeft(glint, -40 + 160 * sweep / 1.1);
        });

        var prompt = CampaignShopScreen.Label(dispensed.HasValue ? "Inspect can" : "Vending machine", MainWindow.Ink, 12);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.VerticalAlignment = VerticalAlignment.Bottom;
        prompt.Margin = new Thickness(0, 0, 0, 6);
        var host = new Grid
        {
            Children = { new Viewbox { Width = 186, Height = 334, Child = cabinet }, prompt }
        };
        var price = CampaignShopScreen.Label(tag, MainWindow.Paper, 13);
        host.Children.Add(new Border { Background = MainWindow.Gold, Padding = new Thickness(5, 2),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Child = price });
        var face = new Border { Name = "SodaCabinet", Width = GumballMachine.FaceWidth, Height = GumballMachine.FaceHeight,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Background = MainWindow.Panel,
            BorderThickness = new Thickness(3), BorderBrush = MainWindow.Green, CornerRadius = new CornerRadius(4), Child = host };
        if (dispensed is not { } awarded) return face;

        var soda = SodaCatalog.Get(awarded);
        var direction = start.X <= TrayRest.X ? 1 : -1;
        var restAngle = direction * 93d;
        var tray = new Button { Name = "SodaTrayCan", Width = 45, Height = 32, Padding = new Thickness(0) };
        var trayArt = PixelArt.Machine("soda-" + soda.Art, TrayWidth, TrayHeight);
        trayArt.RenderTransformOrigin = RelativePoint.Center;
        trayArt.RenderTransform = new RotateTransform(restAngle);
        CampaignCardFocus.Apply(tray, new Border { Background = Brushes.Transparent, Child = trayArt });
        AutomationProperties.SetName(tray, $"Inspect {soda.Name}");
        tray.Click += (_, _) => inspect(awarded, tray, trayArt);
        Canvas.SetLeft(tray, TrayRest.X - tray.Width / 2);
        Canvas.SetTop(tray, TrayRest.Y - tray.Height / 2);
        cabinet.Children.Add(tray);
        if (!animate || !motion.Lively) return face;

        tray.IsVisible = false;
        prompt.Opacity = 0;
        // The can is only seen through the glass and the tray opening; the panel between hides its turn toward the tray.
        var falling = PixelArt.Machine("soda-" + soda.Art, ShelfWidth, ShelfHeight);
        falling.Name = "DispensingSoda";
        var squash = new ScaleTransform();
        var spin = new RotateTransform();
        falling.RenderTransform = new TransformGroup { Children = { squash, spin } };
        falling.RenderTransformOrigin = RelativePoint.Center;
        var dispenseLayer = new Canvas
        {
            Width = 162, Height = 290, IsHitTestVisible = false, Children = { falling },
            Clip = new GeometryGroup { FillRule = FillRule.NonZero,
                Children = { new RectangleGeometry(Glass), new RectangleGeometry(TrayOpening) } }
        };
        var coin = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = MainWindow.Gold,
            BorderBrush = Brush.Parse("#8A5A12"), BorderThickness = new Thickness(1), IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(), RenderTransformOrigin = RelativePoint.Center };
        var button = Overlay(TopButton, "#6FF6F0");
        button.Name = "SodaTopButtonPress";
        button.BorderBrush = Brush.Parse("#145C68");
        button.BorderThickness = new Thickness(1, 2, 1, 1);
        var buttonTravel = new TranslateTransform();
        button.RenderTransform = buttonTravel;
        cabinet.Children.Add(dispenseLayer);
        cabinet.Children.Add(coin);
        cabinet.Children.Add(button);
        Canvas.SetLeft(coin, 131.5);

        // The first rebound stays below the tray lip.
        const double gravity = 1500, restitution = .21, fallAt = .95;
        var landAfter = Math.Sqrt(2 * (TrayRest.Y - start.Y) / gravity);
        var impactSpeed = gravity * landAfter;
        IDisposable? frames = null;
        face.AttachedToVisualTree += Start;
        void Start(object? sender, VisualTreeAttachmentEventArgs e)
        {
            face.AttachedToVisualTree -= Start;
            var began = AnimationClock.Now;
            frames = AnimationClock.Shared.Subscribe(now =>
            {
                var elapsed = now - began;
                if (!motion.Lively) elapsed = double.PositiveInfinity;

                // 1. A coin turns edge-on and drops into the slot; the sign brightens.
                var coinT = Math.Clamp(elapsed / .32, 0, 1);
                Canvas.SetTop(coin, 42 + 24 * coinT * coinT);
                ((ScaleTransform)coin.RenderTransform!).ScaleX = 1 - .75 * Ease(coinT);
                coin.Opacity = coinT < .75 ? 1 : (1 - coinT) * 4;
                signBoost = elapsed is > .1 and < 2 ? .28 * Math.Exp(-(elapsed - .1) * 3.5) : 0;

                // 2. The top selection button depresses, lights up, then springs back.
                var press = (elapsed - .28) / .34;
                var pressed = Ease(press / .2) * (1 - Ease((press - .6) / .4));
                button.Opacity = pressed;
                buttonTravel.Y = 1.1 * pressed;

                // 3. The motor hums: the coil nudges the can forward while the cabinet buzzes.
                var turn = Math.Clamp((elapsed - .5) / (fallAt - .5), 0, 1);
                var buzz = turn is > 0 and < 1 ? Math.Sin(Math.PI * turn) : 0;
                var shakeX = 2 * buzz * Math.Sin(elapsed * Math.PI * 2 * 28);
                var shakeY = elapsed > .3 ? 1.2 * Math.Exp(-(elapsed - .3) * 20) : 0;
                var x = start.X + 1.5 * buzz * Math.Sin(elapsed * 40);
                var y = start.Y;
                var forward = 1 + .1 * Ease(turn);
                var angle = 4 * buzz * Math.Sin(elapsed * 31);

                // 4. It falls down its column and turns onto its side behind the front panel.
                var fall = elapsed - fallAt;
                var settled = false;
                var toTray = 0d;
                if (fall > 0 && fall < landAfter)
                {
                    y = start.Y + .5 * gravity * fall * fall;
                    toTray = Ease((y - Glass.Bottom - ShelfHeight / 2) / (TrayRest.Y - 6 - Glass.Bottom - ShelfHeight / 2));
                    x = start.X + (TrayRest.X - start.X) * toTray;
                    forward = 1.1 - .1 * Math.Clamp(fall * 4, 0, 1);
                    angle = restAngle * toTray;
                }
                else if (fall >= landAfter)
                {
                    var landed = fall - landAfter;
                    var (height, sinceImpact, strength, resting) = Bounce(landed, impactSpeed * restitution, gravity, restitution);
                    x = TrayRest.X;
                    var compression = strength * Math.Exp(-sinceImpact * 16);
                    y = TrayRest.Y - height + .9 * compression;
                    toTray = 1;
                    // The can's local X axis is now almost vertical.
                    squash.ScaleX = 1 - .14 * compression;
                    squash.ScaleY = 1 + .09 * compression;
                    angle = restAngle + direction * 2 * Math.Exp(-landed * 6) * Math.Sin(landed * 23);
                    shakeX += 1.4 * Math.Exp(-landed * 10) * Math.Sin(landed * 42);
                    shakeY += 2.4 * Math.Exp(-landed * 12) * Math.Cos(landed * 26);
                    settled = resting && landed > .6;
                }
                if (fall <= 0 || fall < landAfter) squash.ScaleX = squash.ScaleY = forward;
                falling.Width = ShelfWidth + (TrayWidth - ShelfWidth) * toTray;
                falling.Height = ShelfHeight + (TrayHeight - ShelfHeight) * toTray;
                spin.Angle = angle;
                Canvas.SetLeft(falling, x - falling.Width / 2);
                Canvas.SetTop(falling, y - falling.Height / 2);
                shake.X = shakeX;
                shake.Y = shakeY;
                if (!settled && !double.IsPositiveInfinity(elapsed)) return;

                frames?.Dispose();
                shake.X = shake.Y = signBoost = 0;
                cabinet.Children.Remove(dispenseLayer);
                cabinet.Children.Remove(coin);
                cabinet.Children.Remove(button);
                tray.IsVisible = true;
                tray.Focus();
                Reveal(prompt, motion);
            });
        }
        face.DetachedFromVisualTree += (_, _) => frames?.Dispose();
        Canvas.SetLeft(falling, start.X - ShelfWidth / 2);
        Canvas.SetTop(falling, start.Y - ShelfHeight / 2);
        return face;
    }

    private static double Ease(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Border Overlay(Rect area, string color)
    {
        var overlay = new Border { Width = area.Width, Height = area.Height, Background = Brush.Parse(color),
            Opacity = 0, IsHitTestVisible = false, CornerRadius = new CornerRadius(2) };
        Canvas.SetLeft(overlay, area.X);
        Canvas.SetTop(overlay, area.Y);
        return overlay;
    }

    // Damped bounces after the first impact: height above the floor, time since the latest impact,
    // that impact's relative strength, and whether the can has come to rest.
    private static (double Height, double SinceImpact, double Strength, bool Resting) Bounce(
        double time, double speed, double gravity, double restitution)
    {
        var strength = 1d;
        while (speed > 25)
        {
            var airborne = 2 * speed / gravity;
            if (time < airborne) return (speed * time - .5 * gravity * time * time, time, strength, false);
            time -= airborne;
            strength *= restitution;
            speed *= restitution;
        }
        return (0, time, strength, true);
    }

    private static void Reveal(Control control, PixelMotion motion)
    {
        if (!motion.Lively) { control.Opacity = 1; return; }
        var began = AnimationClock.Now;
        IDisposable? frames = null;
        frames = AnimationClock.Shared.Subscribe(now =>
        {
            control.Opacity = Ease((now - began) / .3);
            if (control.Opacity >= 1) frames?.Dispose();
        });
    }

    public static void Inspect(Grid host, Button tray, Image trayArt, SodaId id, PixelMotion motion, bool firstOpen)
    {
        var shop = host.Children[0];
        var soda = SodaCatalog.Get(id);
        var art = PixelArt.Machine("soda-" + soda.Art, 178, 350);
        art.Name = "InspectedSoda";
        var opening = new SodaOpening(art, id);
        var overlay = new Grid { Name = "SodaInspection", Background = Brush.Parse("#CF100B28") };
        var flight = new Canvas { IsHitTestVisible = false };
        void Close()
        {
            host.Children.Remove(overlay);
            shop.IsEnabled = true;
            trayArt.Opacity = 1;
            tray.Focus();
        }
        var close = MenuParts.Button("Back to shop", false, motion, "CloseSodaInspection", Close, true, 240);
        var effect = new StackPanel
        {
            Name = "SodaEffect", Width = 255, Spacing = 16, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = soda.Description, Foreground = MainWindow.Ink, FontSize = 19, TextWrapping = TextWrapping.Wrap },
                CampaignShopScreen.Label("Active for this run", MainWindow.Green, 12)
            }
        };
        var details = new StackPanel
        {
            Width = 255, VerticalAlignment = VerticalAlignment.Center, Spacing = 18,
            Children = { CampaignShopScreen.Label(soda.Name, MainWindow.Gold, 28), effect, close }
        };
        var card = new Border
        {
            Background = MainWindow.Panel, BorderBrush = MainWindow.Green, BorderThickness = new Thickness(3), Padding = new Thickness(24),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { opening, details } }
        };
        overlay.Children.Add(new Viewbox { StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(18), Child = card });
        overlay.Children.Add(flight);
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        host.Children.Add(overlay);
        shop.IsEnabled = false;
        trayArt.Opacity = 0;
        close.Focus();
        if (!firstOpen || !motion.Lively) return;
        opening.SetTime(0);
        // Keep the hidden text in layout so the heading and close button stay anchored.
        effect.Opacity = 0;
        var effectShift = new TranslateTransform(0, 12);
        effect.RenderTransform = effectShift;
        var landing = new ScaleTransform();
        opening.RenderTransform = landing;
        opening.RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative);

        var flying = PixelArt.Machine("soda-" + soda.Art, TrayWidth, TrayHeight);
        flying.Name = "SodaPopOut";
        var trayAngle = ((RotateTransform)trayArt.RenderTransform!).Angle;
        var spin = new RotateTransform(trayAngle);
        flying.RenderTransform = spin;
        flying.RenderTransformOrigin = RelativePoint.Center;
        flight.Children.Add(flying);
        opening.Opacity = card.Opacity = flying.Opacity = overlay.Opacity = 0;
        // The can crouches in the tray, springs out with a flip, and lands on the stage with a squash.
        const double hop = .16, fly = .58;
        var began = AnimationClock.Now;
        IDisposable? frames = null;
        frames = AnimationClock.Shared.Subscribe(now =>
        {
            if (art.Bounds.Height <= 0) return;
            var elapsed = motion.Lively ? now - began : double.PositiveInfinity;
            overlay.Opacity = Ease(elapsed / .2);
            var t = Math.Clamp((elapsed - hop) / fly, 0, 1);
            var start = trayArt.TranslatePoint(default, flight);
            var startEdge = trayArt.TranslatePoint(new Point(trayArt.Bounds.Width, 0), flight);
            var startCenter = trayArt.TranslatePoint(new Point(trayArt.Bounds.Width / 2, trayArt.Bounds.Height / 2), flight);
            var end = art.TranslatePoint(default, flight);
            var endEdge = art.TranslatePoint(new Point(art.Bounds.Width, art.Bounds.Height), flight);
            if (t >= 1 || start is null || startEdge is null || startCenter is null || end is null || endEdge is null)
            {
                if (flight.Children.Count > 0) flight.Children.Clear();
                var after = elapsed - hop - fly;
                var thud = after is >= 0 and < .5 ? Math.Exp(-after * 11) * Math.Cos(after * 24) : 0;
                landing.ScaleY = 1 - .07 * thud;
                landing.ScaleX = 1 + .05 * thud;
                opening.SetTime(after);
                opening.Opacity = card.Opacity = 1;
                if (after < SodaOpening.Duration) return;
                // The effect slides up once the pour has finished.
                var reveal = Ease((after - SodaOpening.Duration) / .3);
                effect.Opacity = reveal;
                effectShift.Y = 12 * (1 - reveal);
                if (reveal >= 1) frames?.Dispose();
                return;
            }
            var center = startCenter.Value;
            var trayAxis = startEdge.Value - start.Value;
            var trayScale = double.Hypot(trayAxis.X, trayAxis.Y) / trayArt.Bounds.Width;
            var fromSize = new Point(trayArt.Bounds.Width * trayScale, trayArt.Bounds.Height * trayScale);
            flying.Opacity = 1;
            if (elapsed < hop)
            {
                // Anticipation: squash down, then stretch upward out of the tray.
                var p = elapsed / hop;
                var stretch = Math.Sin(2 * Math.PI * p);
                var squashed = new Point(fromSize.X * (1 - .2 * stretch), fromSize.Y * (1 + .14 * stretch));
                flying.Width = squashed.X;
                flying.Height = squashed.Y;
                Canvas.SetLeft(flying, center.X - squashed.X / 2);
                Canvas.SetTop(flying, center.Y - squashed.Y / 2 + fromSize.X * .1 * stretch - 6 * Ease(2 * p - 1));
                return;
            }
            var ease = 1 - Math.Pow(1 - t, 3);
            var arc = Math.Sin(Math.PI * t);
            var destination = end.Value + (endEdge.Value - end.Value) * .5;
            var toSize = endEdge.Value - end.Value;
            var size = fromSize + (toSize - fromSize) * (ease + .09 * arc);
            var position = center + new Point(0, -6) + (destination - center - new Point(0, -6)) * ease;
            flying.Width = size.X;
            flying.Height = size.Y;
            spin.Angle = trayAngle + (Math.Sign(trayAngle) * 360 - trayAngle) * Ease(t);
            Canvas.SetLeft(flying, position.X - size.X / 2);
            Canvas.SetTop(flying, position.Y - size.Y / 2 - 70 * arc);
            card.Opacity = Math.Clamp((t - .25) / .65, 0, 1);
        });
        overlay.DetachedFromVisualTree += (_, _) => frames?.Dispose();
    }

    public static Control Shelf(CampaignInventory inventory)
    {
        var row = new WrapPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var id in inventory.Sodas)
            row.Children.Add(new Border { Padding = new Thickness(4, 0), Child = Can(id, 48) });
        return new StackPanel
        {
            Name = "OwnedSodas", Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { CampaignShopScreen.Label($"YOUR SODAS {inventory.Sodas.Length}/14", MainWindow.Muted, 11), row }
        };
    }
}
