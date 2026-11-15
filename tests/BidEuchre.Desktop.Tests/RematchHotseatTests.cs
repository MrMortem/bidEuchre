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

internal static class RematchHotseatTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Call(MainWindow w, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(w, args);
    private static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Until(Func<bool> done)
    {
        if (!done())
        {
            using var timeout = new CancellationTokenSource(10000);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) timeout.Cancel(); };
            timer.Start(); try { Dispatcher.UIThread.MainLoop(timeout.Token); } finally { timer.Stop(); }
        }
        Require(done(), "The table operation must finish without a hidden presentation wait.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static Button? Button(MainWindow w, string name) => w.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Click(MainWindow w, string name) => Click(Button(w, name) ?? throw new Exception($"The table must offer {name}."));
    private static void Refresh(MainWindow w) => Await((Task)Call(w, "RefreshGameAsync")!);
    private static GameView View(GameSession s, int? seat = null) { var task = s.GetViewAsync(seat); Await(task); return task.Result!; }
    private static int Faces(MainWindow w) => Field<ContentControl>(w, "_tableHost").GetVisualDescendants().OfType<Button>().Count(b => Avalonia.Automation.AutomationProperties.GetName(b)?.Contains(" of ") == true);
    private static void Reveal(MainWindow w)
    {
        Require(Button(w, "HotseatReveal") is not null && Faces(w) == 0, "A handoff hides every private card until explicit Reveal.");
        Click(w, "HotseatReveal"); Until(() => Button(w, "HotseatReveal") is null); Refresh(w);
    }
    private static void Capture(MainWindow w, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } root) return;
        Directory.CreateDirectory(root); using var bitmap = w.CaptureRenderedFrame(); Require(bitmap is not null, "Handoff must render.");
        using var stream = File.Create(Path.Combine(root, name + ".png")); bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class Table : IDisposable
    {
        internal readonly MainWindow Window;
        internal GameSession Session;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rematch-hotseat-" + Guid.NewGuid().ToString("N"));
        private readonly string? _prior = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        internal Table(bool automatic, bool motion = false, int humans = 4)
        {
            Directory.CreateDirectory(Path.Combine(_root, "BidEuchre"));
            File.WriteAllText(Path.Combine(_root, "BidEuchre", "preferences.json"), JsonSerializer.Serialize(new { AutomaticHotseatPrivacy = automatic, MotionEnabled = motion }));
            Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", _root);
            Window = new MainWindow(); Window.Show(); Window.Width = 980; Window.Height = 680; Dispatcher.UIThread.RunJobs();
            Field<DispatcherTimer>(Window, "_refreshTimer").Stop(); Click(Window, "MenuTable");
            Until(() => ReferenceEquals(Field<ContentControl>(Window, "_screenHost").Content, Field<Grid>(Window, "_tableScreen")));
            Field<ComboBox>(Window, "_pace").SelectedIndex = 2;
            var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, new[] { "Alice", "Bea", "Cy", "Dee" }[s],
                s < humans ? PlayerKind.Human : PlayerKind.Bot, s < humans ? null : EngineCatalog.BuiltInEngineId)).ToArray();
            var opening = (Task<GameSession>)Call(Window, "OpenSessionAsync", seats, "Saturday friends", null)!; Await(opening); Session = opening.Result;
            Await(Session.StartAsync()); Refresh(Window);
        }
        public void Dispose()
        {
            var current = Field<GameSession?>(Window, "_session"); Window.Close(); Await(Session.DisposeAsync().AsTask());
            if (current is not null && !ReferenceEquals(current, Session)) Await(current.DisposeAsync().AsTask());
            Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", _prior); Directory.Delete(_root, true);
        }
    }
    internal static void Run()
    {
        var failures = new List<string>();
        void Check(string name, Action test) { try { test(); Console.WriteLine("PASS  " + name); } catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL  " + failures[^1]); } }
        Check("Free-play winners, same-table fresh rematch, prior disposal and New table", Rematch);
        Check("Completed campaign retains its result lifecycle without free-play controls", CampaignExclusion);
        foreach (var motion in new[] { false, true }) Check($"Automatic privacy hides handoff and rejects stale controls ({motion})", () => Handoff(motion));
        Check("Partner's Best both exchanges and next-deal privacy", ExchangesAndDeal);
        Check("Queued Reveal cancels safely when its table is disposed", QueuedRevealDisposal);
        Check("Privacy preference, manual mode, one-human and spectator exclusions", Exclusions);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
    // Missing result controls, reading edited setup instead of live settings, or skipping disposal breaks this flow.
    private static void Rematch()
    {
        using var t = new Table(false, humans: 2); var w = t.Window; var old = t.Session;
        Await(old.DeveloperAsync(g => g.DeveloperEndMatch(0))); Refresh(w);
        Require(Field<TextBlock>(w, "_actionTitle").Text!.Contains("Alice") && Field<TextBlock>(w, "_actionTitle").Text!.Contains("Cy"), "The result must name both actual winning players.");
        Field<ComboBox>(w, "_pace").SelectedIndex = 0; Field<TextBox>(w, "_tableName").Text = "Edited setup";
        Field<TextBox[]>(w, "_seatNames")[0].Text = "Changed"; Capture(w, "freeplay-result");
        Click(w, "FreePlayRematch"); Until(() => Field<GameSession?>(w, "_session") is { Started: true } s && !ReferenceEquals(s, old));
        var next = Field<GameSession>(w, "_session"); var view = View(next);
        Require(next.Seats.Select(s => s.Name).SequenceEqual(new[] { "Alice", "Bea", "Cy", "Dee" }) && next.Seats.SequenceEqual(old.Seats)
            && next.Name == "Saturday friends" && Field<TimeSpan>(next, "_botActionDelay") == TimeSpan.FromMilliseconds(600), "Rematch must use actual seats, names, stable engine IDs, table name and original pace.");
        Require(next.Id != old.Id && !ReferenceEquals(next.Game, old.Game) && view.HandNumber == 1 && view.Scores.All(s => s == 0) && view.Phase == GamePhase.Bidding, "A rematch must start a fresh game.");
        bool disposed = false; try { View(old); } catch (ObjectDisposedException) { disposed = true; }
        Require(disposed && Field<System.Collections.IDictionary>(old, "_bots").Count == 0, "Rematch must dispose the previous session and its bot resources.");
        Await(next.DeveloperAsync(g => g.DeveloperEndMatch(1))); Refresh(w); Click(w, "FreePlayNewTable");
        Until(() => Field<GameSession?>(w, "_session") is null);
        Require(Field<ContentControl>(w, "_playHost").GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "New session"), "New table must reach editable setup after disposal.");
    }
    private static void CampaignExclusion()
    {
        using var t = new Table(false, humans: 1); var w = t.Window;
        Await((Task)Call(w, "ReturnToMenuAsync", new Button())!);
        Click(w, "MenuPlay"); Until(() => Button(w, "CampaignStartMatch") is not null); Click(w, "CampaignStartMatch");
        Until(() => Field<GameSession?>(w, "_session")?.Started == true);
        var campaignSession = Field<GameSession>(w, "_session");
        Await(campaignSession.DeveloperAsync(g => g.DeveloperEndMatch(0))); Refresh(w);
        Require(!Field<CampaignFlow>(w, "_campaign").InMatch && ReferenceEquals(campaignSession, Field<GameSession>(w, "_session")),
            "The completed campaign fixture must reach the result-recorded interval before table disposal.");
        Require(Button(w, "FreePlayRematch") is null && Button(w, "FreePlayNewTable") is null,
            "A recorded campaign result must never gain free-play rematch or New table controls.");
    }
    // Switching viewers before the mask, accepting a retained button, or forgetting settings return breaks privacy.
    private static void Handoff(bool motion)
    {
        using var t = new Table(true, motion); var w = t.Window; var s = t.Session;
        if (Button(w, "HotseatReveal") is not null) Reveal(w);
        Require(Field<int?>(w, "_viewer") == View(s).CurrentSeat && Faces(w) == 6, $"Reveal switches to exactly the acting human's hand: viewer={Field<int?>(w, "_viewer")}, actor={View(s).CurrentSeat}, faces={Faces(w)}, loading={Field<TableInputState>(w, "_input").Loading}, refreshing={Field<bool>(w, "_refreshing")}, pending={Button(w, "HotseatReveal") is not null}, phase={View(s).Phase}.");
        var oldViewer = Field<int?>(w, "_viewer");
        var stale = Field<WrapPanel>(w, "_actionButtons").Children.OfType<Button>().First();
        Click(stale); Refresh(w);
        var pending = View(s).CurrentSeat;
        Require(pending != oldViewer && Button(w, "HotseatReveal") is not null && Field<int?>(w, "_viewer") == oldViewer && Faces(w) == 0,
            "Another human's turn must mask all hands before switching the viewer.");
        Require(!Field<DeckDeal>(w, "_deckDeal").Running && !Field<CardFlights>(w, "_cardFlights").Collecting, "Handoff must remove private presentation overlays.");
        Field<ComboBox>(w, "_viewerSeat").SelectedIndex = pending!.Value;
        Require(Field<int?>(w, "_viewer") == oldViewer && Faces(w) == 0, "A retained or queued dropdown selection cannot reveal the next hand.");
        Click(stale); Require(View(s).CurrentSeat == pending, "A stale previous-player action cannot act during handoff.");
        var prompt = Field<TextBlock>(w, "_centerTitle"); Dispatcher.UIThread.RunJobs();
        Require(prompt.Text!.Contains(View(s).Players[pending.Value].Name), "Handoff must name the receiving player.");
        var pos = prompt.TranslatePoint(default, w)!.Value;
        Require(pos.X >= 0 && pos.Y >= 0 && pos.X + prompt.Bounds.Width <= w.ClientSize.Width + 1 && pos.Y + prompt.Bounds.Height <= w.ClientSize.Height + 1,
            "Handoff text must fit the supported compact window."); Capture(w, $"hotseat-pending-{motion}");
        // A Reveal already queued on the session gate must not expose a hand after settings opens.
        var revealGate = Field<SemaphoreSlim>(s, "_gate"); Await(revealGate.WaitAsync()); Task navigation;
        try { Click(w, "HotseatReveal"); navigation = (Task)Call(w, "OpenTableSettingsAsync")!; }
        finally { revealGate.Release(); }
        Await(navigation); Until(() => Button(w, "SettingsBack") is not null);
        Require(Field<int?>(w, "_viewer") == oldViewer, "A queued Reveal cannot switch viewers after its input generation has expired.");
        Click(w, "SettingsBack");
        Until(() => Button(w, "TableSettings") is not null); Refresh(w);
        Require(Button(w, "HotseatReveal") is not null && Faces(w) == 0 && Field<int?>(w, "_viewer") == oldViewer, "Returning from settings must preserve hidden pending handoff.");
        Reveal(w); var before = JsonSerializer.Serialize(View(s)); Click(stale); Dispatcher.UIThread.RunJobs();
        Require(JsonSerializer.Serialize(View(s)) == before, "A retained previous-player button cannot become a valid next-player action after Reveal.");
        Require(Field<int?>(w, "_viewer") == pending && Faces(w) == 6, "Explicit Reveal must show the receiving human only."); Capture(w, $"hotseat-revealed-{motion}");
        // Queue refresh behind the session gate, then settings changes the input generation before it resolves.
        var gate = Field<SemaphoreSlim>(s, "_gate"); Await(gate.WaitAsync()); Task refresh; Task settings;
        try { refresh = (Task)Call(w, "RefreshGameAsync")!; settings = (Task)Call(w, "OpenTableSettingsAsync")!; }
        finally { gate.Release(); }
        Await(Task.WhenAll(refresh, settings)); Require(Button(w, "SettingsBack") is not null, "A queued refresh cannot replace settings with private table content.");
        Click(w, "SettingsBack"); Until(() => Button(w, "TableSettings") is not null);
    }
    private static void ExchangesAndDeal()
    {
        using var t = new Table(true, motion: true); var w = t.Window; var s = t.Session;
        if (Button(w, "HotseatReveal") is not null) Reveal(w);
        var bidder = View(s).CurrentSeat!.Value;
        Await(s.DeveloperAsync(g => { g.PlaceBid(bidder, BidLevel.PartnersBest); while (g.Phase == GamePhase.Bidding) g.PlaceBid(g.CurrentSeat!.Value, null); g.ChooseContract(bidder, ContractMode.Trump, Suit.Hearts); }));
        Refresh(w); Until(() => !s.WaitingForContractPresentation); Refresh(w);
        Require(View(s).Phase == GamePhase.ExchangingBidderCard && Faces(w) == 6, "The approved bidder can choose their Partner's Best outgoing card.");
        var outgoing = Field<ContentControl>(w, "_tableHost").GetVisualDescendants().OfType<Button>().First(b => b.Focusable);
        Click(outgoing); Refresh(w);
        Require(View(s).Phase == GamePhase.ExchangingPartnerCard && Faces(w) == 0, "The partner's seven-card exchange choice must stay private.");
        Reveal(w); Require(Faces(w) == 7, "The partner sees all seven exchange cards only after Reveal.");
        var beforeStaleExchange = JsonSerializer.Serialize(View(s)); Click(outgoing); Dispatcher.UIThread.RunJobs();
        Require(JsonSerializer.Serialize(View(s)) == beforeStaleExchange, "The retained bidder card cannot become the partner's return choice after Reveal.");
        var returning = Field<ContentControl>(w, "_tableHost").GetVisualDescendants().OfType<Button>().First(b => b.Focusable);
        Click(returning); Refresh(w); Require(Faces(w) == 0 && Button(w, "HotseatReveal") is not null, "Returning the partner's card must hand off privately to the opening player.");
        Reveal(w); var playing = View(s); var active = playing.CurrentSeat!.Value;
        Await(s.DeveloperAsync(g => g.ApplyIllegalPlayPenalty(active))); Refresh(w);
        Require(View(s).Phase == GamePhase.HandComplete, "The fixture must finish one hand.");
        var deal = Field<WrapPanel>(w, "_actionButtons").Children.OfType<Button>().Single(); Click(deal); Refresh(w);
        Require(View(s).HandNumber == 2 && Faces(w) == 0 && Button(w, "HotseatReveal") is not null, "The next deal must hide the new human actor's private hand.");
        Until(() => !s.WaitingForDeal); Reveal(w); Require(Faces(w) == 6, "Canceling a hidden deal must release its lifecycle gate and allow the revealed actor to bid.");
    }
    private static void QueuedRevealDisposal()
    {
        using var t = new Table(true); var w = t.Window; var s = t.Session;
        if (Button(w, "HotseatReveal") is null)
        {
            Click(Field<WrapPanel>(w, "_actionButtons").Children.OfType<Button>().First()); Refresh(w);
        }
        var oldViewer = Field<int?>(w, "_viewer"); var actor = View(s).CurrentSeat!.Value;
        var gate = Field<SemaphoreSlim>(s, "_gate"); Await(gate.WaitAsync()); Task reveal; Task leaving;
        try
        {
            reveal = (Task)Call(w, "RevealHotseatAsync", s, actor, Field<TableInputState>(w, "_input").Revision, Field<long>(w, "_sessionGeneration"))!;
            leaving = (Task)Call(w, "LeaveTableAsync", new Button())!;
        }
        finally { gate.Release(); }
        Await(Task.WhenAll(reveal, leaving));
        Require(Field<GameSession?>(w, "_session") is null && Field<int?>(w, "_viewer") == oldViewer && Button(w, "HotseatReveal") is null,
            "Disposing a table must cancel its queued Reveal without switching or exposing a hand.");
    }
    private static void Exclusions()
    {
        using (var t = new Table(false))
        {
            Require(Button(t.Window, "HotseatReveal") is null && Faces(t.Window) == 6, "Manual hotseat remains the default.");
            var selector = Field<ComboBox>(t.Window, "_viewerSeat"); selector.SelectedIndex = 2; Refresh(t.Window);
            Require(Field<int?>(t.Window, "_viewer") == 2 && Faces(t.Window) == 6, "Manual mode retains normal viewer switching.");
            Call(t.Window, "ShowSettings"); Dispatcher.UIThread.RunJobs();
            var privacy = t.Window.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(c => c.Name == "SettingsHotseatPrivacy");
            Require(privacy is not null && privacy.SelectedIndex == 1, "Automatic privacy must be an explicit opt-in setting."); privacy!.SelectedIndex = 0;
            var prefs = GamePreferences.Load(Path.Combine(Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY")!, "BidEuchre", "preferences.json"));
            Require(JsonSerializer.Serialize(prefs).Contains("\"AutomaticHotseatPrivacy\":true"), "Privacy opt-in must be remembered.");
        }
        foreach (var humans in new[] { 0, 1 })
        {
            using var t = new Table(true, humans: humans);
            Require(Button(t.Window, "HotseatReveal") is null && Faces(t.Window) == (humans == 0 ? 0 : 6), "Spectator and single-human tables must keep their normal presentation.");
        }
    }
}
