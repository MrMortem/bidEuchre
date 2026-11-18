using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class LiveSettingsTests
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static object? Call(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Button Find(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception($"The active screen must expose {name}.");
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Until(Func<bool> ready)
    {
        if (!ready())
        {
            using var deadline = new CancellationTokenSource(10000);
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            poll.Tick += (_, _) =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                if (ready()) deadline.Cancel();
            };
            poll.Start();
            try { Dispatcher.UIThread.MainLoop(deadline.Token); }
            finally { poll.Stop(); }
        }
        Require(ready(), "The table navigation must complete without waiting on a hidden animation.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static string State(GameSession session)
    {
        var view = session.GetViewAsync(0); Await(view); return JsonSerializer.Serialize(view.Result);
    }
    private static void PumpFor(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        Until(() => DateTime.UtcNow >= until);
    }
    private static void Capture(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Require(bitmap is not null, "The live settings preview must render.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run()
    {
        var failures = new List<string>();
        foreach (var campaign in new[] { false, true })
        foreach (var animated in new[] { false, true })
        {
            var label = $"Live graphics settings preserve {(campaign ? "campaign" : "free-play")} table ({(animated ? "pending deal" : "bot delay")})";
            try { PreserveTable(campaign, animated); Console.WriteLine("PASS  " + label); }
            catch (Exception error) { failures.Add(label + ": " + error.Message); Console.WriteLine("FAIL  " + failures[^1]); }
        }
        try { CompletedMatchWinsNavigationRace(); Console.WriteLine("PASS  Completed campaign match wins settings navigation without a forfeit or paused result"); }
        catch (Exception error) { failures.Add("Campaign completion/settings race: " + error.Message); Console.WriteLine("FAIL  " + failures[^1]); }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }

    private static void CompletedMatchWinsNavigationRace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"terminal-settings-{Guid.NewGuid():N}");
        var old = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        new GamePreferences(MotionEnabled: false).Save(Path.Combine(root, "BidEuchre", "preferences.json"));
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", root);
        var window = new MainWindow();
        GameSession? session = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Click(Find(window, "MenuPlay")); Dispatcher.UIThread.RunJobs();
            Click(Find(window, "CampaignStartMatch"));
            Until(() => Field<GameSession?>(window, "_session")?.Started == true);
            session = Field<GameSession>(window, "_session");
            var gate = (SemaphoreSlim)typeof(GameSession).GetField("_gate", Private)!.GetValue(session)!;
            Await(gate.WaitAsync());
            Task ending; Task navigation;
            try
            {
                // The real terminal mutation is already queued when navigation begins awaiting pause.
                ending = session.DeveloperAsync(game => game.DeveloperEndMatch(1));
                navigation = (Task)Call(window, "OpenTableSettingsAsync")!;
            }
            finally { gate.Release(); }
            Await(Task.WhenAll(ending, navigation));
            var flow = Field<CampaignFlow>(window, "_campaign");
            Require(!flow.InMatch && flow.Run!.Lives == 2, "The completed loss must be recorded exactly once before choosing a destination.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SettingsBack"), "A terminal campaign must show its result instead of paused settings.");
            // The table detaches before asynchronous session disposal finishes and the result is presented.
            Until(() => Field<GameSession?>(window, "_session") is null &&
                window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CampaignContinue"));
            Require(flow.Run!.Lives == 2, "Result navigation must not add a forfeit to the completed match.");
            Require(window.GetVisualDescendants().OfType<CampaignInventoryScreen>().Count() == 1, "The result must keep the campaign inventory visible.");
        }
        finally
        {
            window.Close();
            if (session is not null) Await(session.DisposeAsync().AsTask());
            Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", old);
            Directory.Delete(root, true);
        }
    }

    // Removing the live Settings route, pausing too late, rebuilding the engine, or forgetting to
    // finish a cancelled deal each breaks this user flow through the real window and session.
    private static void PreserveTable(bool campaign, bool animated)
    {
        var root = Path.Combine(Path.GetTempPath(), $"live-settings-{Guid.NewGuid():N}");
        var old = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        var preferencesPath = Path.Combine(root, "BidEuchre", "preferences.json");
        new GamePreferences(MotionEnabled: animated, WindowWidth: 1280, WindowHeight: 720).Save(preferencesPath);
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", root);
        var window = new MainWindow();
        GameSession? session = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            // The minimum viewport is where these settings are most useful.
            window.Width = 980; window.Height = 680;
            if (campaign)
            {
                Click(Find(window, "MenuPlay"));
                Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CampaignStartMatch"));
                Click(Find(window, "CampaignStartMatch"));
                Until(() => Field<GameSession?>(window, "_session")?.Started == true);
                session = Field<GameSession>(window, "_session");
                if (!animated && session.Game!.CampaignActor == 0)
                    Await(session.ExecuteCampaignAsync(new(0, session.Game.ExportCampaignSnapshot().Revision,
                        session.Game.DealSerial, new CampaignCommand.Pass())));
            }
            else
            {
                Click(Find(window, "MenuTable"));
                Until(() => ReferenceEquals(Field<ContentControl>(window, "_screenHost").Content, Field<Grid>(window, "_tableScreen")));
                var seats = Enumerable.Range(0, 4).Select(seat => new SeatConfiguration(seat, $"Bot {seat}", PlayerKind.Bot, EngineCatalog.BuiltInEngineId)).ToArray();
                var open = (Task<GameSession>)Call(window, "OpenSessionAsync", seats, "Settings test", null!)!;
                Await(open); session = open.Result;
                Await((Task)Call(window, "StartSessionAsync")!);
            }
            if (!animated) PumpFor(80); // enter the bot delay before navigating
            else Require(session.WaitingForDeal, "The animated fixture must still be awaiting its deal.");
            var game = session.Game;
            var flow = Field<CampaignFlow>(window, "_campaign");
            var lives = flow.Run?.Lives;
            var settings = Find(window, "TableSettings");
            Dispatcher.UIThread.RunJobs();
            var at = settings.TranslatePoint(default, window)!.Value;
            Require(at.X >= 0 && at.Y >= 0 && at.X + settings.Bounds.Width <= window.ClientSize.Width + 1
                && at.Y + settings.Bounds.Height <= window.ClientSize.Height + 1, "Live Settings must be reachable in the minimum window.");
            Rect Bounds(Control control)
            {
                var corner = control.TranslatePoint(default, window)!.Value;
                return new Rect(corner, control.Bounds.Size);
            }
            var navigation = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "TableNavigation");
            var score = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "TableScore");
            foreach (var stripPart in new Control[] { score, Field<TextBlock>(window, "_contractText"), Field<TextBlock>(window, "_actionTitle") })
                Require(!Bounds(navigation).Intersects(Bounds(stripPart)), "Settings and Menu must clear the score/status strip in the compact table.");
            Capture(window, $"settings-table-compact-{campaign}-{animated}");
            var oldActions = Field<WrapPanel>(window, "_actionButtons").Children.OfType<Button>().ToArray();
            Click(settings);
            Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SettingsBack"));
            Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Match paused"), "Settings must communicate that bots are paused.");
            Require(Find(window, "SettingsBack").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Back to table"), "Live settings must offer a direct table return.");
            Require(!window.GetVisualDescendants().OfType<CampaignInventoryScreen>().Any(), "Settings must leave the game's inventory offscreen.");
            var paused = State(session);
            foreach (var stale in oldActions) Click(stale);
            PumpFor(1150); // longer than the default bot action delay
            Capture(window, $"settings-{campaign}-{animated}");
            Require(State(session) == paused, "Neither delayed bots nor stale table buttons may act while settings is open.");
            Require(!session.WaitingForDeal, "A hidden table must release its cancelled deal gate.");
            var motion = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SettingsMotion");
            motion.SelectedIndex = animated ? 2 : 0; // Full, Reduced, Off
            var scale = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SettingsUiScale");
            scale.SelectedIndex = 2;
            var fullscreen = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SettingsFullscreen");
            fullscreen.SelectedIndex = 0; fullscreen.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var saved = GamePreferences.Load(preferencesPath);
            Require(saved.MotionEnabled == !animated && saved.UiScale == 1.5 && !saved.Fullscreen, "Live graphics changes must use normal preference saving.");
            Require(State(session) == paused, "Applying graphics must preserve cards, scores, auction, actor and campaign revision.");
            Click(Find(window, "SettingsBack"));
            Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "TableSettings"));
            Require(ReferenceEquals(Field<GameSession>(window, "_session"), session) && ReferenceEquals(session.Game, game), "Return must retain the session and engine identities.");
            Require(State(session) == paused, "Return must not replay old input or reset the live hand.");
            Require(flow.Run?.Lives == lives, "Opening and closing settings must not cost campaign lives.");
            Require(Field<ContentControl>(window, "_tableHost").IsVisible, "The restored table must finish loading.");
            Require(Field<TableInputState>(window, "_input").Locked == false, "Returning must unlock fresh table input.");
            Require(!(Field<DeckDeal>(window, "_deckDeal").Running), "Return must not replay the cancelled deal animation.");
            Capture(window, $"settings-return-{campaign}-{animated}");
            if (campaign && session.Game!.CampaignActor == 0)
                Await(session.ExecuteCampaignAsync(new(0, session.Game.ExportCampaignSnapshot().Revision,
                    session.Game.DealSerial, new CampaignCommand.Pass())));
            Until(() => State(session) != paused);
            if (campaign)
            {
                var menu = Find(window, "TableBackToMenu"); Click(menu);
                Require(Equals(menu.Content, "Forfeit match?") && ReferenceEquals(Field<GameSession>(window, "_session"), session), "Menu must still require the campaign forfeit confirmation.");
                Require(flow.Run!.Lives == lives, "The first menu press must not spend a life.");
            }
        }
        finally
        {
            window.Close();
            if (session is not null) Await(session.DisposeAsync().AsTask());
            Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", old);
            Directory.Delete(root, true);
        }
    }
}
