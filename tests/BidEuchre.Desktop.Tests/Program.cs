using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

using var testDataDirectory = new DesktopTestDataDirectory();

AppBuilder.Configure<Application>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is null })
    .AfterSetup(builder => builder.Instance!.Styles.Add(new FluentTheme()))
    .SetupWithoutStarting();

if (args is ["--profile-artwork", var artScreen, ..])
{
    ArtworkProfile.Run(artScreen, args.Contains("--preload"));
    return;
}

if (args.Contains("--trick-presentation-only")) { TrickPresentationTests.Run(); return; }

if (args.Contains("--profile-table")) { TableUpdateProfile.Run(); return; }
if (args.Contains("--table-identity-only")) { TableUpdateProfile.Run(verifyIdentity: true); return; }

if (args.Contains("--resume-only")) { CampaignResumeUiTests.Run(); return; }

if (args.Contains("--curated-art-only")) { CuratedArtTests.Run(); return; }

if (args.Contains("--table-rendering-only")) { TableRenderingTests.Run(); return; }

if (args.Contains("--score-history-only")) { ScoreHistoryUiTests.Run(); return; }
if (args.Contains("--poker-reveal-only")) { PokerRevealUiTests.Run(); return; }

if (args.Contains("--plinko-drag-only")) { PlinkoDragTests.Run(); return; }

if (args.Contains("--hand-hover-only")) { HandHoverTests.Run(); return; }

if (args.Contains("--score-jolt-only")) { ScoreJoltTests.Run(); return; }

if (args.Contains("--ante-only"))
{
    var anteMotion = new PixelMotion();
    anteMotion.SetEnabled(false);
    TestAntePreviews(anteMotion);
    return;
}

if (args.Contains("--booster-only")) { CampaignShopUiTests.BoosterAndResume(); return; }

if (args.Contains("--soda-only")) { CampaignShopUiTests.BuySoda(); return; }

if (args.Contains("--shop-only")) { CampaignShopUiTests.Run(); return; }
if (args.Contains("--card-explanations-only")) { CampaignShopUiTests.CardExplanations(); return; }

if (args.Contains("--arcade-only")) { CampaignArcadeUiTests.Run(); return; }

if (args.Contains("--shop-use-only")) { CampaignShopUseUiTests.Run(); return; }

if (args.Contains("--gambler-only")) { CampaignGamblerUiTests.Run(); return; }

if (args.Contains("--contract-ui-only")) { ContractAnnouncementUiTests.Run(); return; }
if (args.Contains("--sort-retry-only")) { ContractAnnouncementUiTests.SortRetry(); return; }
if (args.Contains("--contract-only")) { ContractPresentationTests.Run(); ContractAnnouncementUiTests.Run(); return; }

if (args.Contains("--rematch-hotseat-only")) { RematchHotseatTests.Run(); return; }

if (args.Contains("--engine-setup-only")) { EngineSetupUiTests.Run(); return; }

if (args.Contains("--live-settings-only")) { SessionPauseTests.Run(); LiveSettingsTests.Run(); return; }

CampaignShopUiTests.Run();
CampaignShopUiTests.CardExplanations();
CampaignArcadeUiTests.Run();
CampaignGamblerUiTests.Run();
ScoreHistoryUiTests.Run();
PokerRevealUiTests.Run();
EngineSetupUiTests.Run();
RematchHotseatTests.Run();
ContractPresentationTests.Run();
TrickPresentationTests.Run();
ContractAnnouncementUiTests.Run();
SessionPauseTests.Run();
LiveSettingsTests.Run();
EnhancementBackgroundTests.Run();
ScoreJoltTests.Run();
HandHoverTests.Run();
PlinkoDragTests.Run();
HandReadabilityTests.Run();
CuratedArtTests.Run();
CollectionTests.Run();
TableRenderingTests.Run();
TableUpdateProfile.Run(verifyIdentity: true);
DesktopBugTests.Run();
CampaignRecoveryUiTests.Run();
CampaignResumeUiTests.Run();

