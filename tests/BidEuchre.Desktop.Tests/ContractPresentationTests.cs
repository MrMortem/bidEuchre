using System.Reflection;
using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;

internal static class ContractPresentationTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static bool Pending(GameSession session) => (bool?)typeof(GameSession).GetProperty("WaitingForContractPresentation")?.GetValue(session) ?? false;
    internal static Task Complete(GameSession session, long deal) => (Task)(typeof(GameSession).GetMethod("CompleteContractPresentationAsync")
        ?.Invoke(session, [deal, CancellationToken.None]) ?? Task.CompletedTask);
    internal static GameSession Session(bool campaign = false, bool bots = false, bool presentation = true)
    {
        var seats = Enumerable.Range(0, 4).Select(s => new SeatConfiguration(s, $"Caller {s}", bots ? PlayerKind.Bot : PlayerKind.Human, EngineCatalog.BuiltInEngineId)).ToArray();
        var snapshot = CampaignSnapshot.Create(41);
        snapshot = snapshot with { Inventory = snapshot.Inventory with { Unlocks = Enum.GetValues<GumballId>() } };
        // Reflection keeps the red tests executable before the optional constructor argument exists.
        var constructor = typeof(GameSession).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => p.Name switch
        {
            "id" => (object)Guid.NewGuid().ToString(), "name" => "Contract cue", "seats" => seats,
            "catalog" => new EngineCatalog(), "seed" => 41, "botActionDelay" => TimeSpan.Zero,
            "completedTrickDelay" => TimeSpan.Zero, "campaign" => campaign ? new CampaignContext(snapshot) : null,
            "waitForContractPresentation" => presentation, _ => p.DefaultValue
        }).ToArray();
        return (GameSession)constructor.Invoke(arguments);
    }
    private static async Task Act(GameSession session, string type, BidLevel? bid = null, ContractMode? mode = null, Suit? suit = null, string? card = null)
        => await session.ExecuteAsync(new(session.Game!.CurrentSeat!.Value, type, bid, mode, suit, card));
    internal static async Task Choose(GameSession session, ContractMode mode = ContractMode.Trump, BidLevel bid = BidLevel.Three)
    {
        await Act(session, "bid", mode != ContractMode.Trump && bid == BidLevel.Three ? BidLevel.Four : bid);
        while (session.Game!.Phase == GamePhase.Bidding) await Act(session, "pass");
        await Act(session, "contract", mode: mode, suit: mode == ContractMode.Trump ? Suit.Hearts : null);
    }
    public static void Run()
    {
        foreach (var mode in Enum.GetValues<ContractMode>())
            Task.Run(() => HumanGate(mode)).GetAwaiter().GetResult();
        Task.Run(BotGate).GetAwaiter().GetResult();
        foreach (var variant in new[] { GumballId.Chaos, GumballId.Duelist, GumballId.Telepath })
            Task.Run(() => CampaignGate(variant)).GetAwaiter().GetResult();
        Task.Run(OptionalGate).GetAwaiter().GetResult();
        Task.Run(MisdealGate).GetAwaiter().GetResult();
        Console.WriteLine("PASS  Contract acknowledgment gates human, bot and special-contract actions independently of settings");
    }
    private static async Task HumanGate(ContractMode mode)
    {
        await using var session = Session(); await session.StartAsync(); await Choose(session, mode, mode == ContractMode.Trump ? BidLevel.PartnersBest : BidLevel.Four);
        var action = mode == ContractMode.Trump ? "exchange" : "play";
        Require(Pending(session), "Choosing a contract must atomically arm its presentation before exchange or play.");
        var state = JsonSerializer.Serialize(await session.GetViewAsync(0));
        try { await Act(session, action, card: session.Game!.GetLegalCards(session.Game.CurrentSeat!.Value)[0].Code); throw new Exception("An exchange must wait for the contract cue."); }
        catch (GameRuleException) { }
        Require(JsonSerializer.Serialize(await session.GetViewAsync(0)) == state, "A pending cue must preserve the chosen contract and cards.");
        await Complete(session, session.Game!.DealSerial + 1);
        Require(Pending(session), "A stale completion must not acknowledge another hand.");
        await session.PauseAsync();
        Require(!Pending(session), "Hiding the table must acknowledge an abandoned contract cue.");
        await Complete(session, session.Game.DealSerial);
        try { await Act(session, action, card: session.Game.GetLegalCards(session.Game.CurrentSeat!.Value)[0].Code); throw new Exception("Cue completion must not resume settings."); }
        catch (GameRuleException error) { Require(error.Message.Contains("paused"), "Settings pause remains independent from presentation."); }
        await session.ResumeAsync();
        await Act(session, action, card: session.Game.GetLegalCards(session.Game.CurrentSeat!.Value)[0].Code);
        Require(!Pending(session), "Exchange does not replay the selected-contract cue.");
    }
    private static async Task BotGate()
    {
        await using var session = Session(bots: true); await session.StartAsync();
        using var limit = new CancellationTokenSource(5000);
        while (!Pending(session) && session.Game!.Phase is not (GamePhase.HandComplete or GamePhase.GameComplete)) await Task.Delay(5, limit.Token);
        Require(Pending(session), "A zero-delay bot must stop at the chosen contract before its first card.");
        var view = await session.GetViewAsync(0);
        Require(view!.CurrentTrick.Count == 0 && view.CompletedTricks.Count == 0, "Bot trick play must wait for acknowledgment.");
        await Task.Delay(60); Require(Pending(session) && session.Game!.CompletedTricks.Count == 0, "Polling does not release the bot gate.");
        await Complete(session, view.DealSerial!.Value); await session.WaitForBotsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Require(session.Game!.Phase is GamePhase.HandComplete or GamePhase.GameComplete, "Acknowledgment wakes the existing bot loop.");
    }
    private static async Task CampaignGate(GumballId variant)
    {
        await using var session = Session(campaign: true); await session.StartAsync();
        async Task Command(CampaignCommand command)
        {
            var game = session.Game!;
            await session.ExecuteCampaignAsync(new(game.CampaignActor!.Value, game.ExportCampaignSnapshot().Revision, game.DealSerial, command));
        }
        while (session.Game!.CampaignActor != 0) await Command(new CampaignCommand.Pass());
        await Command(new CampaignCommand.Bid(variant == GumballId.Telepath ? BidLevel.Five : BidLevel.Four, variant));
        while (session.Game!.Contract is null)
        {
            var legal = session.Game.CreateCampaignView(session.Game.CampaignActor).LegalActions;
            await Command(legal.OfType<CampaignCommand.Pass>().FirstOrDefault() ?? legal.OfType<CampaignCommand.ChooseContract>().FirstOrDefault() ?? legal[0]);
        }
        Require(Pending(session), $"{variant}'s initial contract must arm the same presentation gate.");
        var chosen = await session.GetViewAsync(0);
        var before = JsonSerializer.Serialize(chosen);
        try
        {
            await Command(session.Game.CreateCampaignView(session.Game.CampaignActor).LegalActions[0]);
            throw new Exception("A campaign action must wait for its contract announcement.");
        }
        catch (GameRuleException) { }
        Require(JsonSerializer.Serialize(await session.GetViewAsync(0)) == before, "Rejected campaign play preserves the table and revision.");
        await Complete(session, chosen!.DealSerial!.Value);
        Require(!Pending(session), "Acknowledgment releases special-contract play.");
        if (variant == GumballId.Chaos)
        {
            while (session.Game!.CompletedTricks.Count == 0)
                await Command(CampaignBotPolicy.Choose(session.Game.CreateView(session.Game.CampaignActor), session.Game.CampaignActor!.Value));
            Require(!Pending(session), "Chaos boundary rolls update trump without rearming the initial-contract hold.");
        }
    }
    private static async Task MisdealGate()
    {
        await using var session = Session(); await session.StartAsync(); await Choose(session);
        var firstDeal = session.Game!.DealSerial; var hand = session.Game.HandNumber;
        await Complete(session, firstDeal);
        await session.DeveloperAsync(game => game.Redeal());
        Require(session.Game.HandNumber == hand && session.Game.DealSerial != firstDeal, "A misdeal keeps the hand number but changes physical deal identity.");
        await Choose(session);
        Require(Pending(session), "A contract after a misdeal must announce again even at the same hand number.");
        await Complete(session, firstDeal);
        Require(Pending(session), "An old deal callback must not release the new deal's contract cue.");
        await Complete(session, session.Game.DealSerial);
        Require(!Pending(session), "The current deal acknowledgment releases the current cue.");
    }

    private static async Task OptionalGate()
    {
        await using var session = Session(presentation: false); await session.StartAsync(); await Choose(session);
        Require(!Pending(session), "Non-desktop clients never wait for a presentation acknowledgment.");
        await Act(session, "play", card: session.Game!.GetLegalCards(session.Game.CurrentSeat!.Value)[0].Code);
        Require(session.Game.CurrentTrick.Count == 1, "The default session remains usable without a UI callback.");
        await using var pending = Session(bots: true); await pending.StartAsync();
        using var limit = new CancellationTokenSource(5000);
        while (!Pending(pending)) await Task.Delay(5, limit.Token);
        await pending.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }
}
