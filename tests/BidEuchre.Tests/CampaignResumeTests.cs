using System.Text.Json;
using BidEuchre.Core;
using BidEuchre.App;

internal static class CampaignResumeTests
{
    static readonly string[] Names = ["You", "West", "Partner", "East"];
    static GameEngine New(CampaignSnapshot? resources = null) => new(Names, 42,
        new MatchOptions(10000) { HandLimit = 3, NegativeThreshold = -10000 }, new CampaignContext(resources ?? CampaignSnapshot.Create(42)));
    static GameEngine RoundTrip(GameEngine game) => GameEngine.RestoreCampaignMatch(
        JsonSerializer.Deserialize<CampaignMatchSnapshot>(JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot()))!);
    static void EqualViews(GameEngine expected, GameEngine actual)
    {
        foreach (int? seat in new int?[] { null, 0, 1, 2, 3 })
        {
            Assert.Equal(JsonSerializer.Serialize(expected.CreateView(seat)), JsonSerializer.Serialize(actual.CreateView(seat)));
            if (seat is { } s) Assert.Equal(string.Join("|", expected.CreateCampaignView(s).LegalActions), string.Join("|", actual.CreateCampaignView(s).LegalActions));
        }
        Assert.Equal(JsonSerializer.Serialize(expected.ExportCampaignSnapshot()), JsonSerializer.Serialize(actual.ExportCampaignSnapshot()));
    }
    static void Advance(GameEngine game)
    {
        if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); return; }
        var seat = game.CampaignActor!.Value;
        game.ExecuteCampaignAction(seat, game.CreateCampaignView(seat).LegalActions[0]);
    }
    public static void AuctionAndContinuation()
    {
        var game = New(); game.StartGame(1); Advance(game); Advance(game);
        var restored = RoundTrip(game); EqualViews(game, restored);
        for (var guard = 0; game.Phase != GamePhase.GameComplete && guard < 160; guard++)
        { Advance(game); Advance(restored); EqualViews(game, restored); }
        Assert.Equal(GamePhase.GameComplete, restored.Phase);
    }

    public static void EveryBoundaryAndEffects()
    {
        var eagleRepeat = false; var pendingPoints = false; var stickyOverlay = false; var poker = false;
        var pending = new HashSet<string>();
        JokerId[][] groups = [[], [JokerId.OneUp, JokerId.Cheater, JokerId.DoubleDown, JokerId.StickyFingers, JokerId.DejaVu],
            [JokerId.LastLaugh, JokerId.Medusa, JokerId.LuckyDuck, JokerId.DeepDigger, JokerId.RollerCoaster]];
        foreach (var jokers in groups)
        foreach (var variant in new GumballId?[] { null, GumballId.Chaos, GumballId.Poker, GumballId.Telepath, GumballId.Duelist, GumballId.PartnersBest })
        {
            var resources = CampaignSnapshot.Create(93) with { Inventory = new(jokers.Select(j => new JokerState(j)).ToArray(), [], Enum.GetValues<GumballId>()) };
            var enhancements = Enum.GetValues<EnhancementId>(); var sigils = Enum.GetValues<SigilId>();
            for (var i = 0; i < 24; i++) resources.Deck[i] = resources.Deck[i] with { Enhancement = enhancements[i % enhancements.Length], Sigil = sigils[i % sigils.Length] };
            var game = New(resources); game.StartGame(3);
            for (var guard = 0; guard < 180; guard++)
            {
                var snapshot = game.ExportCampaignMatchSnapshot();
                var ids = snapshot.CompletedTricks.SelectMany(t => t.Plays).Select(p => p.Id).ToArray();
                eagleRepeat |= ids.Distinct().Count() < ids.Length;
                pendingPoints |= snapshot.TrickPoints.Length > 0; stickyOverlay |= snapshot.Overlays.Length > 0; poker |= snapshot.PokerRevealed;
                if (snapshot.PendingChoice is { } choice) pending.Add(choice);
                var restored = RoundTrip(game); EqualViews(game, restored);
                if (game.Phase == GamePhase.GameComplete) break;
                if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); restored.StartNextHand(); }
                else
                {
                    var seat = game.CampaignActor!.Value; var actions = game.CreateCampaignView(seat).LegalActions;
                    CampaignCommand action = actions[0];
                    if (game.Phase == GamePhase.Bidding && seat == 0 && game.CreateCampaignView(0).PendingChoice is null)
                        action = actions.OfType<CampaignCommand.Bid>().FirstOrDefault(b => variant == GumballId.PartnersBest ? b.Level == BidLevel.PartnersBest : b.Variant == variant && b.Level == (variant == GumballId.Telepath ? BidLevel.Five : BidLevel.Four)) ?? action;
                    game.ExecuteCampaignAction(seat, action); restored.ExecuteCampaignAction(seat, action);
                }
                EqualViews(game, restored);
            }
            Assert.Equal(GamePhase.GameComplete, game.Phase);
        }
        Assert.True(eagleRepeat, "Matrix must actually replay an Eagle identity.");
        Assert.True(pendingPoints && stickyOverlay && poker, "Matrix must exercise live score ledger, Sticky Fingers overlay and revealed Poker hands.");
        Assert.Equal(4, pending.Count);
    }

    public static void CorruptAndDetached()
    {
        var game = New(); game.StartGame(3); var snapshot = game.ExportCampaignMatchSnapshot();
        var before = JsonSerializer.Serialize(game.CreateView(0));
        var restored = GameEngine.RestoreCampaignMatch(snapshot);
        snapshot.Hands[0][0] = snapshot.Hands[0][1]; snapshot.Resources.Wallets[0] = 500;
        Assert.Equal(before, JsonSerializer.Serialize(game.CreateView(0)));
        Assert.Equal(before, JsonSerializer.Serialize(restored.CreateView(0)));
        snapshot = game.ExportCampaignMatchSnapshot();
        CampaignMatchSnapshot[] invalid = [snapshot with { Version = 99 }, snapshot with { Hands = null! }, snapshot with { CurrentSeat = 7 },
            snapshot with { CurrentSeat = null }, snapshot with { PendingChoice = "unknown", PendingActor = 0 }, snapshot with { Events = [null!] },
            snapshot with { Variant = GumballId.Duelist, DuelOpponent = 1 }, snapshot with { Phase = GamePhase.Playing }, snapshot with { Auction = [null!] }, snapshot with { Overlays = [null!] },
            snapshot with { TrickHistory = [null!] }, snapshot with { Resources = snapshot.Resources with { Inventory = null! } }];
        foreach (var bad in invalid) Assert.Throws<GameRuleException>(() => GameEngine.RestoreCampaignMatch(bad));
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(snapshot))!.AsObject(); node.Remove("FirstHumanRoundCard");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CampaignMatchSnapshot>(node.ToJsonString()));
        while (game.Phase != GamePhase.Playing) Advance(game);
        snapshot = game.ExportCampaignMatchSnapshot(); snapshot.Hands[3] = snapshot.Hands[3].Skip(1).ToArray();
        Assert.Throws<GameRuleException>(() => GameEngine.RestoreCampaignMatch(snapshot));
    }
    public static void DeveloperTerminalPendingAndPanic()
    {
        var game = New(CampaignSnapshot.Create(1) with { Inventory = new([new(JokerId.OneUp)], [], []) });
        game.StartGame(3); game.DeveloperEndMatch(0);
        EqualViews(game, RoundTrip(game));
        game = New(); game.StartGame(3);
        while (game.Phase != GamePhase.HandComplete) Advance(game);
        game.Redeal();
        while (game.Phase != GamePhase.HandComplete) Advance(game);
        EqualViews(game, RoundTrip(game));
    }
}
