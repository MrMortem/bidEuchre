using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

/// <summary>Rounded buttons stand on an edge, sink onto it while pressed, follow the Motion
/// setting and still fit their screens at the largest interface scale.</summary>
internal static class RaisedButtonTests
{
    public static void Run()
    {
        PressSinksOntoTheEdge();
        TopEdgePressStillClicks();
        MotionOffSnaps();
        ScreensFitAtLargestScale();
    }

    private static ContentPresenter Face(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    private static double Sink(Button button) => ((TranslateTransform)Face(button).RenderTransform!).Y;
    private static double Edge(Button button) => Face(button).BoxShadow.Count == 0 ? 0 : Face(button).BoxShadow[0].OffsetY;

    private static (Window Window, Button Button, Func<int> Clicks) Show(PixelMotion motion)
    {
        var clicks = 0;
        var button = MenuParts.Button("Press", true, motion, "Raised", () => clicks++, stay: true);
        var window = new Window { Width = 600, Height = 400, Content = new StackPanel { Children = { button } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, button, () => clicks);
    }

    private static Point At(Button button, Window window, double x, double y) =>
        button.TranslatePoint(new Point(x, y), window)!.Value;

    private static void PressSinksOntoTheEdge()
    {
        var motion = new PixelMotion();
        motion.SetEnabled(false);
        var (window, button, clicks) = Show(motion);
        try
        {
            if (button.CornerRadius.TopLeft < 6) throw new Exception("Shared buttons must have rounded corners.");
            if (Sink(button) != 0 || Edge(button) != 4) throw new Exception("A button at rest must stand on its full edge.");
            var middle = At(button, window, button.Bounds.Width / 2, button.Bounds.Height / 2);
            window.MouseDown(middle, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (Sink(button) != 4 || Edge(button) != 0) throw new Exception("Pressing must sink the face onto its edge.");
            window.MouseUp(middle, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (Sink(button) != 0 || Edge(button) != 4) throw new Exception("Releasing must raise the face again.");
            if (clicks() != 1) throw new Exception("A press and release must click once.");
            Console.WriteLine("PASS  Raised buttons are rounded and sink onto their edge while pressed");
        }
        finally { window.Close(); }
    }

    // The face moves down while pressed; a release where its top used to be must still count.
    private static void TopEdgePressStillClicks()
    {
        var motion = new PixelMotion();
        motion.SetEnabled(false);
        var (window, button, clicks) = Show(motion);
        try
        {
            var top = At(button, window, button.Bounds.Width / 2, 1);
            window.MouseDown(top, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(top, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (clicks() != 1) throw new Exception("Pressing the top edge of a raised button must still click.");
            Console.WriteLine("PASS  Pressing the top edge of a raised button still clicks");
        }
        finally { window.Close(); }
    }

    private static void MotionOffSnaps()
    {
        var motion = new PixelMotion();
        var (window, button, _) = Show(motion);
        try
        {
            button.Focus();
            Dispatcher.UIThread.RunJobs();
            var face = Face(button);
            if (face.Transitions is null || ((TranslateTransform)face.RenderTransform!).Transitions is null)
                throw new Exception("With motion on, the face must ease between states.");
            var start = DateTime.UtcNow;
            while (Sink(button) != -1 && DateTime.UtcNow - start < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(10);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            if (Sink(button) != -1) throw new Exception("With motion on, the selected menu button must ease up a pixel.");
            motion.SetEnabled(false);
            button.IsEnabled = false;
            button.IsEnabled = true;
            button.Focus();
            Dispatcher.UIThread.RunJobs();
            if (face.Transitions is not null || ((TranslateTransform)face.RenderTransform!).Transitions is not null)
                throw new Exception("With motion off, the face must snap between states.");
            if (Sink(button) != 0) throw new Exception("With motion off, a selected button must not lift.");
            if (!button.IsFocused || face.BorderBrush != MainWindow.Gold)
                throw new Exception("The selected button must keep its visible gold focus border.");
            Console.WriteLine("PASS  Raised buttons ease with motion on, snap with it off and keep visible focus");
        }
        finally { window.Close(); }
    }

    private static void ScreensFitAtLargestScale()
    {
        var motion = new PixelMotion();
        motion.SetEnabled(false);
        var scale = GamePreferences.UiScales.Max();
        var (width, height) = (new GamePreferences().WindowWidth, new GamePreferences().WindowHeight);
        var run = new CampaignRun(Money: 30);
        var screens = new (string Name, Func<Control> Build)[]
        {
            ("main menu", () => MainMenu.Build(() => { }, () => { }, () => { }, () => { }, () => { }, motion)),
            ("campaign choice", () => CampaignScreens.PlayChoice(run, () => { }, () => { }, () => { }, motion)),
            ("ante", () => CampaignAnteScreen.Build(run, () => { }, () => { }, null, motion)),
            ("run over", () => CampaignScreens.RunOver(run, () => { }, motion))
        };
        foreach (var (name, build) in screens)
        {
            var window = new Window
            {
                Width = width, Height = height,
                Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = build() }
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var boxes = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)
                    .Select(b => (b.Name, Box: Box(b, window))).ToList();
                if (boxes.Count == 0) throw new Exception($"The {name} screen must show buttons.");
                foreach (var (button, box) in boxes)
                    if (box.Left < 0 || box.Right > width)
                        throw new Exception($"{button} on the {name} screen must fit across the window at {scale:P0}.");
                for (var i = 0; i < boxes.Count; i++)
                    for (var j = i + 1; j < boxes.Count; j++)
                        if (boxes[i].Box.Intersects(boxes[j].Box))
                            throw new Exception($"{boxes[i].Name} and {boxes[j].Name} overlap on the {name} screen at {scale:P0}.");
            }
            finally { window.Close(); }
        }
        Console.WriteLine($"PASS  Raised buttons and their edges fit without overlapping at {scale:P0}");
    }

    // The button, edge included, in window coordinates.
    private static Rect Box(Button button, Window window)
    {
        var bounds = new Rect(0, 0, button.Bounds.Width, button.Bounds.Height);
        var matrix = button.TransformToVisual(window)!.Value;
        return bounds.TransformToAABB(matrix).Deflate(.5);
    }
}