var motion = new PixelMotion();
motion.SetEnabled(false);
CardId[]? selected = null;
var deck = CampaignCards.CreateDeck().ToArray();
var window = new Window
{
    Width = 1280, Height = 1100,
    Content = CampaignDeckPicker.Build(deck, 1, ids => selected = ids, () => { }, motion, "Choose a Sigil target")
};
try
{
    window.Show();
    Dispatcher.UIThread.RunJobs();
    Button Find(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    void Click(Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
    var confirm = Find("ConfirmDeckChoice");
    if (confirm.IsEnabled) throw new Exception("Confirmation must require a card.");
    Click(Find("DeckCard5"));
    if (!confirm.IsEnabled) throw new Exception("Clicking the card face must enable confirmation.");
    Click(Find("DeckCard7"));
    Click(confirm);
    if (selected is not { Length: 1 } || selected[0] != deck[7].Id)
        throw new Exception("Confirmation must return the most recently selected physical card.");
    Console.WriteLine("PASS  Deck picker accepts card-face clicks and confirms the selected card");
}
finally { window.Close(); }

// The gumball machine sells one blind pull, with and without its animation.
foreach (var animated in new[] { false, true })
{
    var path = Path.Combine(Path.GetTempPath(), $"gumball-ui-{Guid.NewGuid():N}.json");
    var shop = new Window { Width = 1280, Height = 1100 };
    var moving = new PixelMotion();
    moving.SetEnabled(animated);
    try
    {
        if (!CampaignStore.Save(new CampaignRun(Money: 30, Phase: CampaignPhase.Shop), path)) throw new Exception("Could not prepare disposable save.");
        var flow = new CampaignFlow(path, screen => shop.Content = screen, _ => Task.CompletedTask, () => { }, moving);
        flow.Open();
        shop.Show();
        void Click(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var button = shop.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            button.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shop)!.Value;
            shop.MouseDown(point, MouseButton.Left);
            shop.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        string? Text(string name) => shop.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == name)?.Text;
        // With motion on, screens and the machine take real time to finish.
        long Await(Func<bool> done)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var limit = new CancellationTokenSource(8000);
            // Timers only fire inside a running loop, so run one until the screen has caught up.
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            poll.Tick += (_, _) => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); if (done()) limit.Cancel(); };
            poll.Start();
            Dispatcher.UIThread.MainLoop(limit.Token);
            poll.Stop();
            return clock.ElapsedMilliseconds;
        }
        Click("CampaignContinue");
        Await(() => Text("GumballTag") is not null);
        if (Text("GumballTag") != "$3") throw new Exception("The machine must show its price.");
        if (shop.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text is "Chaos" or "Poker" or "Duelist" or "Telepath"))
            throw new Exception("The machine must not name a bid before the pull.");
        Click("GumballMachine");
        Click("PullGumball");
        // Paid and saved at once, before any gumball has rolled out.
        var saved = CampaignStore.Load(path)!;
        if (saved.Money != 27 || saved.Resources.Inventory.Unlocks.Length != 1) throw new Exception("A pull must charge once and save its bid immediately.");
        if (animated)
        {
            if (Text("CampaignNote") is not null) throw new Exception("The result must wait for the animation.");
            if (Await(() => Text("CampaignNote") is not null) < 1500) throw new Exception("The animation must play before the result.");
        }
        Dispatcher.UIThread.RunJobs();
        var name = FeatureCatalog.Get(FeatureCategory.Gumball, saved.Resources.Inventory.Unlocks[0].ToString()).Name;
        if (Text("CampaignNote") != $"The gumball unlocked the {name} bid!") throw new Exception("The shop must report the bid that came out.");
        if (Text("GumballTag") != "SOLD") throw new Exception("The machine must read SOLD after its pull.");
        if (shop.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "GumballMachine")) throw new Exception("A sold machine cannot be pressed.");
        if (!shop.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == $"GUMBALLS 1/{CampaignInventory.GumballCount}")) throw new Exception("The shelf must count the gumball.");
        if (flow.Run!.Money != 27 || flow.Run.Resources.Inventory.Unlocks.Length != 1) throw new Exception("A pull must not repeat.");
        Console.WriteLine($"PASS  Gumball machine sells one blind pull and reports its bid ({(animated ? "animated" : "motion off")})");
    }
    finally { shop.Close(); File.Delete(path); }
}

TestAntePreviews(motion);

static void TestAntePreviews(PixelMotion motion)
{
    foreach (var (match, title, threshold) in new[] { (5, "ANTE 2", "±16 POINTS"), (16, "ANTE 6", "±44 POINTS") })
        TestAntePreview(motion, match, title, threshold);
}

static void TestAntePreview(PixelMotion motion, int match, string title, string threshold)
{
    var path = Path.Combine(Path.GetTempPath(), $"ante-ui-{Guid.NewGuid():N}.json");
    var ante = new Window { Width = 1280, Height = 1100 };
    try
    {
        if (!CampaignStore.Save(new CampaignRun(Match: match, Money: 14), path)) throw new Exception("Could not prepare disposable save.");
        var started = 0;
        var flow = new CampaignFlow(path, screen => ante.Content = screen, _ => { started++; return Task.CompletedTask; }, () => { }, motion);
        flow.Open();
        ante.Show();
        void Click(string name)
        {
            Dispatcher.UIThread.RunJobs();
            var button = ante.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), ante)!.Value;
            ante.MouseDown(point, MouseButton.Left);
            ante.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Click("CampaignContinue");
        var texts = ante.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        string Status(int slot) => ante.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == $"AnteStatus{slot}").Text!;
        var current = (match - 1) % 3 + 1;
        foreach (var expected in new[] { title, threshold, "8 HANDS · TIES CONTINUE", "$14", "JOKERS", "TAROTS / SIGILS" }
            .Concat(match > 9 ? ["LOSE ♥2", "LOSE ♥3"] : ["LOSE ♥1", "LOSE ♥2"]))
            if (!texts.Contains(expected)) throw new Exception($"The Ante screen must show \"{expected}\".");
        for (var slot = 1; slot <= 3; slot++)
        {
            var panel = ante.GetVisualDescendants().OfType<Border>().Single(b => b.Name == $"AntePanel{slot}");
            var reward = slot == 3 ? "REWARD $7+" : "REWARD $5+";
            if (!panel.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == reward))
                throw new Exception($"Ante panel {slot} must show {reward}.");
        }
        if (texts.Count(t => t == threshold) != 3) throw new Exception("Every match panel must show the threshold.");
        if (current == 2 && (Status(1) != "PLAYED" || Status(3) != "UPCOMING")) throw new Exception("Panels around the current match must read PLAYED and UPCOMING.");
        if (current == 1 && (Status(2) != "UPCOMING" || Status(3) != "UPCOMING")) throw new Exception("Later panels must read UPCOMING.");
        var start = ante.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CampaignStartMatch");
        if (start.GetVisualAncestors().OfType<Border>().Count(b => b.Name == $"AntePanel{current}") != 1)
            throw new Exception("Only the current match's panel may hold the Start button.");
        if (!start.IsFocused) throw new Exception("The Start button must be focused on arrival.");
        Click("CampaignStartMatch");
        if (started != 1 || flow.Run!.Phase != CampaignPhase.InMatch) throw new Exception("Start Match must begin the match.");
        Console.WriteLine($"PASS  Ante screen shows three match panels and starts the current one ({title})");
    }
    finally { ante.Close(); File.Delete(path); }
}
