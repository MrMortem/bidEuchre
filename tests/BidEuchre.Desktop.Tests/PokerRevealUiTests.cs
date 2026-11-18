using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class PokerRevealUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        using var directory = new DesktopTestDataDirectory();
        RevealsAllCardsAndAutomaticallyRedeals();
        RestoresRevealAndFinishesMatchAfterSettings();
    }

    private static void RevealsAllCardsAndAutomaticallyRedeals()
    {
        var window = new MainWindow();
        try
        {
            var (game, run, winningCards) = Fixture();
            Save(window, game, run);
            var session = Open(window, motion: true);
            Await(session.DeveloperAsync(WinPokerAuction));
            var originalDeal = game.DealSerial;
            VerifyReveal(window, session, winningCards);
            Capture(window);
            Until(() => Rendered(window)?.DealSerial == originalDeal + 1, "The Poker reveal must automatically redeal.");
            var next = Rendered(window)!;
            Require(next.Phase == GamePhase.Bidding && next.HandNumber == 2 && next.HandHistory.Count == 1,
                "One Poker showdown must finish one hand and open the next auction.");
            Require(next.Scores.SequenceEqual(new[] { 0, 4 }) && next.CompletedTricks.Count == 0,
                "Redealing must preserve the Poker award without playing a trick.");
            Require(next.Campaign!.Players.Where(player => player.Seat != 0).All(player => player.VisibleCards.Count == 0),
                "Cards from the next deal must be private again.");
            Require(session.WaitingForDeal, "The new auction must wait for the animated deal to finish.");
            Until(() => !session.WaitingForDeal, "The animated deal must release the next auction.");
            Console.WriteLine("PASS  Desktop Poker reveals all 24 cards, highlights the winner's physical five and automatically redeals");
        }
        finally { window.Close(); }
    }

    private static void RestoresRevealAndFinishesMatchAfterSettings()
    {
        var window = new MainWindow();
        try
        {
            var (game, run, winningCards) = Fixture(tied: true);
            game.DeveloperSetScores(0, run.Threshold - 4);
            WinPokerAuction(game);
            Save(window, game, run);
            var session = Open(window);
            VerifyReveal(window, session, winningCards, tied: true);
            Click(window, "TableSettings");
            Until(() => Visible<Button>(window, "SettingsBack") is not null, "Settings must open during a Poker reveal.");
            Pump(TimeSpan.FromMilliseconds(3200), () => false);
            var paused = session.GetViewAsync(0);
            Await(paused);
            Require(paused.Result?.Phase == GamePhase.PokerReveal && paused.Result.Scores.SequenceEqual(new[] { 6, run.Threshold }),
                "Pausing during a restored reveal must hold the result and preserve the award.");
            Click(window, "SettingsBack");
            VerifyReveal(window, session, winningCards, tied: true);
            Until(() => Visible<ScrollViewer>(window, "HandScoreSummary") is { } summary
                && Labels(summary).Contains("Hand 1 · Poker"),
                "A threshold-reaching Poker hand must reach final score history after the reveal.");
            var history = Labels(Visible<ScrollViewer>(window, "HandScoreSummary")!);
            Require(history.Contains("West") && history.Contains("Partner") && history.Contains("Five of a kind")
                && history.Contains("Poker +4") && history.Contains("Poker copies +2")
                && !history.Contains("MADE") && !history.Contains("SET") && !history.Contains("TRICKS"),
                "Final history must explain the Poker award without contract or trick results.");
            var saved = CampaignStore.Load(Field<string>(window, "_campaignPath"))!;
            Require(saved.Phase == CampaignPhase.Shop && saved.Match == 2 && saved.Lives == run.Lives - 1
                && saved.LiveMatch is null, "The terminal Poker result must settle the match exactly once.");
            Console.WriteLine("PASS  Desktop restores a Poker result, holds through Settings, and saves the terminal match outcome");
        }
        finally { window.Close(); }
    }

    private static (GameEngine Game, CampaignRun Run, string[] WinningCards) Fixture(bool tied = false)
    {
        var run = new CampaignRun(Phase: CampaignPhase.InMatch);
        var resources = CampaignSnapshot.Create(31) with
        {
            Inventory = new([], [], [GumballId.Poker])
            {
                GumballExtraCopies = tied ? new() { [GumballId.Poker] = 2 } : []
            }
        };
        var game = new GameEngine(["You", "West", "Partner", "East"], 2, run.NextMatch(), new(resources));
        game.StartGame(3);
        int[] winningSeats = tied ? [1, 2] : [1];
        var hands = winningSeats.Select(seat => game.CreateCampaignView(seat).Players[seat].VisibleCards).ToArray();
        var winners = hands.SelectMany(hand => hand.Take(5)).Select(card => card.Id).ToHashSet();
        var unusedCards = hands.Select(hand => hand[5].Id).ToHashSet();
        // A losing seat shares the winners' face, so face-based highlighting would mark an extra card.
        var otherAce = game.CreateCampaignView(3).Players[3].VisibleCards[0].Id;
        game.DeveloperReplaceResources(state => state with
        {
            Deck = state.Deck.Select(card => winners.Contains(card.Id) || card.Id == otherAce
                ? card with { Face = new(Suit.Spades, Rank.Ace) }
                : unusedCards.Contains(card.Id) ? card with { Face = new(Suit.Hearts, Rank.Nine) } : card).ToArray()
        });
        return (game, run, winners.Select(id => $"Card #{id.Value + 1} of the shared deck").Order().ToArray());
    }

    private static void WinPokerAuction(GameEngine game)
    {
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        while (game.Phase == GamePhase.Bidding)
            game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
    }

    private static void VerifyReveal(MainWindow window, GameSession session, string[] winningCards, bool tied = false)
    {
        Until(() => Rendered(window)?.Phase is not (null or GamePhase.Bidding), "The Poker auction must finish.");
        var banner = Visible<Border>(window, "TableChoices");
        var labels = banner is null ? "" : Labels(banner);
        Require(labels.Contains("Poker showdown") && labels.Contains("West") && labels.Contains("Five of a kind")
            && labels.Contains("Team 2 +4"),
            "The reveal banner must show the winning player, poker hand, team and award.");
        if (tied)
            Require(labels.Contains("Partner") && labels.Contains("Team 1 +4") && labels.Contains("Team 1 +2 Poker copies"),
                "The banner must show both winners and explain the human team's duplicate reward.");
        var view = Rendered(window)!;
        Require(view.Phase == GamePhase.PokerReveal && view.Contract is null && !session.WaitingForContractPresentation
            && view.CurrentTrick.Count == 0 && view.CompletedTricks.Count == 0, "Poker must go directly to a reveal without a contract or tricks.");
        var cards = window.GetVisualDescendants().OfType<Button>().Where(card => CardLabel(card) is not null).ToArray();
        Require(cards.Length == 24 && cards.All(card => !card.Focusable), "All four hands must be face up and non-playable.");
        var highlighted = cards.Where(card => card.Classes.Contains("poker-winning-card")).ToArray();
        Require(winningCards.SequenceEqual(highlighted.Select(CardLabel).Order()) &&
            highlighted.All(card => card.BorderThickness.Left > 1 && ReferenceEquals(card.BorderBrush, MainWindow.Gold)),
            "Only the five physical cards making each winning hand may have a gold highlight.");
    }

    private static void Save(MainWindow window, GameEngine game, CampaignRun run) =>
        Require(CampaignStore.Save(run.WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, true, null))),
            Field<string>(window, "_campaignPath")), "The Poker fixture must save.");

    private static GameSession Open(MainWindow window, bool motion = false)
    {
        Field<PixelMotion>(window, "_motion").SetEnabled(motion);
        window.Width = 980;
        window.Height = 680;
        window.Show();
        window.Activate();
        Field<CampaignFlow>(window, "_campaign").Open();
        Dispatcher.UIThread.RunJobs();
        Click(window, "CampaignContinue");
        Until(() => Field<GameSession?>(window, "_session")?.Started == true && Rendered(window) is not null,
            "Continue must open the campaign table.");
        return Field<GameSession>(window, "_session");
    }

    private static GameView? Rendered(MainWindow window) => Field<GameView?>(window, "_renderedView");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static T? Visible<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>()
        .FirstOrDefault(control => control.Name == name && control.IsEffectivelyVisible);
    private static void Click(Control root, string name) => Visible<Button>(root, name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Labels(Control root) => string.Join("\n", root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
    private static string? CardLabel(Button card) => (ToolTip.GetTip(card) as StackPanel)?.Children.OfType<TextBlock>()
        .Select(label => label.Text).FirstOrDefault(text => text?.StartsWith("Card #", StringComparison.Ordinal) == true);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void Await(Task task)
    {
        Until(() => task.IsCompleted, "The session operation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void Until(Func<bool> done, string message)
    {
        Pump(TimeSpan.FromSeconds(7), done);
        Require(done(), message);
    }

    private static void Pump(TimeSpan duration, Func<bool> done)
    {
        if (!done())
        {
            using var timeout = new CancellationTokenSource(duration);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                if (done()) timeout.Cancel();
            };
            timer.Start();
            try { Dispatcher.UIThread.MainLoop(timeout.Token); }
            finally { timer.Stop(); }
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(MainWindow window)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } root) return;
        Directory.CreateDirectory(root);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("The Poker reveal must render.");
        using var file = File.Create(Path.Combine(root, "poker-reveal-compact.png"));
        bitmap.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
