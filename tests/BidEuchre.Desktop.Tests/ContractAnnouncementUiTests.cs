using System.Diagnostics;
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

internal static class ContractAnnouncementUiTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MainWindow w, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(w)!;
    private static void Set(MainWindow w, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(w, value);
    private static object? Call(MainWindow w, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(w, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Until(Func<bool> done)
    {
        if (!done())
        {
            using var limit = new CancellationTokenSource(7000);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
            timer.Start(); try { Dispatcher.UIThread.MainLoop(limit.Token); } finally { timer.Stop(); }
        }
        Require(done(), "Contract presentation must finish without a hidden lifecycle wait.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Click(MainWindow w, string name) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static TextBlock? Text(MainWindow w, string name) => w.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == name);
    private static void Capture(MainWindow w, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } root) return;
        Directory.CreateDirectory(root); using var bitmap = w.CaptureRenderedFrame(); Require(bitmap is not null, "Contract preview must render.");
        using var stream = File.Create(Path.Combine(root, name + ".png")); bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    public static void Run()
    {
        MotionCues();
        SortRetry();
        BotCallerAndHiddenPage();
        foreach (var animated in new[] { false, true })
        foreach (var mode in Enum.GetValues<ContractMode>()) Announce(animated, mode);
        Console.WriteLine("PASS  Caller and suit/mode remain readable, hold before play, suppress replay and recover from settings/session changes");
    }
    private static void MotionCues()
    {
        using var session = new SessionHolder(ContractPresentationTests.Session());
        Await(session.Value.StartAsync());
        var before = session.Value.GetViewAsync(0); Await(before);
        var tracker = new TableMotionTracker(); tracker.Update(before.Result, "first");
        Await(ContractPresentationTests.Choose(session.Value));
        var after = session.Value.GetViewAsync(0); Await(after);
        bool Flag(TableMotionCue cue, string name) => (bool?)cue.GetType().GetProperty(name)?.GetValue(cue) ?? false;
        Require(Flag(tracker.Update(after.Result, "first"), "ContractChosen"), "A human chosen contract produces a one-shot public cue.");
        Require(!Flag(tracker.Update(after.Result, "first"), "ContractChosen"), "Ordinary polling never repeats the contract cue.");
        Require(!Flag(tracker.Update(after.Result, "other"), "ContractChosen"), "Changing viewer/session does not replay history.");
        var changed = after.Result! with { Contract = after.Result.Contract! with { Trump = Suit.Clubs } };
        var cue = tracker.Update(changed, "other");
        Require(!Flag(cue, "ContractChosen") && Flag(cue, "TrumpChanged"), "Chaos trump refreshes its compact indicator without another start hold.");
        cue = tracker.Update(changed with { DealSerial = changed.DealSerial + 1 }, "other");
        Require(Flag(cue, "ContractChosen") && !Flag(cue, "TrumpChanged"), "A new deal at the same hand number gets its own initial contract cue.");
    }
    internal static void SortRetry()
    {
        var w = new MainWindow(); var session = ContractPresentationTests.Session(campaign: true);
        try
        {
            Field<DispatcherTimer>(w, "_refreshTimer").Stop(); Await(session.StartAsync()); Set(w, "_session", session); Set(w, "_viewer", 0);
            Await(session.DeveloperAsync(game =>
            {
                game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Bid(BidLevel.Three));
                while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
                game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
            }));
            var viewTask = session.GetViewAsync(0); Await(viewTask); var view = viewTask.Result!;
            var expected = HandOrder.Sort(view.Campaign!.Players[0].VisibleCards, c => c.Face, Field<GamePreferences>(w, "_preferences").HandSort, view.Contract).Select(c => c.Id).ToArray();
            Await(session.ArrangeCampaignHandAsync(0, expected.Reverse().ToArray()));
            viewTask = session.GetViewAsync(0); Await(viewTask); view = viewTask.Result!;
            Await(session.PauseAsync());
            var attempt = (Task<bool>)Call(w, "AutoArrangeCampaignHandAsync", session, view, view.Campaign)!; Await(attempt);
            Require(!attempt.Result, "A settings-paused sort attempt cannot mutate the hand.");
            Field<ContentControl>(w, "_playHost").Content = (Control)Call(w, "BuildGamePage")!;
            Set(w, "_liveSettingsOpen", true);
            // Exercise the real settings return: its first refresh is still paused. The return
            // must refresh again after resume, otherwise a newly chosen contract remains unsorted.
            Await((Task)Call(w, "ReturnFromTableSettingsAsync", session, Field<long>(w, "_sessionGeneration"))!);
            var arranged = session.GetViewAsync(0); Await(arranged);
            Require(arranged.Result!.Campaign!.Players[0].VisibleCards.Select(c => c.Id).SequenceEqual(expected), "The retry restores the preferred trump-first order.");
            Console.WriteLine("PASS  A paused contract sort retries after resume instead of recording a failed stage");
        }
        finally { w.Close(); Await(session.DisposeAsync().AsTask()); }
    }
    private static void BotCallerAndHiddenPage()
    {
        var w = new MainWindow(); var session = ContractPresentationTests.Session(bots: true);
        try
        {
            w.Show(); w.Width = 980; w.Height = 680; Dispatcher.UIThread.RunJobs(); Field<DispatcherTimer>(w, "_refreshTimer").Stop();
            Set(w, "_session", session); Set(w, "_viewer", 0);
            Field<ContentControl>(w, "_playHost").Content = (Control)Call(w, "BuildGamePage")!;
            Call(w, "ShowTable"); Await(session.StartAsync()); Until(() => ContractPresentationTests.Pending(session));
            Await((Task)Call(w, "RefreshGameAsync")!); Until(() => Text(w, "ContractAnnouncementCaller") is not null);
            Require(Text(w, "ContractAnnouncementCaller")!.Text == $"Caller {session.Game!.Bidder}", "A bot caller uses the same visible caller announcement as a human.");
            Capture(w, "contract-bot-caller");
            Call(w, "ShowMenu"); Await((Task)Call(w, "RefreshGameAsync")!);
            Until(() => !ContractPresentationTests.Pending(session));
            Require(Text(w, "ContractAnnouncementCaller") is null, "A hidden page removes its cue and acknowledges the presentation gate.");
        }
        finally { w.Close(); Await(session.DisposeAsync().AsTask()); }
    }

    private static void Announce(bool animated, ContractMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), $"contract-ui-{Guid.NewGuid():N}");
        var old = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        new GamePreferences(MotionEnabled: animated, WindowWidth: 980, WindowHeight: 680).Save(Path.Combine(root, "BidEuchre", "preferences.json"));
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", root);
        var w = new MainWindow(); GameSession? session = null;
        try
        {
            w.Show(); w.Width = 980; w.Height = 680; Dispatcher.UIThread.RunJobs(); Click(w, "MenuTable");
            var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"Caller {s}", PlayerKind.Human, null)).ToArray();
            var opening = (Task<GameSession>)Call(w, "OpenSessionAsync", seats, "Cue table", null)!; Await(opening); session = opening.Result;
            Await(session.StartAsync()); Await((Task)Call(w, "RefreshGameAsync")!);
            var choosing = ContractPresentationTests.Choose(session, mode); Await(choosing);
            var caller = session.Game!.Bidder!.Value;
            var clock = Stopwatch.StartNew(); Await((Task)Call(w, "RefreshGameAsync")!);
            Until(() => Text(w, "ContractAnnouncementCaller") is not null);
            Require(Text(w, "ContractAnnouncementCaller")!.Text == $"Caller {caller}", "The announced caller is the actual bidder.");
            var value = Text(w, "ContractAnnouncementValue")!;
            Require(value.Text!.Contains(mode == ContractMode.Trump ? "Hearts" : mode.ToString()), "The initial cue names the chosen suit or High/Low mode.");
            Require(ContractPresentationTests.Pending(session) && !Field<ContentControl>(w, "_tableHost").IsEnabled,
                "The native hand remains disabled during the readable cue, including Motion off.");
            var badge = value.GetVisualAncestors().OfType<Border>().First(b => b.Name == "ContractAnnouncement");
            if (!animated) Require(badge.Opacity == 1, "Motion off shows the full static cue throughout the reading hold.");
            var top = badge.TranslatePoint(default, w)!.Value;
            Require(top.X >= 0 && top.Y >= 0 && top.X + badge.Bounds.Width <= w.ClientSize.Width + 1
                && top.Y + badge.Bounds.Height <= w.ClientSize.Height + 1, "The caller and value fit the supported small window.");
            Capture(w, $"contract-{animated}-{mode}");
            if (!animated && mode == ContractMode.Trump)
            {
                Click(w, "TableSettings"); Until(() => w.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SettingsBack"));
                Require(!ContractPresentationTests.Pending(session), "Settings safely acknowledges the canceled visible cue.");
                Click(w, "SettingsBack"); Until(() => w.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "TableSettings"));
                Require(Text(w, "ContractAnnouncementCaller") is null && Field<TextBlock>(w, "_contractText").Text!.Contains($"Caller {caller}"), "Return shows the caller and chosen trump without replaying the center cue.");
            }
            else if (animated && mode == ContractMode.Low)
            {
                var detached = (GameSession)Call(w, "DetachSession")!;
                Await(detached.DisposeAsync().AsTask());
                Dispatcher.UIThread.RunJobs();
                Require(Text(w, "ContractAnnouncementCaller") is null, "Replacing/disposal removes a pending cue without a stale completion.");
            }
            else
            {
                Until(() => !ContractPresentationTests.Pending(session));
                Require(clock.ElapsedMilliseconds >= 1400, "The initial announcement holds about 1.5 seconds independently of motion preference.");
                Await((Task)Call(w, "RefreshGameAsync")!); Await((Task)Call(w, "RefreshGameAsync")!);
                Require(Text(w, "ContractAnnouncementCaller") is null, "Repeated refreshes do not replay the initial cue.");
                Require(Field<TextBlock>(w, "_contractText").Text!.Contains($"Caller {caller}"), "The compact contract retains its caller after the hold.");
            }
        }
        finally { w.Close(); if (session is not null) Await(session.DisposeAsync().AsTask()); Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", old); Directory.Delete(root, true); }
    }
    private sealed class SessionHolder(GameSession value) : IDisposable { public GameSession Value => value; public void Dispose() => Await(value.DisposeAsync().AsTask()); }
}
