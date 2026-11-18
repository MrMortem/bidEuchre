using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignGamblerUiTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Until(Func<bool> done)
    {
        if (!done())
        {
            using var limit = new CancellationTokenSource(7000);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
            timer.Start();
            try { Dispatcher.UIThread.MainLoop(limit.Token); }
            finally { timer.Stop(); }
        }
        Require(done(), "The Gambler table interaction must finish.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static Button Find(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    private static Button HandCard(MainWindow window, CardId id) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        ToolTip.GetTip(button) is Control tip && tip.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == $"Card #{id.Value + 1} of the shared deck"));
    private static void Click(MainWindow window, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Until(() => !Field<bool>(window, "_refreshing"));
    }

    public static void Run()
    {
        var window = new MainWindow();
        var flow = Field<CampaignFlow>(window, "_campaign");
        var path = Field<string>(window, "_campaignPath");
        GameSession? session = null;
        try
        {
            Field<PixelMotion>(window, "_motion").SetEnabled(false);
            Field<DispatcherTimer>(window, "_refreshTimer").Stop();
            var resources = CampaignSnapshot.Create(123) with { Inventory = new([], [TarotId.Gambler], []) };
            var game = new GameEngine(["You", "West", "Partner", "East"], 3, new CampaignRun().NextMatch(), new(resources));
            game.StartGame(3);
            var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), new(game.DealSerial, true, null)));
            Require(CampaignStore.Save(run, path), "The held Gambler fixture must save.");
            window.Show(); flow.Open(); Dispatcher.UIThread.RunJobs(); Click(window, Find(window, "CampaignContinue"));
            Until(() => Field<GameSession?>(window, "_session")?.Started == true && window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ItemTarot0"));
            session = Field<GameSession>(window, "_session");
            var initial = Await(session.GetViewAsync(0))!.Campaign!;
            var hand = initial.Players[0].VisibleCards.Select(card => card.Id).ToArray();
            Require(hand.Length == 6, "A held Gambler starts with six cards in the live hand.");
            var selected = new[] { hand[1], hand[4] };
            var before = File.ReadAllText(path);

            Click(window, Find(window, "ItemTarot0"));
            Require(!Find(window, "UseItem").IsEnabled, "Held Gambler must not confirm without targets.");
            Click(window, HandCard(window, selected[0]));
            Require(!Find(window, "UseItem").IsEnabled, "Held Gambler must not confirm with just one target.");
            Click(window, HandCard(window, selected[0]));
            Require(!Find(window, "UseItem").IsEnabled, "The same physical hand card cannot count as two targets.");
            Click(window, HandCard(window, selected[0]));
            Click(window, HandCard(window, selected[1]));
            Require(Find(window, "UseItem").IsEnabled, "Held Gambler must confirm after selecting two distinct cards.");
            Require(hand.All(id => HandCard(window, id).IsEnabled == selected.Contains(id)), "At two selected hand cards, only the selected cards must remain enabled for deselection.");
            Click(window, HandCard(window, selected[0]));
            Require(!Find(window, "UseItem").IsEnabled && hand.All(id => HandCard(window, id).IsEnabled), "Deselecting a target must reopen the other card choices.");
            Click(window, Find(window, "CancelItem"));
            Require(File.ReadAllText(path) == before, "Canceling held Gambler must preserve its inventory, cards, and RNG.");

            Click(window, Find(window, "ItemTarot0"));
            Require(!Find(window, "UseItem").IsEnabled, "Reopening held Gambler must clear the canceled choices.");
            foreach (var id in selected) Click(window, HandCard(window, id));
            Click(window, Find(window, "UseItem"));
            Until(() => flow.Run!.Resources.Inventory.Tarot.Length == 0 && !Field<bool>(window, "_refreshing"));
            var saved = CampaignStore.Load(path)!;
            var lucky = saved.Resources.Deck.Where(card => card.Enhancement == EnhancementId.Lucky).Select(card => card.Id).ToArray();
            Require(lucky.Length == 2 && lucky.All(selected.Contains), "Held Gambler must make exactly the two selected physical hand cards Lucky.");
            var after = Await(session.GetViewAsync(0))!.Campaign!;
            Require(after.Players[0].VisibleCards.Where(card => selected.Contains(card.Id)).All(card => card.Enhancement == EnhancementId.Lucky), "Both selected cards must remain Lucky in the live hand.");
            Console.WriteLine("PASS  Held Gambler requires two distinct hand cards, restores choices after deselection, cancels unchanged, and enhances exactly the chosen IDs");
        }
        finally
        {
            window.Close();
            if (session is not null) Await(session.DisposeAsync().AsTask());
        }
    }
}
