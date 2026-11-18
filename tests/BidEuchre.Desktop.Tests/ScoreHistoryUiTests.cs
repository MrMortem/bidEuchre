using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class ScoreHistoryUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Call(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Until(Func<bool> done)
    {
        if (!done())
        {
            using var timeout = new CancellationTokenSource(10000);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) timeout.Cancel(); };
            timer.Start(); try { Dispatcher.UIThread.MainLoop(timeout.Token); } finally { timer.Stop(); }
        }
        Require(done(), "The result transition must finish."); Dispatcher.UIThread.RunJobs();
    }
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static string Text(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static Button Button(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception("The table must expose " + name + ".");
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static GameView Sample(GamePhase phase, bool panic = false)
    {
        var contract = Contract.Create(BidLevel.Four, ContractMode.High, null);
        var first = new HandResult(1, 0, Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Clubs), 6, 0, 35, 4, "All six tricks were played.")
        { ScoreBreakdowns = [new(0, 6, 7, 2, 1, 2, 0) { GumballBonuses = [new(0, GumballId.Chaos, 3)] }, new(1, 0, 2, 0, 0, 2, 0)] };
        var second = new HandResult(2, 1, contract, 2, 4, 4, -4, "All six tricks were played.")
        { ScoreBreakdowns = [new(0, 4, 0, 0, 0, 1, 0), new(1, -4, 0, 0, 0, 1, 0)] };
        return new GameView(phase, panic ? 3 : 2, 3, phase == GamePhase.Playing ? 0 : null, [39, 0],
            Enumerable.Range(0, 4).Select(s => new PlayerView(s, new[] { "Alice", "Bea", "Cy", "Dee" }[s], s % 2, 0, null, false)).ToArray(),
            [], null, panic ? null : 1, panic ? null : contract, [], [], [4, 2], phase == GamePhase.GameComplete ? 0 : null,
            new(false, [], [], [], []), ["Current hand public line"])
        {
            HandHistory = [first, second], CompletionReason = panic ? "Panic Button: your team -8; hand consumed." : "All six tricks were played.",
            PublicEvents = ["Hand 1 began.", "Effect 1: Golden +$1 for Alice.", "The Partners Best cards were exchanged privately.", "Hand 2 began."]
        };
    }
    private sealed class Table : IDisposable
    {
        internal readonly MainWindow Window;
        internal readonly GameSession Session;
        internal Table()
        {
            Window = new MainWindow(); Window.Show(); Window.Width = 980; Window.Height = 680; Dispatcher.UIThread.RunJobs();
            Field<PixelMotion>(Window, "_motion").SetEnabled(false); Field<DispatcherTimer>(Window, "_refreshTimer").Stop();
            var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, new[] { "Alice", "Bea", "Cy", "Dee" }[s], PlayerKind.Human, null)).ToArray();
            var task = (Task<GameSession>)Call(Window, "OpenSessionAsync", seats, "Scoring table", null)!; Await(task); Session = task.Result;
            Call(Window, "ShowTable"); Await(Session.StartAsync()); Dispatcher.UIThread.RunJobs();
        }
        public void Dispose() { Window.Close(); Await(Session.DisposeAsync().AsTask()); }
    }
    private static void Capture(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } root) return;
        Directory.CreateDirectory(root); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var bitmap = window.CaptureRenderedFrame(); Require(bitmap is not null, "Score history must render.");
        using var stream = File.Create(Path.Combine(root, name + ".png")); bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var (name, test) in new (string, Action)[] { ("Hand and final history", HandAndFinalHistory), ("Public log during play", PublicLogDuringPlay), ("Campaign retained history", CampaignResultRetainsHistory) })
        {
            try { test(); Console.WriteLine("PASS  " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.WriteLine("FAIL  " + failures[^1]); }
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
    // Removing the summary, losing team components, or pushing final controls outside the viewport breaks this.
    private static void HandAndFinalHistory()
    {
        using var table = new Table(); var window = table.Window;
        Call(window, "RenderActions", Sample(GamePhase.HandComplete)); Dispatcher.UIThread.RunJobs();
        var summary = window.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(c => c.Name == "HandScoreSummary");
        Require(summary is not null, "A completed hand must expose a scrollable structured score summary.");
        var text = Text(summary!);
        foreach (var expected in new[] { "Hand 2", "SET", "4", "High", "TRICKS", "Team 1", "Team 2", "contract", "cards", "Jokers", "trick modifiers", "hand ×1", "−4" })
            Require(text.Contains(expected), "The summary must explain " + expected + ".");
        Capture(window, "hand-summary-compact");
        Call(window, "RenderWaitingTable"); Dispatcher.UIThread.RunJobs();
        Require(!summary!.IsEffectivelyVisible, "A new table must clear the previous match's completed summary.");
        Call(window, "RenderActions", Sample(GamePhase.GameComplete)); Dispatcher.UIThread.RunJobs();
        text = Text(window);
        Require(text.Contains("Alice") && text.Contains("Cy") && text.Contains("39") && text.Contains("Final scores"), "Free play must name the winners and final team scores.");
        summary = window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "HandScoreSummary");
        text = Text(summary); Require(text.IndexOf("Hand 1", StringComparison.Ordinal) < text.IndexOf("Hand 2", StringComparison.Ordinal), "Final hand history must be chronological.");
        Require(text.Contains("Chaos +3") && text.Contains("after hand multiplier") && text.Contains("hand ×2"), "The detailed history must explain per-type fixed rewards after the multiplier.");
        foreach (var name in new[] { "FreePlayRematch", "FreePlayNewTable" })
        {
            var button = Button(window, name); var point = button.TranslatePoint(default, window)!.Value;
            Require(point.Y >= 0 && point.Y + button.Bounds.Height <= window.ClientSize.Height + 1, "Final controls must stay reachable at the supported compact size.");
        }
        Capture(window, "final-history-compact");
        summary.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Require(Text(summary).Contains("Golden +$1"), "The final history's public log must expand safely.");
        Call(window, "RenderActions", Sample(GamePhase.HandComplete, panic: true)); Dispatcher.UIThread.RunJobs();
        summary = window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "HandScoreSummary");
        Require(Text(summary).Contains("Panic Button") && !Text(summary).Contains("Hand 2"), "A consumed hand must show its own reason, not the previous scored contract.");

    }
    private static void PublicLogDuringPlay()
    {
        using var table = new Table(); var window = table.Window; var sample = Sample(GamePhase.Playing);
        Call(window, "RenderActions", sample); Click(Button(window, "TablePublicLog"));
        var log = window.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(c => c.Name == "PublicEventLog");
        Require(log is not null && log.IsEffectivelyVisible, "The public effect log must be readable during play.");
        var text = Text(log!); Require(text.Contains("Golden +$1") && text.Contains("exchanged privately"), "Money effects and public exchange acknowledgements belong in the log.");
        Call(window, "RenderActions", sample with { PublicEvents = [.. sample.PublicEvents, "Effect 2: Fish +2 points for team 1."] });
        Require(Text(log!).Contains("Fish +2"), "An open public log must update as play continues.");
        Capture(window, "public-log-compact"); Click(Button(window, "ClosePublicLog")); Require(!log!.IsEffectivelyVisible, "The log must close back to play.");
        Console.WriteLine("PASS  Score explanations show structured hand/final history and a live public money/effect log at compact size");
    }
    private static void CampaignResultRetainsHistory()
    {
        using var table = new Table(); var window = table.Window; var flow = Field<CampaignFlow>(window, "_campaign");
        typeof(CampaignFlow).GetProperty(nameof(CampaignFlow.Run))!.SetValue(flow, new CampaignRun(Phase: CampaignPhase.InMatch));
        Await((Task)Call(window, "CompleteCampaignMatchAsync", table.Session, Sample(GamePhase.GameComplete))!);
        Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CampaignContinue"));
        var summary = window.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(c => c.Name == "HandScoreSummary");
        Require(summary is not null && Text(summary).Contains("Hand 2") && Text(summary).Contains("SET"), "The campaign result page must retain score history after the table is disposed.");
        Capture(window, "campaign-result-history-compact");
        Require(Button(window, "CampaignContinue").IsEffectivelyVisible, "Campaign history must preserve Continue.");
        Console.WriteLine("PASS  Campaign final hand history remains readable after automatic result transition");
    }
}
