using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignResumeUiTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Private)!.GetValue(o)!;
    static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, Private)!.Invoke(o, args);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Until(Func<bool> done)
    {
        if (!done())
        {
            using var limit = new CancellationTokenSource(7000); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
            timer.Start(); try { Dispatcher.UIThread.MainLoop(limit.Token); } finally { timer.Stop(); }
        }
        Require(done(), "Continue must open the saved match without an abandoned animation gate."); Dispatcher.UIThread.RunJobs();
    }
    static void Await(Task t) { Until(() => t.IsCompleted); t.GetAwaiter().GetResult(); }
    static void Click(Control root, string text) => root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Campaign" + text.Replace(" ", "")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Capture(MainWindow w, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } root) return;
        Directory.CreateDirectory(root); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = w.CaptureRenderedFrame(); Require(bitmap is not null, "The restored table must render.");
        using var stream = File.Create(Path.Combine(root, name + ".png")); bitmap!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    public static void Run()
    {
        ManualContinueAndForfeit(); AutomaticStages(); ClosePreservesAndTerminalOnce(); LegacyNoticeAndFailedRestore();
    }
    internal static void FinalScoringChainFinishesBeforeResults()
    {
        var window = new MainWindow();
        var flow = Field<CampaignFlow>(window, "_campaign");
        var path = Field<string>(window, "_campaignPath");
        var run = new CampaignRun(Phase: CampaignPhase.InMatch);
        var state = CampaignSnapshot.Create(73) with { Inventory = new([new(JokerId.LastLaugh)], [], []) };
        for (var i = 0; i < state.Deck.Length; i++)
            state.Deck[i] = state.Deck[i] with { Enhancement = EnhancementId.Stone, Sigil = SigilId.Fish };
        var game = new GameEngine(["You", "West", "Partner", "East"], 73, run.NextMatch(), new(state));
        game.StartGame(3);
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three));
        while (game.Phase == GamePhase.Bidding)
            game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));

        static void Play(GameEngine engine)
        {
            var actor = engine.CampaignActor!.Value;
            engine.ExecuteCampaignAction(actor, engine.CreateCampaignView(actor).LegalActions.OfType<CampaignCommand.Play>().First());
        }
        while (game.CompletedTricks.Count < 5) Play(game);
        run = run.WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, true, null)));
        Require(CampaignStore.Save(run, path), "The long final-trick fixture must save.");

        try
        {
            Field<PixelMotion>(window, "_motion").SetEnabled(true);
            Field<DispatcherTimer>(window, "_refreshTimer").Stop();
            window.Show();
            flow.Open();
            Dispatcher.UIThread.RunJobs();
            Click(window, "Continue");
            Until(() => Field<GameSession?>(window, "_session")?.Started == true);
            var session = Field<GameSession>(window, "_session");
            Await((Task)Call(window, "RefreshGameAsync")!);
            Await(session.DeveloperAsync(engine =>
            {
                while (engine.Phase == GamePhase.Playing) Play(engine);
            }));
            Require(session.Game!.Phase == GamePhase.GameComplete, "The enhanced last trick must end this match.");
            Await((Task)Call(window, "RefreshGameAsync")!);
            var flights = Field<CardFlights>(window, "_cardFlights");
            Require(flights.Collecting && !flow.InMatch, "Persist the result while the table is still presenting the final trick.");

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            Until(() => elapsed.ElapsedMilliseconds >= 2400 || Field<GameSession?>(window, "_session") is null);
            Require(ReferenceEquals(session, Field<GameSession?>(window, "_session")) && flights.Collecting,
                "The result must wait even when the scoring chain exceeds the old fixed cutoff.");
            Until(() => Field<GameSession?>(window, "_session") is null);
            Require(!flights.Collecting, "Collection must settle before the result replaces the table.");
            Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "CampaignContinue"));
            Console.WriteLine("PASS  A long final scoring chain and trick collection finish before the campaign result");
        }
        finally { window.Close(); }
    }

    private static void ManualContinueAndForfeit()
    {
        var w = new MainWindow(); var flow = Field<CampaignFlow>(w, "_campaign"); var path = Field<string>(w, "_campaignPath");
        try
        {
            Field<PixelMotion>(w, "_motion").SetEnabled(false); Field<DispatcherTimer>(w, "_refreshTimer").Stop(); w.Show();
            var resources = CampaignSnapshot.Create(123) with { Inventory = new([new(JokerId.StickyFingers)], [], []) };
            var game = new GameEngine(["You", "West", "Partner", "East"], 3, new CampaignRun().NextMatch(), new(resources)); game.StartGame(3);
            var order = game.CreateCampaignView(0).Players[0].VisibleCards.Select(c => c.Id).Reverse().ToArray(); game.ArrangeCampaignHand(0, order);
            var run = new CampaignRun(Lives: 2, Match: 3, Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, true, null)));
            Require(CampaignStore.Save(run, path), "Fixture checkpoint must save.");
            flow.Open(); Dispatcher.UIThread.RunJobs(); Click(w, "Continue");
            Until(() => Field<GameSession?>(w, "_session")?.Started == true);
            var session = Field<GameSession>(w, "_session"); Await(session.PauseAsync());
            Require(flow.Run!.Lives == 2 && flow.Run.Match == 3, "Continue cannot charge or advance a match.");
            var restored = session.GetViewAsync(0); Await(restored);
            Require(JsonSerializer.Serialize(game.CreateView(0)) == JsonSerializer.Serialize(restored.Result), "Continue must preserve all visible state and manual order.");
            Capture(w, "resume-manual");
            Require(!session.WaitingForDeal && !session.WaitingForContractPresentation, "Restored animation gates start safely acknowledged.");
            // A manually arranged auction remains manual when the contract appears later.
            Await(session.ResumeAsync());
            Await(session.DeveloperAsync(g => { g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three)); while (g.Phase == GamePhase.Bidding) g.ExecuteCampaignAction(g.CampaignActor!.Value, new CampaignCommand.Pass()); g.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts)); }));
            Await(session.PauseAsync()); Await((Task)Call(w, "RefreshGameAsync")!);
            var after = session.GetViewAsync(0); Await(after);
            Require(after.Result!.Campaign!.Players[0].VisibleCards.Select(c => c.Id).SequenceEqual(order), "Restored manual order survives contract-stage auto-sort.");
            Await(session.ResumeAsync());
            w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SortSuit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => flow.Run!.Arrangement?.ManuallyArranged == false); Await(session.PauseAsync());
            Require(flow.Run.Arrangement!.AutoStage is not null, "An explicit sort replaces the persisted manual policy with its automatic stage.");
            var menu = Field<Button>(w, "_menuButton");
            Await((Task)Call(w, "ReturnToMenuAsync", menu)!); Require(flow.Run.Lives == 2, "First Forfeit press only confirms.");
            Await((Task)Call(w, "ReturnToMenuAsync", menu)!);
            Require(flow.Run.Lives == 0 && flow.Run.LiveMatch is null, "Explicit boss forfeit immediately consumes two lives and clears live state.");
            Console.WriteLine("PASS  Continue restores exact cards and manual-order policy; explicit Forfeit charges immediately");
        }
        finally { w.Close(); }
    }

    private static void AutomaticStages()
    {
        var w = new MainWindow(); var path = Field<string>(w, "_campaignPath"); var run = Fixture();
        var game = GameEngine.RestoreCampaignMatch(run.LiveMatch!);
        var sort = Field<GamePreferences>(w, "_preferences").HandSort;
        var biddingOrder = HandOrder.Sort(game.CreateCampaignView(0).Players[0].VisibleCards, c => c.Face, sort, null).Select(c => c.Id).ToArray();
        game.ArrangeCampaignHand(0, biddingOrder);
        run = run.WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, false, $"::{sort}")));
        Require(CampaignStore.Save(run, path), "Automatic-stage fixture saves.");
        try
        {
            Open(w); var session = Field<GameSession>(w, "_session"); Await(session.PauseAsync());
            var view = session.GetViewAsync(0); Await(view);
            Require(view.Result!.Campaign!.Players[0].VisibleCards.Select(c => c.Id).SequenceEqual(biddingOrder), "An already sorted auction resumes that order.");
            Await(session.ResumeAsync());
            Await(session.DeveloperAsync(g => { g.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three)); while (g.Phase == GamePhase.Bidding) g.ExecuteCampaignAction(g.CampaignActor!.Value, new CampaignCommand.Pass()); g.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts)); }));
            Await((Task)Call(w, "RefreshGameAsync")!); Await(session.PauseAsync());
            view = session.GetViewAsync(0); Await(view);
            var expected = HandOrder.Sort(view.Result!.Campaign!.Players[0].VisibleCards, c => c.Face, sort, view.Result.Contract).Select(c => c.Id);
            Require(view.Result.Campaign.Players[0].VisibleCards.Select(c => c.Id).SequenceEqual(expected), "Unmanually arranged restored hands still receive their later contract-stage sort.");
            var arrangement = Field<CampaignFlow>(w, "_campaign").Run!.Arrangement;
            Require(arrangement is { ManuallyArranged: false } && arrangement.AutoStage == $"Trump:Hearts:{sort}", "The new automatic stage is saved atomically with its order.");
        }
        finally { w.Close(); }
        Console.WriteLine("PASS  Restored automatic arrangement resumes its completed stage and applies a later contract-stage sort");
    }

    private static CampaignRun Fixture(bool terminal = false)
    {
        var run = new CampaignRun(Lives: 2, Match: 2, Phase: CampaignPhase.InMatch);
        var game = new GameEngine(["You", "West", "Partner", "East"], 2, run.NextMatch(), new(CampaignSnapshot.Create(31)));
        game.StartGame(3); if (terminal) game.DeveloperEndMatch(0);
        return run.WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, true, null)));
    }
    private static void Open(MainWindow w)
    {
        Field<PixelMotion>(w, "_motion").SetEnabled(false); Field<DispatcherTimer>(w, "_refreshTimer").Stop(); w.Show();
        Field<CampaignFlow>(w, "_campaign").Open(); Dispatcher.UIThread.RunJobs(); Click(w, "Continue");
        Until(() => Field<GameSession?>(w, "_session")?.Started == true);
    }
    private static void ClosePreservesAndTerminalOnce()
    {
        var w = new MainWindow(); var path = Field<string>(w, "_campaignPath"); var run = Fixture();
        Require(CampaignStore.Save(run, path), "Live close fixture saves.");
        Open(w); var session = Field<GameSession>(w, "_session"); Await(session.PauseAsync());
        var saved = File.ReadAllText(path); w.Close(); Await(session.DisposeAsync().AsTask());
        Require(File.ReadAllText(path) == saved && CampaignStore.Load(path)!.Lives == 2, "Closing only preserves progress and cannot forfeit.");
        w = new MainWindow();
        try
        {
            Open(w); session = Field<GameSession>(w, "_session"); Await(session.PauseAsync());
            var snapshot = session.GetCampaignCheckpointAsync(); Await(snapshot);
            Require(JsonSerializer.Serialize(snapshot.Result!.Match) == JsonSerializer.Serialize(run.LiveMatch), "A close/Continue cycle restores the same checkpoint.");
        }
        finally { w.Close(); Await(session.DisposeAsync().AsTask()); }
        w = new MainWindow(); run = Fixture(terminal: true); Require(CampaignStore.Save(run, path), "Terminal fixture saves.");
        try
        {
            Open(w); var flow = Field<CampaignFlow>(w, "_campaign");
            Until(() => flow.Run?.Phase != CampaignPhase.InMatch);
            Require(flow.Run!.Match == 3 && flow.Run.Lives == 2 && flow.Run.LiveMatch is null, "Restored terminal settles immediately and clears the checkpoint.");
            var settledMoney = flow.Run.Money;
            flow.MatchCompleted(true, [1, 0]); flow.ForfeitMatch();
            Require(flow.Run.Money == settledMoney && flow.Run.Lives == 2 && flow.Run.Match == 3, "Repeated terminal and Forfeit reports cannot settle twice.");
            Require(CampaignStore.Load(path)!.Money == settledMoney, "The terminal result is durable before its delayed presentation.");
        }
        finally { w.Close(); }
        Console.WriteLine("PASS  Window close preserves the same live match; restored terminal checkpoints settle once before presentation");
    }
    private static void LegacyNoticeAndFailedRestore()
    {
        var w = new MainWindow(); var path = Field<string>(w, "_campaignPath");
        Require(CampaignStore.Save(new CampaignRun(Lives: 2, Match: 7, Money: 23), path), "Legacy fixture starts from resources.");
        var json = JsonNode.Parse(File.ReadAllText(path))!; json["Version"] = 2; json["Run"]!["Phase"] = 1; File.WriteAllText(path, json.ToJsonString());
        try
        {
            Open(w); var session = Field<GameSession>(w, "_session"); Await(session.PauseAsync()); Await((Task)Call(w, "RefreshGameAsync")!);
            var flow = Field<CampaignFlow>(w, "_campaign");
            Require(flow.Run!.Lives == 2 && flow.Run.Match == 7 && flow.Run.Money == 23 && flow.Run.LiveMatch is not null, "Legacy restart preserves lives, match and money.");
            Capture(w, "resume-legacy-notice");
            Require(Field<TextBlock>(w, "_sessionError").Text!.Contains("older save"), "Legacy limitation remains visible on the table.");
            Require(JsonNode.Parse(File.ReadAllText(path))!["Version"]!.GetValue<int>() == 3, "Only a successful complete checkpoint upgrades the save.");
        }
        finally { w.Close(); }
        var run = Fixture(); Require(CampaignStore.Save(run, path), "Restore failure fixture saves."); var original = File.ReadAllText(path);
        var host = new Window(); var motion = new PixelMotion(); motion.SetEnabled(false);
        var failed = new CampaignFlow(path, c => host.Content = c, _ => throw new InvalidOperationException("test startup failure"), () => { }, motion);
        try
        {
            host.Show(); failed.Open(); Dispatcher.UIThread.RunJobs(); Click(host, "Continue"); Dispatcher.UIThread.RunJobs();
            Require(File.ReadAllText(path) == original && failed.Run!.Lives == run.Lives && failed.Run.LiveMatch is not null, "Failed startup keeps the original checkpoint intact.");
            Require(host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CampaignRetry"), "Failed startup offers a retry.");
        }
        finally { host.Close(); }
        Console.WriteLine("PASS  Legacy restart displays its limitation without life loss; failed restore keeps its checkpoint and offers retry");
    }
}
