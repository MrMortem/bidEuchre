using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class TableUpdateProfile
{
    public static void Run(bool verifyIdentity = false)
    {
        var window = new MainWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        var build = typeof(MainWindow).GetMethod("BuildTable", flags)!.CreateDelegate<Func<GameView, Control>>(window);
        Field<DispatcherTimer>("_refreshTimer").Stop();
        Field<PixelMotion>("_motion").SetEnabled(false);
        var engine = new GameEngine(["South", "West", "North", "East"], 73);
        engine.StartGame(0);
        var before = engine.CreateView(0);
        engine.PlaceBid(engine.CurrentSeat!.Value, null);
        var after = engine.CreateView(0);
        var host = new ContentControl();
        window.Content = host;
        window.Show();
        try
        {
            void Update(GameView view)
            {
                host.Content = build(view);
                Dispatcher.UIThread.RunJobs();
            }
            Update(before);
            var originals = ((Control)host.Content!).GetVisualDescendants().OfType<Button>().Where(button => button.Width == 124).ToArray();
            Update(after);
            if (verifyIdentity)
            {
                var current = ((Control)host.Content!).GetVisualDescendants().OfType<Button>().Where(button => button.Width == 124).ToArray();
                if (originals.Length != 6 || !originals.SequenceEqual(current))
                    throw new Exception("The visible hand must retain its card controls across an opponent bid.");
                return;
            }
            for (var i = 0; i < 20; i++) Update(i % 2 == 0 ? before : after);
            var samples = new double[160];
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < samples.Length; i++)
            {
                var started = Stopwatch.GetTimestamp();
                Update(i % 2 == 0 ? before : after);
                samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / samples.Length;
            Array.Sort(samples);
            Console.WriteLine($"Table update ({samples.Length}): median {samples[79]:F3} ms, p95 {samples[151]:F3} ms, {bytes:N0} UI-thread bytes/update");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
