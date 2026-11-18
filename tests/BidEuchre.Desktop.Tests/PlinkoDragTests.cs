using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Desktop;

internal static class PlinkoDragTests
{
    private static double LaneX(int lane) => 78 + 40 * CampaignArcade.PlinkoStartPosition(lane);

    public static void Run()
    {
        var plinko = new PlinkoControl { Draggable = true };
        var lanes = new List<int>();
        plinko.LaneDragged = lanes.Add;
        var window = new Window { Width = 600, Height = 600, Content = plinko };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var dropper = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlinkoDropper");
            var board = (Canvas)dropper.Parent!;
            Point Screen(double x) => board.TranslatePoint(new Point(x, Canvas.GetTop(dropper) + dropper.Height / 2), window)!.Value;
            double Centre() => Canvas.GetLeft(dropper) + dropper.Width / 2;

            var start = Screen(LaneX(4));
            window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
            // Partway between lanes the dropper follows the pointer freely.
            window.MouseMove(Screen(LaneX(5) + 17)); Dispatcher.UIThread.RunJobs();
            if (Math.Abs(Centre() - (LaneX(5) + 17)) > 1) throw new Exception("The dropper must follow the pointer while dragged.");
            var end = Screen(LaneX(6) + 12);
            window.MouseMove(end); window.MouseUp(end, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            if (lanes.LastOrDefault(-1) != 6 || Math.Abs(Centre() - LaneX(6)) > .01)
                throw new Exception("Releasing the dropper must snap it to the nearest lane and choose that lane.");
            // Past the last lane it stops at the rail's end.
            window.MouseMove(Screen(LaneX(6))); window.MouseDown(Screen(LaneX(6)), MouseButton.Left);
            window.MouseMove(Screen(1190)); window.MouseUp(Screen(1190), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            if (lanes[^1] != CampaignArcade.PlinkoLaneCount - 1 || Math.Abs(Centre() - LaneX(CampaignArcade.PlinkoLaneCount - 1)) > .01)
                throw new Exception("Dragging past the rail must stop at the last lane.");
            Console.WriteLine("PASS  The Plinko dropper drags along its rail and snaps to the nearest lane");

            plinko.Draggable = false; lanes.Clear();
            var locked = Screen(Centre());
            window.MouseMove(locked); window.MouseDown(locked, MouseButton.Left);
            window.MouseMove(Screen(LaneX(0))); window.MouseUp(Screen(LaneX(0)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            if (lanes.Count != 0) throw new Exception("A played or unaffordable board must not change lanes by dragging.");
            Console.WriteLine("PASS  The Plinko dropper stays put when the board cannot be played");
        }
        finally { window.Close(); }
        TimedDrop();
    }

    private static void Pump(int milliseconds)
    {
        using var limit = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(limit.Token);
    }

    // With motion on, the dropper sweeps the rail and the drop takes whichever lane is under the nozzle.
    private static void TimedDrop()
    {
        var run = CampaignArcadeUiTests.PlinkoFixture(5);
        var motion = new PixelMotion();
        int? dropped = null;
        var actions = new ArcadeActions((game, choice) =>
        {
            dropped = choice;
            run = CampaignArcade.PlayPlinko(run, choice);
            return run.Shop!.Arcade!.Pending;
        }, _ => { }, _ => { }, _ => { }, () => { }, () => { });
        var window = new Window { Width = 1100, Height = 800, Content = ArcadeScreen.Build(run, actions, motion) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
            if (controls.Any(c => c.Name?.StartsWith("PlinkoLane") == true) || !controls.Any(c => c.Name == "PlinkoTimingHint"))
                throw new Exception("With motion on, timing the drop replaces the lane buttons.");
            var dropper = controls.Single(c => c.Name == "PlinkoDropper");
            double Centre() => Canvas.GetLeft(dropper) + dropper.Width / 2;
            var seen = new HashSet<int>();
            for (var i = 0; i < 12; i++) { Pump(70); seen.Add((int)Math.Round(Centre())); }
            if (seen.Count < 6) throw new Exception("The dropper must keep sweeping the rail while waiting for the drop.");
            var at = Centre();
            var expected = Enumerable.Range(0, CampaignArcade.PlinkoLaneCount).MinBy(lane => Math.Abs(LaneX(lane) - at));
            var play = (Button)controls.Single(c => c.Name == "ArcadePlay");
            play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            if (dropped != expected) throw new Exception($"The drop must use the lane under the nozzle ({expected}), not {dropped}.");
            Pump(200);
            if (Math.Abs(Centre() - LaneX(expected)) > .01) throw new Exception("After the press the dropper must stop on its lane.");
            Console.WriteLine("PASS  With motion on, the Plinko dropper sweeps and the drop takes the lane under the nozzle");
        }
        finally { window.Close(); motion.SetEnabled(false); }
    }
}
