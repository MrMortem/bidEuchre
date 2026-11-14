using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class TableRenderingTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static object? Call(MainWindow window, string name, params object[] args) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static Rect Bounds(Control control, Window window)
    {
        var top = control.TranslatePoint(default, window)!.Value;
        var bottom = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        return new Rect(top, bottom);
    }
    // Hand cards are rotated, so take the box around all four corners.
    private static Rect CornerBounds(Control control, Window window)
    {
        var w = control.Bounds.Width; var h = control.Bounds.Height;
        var points = new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) }.Select(p => control.TranslatePoint(p, window)!.Value).ToArray();
        return new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)), new Point(points.Max(p => p.X), points.Max(p => p.Y)));
    }
    private static void Await(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Flush(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new Exception("UI operation timed out.");
        task.GetAwaiter().GetResult();
    }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    public static void Run()
    {
        var failures = new List<string>();
        void Test(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS  " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.WriteLine("FAIL  " + failures[^1]); }
        }
        var id = new CardId(20);
        var campaign = new CampaignGameView(0, 1, 0,
            [new(0, 1, [new(id, new Card(Suit.Spades, Rank.Ten), EnhancementId.Wild, null)], false)],
            [new CampaignCommand.Bid(BidLevel.Alone), new CampaignCommand.Bid(BidLevel.PartnersBest), new CampaignCommand.Bid((BidLevel)2)],
            [], [], new([], [], []), 0, 1, 24, null);
        Test("Campaign labels name bids, seats and card faces", () =>
        {
            Require(CampaignTableControls.Describe(new CampaignCommand.Bid(BidLevel.PartnersBest), campaign) == "Bid Partners Best", "Partners Best must have a readable name.");
            Require(CampaignTableControls.Describe(new CampaignCommand.Play(id, Suit.Clubs), campaign) == "Play 10♠ as Clubs", "Card labels must use a face, without internal IDs.");
            Require(CampaignTableControls.Describe(new CampaignCommand.SelectOpponent(1), campaign) == "Duel West", "Use table seat names.");
            Require(CampaignTableControls.Describe(new CampaignCommand.ChooseRevealSeat(1), campaign,
                [new(1, "Custom West", 1, 0, null, false)]) == "Reveal a card from Custom West", "Use actual table names when supplied.");
        });
        Test("Lowballer precedes higher bid levels", () => Require(
            CampaignTableControls.CenterCommands(campaign).OfType<CampaignCommand.Bid>().First().Level == (BidLevel)2, "Bid 2 must precede higher bids."));
        Test("Wild choices stay on the card", () =>
        {
            var wild = campaign with { LegalActions = Enum.GetValues<Suit>().Select(s => (CampaignCommand)new CampaignCommand.Play(id, s)).ToArray() };
            Require(!CampaignTableControls.CenterCommands(wild).Any(), "Wild plays must not fill the table action panel.");
        });
        var bids = new List<CampaignCommand> { new CampaignCommand.Pass(), new CampaignCommand.Bid((BidLevel)2), new CampaignCommand.PanicButton() };
        foreach (var level in new[] { BidLevel.Three, BidLevel.Four, BidLevel.Five, BidLevel.Six, BidLevel.PartnersBest, BidLevel.Alone })
        {
            bids.Add(new CampaignCommand.Bid(level));
            if (level >= BidLevel.Four && level <= BidLevel.Six) bids.Add(new CampaignCommand.Bid(level, GumballId.Chaos));
            if (level == BidLevel.Four) bids.Add(new CampaignCommand.Bid(level, GumballId.Poker));
            if (level <= BidLevel.Five) bids.Add(new CampaignCommand.Bid(level, GumballId.Duelist));
            if (level >= BidLevel.Five && level <= BidLevel.Six) bids.Add(new CampaignCommand.Bid(level, GumballId.Telepath));
        }
        foreach (var size in new[] { new Size(1320, 860), new Size(1280, 720), new Size(980, 680) })
        foreach (var scale in new[] { 1.0, 1.5 })
        foreach (var inCampaign in new[] { false, true })
        Test($"{(inCampaign ? "Campaign" : "Free-play")} table fits {size} with requested scale {scale}", () =>
        {
            var window = new MainWindow();
            try
            {
                Field<DispatcherTimer>(window, "_refreshTimer").Stop();
                if (inCampaign)
                {
                    var session = new GameSession("layout", "Layout", [], new EngineCatalog(), 1);
                    Set(window, "_session", session); Set(window, "_campaignSession", session);
                }
                Set(window, "_preferences", new GamePreferences(MotionEnabled: false, WindowWidth: (int)size.Width, WindowHeight: (int)size.Height, UiScale: scale));
                Call(window, "ApplyGraphics");
                window.Width = size.Width; window.Height = size.Height;
                var host = Field<ContentControl>(window, "_pageHost");
                host.Content = Call(window, "BuildGamePage");
                Field<ContentControl>(window, "_screenHost").Content = Field<Grid>(window, "_tableScreen");
                Field<ContentControl>(window, "_tableHost").Content = Call(window, "BuildWaitingTable", (object)Enumerable.Range(0, 4).Select(i => new PlayerView(i, "Player " + i, i % 2, 6, null, false)).ToArray());
                Field<TextBlock>(window, "_contractText").Text = "Partners Best · Spades";
                var actionView = new GameEngine(["You", "West", "Partner", "East"], 1).CreateView(0) with
                { Phase = GamePhase.Bidding, Campaign = campaign with { LegalActions = bids } };
                Call(window, "RenderActions", actionView);
                if (inCampaign)
                {
                    Field<TextBlock>(window, "_campaignStrip").Text = "♥ 3 · $400 · ANTE 12 · MATCH 3/3 · BOSS";
                    Field<TextBlock>(window, "_scoreEyebrow").Text = "TEAMS · ±68 · 8 HANDS";
                    Field<TextBlock>(window, "_dealerText").Text = "HAND 8 · TRICKS 3–3";
                    Call(window, "RenderInventory", campaign with { Inventory = new(
                        Enum.GetValues<JokerId>().Take(5).Select(j => new JokerState(j)).ToArray(),
                        [TarotId.LovePotion, TarotId.Nightshade], []) { Sigils = [SigilId.Fish, SigilId.Bear] } });
                }
                window.Show(); Flush();
                if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is { } output)
                {
                    Directory.CreateDirectory(output);
                    using var bitmap = window.CaptureRenderedFrame();
                    if (bitmap is not null) { using var stream = File.Create(Path.Combine(output, $"table-{inCampaign}-{size.Width}-{size.Height}-{scale}.png")); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                }
                var panel = Bounds(Field<Border>(window, "_centerPanel"), window);
                var table = Bounds(Field<ContentControl>(window, "_tableHost"), window);
                Require(table.Contains(panel.Center), "Choices sit in the middle of the table.");
                var hands = Field<ContentControl>(window, "_tableHost").GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Width == 124 && b.Height == 176).Select(b => CornerBounds(b, window)).ToArray();
                Require(hands.Length == 24, "Every hand must be on the table.");
                Require(hands.All(card => !panel.Intersects(card)), $"Choices must not cover any hand ({panel} vs {hands.FirstOrDefault(c => panel.Intersects(c))}).");
                Require(panel.Bottom <= window.Bounds.Height + 1 && panel.Right <= window.Bounds.Width + 1, "Choices must fit in the viewport.");
                var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToArray();
                Require(visible.Any(t => t.Text is "JOKERS" or "CARDS") == inCampaign, "Only campaigns must show inventory shelves.");
                if (inCampaign)
                {
                    var status = Field<TextBlock>(window, "_campaignStrip");
                    Require(status.TextLayout.Width <= status.Bounds.Width + 1 && status.TextLayout.Height <= status.Bounds.Height + 1,
                        "The full boss status must fit without clipping.");
                    var inventory = window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "TableInventory");
                    Require(inventory.Bounds.Width > 600, "Inventory must have a readable viewport.");
                }
                var contract = Field<TextBlock>(window, "_contractText");
                Require(contract.TextLayout.Width <= contract.Bounds.Width + 1, "The contract must fit without clipping.");
                Require(Bounds(contract, window).Height >= 14, "Contract text must stay readable.");
                var scroll = Field<Border>(window, "_centerPanel").GetVisualDescendants().OfType<ScrollViewer>().Single();
                scroll.Offset = new Vector(0, scroll.Extent.Height); Flush();
                var actions = Field<WrapPanel>(window, "_actionButtons").Children;
                string[] Labels() => actions.OfType<Button>().Select(b => ((TextBlock)b.Content!).Text!).ToArray();
                void CheckLabels()
                {
                    foreach (var label in actions.OfType<Button>().Select(b => (TextBlock)b.Content!))
                        Require(label.TextLayout.Width <= label.Bounds.Width + 1, "Choice labels must wrap without truncation.");
                    var last = actions.Last();
                    Require(Bounds(last, window).Bottom <= Bounds(Field<Border>(window, "_centerPanel"), window).Bottom + 1, "The last choice must be reachable by scrolling.");
                }
                void Press(string text)
                {
                    actions.OfType<Button>().Single(b => ((TextBlock)b.Content!).Text == text).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Flush();
                }
                // Step 1: a number. Gumballs wait for step 2.
                Require(Labels().SequenceEqual(["Pass", "2", "3", "4", "5", "6", "Partners Best", "Alone", "Panic Button · −8 points · consumes this hand"]),
                    "Step 1 offers Pass and each bid level once: " + string.Join(", ", Labels()));
                CheckLabels();
                Press("4");
                if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is { } stepOutput)
                {
                    using var bitmap = window.CaptureRenderedFrame();
                    if (bitmap is not null) { using var stream = File.Create(Path.Combine(stepOutput, $"table-gumball-{inCampaign}-{size.Width}-{size.Height}-{scale}.png")); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                }
                var gumballPanel = Bounds(Field<Border>(window, "_centerPanel"), window);
                Require(hands.All(card => !gumballPanel.Intersects(card)), "Gumball choices must not cover any hand.");
                Require(Labels().SequenceEqual(["No gumball", "Chaos", "Poker", "Duelist", "← Back"]), "Step 2 offers this level's gumballs: " + string.Join(", ", Labels()));
                Require(Field<TextBlock>(window, "_centerTitle").Text == "Bid 4 · add a gumball?", "Step 2 names the chosen number.");
                Require(Equals(ToolTip.GetTip(actions.OfType<Button>().Single(b => ((TextBlock)b.Content!).Text == "Chaos")),
                    FeatureCatalog.Get(FeatureCategory.Gumball, "Chaos").Description), "Gumballs must explain their effect on hover.");
                CheckLabels();
                Press("← Back");
                Require(Labels().Length == 9, "Back returns to the numbers.");
                // Choices coming and going must not slide the table sideways.
                Field<WrapPanel>(window, "_actionButtons").Children.Clear();
                Call(window, "ShowCenter", false); Flush();
                Require(Bounds(Field<ContentControl>(window, "_tableHost"), window) == table, "Hiding choices must not move or resize the table.");
                Require(!Field<Border>(window, "_centerPanel").IsVisible, "Hidden choices must not take clicks.");
            }
            finally { window.Close(); }
        });
        Test("Wild suit picker waits for a suit and submits that legal play", () =>
        {
            var snapshot = CampaignSnapshot.Create(42);
            snapshot = snapshot with { Deck = snapshot.Deck.Select(card => card with { Enhancement = EnhancementId.Wild }).ToArray() };
            var seed = Enumerable.Range(1, 100).First(candidate =>
            {
                var game = new GameEngine(["You", "West", "Partner", "East"], candidate);
                game.StartGame(); return game.Dealer == 3;
            });
            var session = new GameSession("wild-ui", "Wild test", Enumerable.Range(0, 4).Select(seat =>
                new SeatConfiguration(seat, new[] { "You", "West", "Partner", "East" }[seat], PlayerKind.Human, null)).ToArray(),
                new EngineCatalog(), seed, campaign: new CampaignContext(snapshot));
            var window = new MainWindow();
            try
            {
                Await(session.StartAsync());
                GameView view = Await(session.GetViewAsync(0))!;
                for (var step = 0; step < 60 && !(view.Phase == GamePhase.Playing && view.CurrentSeat == 0); step++)
                {
                    var seat = view.CurrentSeat!.Value;
                    var actor = Await(session.GetViewAsync(seat))!;
                    var legal = actor.Campaign!.LegalActions;
                    var command = actor.Phase == GamePhase.Bidding
                        ? legal.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand ?? legal.First()
                        : legal.First();
                    Await(session.ExecuteCampaignAsync(new(seat, actor.Campaign.Revision, actor.Campaign.DealSerial, command)));
                    view = Await(session.GetViewAsync(0))!;
                }
                Require(view.Phase == GamePhase.Playing && view.CurrentSeat == 0, "Fixture must reach a human Wild play.");
                Set(window, "_session", session); Set(window, "_campaignSession", session); Set(window, "_viewer", 0);
                Field<DispatcherTimer>(window, "_refreshTimer").Stop();
                Field<PixelMotion>(window, "_motion").SetEnabled(false);
                var page = Call(window, "BuildGamePage");
                Field<ContentControl>(window, "_pageHost").Content = page;
                Field<ContentControl>(window, "_screenHost").Content = Field<Grid>(window, "_tableScreen");
                Field<ContentControl>(window, "_tableHost").Content = Call(window, "BuildTable", view);
                window.Show(); Flush();
                var button = window.GetVisualDescendants().OfType<Button>().Last(b => b.Flyout is Flyout);
                var revision = view.Campaign!.Revision;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Flush();
                Require(((Flyout)button.Flyout!).IsOpen, "Pressing a Wild card must open its suit picker.");
                Require(!Field<TextBlock>(window, "_status").IsVisible, "Opening a Wild card must not send a plain-card action or show an error.");
                Require(Await(session.GetViewAsync(0))!.Campaign!.Revision == revision, "Opening the suit picker must not play the card.");
                var flyout = (Flyout)button.Flyout!;
                var options = ((StackPanel)flyout.Content!).Children.OfType<Button>().ToArray();
                Require(options.Length > 1 && options.Length <= 4, "Only the selected card's legal suits must be offered.");
                options[^1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Flush();
                var after = Await(session.GetViewAsync(0))!;
                Require(after.Campaign!.Revision == revision + 1, "Choosing the suit must submit exactly one campaign play.");
                Require(after.Campaign.Trick.Any(play => play.Seat == 0 && play.EffectiveSuit == Suit.Spades), "The chosen suit must reach the engine.");
            }
            finally { window.Close(); Await(session.DisposeAsync().AsTask()); }
        });
        Test("UI scale adapts on resize and restores the requested scale", () =>
        {
            var window = new MainWindow();
            try
            {
                Set(window, "_preferences", new GamePreferences(MotionEnabled: false, UiScale: 1.5));
                window.Width = 1920; window.Height = 1080; window.Show(); Flush();
                double Scale() => ((Avalonia.Media.ScaleTransform)Field<LayoutTransformControl>(window, "_scaleHost").LayoutTransform!).ScaleX;
                Require(Scale() == 1.5, "150% must remain available when it fits.");
                window.Width = 1280; window.Height = 720; Flush();
                Require(Scale() == 1, "150% must be capped at the reported 1280×720 size.");
                window.Width = 1920; window.Height = 1080; Flush();
                Require(Scale() == 1.5, "Enlarging must restore the user's requested scale.");
            }
            finally { window.Close(); }
        });
        Test("Compact Ante keeps Main Menu visible", () =>
        {
            var motion = new PixelMotion(); motion.SetEnabled(false);
            var window = new Window { Width = 653, Height = 453, Content = CampaignAnteScreen.Build(new(), () => { }, () => { }, null, motion) };
            try
            {
                window.Show(); Flush();
                var menu = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CampaignMainMenu");
                Require(Bounds(menu, window).Bottom <= window.Bounds.Height, "Ante Main Menu must stay visible without scrolling.");
            }
            finally { window.Close(); }
        });
        if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
    }
}
