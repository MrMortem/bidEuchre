using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

static class CampaignServerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static async Task<JsonElement> State(WebCampaigns service, string token) => Element(await service.GetAsync(token));
    private static string Version(JsonElement value) => value.GetProperty("version").GetString()!;
    private static Task<JsonElement> Change(WebCampaigns service, string token, JsonElement state, string action, object? fields = null) => ChangeVersion(service, token, Version(state), action, fields);
    private static async Task<JsonElement> ChangeVersion(WebCampaigns service, string token, string version, string action, object? fields = null)
    {
        var body = fields is null ? new Dictionary<string, JsonElement>() : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields, Json))!;
        body.Add("expectedVersion", Element(version));
        return Element(await service.MutateAsync(token, action, Element(body)));
    }
    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); throw new Exception($"Expected {code}."); }
        catch (CampaignApiException error) { Assert(error.Code == code, $"Expected {code}, got {error.Code}."); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), $"bideuchre-server-test-{Guid.NewGuid():N}");
        public readonly string Token = WebCampaigns.NewToken();
        public WebCampaigns Service;
        public IConfiguration Config { get; }
        public Fixture(int capacity = 32, int idle = 1800)
        {
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["WebCampaigns:DataDirectory"] = Root, ["WebCampaigns:MaxSessions"] = capacity.ToString(), ["WebCampaigns:IdleTimeoutSeconds"] = idle.ToString() }).Build();
            Service = NewService();
        }
        public WebCampaigns NewService() => new(new EngineCatalog(), Config, NullLogger<WebCampaigns>.Instance);
        public string SavePath => Path.Combine(Root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token))).ToLowerInvariant(), "campaign.json");
        public void Save(CampaignRun run) => Assert(CampaignStore.Save(new CampaignSave(3, run) { Metadata = new("fixture", 5) }, SavePath), "Fixture save failed.");
        public async Task Restart() { await Service.DisposeAsync(); Service = NewService(); }
        public async ValueTask DisposeAsync() { await Service.DisposeAsync(); Directory.Delete(Root, true); }
    }
    public static async Task RunAsync()
    {
        Commands(); await Lifecycle(); await Shop(); await CorruptAndDirty(); await CapacityAndExpiry(); await Terminal(); await PendingChoices(); await LifecycleRaces();
    }
    private static void Commands()
    {
        CampaignCommand[] commands = [new CampaignCommand.Bid(BidLevel.Four, GumballId.Chaos), new CampaignCommand.Pass(), new CampaignCommand.PanicButton(),
            new CampaignCommand.Play(new(3), Suit.Hearts), new CampaignCommand.Exchange(new(4)), new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades),
            new CampaignCommand.UseTarot(2, [new(1), new(4), new(5)]), new CampaignCommand.UseSigil(1, new(7)), new CampaignCommand.SwapPartnerCard(new(8)),
            new CampaignCommand.ChooseRevealSeat(1), new CampaignCommand.RevealPartnerCard(new(10)), new CampaignCommand.SelectOpponent(3)];
        foreach (var command in commands.Concat(Enum.GetValues<GumballId>().Select(id => new CampaignCommand.Bid(BidLevel.Four, id))))
        {
            var wire = Element(CampaignWire.Command(command)); var parsed = CampaignWire.ParseCommand(wire);
            Assert(wire.GetRawText() == Element(CampaignWire.Command(parsed)).GetRawText(), "Command union round trip lost a field.");
        }
        foreach (var invalid in new[] { "{}", "null", "[]", "{\"kind\":\"wat\"}", "{\"kind\":\"pass\",\"seat\":0}",
            "{\"kind\":\"play\",\"cardId\":24}", "{\"kind\":\"bid\",\"level\":4}", "{\"kind\":\"bid\",\"level\":\"99\"}",
            "{\"kind\":\"use-tarot\",\"slot\":0,\"targetIds\":[1,1]}", "{\"kind\":\"use-sigil\",\"slot\":4,\"targetId\":0}",
            "{\"kind\":\"choose-contract\",\"mode\":\"High\",\"trump\":\"Hearts\"}", "{\"kind\":\"choose-contract\",\"mode\":\"Trump\"}",
            "{\"kind\":\"select-opponent\",\"targetSeat\":-1}", "{\"kind\":\"play\",\"cardId\":1,\"effectiveSuit\":\"clubs\"}" })
        {
            try { CampaignWire.ParseCommand(JsonDocument.Parse(invalid).RootElement); throw new Exception("Accepted malformed command: " + invalid); }
            catch (CampaignApiException e) { Assert(e.Code == "invalid_request", "Wrong malformed command diagnostic."); }
        }
        Console.WriteLine("PASS  Campaign wire covers all command variants and rejects malformed discriminators, fields, IDs and enum payloads");
    }
    private static async Task Lifecycle()
    {
        await using var f = new Fixture(); var empty = await State(f.Service, f.Token);
        var intro = await Change(f.Service, f.Token, empty, "new");
        var started = await Change(f.Service, f.Token, intro, "start-match");
        Assert(started.GetProperty("phase").GetString() == "InMatch", "Start must create a live campaign.");
        // Wait for the first human decision, then no background actor can change the following comparisons.
        for (var n = 0; n < 100 && started.GetProperty("table").GetProperty("currentSeat").GetInt32() != 0; n++)
        { await Task.Delay(100); started = await State(f.Service, f.Token); }
        var table = started.GetProperty("table"); Assert(table.GetProperty("currentSeat").GetInt32() == 0, "Bots did not reach the human.");
        Assert(table.GetProperty("players").EnumerateArray().Where(p => p.GetProperty("seat").GetInt32() != 0).All(p => p.GetProperty("visibleCards").GetArrayLength() == 0), "Hidden hands leaked.");
        var hand = table.GetProperty("players")[0].GetProperty("visibleCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).Reverse().ToArray();
        var arranged = await Change(f.Service, f.Token, started, "arrange-hand", new { expectedRevision = table.GetProperty("revision").GetInt64(), expectedDealSerial = table.GetProperty("dealSerial").GetInt64(), orderedCardIds = hand });
        Assert(Version(arranged) != Version(started), "Arrangement must advance profile version.");
        Assert(arranged.GetProperty("table").GetProperty("revision").GetInt64() == table.GetProperty("revision").GetInt64(), "Arrangement changes no engine revision.");
        await Reject(() => Change(f.Service, f.Token, started, "commands", new { expectedRevision = table.GetProperty("revision").GetInt64(), expectedDealSerial = table.GetProperty("dealSerial").GetInt64(), command = table.GetProperty("legalCommands")[0] }), "stale_state");
        await Reject(() => Change(f.Service, f.Token, arranged, "next-hand", new { expectedRevision = table.GetProperty("revision").GetInt64(), expectedDealSerial = table.GetProperty("dealSerial").GetInt64() + 1 }), "stale_state");
        await f.Restart(); var dormant = await State(f.Service, f.Token);
        Assert(dormant.GetProperty("requiresContinue").GetBoolean() && dormant.GetProperty("table").ValueKind == JsonValueKind.Null, "GET must not activate dormant bots.");
        Assert(Version(dormant) == Version(arranged), "Atomic metadata must survive restart.");
        var resumed = await Change(f.Service, f.Token, dormant, "continue");
        Assert(resumed.GetProperty("table").GetProperty("players")[0].GetProperty("visibleCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).SequenceEqual(hand), "Resume lost physical hand order.");
        Assert(resumed.GetProperty("run").GetProperty("lives").GetInt32() == 3, "Resume cost a life.");
        var result = await Change(f.Service, f.Token, resumed, "forfeit"); Assert(result.GetProperty("phase").GetString() == "Result", "Forfeit did not settle.");
        Assert(result.GetProperty("run").GetProperty("lives").GetInt32() == 2, "Forfeit life cost wrong.");
        await Reject(() => Change(f.Service, f.Token, resumed, "forfeit"), "stale_state");
        await f.Restart(); var savedResult = await State(f.Service, f.Token); Assert(savedResult.GetProperty("phase").GetString() == "Result", "Result not persisted.");
        var shop = await Change(f.Service, f.Token, savedResult, "result/continue"); Assert(shop.GetProperty("shop").GetProperty("offers").GetArrayLength() == 5, "Result did not open stock.");
        var stock = shop.GetProperty("shop").GetRawText(); Assert((await State(f.Service, f.Token)).GetProperty("shop").GetRawText() == stock, "Read rerolled stock.");
        var next = await Change(f.Service, f.Token, shop, "shop/leave"); Assert(next.GetProperty("phase").GetString() == "Intro", "Shop leave failed.");
        var deleted = await Change(f.Service, f.Token, next, "delete"); Assert(!deleted.GetProperty("hasRun").GetBoolean() && !File.Exists(f.SavePath), "Deletion failed.");
        await Reject(() => Change(f.Service, f.Token, next, "new"), "stale_state");
        Console.WriteLine("PASS  Campaign exact resume, private hands, reorder version, stale gates, forfeit/result/shop and delete lifecycle");
    }
    private static async Task Shop()
    {
        await using var f = new Fixture();
        var resources = CampaignSnapshot.Create(12); resources.Wallets[0] = 100;
        resources = resources with { Inventory = new([new(JokerId.Alpha)], [TarotId.Chameleon], Enum.GetValues<GumballId>()) { Sigils = [SigilId.Bear] } };
        f.Save(CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop) { Resources = resources }));
        var state = await State(f.Service, f.Token);
        var pull = state.GetProperty("shop").GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("category").GetString() == "Gumball").GetProperty("id").GetString();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { try { return (await Change(f.Service, f.Token, state, "shop/pull", new { offerId = pull })).GetProperty("feedback").GetProperty("grants")[0].GetProperty("duplicate").GetBoolean() ? "accepted" : "wrong"; } catch (CampaignApiException e) { return e.Code; } }));
        Assert(attempts.Count(s => s == "accepted") == 1 && attempts.Count(s => s == "stale_state") == 1, "Concurrent pull must charge exactly once and report duplicate.");
        state = await State(f.Service, f.Token); Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 97, "Pull payment wrong.");
        state = await Change(f.Service, f.Token, state, "shop/reroll"); Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 95, "Reroll payment wrong.");
        Assert(state.GetProperty("shop").GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("category").GetString() == "Gumball").GetProperty("sold").GetBoolean(), "Reroll replenished gumball.");
        state = await Change(f.Service, f.Token, state, "items/use", new { category = "Tarot", slot = 0, targetIds = new[] { 0 } });
        Assert(state.GetProperty("deck")[0].GetProperty("enhancement").GetString() == "Wild", "Tarot not applied to physical shared deck.");
        state = await Change(f.Service, f.Token, state, "items/discard", new { category = "Sigil", slot = 0 });
        try { await Change(f.Service, f.Token, state, "items/use", new { category = "Sigil", slot = 0, targetIds = new[] { 0 } }); throw new Exception("Empty slot accepted."); }
        catch (GameRuleException) { }
        state = await Change(f.Service, f.Token, state, "shop/sell", new { jokerId = "Alpha" });
        Assert(state.GetProperty("inventory").GetProperty("jokers").GetArrayLength() == 0 && state.GetProperty("run").GetProperty("money").GetInt32() == 97, "Sale wrong.");
        var offer = state.GetProperty("shop").GetProperty("offers").EnumerateArray().First(o => o.GetProperty("category").GetString() == "Sigil");
        state = await Change(f.Service, f.Token, state, "shop/buy-use", new { offerId = offer.GetProperty("id").GetString(), targetIds = new[] { 2 } });
        Assert(state.GetProperty("deck")[2].GetProperty("sigil").GetString() == offer.GetProperty("featureId").GetString(), "Buy/use effect wrong.");
        Console.WriteLine("PASS  Campaign shop concurrent duplicate pull, reroll, stored items, sell and buy/use share authoritative transactions");
    }
    private static async Task CorruptAndDirty()
    {
        await using var f = new Fixture(1, 1);
        Directory.CreateDirectory(Path.GetDirectoryName(f.SavePath)!); File.WriteAllText(f.SavePath, "{broken");
        var recovery = await State(f.Service, f.Token); Assert(recovery.GetProperty("phase").GetString() == "Recovery" && File.ReadAllText(f.SavePath) == "{broken", "Corrupt save overwritten by read.");
        var state = await Change(f.Service, f.Token, recovery, "new");
        File.Delete(f.SavePath); Directory.CreateDirectory(f.SavePath); // force atomic replacement failure without relying on Unix permissions
        state = await Change(f.Service, f.Token, state, "new"); Assert(state.GetProperty("saveStatus").GetString() == "failed", "Accepted save failure must be visible.");
        await Task.Delay(1100); await f.Service.ExpireAsync();
        await Reject(() => f.Service.GetAsync(WebCampaigns.NewToken()), "capacity");
        Assert(Version(await State(f.Service, f.Token)) == Version(state), "Dirty expiry discarded accepted state.");
        Directory.Delete(f.SavePath); await Task.Delay(1100); await f.Service.ExpireAsync();
        await f.Restart(); Assert((await State(f.Service, f.Token)).GetProperty("hasRun").GetBoolean(), "Recovered save missing.");
        Console.WriteLine("PASS  Corrupt saves stay untouched and failed writes retain dirty state through capacity/expiry until persistence recovers");
    }
    private static async Task CapacityAndExpiry()
    {
        await using var f = new Fixture(1, 1);
        try { await using var duplicate = f.NewService(); throw new Exception("Root lock permitted duplicate ownership."); }
        catch (InvalidOperationException error) { Assert(error.Message.Contains("owned"), "Unexpected root-lock error."); }
        var state = await Change(f.Service, f.Token, await State(f.Service, f.Token), "new");
        await Reject(() => f.Service.GetAsync(WebCampaigns.NewToken()), "capacity");
        await Task.Delay(1100); await f.Service.ExpireAsync();
        var other = await State(f.Service, WebCampaigns.NewToken()); Assert(!other.GetProperty("hasRun").GetBoolean(), "Profile isolation broken.");
        Assert(CampaignStore.LoadSave(f.SavePath).Save?.Run.Lives == 3, "Expiry changed run.");
        Console.WriteLine("PASS  Campaign root ownership, capacity reservations and idle eviction preserve saved runs");
    }
    private static async Task Terminal()
    {
        await using var f = new Fixture();
        var run = new CampaignRun(); var game = new GameEngine(["You", "Opponent 1", "Partner", "Opponent 3"], 5, run.NextMatch(), new CampaignContext(run.Resources)); game.StartGame();
        var guard = 0;
        while (game.Phase != GamePhase.GameComplete && guard++ < 2000)
        {
            if (game.Phase == GamePhase.HandComplete) game.StartNextHand();
            else { var seat = game.CampaignActor!.Value; game.ExecuteCampaignAction(seat, CampaignBotPolicy.Choose(game.CreateView(seat), seat)); }
        }
        Assert(game.Phase == GamePhase.GameComplete, "Fixture match never ended.");
        f.Save(run.StartMatch().WithCheckpoint(new(game.ExportCampaignMatchSnapshot())));
        var dormant = await State(f.Service, f.Token); var result = await Change(f.Service, f.Token, dormant, "continue");
        Assert(result.GetProperty("phase").GetString() == "Result", "Restored terminal not settled.");
        Assert(result.GetProperty("pendingResult").GetProperty("summary").GetProperty("handHistory").GetArrayLength() > 0, "Structured result history missing.");
        var money = result.GetProperty("run").GetProperty("money").GetInt32(); var lives = result.GetProperty("run").GetProperty("lives").GetInt32();
        await f.Restart(); var saved = await State(f.Service, f.Token); var again = await Change(f.Service, f.Token, saved, "continue");
        Assert(again.GetProperty("run").GetProperty("money").GetInt32() == money && again.GetProperty("run").GetProperty("lives").GetInt32() == lives, "Terminal settlement paid or charged twice.");
        Assert(again.GetProperty("pendingResult").GetProperty("summary").GetRawText() == result.GetProperty("pendingResult").GetProperty("summary").GetRawText(), "Result summary did not survive restart.");
        Console.WriteLine("PASS  A real terminal match restores and settles once with durable public score history");
    }
    private static async Task PendingChoices()
    {
        foreach (var kind in new[] { "swap-partner-card", "choose-reveal-seat", "select-opponent", "reveal-partner-card", "exchange" })
        {
            await using var f = new Fixture();
            var inventory = new CampaignInventory(kind is "swap-partner-card" or "choose-reveal-seat" ? [new(JokerId.OneUp), new(JokerId.Cheater)] : [], [], Enum.GetValues<GumballId>());
            var run = new CampaignRun { Resources = CampaignSnapshot.Create(93) with { Inventory = inventory } };
            var game = new GameEngine(["You", "Opponent 1", "Partner", "Opponent 3"], 42, run.NextMatch(), new CampaignContext(run.Resources)); game.StartGame(3);
            if (kind == "choose-reveal-seat") game.ExecuteCampaignAction(0, game.CreateCampaignView(0).LegalActions[0]);
            if (kind is "select-opponent" or "reveal-partner-card" or "exchange")
            {
                var bidder = 0;
                while (game.CurrentSeat != bidder) game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
                game.ExecuteCampaignAction(bidder, kind == "exchange" ? new CampaignCommand.Bid(BidLevel.PartnersBest)
                    : new CampaignCommand.Bid(kind == "reveal-partner-card" ? BidLevel.Five : BidLevel.Four, kind == "reveal-partner-card" ? GumballId.Telepath : GumballId.Duelist));
                while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CurrentSeat!.Value, new CampaignCommand.Pass());
                if (kind == "exchange") game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades));
            }
            Assert(game.CampaignActor == (kind == "reveal-partner-card" ? 2 : 0), "Fixture pending actor wrong for " + kind);
            f.Save(run.StartMatch().WithCheckpoint(new(game.ExportCampaignMatchSnapshot())));
            var state = await Change(f.Service, f.Token, await State(f.Service, f.Token), "continue"); var table = state.GetProperty("table");
            if (kind == "reveal-partner-card")
            {
                Assert(table.GetProperty("legalCommands").GetArrayLength() == 0, "Partner private choices leaked to human.");
                for (var n = 0; n < 50 && table.GetProperty("pendingChoice").ValueKind != JsonValueKind.Null; n++)
                { await Task.Delay(100); state = await State(f.Service, f.Token); table = state.GetProperty("table"); }
                Assert(table.GetProperty("pendingChoice").ValueKind == JsonValueKind.Null && table.GetProperty("players")[2].GetProperty("visibleCards").GetArrayLength() == 1, "Fresh partner bot did not resolve Telepath reveal.");
                continue;
            }
            var command = table.GetProperty("legalCommands").EnumerateArray().First(c => c.GetProperty("kind").GetString() == kind);
            var next = await Change(f.Service, f.Token, state, "commands", new { expectedRevision = table.GetProperty("revision").GetInt64(), expectedDealSerial = table.GetProperty("dealSerial").GetInt64(), command });
            Assert(Version(next) != Version(state), "Pending choice did not checkpoint: " + kind);
            Assert(CampaignStore.LoadSave(f.SavePath).Status == CampaignLoadStatus.Loaded, "Pending choice save invalid: " + kind);
        }
        Console.WriteLine("PASS  Human pending OneUp/Cheater/Duelist/Telepath and Partners Best exchange restore and execute through typed wire commands");
    }

    private static async Task LifecycleRaces()
    {
        var bad = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["WebCampaigns:DataDirectory"] = Path.Combine(AppContext.BaseDirectory, "private-saves") }).Build();
        try { await using var service = new WebCampaigns(new EngineCatalog(), bad, NullLogger<WebCampaigns>.Instance); throw new Exception("Application output subtree accepted as campaign root."); }
        catch (ArgumentException) { }
        await using var f = new Fixture(4, 1);
        var run = new CampaignRun(); var engine = new GameEngine(["You", "Opponent 1", "Partner", "Opponent 3"], 7, run.NextMatch(), new CampaignContext(run.Resources)); engine.StartGame(3);
        f.Save(run.StartMatch().WithCheckpoint(new(engine.ExportCampaignMatchSnapshot())));
        var state = await Change(f.Service, f.Token, await State(f.Service, f.Token), "continue");
        var table = state.GetProperty("table");
        var fields = new { expectedRevision = table.GetProperty("revision").GetInt64(), expectedDealSerial = table.GetProperty("dealSerial").GetInt64(),
            orderedCardIds = table.GetProperty("players")[0].GetProperty("visibleCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).Reverse().ToArray() };
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { try { await Change(f.Service, f.Token, state, "arrange-hand", fields); return "accepted"; } catch (CampaignApiException e) { return e.Code; } }));
        Assert(outcomes.Count(o => o == "accepted") == 1 && outcomes.Count(o => o == "stale_state") == 7, "Same-revision concurrent arrange must accept once.");
        await Task.Delay(1100); await f.Service.ExpireAsync();
        var dormant = await State(f.Service, f.Token); Assert(dormant.GetProperty("requiresContinue").GetBoolean(), "Live idle session was not evicted.");
        state = await Change(f.Service, f.Token, dormant, "continue"); Assert(state.GetProperty("run").GetProperty("lives").GetInt32() == 3, "Live expiry cost a life.");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async n => { if (n % 2 == 0) await State(f.Service, f.Token); else await f.Service.ExpireAsync(); }));
        await f.Service.DisposeAsync();
        await Reject(() => f.Service.GetAsync(f.Token), "unavailable");
        Console.WriteLine("PASS  Concurrent live same-revision edits accept once; leased reads and live expiry preserve a resumable match");
    }

}
