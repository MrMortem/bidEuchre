using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class CampaignArcadeUiTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == name)
        ?? throw new Exception($"Missing Arcade control {name}.");
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static bool Claimable(Window window) => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "ArcadeKeep" or "CollectorCard0");
    private static int SigilCard(ArcadeResult result) => Array.FindIndex(result.PackCards!, k => k != CampaignArcade.LegendaryKey);
    private static PixelMotion Still() { var motion = new PixelMotion(); motion.SetEnabled(false); return motion; }

    internal static CampaignRun PlinkoFixture(int score, CampaignInventory? inventory = null)
    {
        for (ulong seed = 1; seed <= 10000; seed++)
        {
            var run = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0], Inventory = inventory ?? CampaignSnapshot.Create(seed).Inventory } });
            if (CampaignArcade.PlayPlinko(run, 4).Shop!.Arcade!.Pending!.Score == score) return run;
        }
        throw new Exception("Could not find deterministic Plinko fixture.");
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Require(frame is not null, "The Arcade must render.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static void Await(Func<bool> done)
    {
        if (done()) return;
        using var limit = new CancellationTokenSource(5000);
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
        poll.Start(); Dispatcher.UIThread.MainLoop(limit.Token); poll.Stop();
        Require(done(), "The Arcade animation must settle promptly.");
    }

    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var initial = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(41) with { Wallets = [30, 0, 0, 0] } });
            Require(CampaignStore.Save(initial, path), "The Arcade fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade"));
            Require(Find(window, "ArcadePlinko").IsEnabled && Find(window, "ArcadeRoulette").IsEnabled, "The Arcade must expose both games.");
            Require(Find(window, "ArcadePlay").IsEnabled, "The first play must be available with enough money.");
            Click(Find(window, "PlinkoLane6"));
            var dropper = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlinkoDropper");
            Require(Math.Abs(Canvas.GetLeft(dropper) + dropper.Width / 2 - (78 + 40 * CampaignArcade.PlinkoStartPosition(6))) < .01,
                "Selecting a lane must move the dropper to that exact release position.");
            Require(Find(window, "PlinkoLane6").IsFocused, "Selecting a drop position must keep keyboard focus on the replacement choice.");
            Capture(window, "arcade-plinko-ready");
            Click(Find(window, "ArcadeBack"));
            Require(Find(window, "CampaignNextMatch").IsEnabled, "Back from an unplayed Arcade must return to the shop.");
            Console.WriteLine("PASS  Shop opens the Arcade submenu and returns before playing");
        }
        finally { window.Close(); File.Delete(path); }
        PackRevealAndForcedUse();
        PackRevealSurvivesLeaving();
        LegendaryPickShowsJokers();
        RewardAndResume(3);
        LossAndRoulette();
        MotionAndDetach();
        AnimationsReachSavedResults();
        FullInventoryDiscard();
        SaveFailureDoesNotPublishArcadeActions();
    }

    private static void RewardAndResume(int score)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-prize-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var initial = PlinkoFixture(score);
            Require(CampaignStore.Save(initial, path), "The reward fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade"));
            var play = Find(window, "ArcadePlay"); Click(play);
            var saved = CampaignStore.Load(path)!;
            var result = saved.Shop!.Arcade!.Pending!;
            Require(saved.Money == 27 && saved.Resources.Wallets[0] == 27 && result.Score == score, "The reduced-motion play must save one charge and its reward before revealing it.");
            Require(!Find(window, "ArcadeBack").IsEnabled && !Find(window, "ArcadeRoulette").IsEnabled, "A pending prize must require a claim before leaving or playing again.");
            var exactSave = File.ReadAllText(path); Click(play);
            Require(File.ReadAllText(path) == exactSave, "Replaying an old paid control must not charge or draw again.");
            var ball = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlinkoBall");
            Require(Math.Abs(Canvas.GetLeft(ball) + ball.Width / 2 - (112 + result.Landing * 98)) < .01, "The visible ball must finish in the authoritative reward slot.");
            Capture(window, score >= 5 ? "arcade-plinko-sigil" : "arcade-plinko-ticket");

            flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Require(Find(window, "ArcadeKeep").IsEnabled && flow.Run!.Shop!.Arcade!.Pending!.PlayId == result.PlayId,
                "Continue must restore the saved prize directly, without another play.");
            Require(File.ReadAllText(path) == exactSave, "Restoring a pending result must preserve its exact state.");
            var keep = Find(window, "ArcadeKeep"); Click(keep);
            Require(flow.Run!.Money == 27 && flow.Run.Resources.Inventory.ItemCount == initial.Resources.Inventory.ItemCount + 1,
                "Keep must grant exactly one prize without charging again.");
            Require(flow.Run.Shop!.Arcade!.Pending is null && !Find(window, "ArcadePlay").IsEnabled, "Plinko must remain spent for the visit after claiming its prize.");
            var keptSave = File.ReadAllText(path); Click(keep);
            Require(File.ReadAllText(path) == keptSave, "A stale Keep control must not duplicate a prize.");
            Click(Find(window, "ArcadeRoulette")); Require(Find(window, "ArcadePlay").IsEnabled, "The other game remains available after a prize.");
            Click(Find(window, "ArcadeBack")); Click(Find(window, "ShopReroll")); Click(Find(window, "ShopArcade"));
            Require(!Find(window, "ArcadePlay").IsEnabled, "Stock rerolls must not restore a used Arcade game.");
            Console.WriteLine($"PASS  Plinko {score}-point reward pays once, restores, claims once, and stays spent after a reroll");
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static void LossAndRoulette()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-roulette-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var initial = PlinkoFixture(2);
            Require(CampaignStore.Save(initial, path), "The loss fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade")); Click(Find(window, "ArcadePlay"));
            Require(!Find(window, "ArcadeBack").IsEnabled && Find(window, "ArcadeDone").IsEnabled, "A no-prize result must be acknowledged before leaving.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "ArcadeKeep" or "ArcadeUse"), "A loss must not offer an item reward.");
            Click(Find(window, "ArcadeDone"));
            Require(flow.Run!.Resources.Inventory.ItemCount == initial.Resources.Inventory.ItemCount && flow.Run.Money == 27, "A loss charges once and gives no item.");
            var landing = CampaignArcade.PlayRoulette(flow.Run, 1).Shop!.Arcade!.Pending!.Landing;
            Click(Find(window, "ArcadeRoulette"));
            Click(Find(window, $"RoulettePocket{landing}")); Capture(window, "arcade-roulette-ready");
            Click(Find(window, "ArcadePlay"));
            var result = flow.Run.Shop!.Arcade!.Pending!;
            Require(result.Landing == landing && result.Score == 100 && flow.Run.Money == 24 && CampaignArcade.IsPack(result), "The chosen exact pocket must pay for one spin and win a Collector's Pack.");
            var wheel = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "RouletteWheel");
            var angle = ((RotateTransform)wheel.RenderTransform!).Angle;
            Require(Math.Abs((angle + (landing - 1) * 30) % 360) < .01, "The wheel's winning pocket must stop beneath the fixed pointer.");
            Capture(window, "arcade-roulette-sigil");
            var index = SigilCard(result);
            Click(Find(window, $"CollectorCard{index}"));
            Require(flow.Run.Money == 24 && CampaignShop.UseSessionCards(flow.Run).Select(c => c.Id).SequenceEqual(result.TargetCards), "Picking a pack card must open the saved six-card set without another charge.");
            Require(!Find(window, "FinishShopUse").IsEnabled, "The Arcade prize must be applied before its six-card menu can be left.");
            Click(Find(window, $"DeckCard{result.TargetCards[0].Value}")); Click(Find(window, "ConfirmDeckChoice")); Click(Find(window, "FinishShopUse"));
            Require(flow.Run.Resources.Deck.Single(c => c.Id == result.TargetCards[0]).Sigil == Enum.Parse<SigilId>(result.PackCards![index]), "The picked Sigil must land on the exact chosen card.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name?.Contains(":prize") is true), "A claimed Arcade prize must never appear as shop stock.");
            Console.WriteLine("PASS  Arcade loss gives nothing; roulette lands on its saved pocket and uses its prize through the committed six-card flow");
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static void MotionAndDetach()
    {
        foreach (var detach in new[] { false, true })
        {
            var initial = PlinkoFixture(3);
            var current = initial;
            var plays = 0;
            var motion = new PixelMotion();
            var window = new Window { Width = 1100, Height = 800 };
            var actions = new ArcadeActions((game, choice) =>
            {
                plays++; current = CampaignArcade.PlayPlinko(current, choice); return current.Shop!.Arcade!.Pending;
            }, _ => { }, _ => { }, _ => { }, () => { }, () => { });
            try
            {
                window.Content = ArcadeScreen.Build(current, actions, motion); window.Show(); Dispatcher.UIThread.RunJobs();
                Click(Find(window, "ArcadePlay"));
                Require(current.Money == 27 && plays == 1, "Animation must start after its authoritative payment.");
                Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ArcadeKeep"), "The prize must wait for the animated result.");
                if (detach)
                {
                    window.Content = new Border(); Dispatcher.UIThread.RunJobs();
                    window.Content = ArcadeScreen.Build(current, actions, motion); Dispatcher.UIThread.RunJobs();
                    Require(Find(window, "ArcadeKeep").IsEnabled && plays == 1, "Reopening after an interrupted animation must reveal the pending result without replaying it.");
                }
                else
                {
                    motion.SetEnabled(false);
                    Await(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ArcadeKeep"));
                    Require(Find(window, "ArcadeUse").IsEnabled && plays == 1, "Disabling motion mid-play must settle the same result and restore its claim controls.");
                }
            }
            finally { window.Close(); motion.SetEnabled(false); }
        }
        var full = CampaignSnapshot.Create(1).Inventory with { Tarot = [TarotId.Gambler, TarotId.Prospector, TarotId.TaxCollector], Sigils = [SigilId.Fish] };
        var pending = CampaignArcade.PlayPlinko(PlinkoFixture(3, full), 4);
        var fullWindow = new Window
        {
            Width = 1100, Height = 800, Content = ArcadeScreen.Build(pending,
                new ArcadeActions((_, _) => null, _ => throw new Exception("Keep must not run with full inventory."), _ => { }, _ => { }, () => { }, () => { }), Still())
        };
        try
        {
            fullWindow.Show(); Dispatcher.UIThread.RunJobs();
            Require(!Find(fullWindow, "ArcadeKeep").IsEnabled && Find(fullWindow, "ArcadeUse").IsEnabled, "Full inventory must preserve Use Now while disabling Keep.");
        }
        finally { fullWindow.Close(); }
        Console.WriteLine("PASS  Arcade reduced motion and interrupted animations restore the saved reward; full inventory allows Use Now");
    }

    private static void FullInventoryDiscard()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-discard-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var inventory = CampaignSnapshot.Create(1).Inventory with
            { Tarot = [TarotId.Gambler, TarotId.Prospector, TarotId.TaxCollector], Sigils = [SigilId.Fish] };
            var initial = CampaignArcade.PlayPlinko(PlinkoFixture(3, inventory), 4);
            Require(CampaignStore.Save(initial, path), "The full-inventory prize must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            var expander = window.GetVisualDescendants().OfType<Expander>().SingleOrDefault(e => e.Name == "ArcadeFreeSlot");
            Require(expander is not null, "A full inventory must expose stored-card discard choices while claiming a prize.");
            expander!.IsExpanded = true; Dispatcher.UIThread.RunJobs();
            Click(Find(window, "ArcadeDiscardTarot0"));
            var saved = CampaignStore.Load(path)!;
            Require(saved.Resources.Inventory.Tarot.SequenceEqual([TarotId.Prospector, TarotId.TaxCollector]) && saved.Resources.Inventory.Sigils.SequenceEqual([SigilId.Fish]),
                "Discard must remove exactly the selected stored item.");
            Require(saved.Shop!.Arcade!.Pending!.TargetCards.SequenceEqual(initial.Shop!.Arcade!.Pending!.TargetCards) && saved.Money == initial.Money,
                "Discarding must preserve the paid prize and its saved six cards.");
            Require(Find(window, "ArcadeKeep").IsEnabled, "Freeing one card slot must enable keeping the same prize.");
            Click(Find(window, "ArcadeKeep"));
            Require(flow.Run!.Resources.Inventory.ItemCount == 4 && flow.Run.Shop!.Arcade!.Pending is null, "The prize must fill the freed slot exactly once.");
            Console.WriteLine("PASS  Full inventory can discard a stored card and keep the same paid Arcade prize");
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static void AnimationsReachSavedResults()
    {
        foreach (var game in new[] { ArcadeGame.Plinko, ArcadeGame.Roulette })
        {
            var run = PlinkoFixture(5);
            var plays = 0;
            var motion = new PixelMotion();
            var window = new Window { Width = 1100, Height = 800 };
            try
            {
                var actions = new ArcadeActions((chosen, choice) =>
                {
                    plays++;
                    run = chosen == ArcadeGame.Plinko ? CampaignArcade.PlayPlinko(run, choice) : CampaignArcade.PlayRoulette(run, choice);
                    return run.Shop!.Arcade!.Pending;
                }, _ => { }, _ => { }, _ => { }, () => { }, () => { });
                window.Content = ArcadeScreen.Build(run, actions, motion); window.Show(); Dispatcher.UIThread.RunJobs();
                if (game == ArcadeGame.Roulette)
                {
                    var landing = CampaignArcade.PlayRoulette(run, 1).Shop!.Arcade!.Pending!.Landing;
                    Click(Find(window, "ArcadeRoulette")); Click(Find(window, $"RoulettePocket{landing}"));
                }
                var started = System.Diagnostics.Stopwatch.StartNew();
                Click(Find(window, "ArcadePlay"));
                Await(() => started.ElapsedMilliseconds >= 900);
                Require(!Claimable(window), "Motion-enabled play must show the saved path or spin before its prize.");
                Capture(window, game == ArcadeGame.Plinko ? "arcade-plinko-in-motion" : "arcade-roulette-in-motion");
                Await(() => Claimable(window));
                Require(plays == 1 && run.Money == 27 && started.ElapsedMilliseconds >= 2200,
                    "A completed animation must reveal exactly one already-paid reward.");
                if (game == ArcadeGame.Plinko)
                {
                    var ball = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlinkoBall");
                    Require(Math.Abs(Canvas.GetTop(ball) + ball.Height / 2 - 1082) < .01, "The animated ball must settle above the printed slot number.");
                }
                else
                {
                    var wheel = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "RouletteWheel");
                    Require(Math.Abs((((RotateTransform)wheel.RenderTransform!).Angle + (run.Shop!.Arcade!.Pending!.Landing - 1) * 30) % 360) < .01,
                        "The completed spin must put its saved winning pocket under the pointer.");
                }
            }
            finally { window.Close(); motion.SetEnabled(false); }
        }
        Console.WriteLine("PASS  Full Plinko and roulette animations reveal the saved prize and settle on the authoritative slot or pocket");
    }

    private static void SaveFailureDoesNotPublishArcadeActions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-durable-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var initial = PlinkoFixture(5);
            Require(CampaignStore.Save(initial, path), "The save-failure fixture must save initially.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue")); Click(Find(window, "ShopArcade"));
            void Fail(string control)
            {
                var before = System.Text.Json.JsonSerializer.Serialize(flow.Run);
                File.Delete(path); Directory.CreateDirectory(path);
                Click(Find(window, control));
                Require(System.Text.Json.JsonSerializer.Serialize(flow.Run) == before, $"A failed save must not publish {control}'s payment, reward or effect.");
                Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Could not save") is true), "A failed Arcade save must explain that the action needs retrying.");
                Directory.Delete(path);
                Require(CampaignStore.Save(flow.Run!, path), "The prior run must be recoverable after storage is available.");
            }
            Fail("ArcadePlay");
            Require(Find(window, "ArcadePlay").IsEnabled && flow.Run!.Money == 30, "A failed play must stay available without a charge.");
            Click(Find(window, "ArcadePlay"));
            Require(flow.Run!.Money == 27 && flow.Run.Shop!.Arcade!.Pending!.Score == 5, "Retry after saving recovers must produce one paid deterministic result.");
            var card = $"CollectorCard{SigilCard(flow.Run.Shop!.Arcade!.Pending!)}";
            Fail(card); Require(flow.Run.Shop!.UseSession is null && Find(window, card).IsEnabled, "A pack pick must not commit until its session is saved.");
            Click(Find(window, card));
            var target = CampaignShop.UseSessionCards(flow.Run)[0].Id;
            Click(Find(window, $"DeckCard{target.Value}")); Fail("ConfirmDeckChoice");
            Require(!Find(window, "FinishShopUse").IsEnabled, "An unsaved prize effect must remain unapplied.");
            Click(Find(window, $"DeckCard{target.Value}")); Click(Find(window, "ConfirmDeckChoice"));
            Fail("FinishShopUse"); Require(Find(window, "FinishShopUse").IsEnabled, "An unsaved finish must preserve the saved six-card opportunity.");
            Click(Find(window, "FinishShopUse"));
            Require(flow.Run.Money == 27 && CampaignStore.Load(path)!.Shop!.UseSession is null, "Durable retries must finish with exactly one charge and one applied prize.");
            Console.WriteLine("PASS  Arcade save failures preserve prior payment, claim and card effects until a durable retry succeeds");
        }
        finally { window.Close(); if (Directory.Exists(path)) Directory.Delete(path); File.Delete(path); }
    }

    private static void PackRevealAndForcedUse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-pack-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            Require(CampaignStore.Save(PlinkoFixture(5), path), "The pack fixture must save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade")); Click(Find(window, "ArcadePlay"));
            var result = flow.Run!.Shop!.Arcade!.Pending!;
            Require(CampaignArcade.IsPack(result) && flow.Run.Money == 27, "A 5-point drop must pay once and save a Collector's Pack.");
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "ArcadeKeep" or "ArcadeUse"), "A Collector's Pack can never be kept.");
            for (var i = 0; i < 3; i++)
            {
                var key = result.PackCards![i];
                var expected = key == CampaignArcade.LegendaryKey ? $"Legendary — fills {5 - flow.Run.Resources.Inventory.Jokers.Length} empty Joker slots"
                    : $"{FeatureCatalog.CollectorTitle(Enum.Parse<SigilId>(key))} — {key} Sigil";
                var button = Find(window, $"CollectorCard{i}");
                Require(button.IsEnabled && AutomationProperties.GetName(button) == expected, $"Pack card {i} must be a named, reduced-motion-ready choice: {expected}.");
            }
            Capture(window, "arcade-collector-pack");
            var exactSave = File.ReadAllText(path);
            flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Require(Find(window, "CollectorCard0").IsEnabled && File.ReadAllText(path) == exactSave, "Continue must restore the same unopened pack.");
            var index = SigilCard(result);
            var stale = Find(window, $"CollectorCard{index}");
            Click(stale);
            Require(flow.Run!.Shop!.UseSession!.FeatureId == result.PackCards![index], "The picked Sigil must open its six-card menu.");
            Require(!Find(window, "FinishShopUse").IsEnabled, "The picked Sigil must be applied before its menu can be left.");
            var committed = File.ReadAllText(path); Click(stale);
            Require(File.ReadAllText(path) == committed, "A stale pack control must not pick twice.");
            Click(Find(window, $"DeckCard{result.TargetCards[1].Value}")); Click(Find(window, "ConfirmDeckChoice")); Click(Find(window, "FinishShopUse"));
            Require(flow.Run.Resources.Deck.Single(c => c.Id == result.TargetCards[1]).Sigil == Enum.Parse<SigilId>(result.PackCards[index])
                && flow.Run.Resources.Inventory.Sigils.Length == 0, "The Sigil must land on the chosen card and never enter inventory.");
            Console.WriteLine("PASS  Collector's Pack shows three named cards, restores unopened, and forces the picked Sigil onto one of six cards");
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static void LegendaryPickShowsJokers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcade-legendary-ui-{Guid.NewGuid():N}.json");
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            CampaignRun? fixture = null;
            for (ulong seed = 1; seed <= 20000 && fixture is null; seed++)
            {
                var run = CampaignShop.Open(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
                { Resources = CampaignSnapshot.Create(seed) with { Wallets = [30, 0, 0, 0] } });
                if (CampaignArcade.PlayPlinko(run, 4).Shop!.Arcade!.Pending!.PackCards?.Contains(CampaignArcade.LegendaryKey) is true) fixture = run;
            }
            Require(fixture is not null && CampaignStore.Save(fixture!, path), "A Legendary fixture must exist and save.");
            var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, Still());
            flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs(); Click(Find(window, "CampaignContinue"));
            Click(Find(window, "ShopArcade")); Click(Find(window, "ArcadePlay"));
            var before = flow.Run!.Resources.Inventory.Jokers.Select(j => j.Id).ToArray();
            var index = Array.IndexOf(flow.Run.Shop!.Arcade!.Pending!.PackCards!, CampaignArcade.LegendaryKey);
            Click(Find(window, $"CollectorCard{index}"));
            var granted = flow.Run.Resources.Inventory.Jokers.Select(j => j.Id).Except(before).ToArray();
            Require(flow.Run.Resources.Inventory.Jokers.Length == 5 && granted.Length == 5 - before.Length && flow.Run.Shop!.UseSession is null,
                "The Legendary must fill free Joker slots without a six-card menu.");
            var note = window.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "CollectorLegendaryNote");
            Require(note is not null && granted.All(id => note!.Text!.Contains(FeatureCatalog.Get(FeatureCategory.Joker, id.ToString()).Name)),
                "The Arcade must name every Joker the Legendary granted.");
            Require(Find(window, "ArcadeBack").IsEnabled, "After the Legendary the player may return to the shop.");
            Console.WriteLine("PASS  Legendary pack card fills free Joker slots and names each granted Joker");
        }
        finally { window.Close(); File.Delete(path); }
    }

    // A finished motion reveal must not crash when its screen is later replaced.
    private static void PackRevealSurvivesLeaving()
    {
        var motion = new PixelMotion();
        var window = new Window { Width = 1100, Height = 800 };
        try
        {
            var pending = CampaignArcade.PlayPlinko(PlinkoFixture(5), 4);
            window.Content = ArcadeScreen.Build(pending, new ArcadeActions((_, _) => null, _ => { }, _ => { }, _ => { }, () => { }, () => { }), motion);
            window.Show(); Dispatcher.UIThread.RunJobs();
            Await(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CollectorCard2" && b.IsEnabled));
            window.Content = new Border(); Dispatcher.UIThread.RunJobs();
            Console.WriteLine("PASS  A finished collector pack reveal can be left without error");
        }
        finally { window.Close(); motion.SetEnabled(false); }
    }
}
