using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class HandReadabilityTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }
    private static Point At(Control card, Visual surface, double x, double y) => card.TranslatePoint(new Point(x * card.Bounds.Width, y * card.Bounds.Height), surface)!.Value;
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static Rect Corners(Control card, Visual surface)
    {
        var points = new[] { At(card, surface, 0, 0), At(card, surface, 0, 1), At(card, surface, 1, 0), At(card, surface, 1, 1) };
        return new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)), new Point(points.Max(p => p.X), points.Max(p => p.Y)));
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } output) return;
        Directory.CreateDirectory(output);
        using var bitmap = window.CaptureRenderedFrame();
        Require(bitmap is not null, "Skia must render card readability evidence.");
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run()
    {
        var failures = new List<string>();
        foreach (var test in new Action[] { CornerSigils, ReachableHands, ReorderSlots, PlayFlightOrigins })
            try { test(); Console.WriteLine("PASS  " + test.Method.Name); }
            catch (Exception error) { failures.Add(test.Method.Name + ": " + error.Message); Console.WriteLine("FAIL  " + failures[^1]); }
        if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
    }

    // A regression back to a large left-side badge would cover the printed index or pips.
    private static void CornerSigils()
    {
        var sheet = new WrapPanel { Width = 1040 };
        var active = new[] { SigilId.Bear, SigilId.Eagle, SigilId.Fish, SigilId.Spider, SigilId.Frog, SigilId.Duck, SigilId.Vulture, SigilId.Chameleon };
        foreach (var size in new[] { new Size(72, 102), new Size(104, 148), new Size(124, 176), new Size(180, 256) })
        foreach (var sigil in active)
        foreach (var face in from suit in Enum.GetValues<Suit>() from rank in Enum.GetValues<Rank>() select new Card(suit, rank))
        {
            var content = (Grid)PixelArt.CardContent(face, EnhancementId.Golden, sigil);
            content.Measure(size); content.Arrange(new Rect(size));
            var badge = content.Children.OfType<Image>().Single(i => i.Name == "CardSigil");
            var bounds = Corners(badge, content);
            Require(bounds.Left >= size.Width * .72 && bounds.Right <= size.Width * .99 && bounds.Top > 0 && bounds.Bottom <= size.Height * .19,
                $"{face}/{sigil} badge must be inset in the top-right blank corner, clear of the printed indices and suit art; got {bounds} on {size}.");
            Require(bounds.Width >= size.Width * .18 && bounds.Height >= size.Height * .12, "The corner badge must remain identifiable in small card previews.");
            var faceLayer = content.Children.OfType<Image>().ElementAt(1);
            var printed = Corners(faceLayer, content);
            Require(printed.Left == 0 && printed.Top == 0 && Math.Abs(printed.Width - size.Width) <= 1 && Math.Abs(printed.Height - size.Height) <= 1,
                "Adding a Sigil must preserve the full printed face, allowing one pixel for layout rounding.");
            if (size.Width == 124 && face.Rank == Rank.Ten)
                sheet.Children.Add(new Border { Width = 124, Height = 176, Margin = new Thickness(3), Child = content });
        }
        var window = new Window { Width = 1060, Height = 780, Content = new ScrollViewer { Content = sheet } };
        try { window.Show(); Flush(); Capture(window, "corner-sigils-all-suits"); }
        finally { window.Close(); }
    }

    private static Button CardButton(int index, Action? clicked = null)
    {
        var button = new Button
        {
            Width = 124, Height = 176, Margin = new Thickness(2), Padding = new Thickness(0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Content = PixelArt.CardContent(new Card((Suit)(index % 4), (Rank)(9 + index % 6)), null, SigilId.Fish)
        };
        button.Click += (_, _) => clicked?.Invoke();
        return button;
    }

    // Exercise the production table placement and transformed pointer targets at every offered scale/size.
    private static void ReachableHands()
    {
        foreach (var size in GamePreferences.WindowSizes.Select(s => new Size(s.Width, s.Height)).Append(new Size(980, 680)))
        foreach (var requested in GamePreferences.UiScales)
        {
            var effective = GamePreferences.UiScales.Where(s => s <= requested && s <= Math.Min(size.Width / 980, size.Height / 680) + .001).DefaultIfEmpty(1).Max();
            var clicks = new int[4, 6];
            var cards = Enumerable.Range(0, 4).Select(seat => Enumerable.Range(0, 6).Select(index => CardButton(index, () => clicks[seat, index]++)).ToArray()).ToArray();
            var hands = Enumerable.Range(0, 4).Select(seat => HandFan.Arrange(seat, new Border(), cards[seat], Still())).ToArray();
            var builder = new MainWindow();
            var table = (Control)typeof(MainWindow).GetMethod("PixelTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(builder, [hands, new Border(), null])!;
            table.Focusable = true;
            var window = new Window { Width = size.Width, Height = size.Height, Content = new LayoutTransformControl
                { LayoutTransform = new ScaleTransform(effective, effective), Child = table } };
            try
            {
                window.Show(); Flush();
                for (var seat = 0; seat < 4; seat++)
                for (var index = 0; index < 6; index++)
                {
                    var card = cards[seat][index];
                    Require(new Rect(window.ClientSize).Contains(Corners(card, window)), $"Seat {seat} card {index} clips at {size}/{requested}.");
                    // The exposed upper-left index is an actual click target even under adjacent cards.
                    var point = At(card, window, .12, .12);
                    // A played card normally redraws the table; clear its retained keyboard focus in this static fixture.
                    table.Focus(); window.MouseMove(default); Flush(); window.MouseMove(point); Flush();
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Flush();
                    Require(clicks[seat, index] == 1, $"Seat {seat} card {index} must remain individually clickable at {size}/{requested}.");
                }
                if (requested == 1 || size.Width == 1920 && requested == 1.5)
                    Capture(window, $"readable-hands-{size.Width}-{size.Height}-{requested}");
            }
            finally { window.Close(); builder.Close(); }
        }
    }

    // Public flights must start at the enlarged/rotated card, including a scaled table.
    private static void PlayFlightOrigins()
    {
        var overlay = new Canvas();
        var cards = Enumerable.Range(0, 4).Select(seat => Enumerable.Range(0, 6).Select(i => CardButton(i)).ToArray()).ToArray();
        var hands = Enumerable.Range(0, 4).Select(seat => HandFan.Arrange(seat, new Border(), cards[seat], Still())).ToArray();
        var builder = new MainWindow();
        var table = (Control)typeof(MainWindow).GetMethod("PixelTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(builder, [hands, new Border(), null])!;
        var window = new Window { Width = 1600, Height = 900, Content = new Grid { Children = { table, overlay } } };
        var flights = new CardFlights(overlay);
        try
        {
            window.Show(); Flush();
            var origins = cards.Select(hand => At(hand[2], overlay, .5, .5)).ToArray();
            var widths = cards.Select(hand => Distance(At(hand[2], overlay, 1, 0), At(hand[2], overlay, 0, 0))).ToArray();
            for (var seat = 0; seat < 4; seat++) flights.HandCard($"{seat}:card", cards[seat][2], HandFan.Angle(seat, 2, 6));
            flights.BeginFrame();
            var targets = new List<Border>();
            for (var seat = 0; seat < 4; seat++)
            {
                var target = new Border { Width = 124, Height = 176 };
                Canvas.SetLeft(target, 600 + seat * 50); Canvas.SetTop(target, 350);
                overlay.Children.Add(target); targets.Add(target);
                var index = seat;
                flights.PlayedCard($"{seat}:card", target, true, () => new Border
                    { Name = $"FlightFace{index}", Width = 124, Height = 176, Child = PixelArt.CardContent(Card.Parse("AS"), null, SigilId.Fish) });
            }
            Flush(); flights.EndFrame(true); Flush();
            var moving = overlay.Children.OfType<Viewbox>().ToArray();
            Require(moving.Length == 4, "Each seat must produce a public play flight.");
            foreach (var visual in moving)
            {
                var seat = int.Parse(visual.Child!.Name![10..]);
                Require(Distance(At(visual, overlay, .5, .5), origins[seat]) < .01, "A play flight must start at its hand card center.");
                Require(Math.Abs(Distance(At(visual, overlay, 1, 0), At(visual, overlay, 0, 0)) - widths[seat]) < .01,
                    "A play flight must inherit the enlarged hand card's visible width.");
                CuratedArtTests.ContainsSigil(visual, SigilId.Fish);
            }
            flights.Cancel(); Flush();
            Require(targets.All(t => t.Opacity == 1), "Cancelling flights must reveal the actual trick cards.");
        }
        finally { flights.Cancel(); window.Close(); builder.Close(); }
    }

    // A wider fan must use the same slot spacing for pointer drop destinations as for resting cards.
    private static void ReorderSlots()
    {
        foreach (var scale in GamePreferences.UiScales)
        foreach (var target in new[] { 0, 1, 3, 5 })
        {
            (int From, int To)? moved = null;
            var clicks = 0;
            var cards = Enumerable.Range(0, 6).Select(i => CardButton(i, () => clicks++)).ToArray();
            var hand = HandFan.Arrange(0, new Border(), cards, Still(), (from, to) => moved = (from, to));
            var window = new Window { Width = 850, Height = 400, Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = hand } };
            try
            {
                window.Show(); Flush();
                const int from = 2;
                var start = At(cards[from], window, .12, .12);
                var delta = At(cards[target], window, .5, .5) - At(cards[from], window, .5, .5);
                window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + delta); window.MouseUp(start + delta, MouseButton.Left); Flush();
                Require(moved == (from, target), $"Dragging at {scale} must map to the visible slot {target}; got {moved}.");
                Require(clicks == 0 && !HandDrag.Active, "A reorder must not play the card or leave dragging active.");
            }
            finally { window.Close(); }
        }
    }
}
