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
        Commands(); await Lifecycle(); await PokerReveal(); await Shop(); await Booster(); await Arcade(); await ArcadeSaveFailure(); await Tickets(); await LegacyShop(); await LegacyCommittedArcadeUpgrade(); await RetiredGumballShop(); await CorruptAndDirty(); await CapacityAndExpiry(); await Terminal(); await PendingChoices(); await LifecycleRaces();
    }
    private static void Commands()
    {
        CampaignCommand[] commands = [new CampaignCommand.Bid(BidLevel.Four, GumballId.Chaos), new CampaignCommand.Pass(),
            new CampaignCommand.Play(new(3), Suit.Hearts), new CampaignCommand.Exchange(new(4)), new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades),
            new CampaignCommand.UseTarot(2, [new(1), new(4), new(5)]), new CampaignCommand.UseSigil(4, new(7)), new CampaignCommand.SwapPartnerCard(new(8)),
            new CampaignCommand.ChooseRevealSeat(1), new CampaignCommand.RevealPartnerCard(new(10)), new CampaignCommand.SelectOpponent(3), new CampaignCommand.CompletePoker()];
        foreach (var command in commands.Concat(Enum.GetValues<GumballId>().Select(id => new CampaignCommand.Bid(BidLevel.Four, id))))
        {
            var wire = Element(CampaignWire.Command(command)); var parsed = CampaignWire.ParseCommand(wire);
            Assert(wire.GetRawText() == Element(CampaignWire.Command(parsed)).GetRawText(), "Command union round trip lost a field.");
        }
        foreach (var invalid in new[] { "{}", "null", "[]", "{\"kind\":\"wat\"}", "{\"kind\":\"panic-button\"}", "{\"kind\":\"bid\",\"level\":\"Four\",\"variant\":\"PanicButton\"}", "{\"kind\":\"pass\",\"seat\":0}",
            "{\"kind\":\"play\",\"cardId\":24}", "{\"kind\":\"bid\",\"level\":4}", "{\"kind\":\"bid\",\"level\":\"99\"}",
            "{\"kind\":\"use-tarot\",\"slot\":0,\"targetIds\":[1,1]}", "{\"kind\":\"use-sigil\",\"slot\":5,\"targetId\":0}",
            "{\"kind\":\"choose-contract\",\"mode\":\"High\",\"trump\":\"Hearts\"}", "{\"kind\":\"choose-contract\",\"mode\":\"Trump\"}",
            "{\"kind\":\"select-opponent\",\"targetSeat\":-1}", "{\"kind\":\"play\",\"cardId\":1,\"effectiveSuit\":\"clubs\"}" })
        {
            try { CampaignWire.ParseCommand(JsonDocument.Parse(invalid).RootElement); throw new Exception("Accepted malformed command: " + invalid); }
            catch (CampaignApiException e) { Assert(e.Code == "invalid_request", "Wrong malformed command diagnostic."); }
        }
        Console.WriteLine("PASS  Campaign wire covers all command variants and rejects malformed discriminators, fields, IDs and enum payloads");
    }
    private static async Task PokerReveal()
    {
        await using var f = new Fixture();
        var run = new CampaignRun { Resources = CampaignSnapshot.Create(45) with { Inventory = new([], [], [GumballId.Poker]) } };
        var game = new GameEngine(["You", "Opponent 1", "Partner", "Opponent 3"], 7, run.NextMatch(), new CampaignContext(run.Resources));
        game.StartGame(3);
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        for (var seat = 1; seat < 4; seat++) game.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        f.Save(run.StartMatch().WithCheckpoint(new(game.ExportCampaignMatchSnapshot())));

        var dormant = await State(f.Service, f.Token);
        Assert(dormant.GetProperty("requiresContinue").GetBoolean(), "Poker must resume from a saved reveal.");
        var resumed = await Change(f.Service, f.Token, dormant, "continue");
        var table = resumed.GetProperty("table");
        Assert(table.GetProperty("phase").GetString() == "PokerReveal" && table.GetProperty("contract").ValueKind == JsonValueKind.Null,
            "The resumed Poker reveal must have no trick contract.");
        Assert(table.GetProperty("players").EnumerateArray().All(p => p.GetProperty("visibleCards").GetArrayLength() == 6), "All four Poker hands must be visible.");
        var capabilities = resumed.GetProperty("capabilities");
        Assert(!capabilities.GetProperty("items-use").GetProperty("enabled").GetBoolean() && !capabilities.GetProperty("arrange-hand").GetProperty("enabled").GetBoolean(),
            "The reveal must disable item use and arrangement.");
        var poker = table.GetProperty("pokerResult");
        Assert(poker.GetProperty("teamPoints").EnumerateArray().Any(p => p.GetInt32() == 4), "The Poker award must be visible.");
        Assert(poker.GetProperty("winners").GetArrayLength() > 0, "At least one Poker winner must be exposed.");
        foreach (var winner in poker.GetProperty("winners").EnumerateArray())
        {
            var handIds = table.GetProperty("players")[winner.GetProperty("seat").GetInt32()].GetProperty("visibleCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToHashSet();
            var winningIds = winner.GetProperty("cardIds").EnumerateArray().Select(c => c.GetInt32()).ToArray();
            Assert(winner.GetProperty("category").GetString() is { Length: > 0 } && winningIds.Distinct().Count() == 5 && winningIds.All(handIds.Contains),
                "The winning category and five physical IDs must refer to that player's revealed hand.");
        }
        var commands = table.GetProperty("legalCommands");
        Assert(commands.GetArrayLength() == 1 && commands[0].GetProperty("kind").GetString() == "complete-poker", "Poker must only offer the reveal acknowledgement.");
        var revision = table.GetProperty("revision").GetInt64();
        var dealSerial = table.GetProperty("dealSerial").GetInt64();
        var fields = new { expectedRevision = revision, expectedDealSerial = dealSerial, command = commands[0] };
        await Reject(() => Change(f.Service, f.Token, resumed, "commands", fields with { expectedDealSerial = dealSerial + 1 }), "stale_state");
        var advanced = await Change(f.Service, f.Token, resumed, "commands", fields);
        var next = advanced.GetProperty("table");
        Assert(next.GetProperty("phase").GetString() == "Bidding" && next.GetProperty("dealSerial").GetInt64() == dealSerial + 1
            && next.GetProperty("scores").GetRawText() == table.GetProperty("scores").GetRawText(), "Acknowledging the reveal must deal once and leave the award intact.");
        await Reject(() => Change(f.Service, f.Token, resumed, "commands", fields), "stale_state");
        Console.WriteLine("PASS  Poker wire exposes the saved showdown and advances exactly one guarded deal");
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
        var shop = await Change(f.Service, f.Token, savedResult, "result/continue"); Assert(shop.GetProperty("shop").GetProperty("offers").GetArrayLength() == 4, "Result did not open stock.");
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
        Assert(!state.GetProperty("capabilities").GetProperty("items-use").GetProperty("enabled").GetBoolean(), "Shop enabled held item use.");
        await RejectUnchanged(f, state, "items/use", new { category = "Tarot", slot = 0, targetIds = new[] { 0 } });
        await RejectUnchanged(f, state, "items/use", new { category = "Sigil", slot = 0, targetIds = new[] { 0 } });
        state = await Change(f.Service, f.Token, state, "items/discard", new { category = "Sigil", slot = 0 });
        try { await Change(f.Service, f.Token, state, "items/use", new { category = "Sigil", slot = 0, targetIds = new[] { 0 } }); throw new Exception("Empty slot accepted."); }
        catch (GameRuleException) { }
        state = await Change(f.Service, f.Token, state, "shop/sell", new { jokerId = "Alpha" });
        Assert(state.GetProperty("inventory").GetProperty("jokers").GetArrayLength() == 0 && state.GetProperty("run").GetProperty("money").GetInt32() == 97, "Sale wrong.");
        Assert(state.GetProperty("shop").GetProperty("offers").EnumerateArray().All(o => o.GetProperty("category").GetString() != "Sigil"), "Arcade must replace direct Sigil stock.");
        Assert(state.GetProperty("shop").GetProperty("arcade").GetProperty("price").GetInt32() == 12, "Arcade entry price missing.");
        Assert(state.GetProperty("catalog").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "sigil:vulture").GetProperty("collectorTitle").GetString() == "Resourceful", "Sigil catalog entries need their collector title.");
        var vending = state.GetProperty("shop").GetProperty("vending");
        Assert(vending.GetProperty("price").GetInt32() == 8 && vending.GetProperty("buy").GetProperty("enabled").GetBoolean(), "The shop must offer a soda for $8.");
        state = await Change(f.Service, f.Token, state, "shop/soda");
        var soda = state.GetProperty("inventory").GetProperty("sodas")[0].GetString();
        Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 89, "A soda sale must charge once.");
        await RejectUnchanged(f, state, "shop/soda", new { });
        await f.Restart();
        state = await State(f.Service, f.Token);
        Assert(state.GetProperty("inventory").GetProperty("sodas")[0].GetString() == soda && state.GetProperty("shop").GetProperty("vending").GetProperty("sold").GetBoolean(), "Vending stock and ownership must survive a restart.");
        state = await Change(f.Service, f.Token, state, "shop/leave");
        Assert(!state.GetProperty("capabilities").GetProperty("items-use").GetProperty("enabled").GetBoolean(), "Intro enabled held item use.");
        await RejectUnchanged(f, state, "items/use", new { category = "Tarot", slot = 0, targetIds = new[] { 0 } });
        Console.WriteLine("PASS  Shop sample projection, off-sample atomic rejection, held item phase restrictions, and authoritative purchases");
    }
    private static async Task Booster()
    {
        foreach (var skip in new[] { false, true })
        {
            await using var fixture = new Fixture();
            JokerId[] owned = [JokerId.Alpha, JokerId.SlickWilly, JokerId.Cheater, JokerId.HighRoller, JokerId.Medusa];
            var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 20, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(9) with { Inventory = new(owned.Select(id => new JokerState(id)).ToArray(), [], []) { Sodas = skip ? [SodaId.PackAPunch, SodaId.PeachPlus] : [] } } });
            fixture.Save(run);
            var before = await State(fixture.Service, fixture.Token);
            JsonElement Pack(JsonElement state) => state.GetProperty("shop").GetProperty(skip ? "bonusBooster" : "booster");
            Assert(Pack(before).GetProperty("price").GetInt32() == 6 && Pack(before).GetProperty("buy").GetProperty("enabled").GetBoolean(), "Stocked pack should be purchasable for $6.");
            var paid = skip ? await Change(fixture.Service, fixture.Token, before, "shop/booster/open", new { packIndex = 1 })
                : await Change(fixture.Service, fixture.Token, before, "shop/booster/open");
            if (skip) Assert(!paid.GetProperty("shop").GetProperty("booster").GetProperty("sold").GetBoolean(), "Buying the bonus pack leaves the first on the shelf.");
            await Reject(() => Change(fixture.Service, fixture.Token, before, "shop/booster/open"), "stale_state");
            Assert(Pack(paid).GetProperty("awaitingOpen").GetBoolean() && Pack(paid).GetProperty("choices").ValueKind == JsonValueKind.Null, "Hide the contents of the paid sealed pack.");
            await RejectUnchanged(fixture, paid, "shop/booster/skip", new { });
            await fixture.Restart();
            var sealedState = await State(fixture.Service, fixture.Token);
            Assert(Pack(sealedState).GetRawText() == Pack(paid).GetRawText(), "Restore the sealed booster.");
            paid = await Change(fixture.Service, fixture.Token, sealedState, "shop/booster/tear");
            await RejectUnchanged(fixture, paid, "shop/booster/tear", new { });
            var choices = Pack(paid).GetProperty("choices");
            var ids = choices.EnumerateArray().Select(id => id.GetString()).ToArray();
            Assert(ids.Length == (skip ? 5 : 4) && ids.Distinct().Count() == ids.Length && !ids.Intersect(owned.Select(id => id.ToString())).Any(), "Offer distinct unowned Jokers with the additional Peach Plus choice.");
            Assert(!paid.GetProperty("capabilities").GetProperty("shop-leave").GetProperty("enabled").GetBoolean(), "Keep paid choices open.");
            await RejectUnchanged(fixture, paid, "shop/booster/pick", new { jokerId = "Alpha" });
            await fixture.Restart();
            var resumed = await State(fixture.Service, fixture.Token);
            Assert(Pack(resumed).GetProperty("choices").GetRawText() == choices.GetRawText(), "Restart must restore the same choices.");
            Assert(!Pack(resumed).GetProperty("pick").GetProperty("enabled").GetBoolean(), "Full inventory disables keeping without disabling skip.");
            await RejectUnchanged(fixture, resumed, "shop/booster/pick", new { jokerId = ids[0] });
            Assert(resumed.GetProperty("inventory").GetProperty("jokers").EnumerateArray().All(j => j.GetProperty("sell").GetProperty("enabled").GetBoolean()), "Full inventory can sell any owned Joker.");
            if (!skip)
            {
                resumed = await Change(fixture.Service, fixture.Token, resumed, "shop/sell", new { jokerId = "Alpha" });
                await fixture.Restart();
                resumed = await State(fixture.Service, fixture.Token);
                Assert(Pack(resumed).GetProperty("choices").GetRawText() == choices.GetRawText() && Pack(resumed).GetProperty("pick").GetProperty("enabled").GetBoolean(), "Sale leaves the same four purchasable choices after restart.");
                Assert(!resumed.GetProperty("capabilities").GetProperty("shop-sell").GetProperty("enabled").GetBoolean(), "Only sell to free one slot.");
            }
            var done = skip ? await Change(fixture.Service, fixture.Token, resumed, "shop/booster/skip")
                : await Change(fixture.Service, fixture.Token, resumed, "shop/booster/pick", new { jokerId = ids[0] });
            var jokers = done.GetProperty("inventory").GetProperty("jokers").EnumerateArray().Select(j => j.GetProperty("id").GetString()).ToArray();
            var expected = owned.Select(id => id.ToString());
            Assert(done.GetProperty("run").GetProperty("money").GetInt32() == (skip ? 14 : 16) && jokers.SequenceEqual(skip ? expected : expected.Skip(1).Append(ids[0]!)), "Pick or skip must settle one paid pack.");
            Assert(Pack(done).GetProperty("sold").GetBoolean() && Pack(done).GetProperty("choices").ValueKind == JsonValueKind.Null, "Resolved booster stays sold.");
            var rerolled = await Change(fixture.Service, fixture.Token, done, "shop/reroll");
            Assert(Pack(rerolled).GetRawText() == Pack(done).GetRawText(), "Reroll keeps the same sold booster.");
        }
        Console.WriteLine("PASS  Web Joker Boosters reveal once, resume, and resolve by pick or skip");
    }
    private static async Task Arcade()
    {
        foreach (var game in new[] { "plinko", "roulette" })
        foreach (var score in game == "plinko" ? new[] { 1, 3, 5 } : new[] { 0, 50, 100 })
        {
            await using var f = new Fixture();
            CampaignRun? chosen = null;
            for (ulong seed = 1; seed < 1000; seed++)
            {
                var candidate = CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop)
                { Resources = CampaignSnapshot.Create(seed) with { Inventory = new([], [TarotId.Gambler, TarotId.TaxCollector, TarotId.Astronomer], []) { Sigils = [SigilId.Bear] } } });
                if (!CampaignArcade.Available(candidate)) continue;
                var result = game == "plinko" ? CampaignArcade.PlayPlinko(candidate, 4) : CampaignArcade.PlayRoulette(candidate, 1);
                if (result.Shop!.Arcade!.Pending!.Score == score) { chosen = candidate; break; }
            }
            Assert(chosen is not null, "Missing deterministic Arcade fixture."); f.Save(chosen!);
            var state = await State(f.Service, f.Token); var before = state;
            object selection = game == "plinko" ? new { lane = 4 } : new { pocket = 1 };
            await Reject(() => Change(f.Service, f.Token, state, "shop/arcade/" + game, new { lane = 4, score = 100 }), "invalid_request");
            state = await Change(f.Service, f.Token, state, "shop/arcade/" + game, selection);
            var resultView = state.GetProperty("shop").GetProperty("arcade").GetProperty("pending");
            var playId = resultView.GetProperty("playId").GetString();
            Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 88 && resultView.GetProperty("score").GetInt32() == score, "Arcade price or score wrong.");
            Assert(!state.GetProperty("capabilities").GetProperty("shop-leave").GetProperty("enabled").GetBoolean(), "Pending result must block leaving.");
            Assert(!state.GetProperty("shop").GetProperty("reroll").GetProperty("enabled").GetBoolean(), "Pending result must block rerolls.");
            await Reject(() => Change(f.Service, f.Token, before, "shop/arcade/" + game, selection), "stale_state");
            await RejectUnchanged(f, state, "shop/arcade/" + game, selection);
            await RejectUnchanged(f, state, "shop/leave", new { });
            await RejectUnchanged(f, state, "shop/reroll", new { });
            await f.Restart(); state = await State(f.Service, f.Token);
            Assert(state.GetProperty("shop").GetProperty("arcade").GetProperty("pending").GetRawText() == resultView.GetRawText(), "Restart rerolled a paid Arcade result.");
            if (game == "plinko" ? score <= 2 : score == 0) state = await Change(f.Service, f.Token, state, "shop/arcade/dismiss", new { playId });
            else
            {
                await RejectUnchanged(f, state, "shop/arcade/keep", new { playId });
                await RejectUnchanged(f, state, "shop/arcade/dismiss", new { playId });
                if (resultView.GetProperty("awaitingOpen").GetBoolean())
                {
                    Assert(resultView.GetProperty("pack").ValueKind == JsonValueKind.Null && !resultView.GetProperty("pick").GetProperty("enabled").GetBoolean(), "Sealed Spectral Packs conceal their contents.");
                    await RejectUnchanged(f, state, "shop/arcade/pick", new { playId, index = 0 });
                    state = await Change(f.Service, f.Token, state, "shop/arcade/tear", new { playId });
                    resultView = state.GetProperty("shop").GetProperty("arcade").GetProperty("pending");
                    await RejectUnchanged(f, state, "shop/arcade/tear", new { playId });
                    await f.Restart(); state = await State(f.Service, f.Token);
                    Assert(state.GetProperty("shop").GetProperty("arcade").GetProperty("pending").GetRawText() == resultView.GetRawText(), "Restore the opened Spectral Pack.");
                    var pack = resultView.GetProperty("pack").EnumerateArray().ToArray();
                    Assert(resultView.GetProperty("packWrapper").GetString() == "curated-collector-pack", "Arcade jackpots use the Spectral Pack wrapper.");
                    Assert(pack.Length == 3 && pack.All(c => c.GetProperty("title").GetString() is { Length: > 0 }), "A Sigil prize must show a three-card pack with titles.");
                    Assert(!resultView.GetProperty("keep").GetProperty("enabled").GetBoolean() && !resultView.GetProperty("use").GetProperty("enabled").GetBoolean()
                        && resultView.GetProperty("pick").GetProperty("enabled").GetBoolean(), "A pack can only be opened, never kept.");
                    await RejectUnchanged(f, state, "shop/arcade/use", new { playId });
                    var index = Array.FindIndex(pack, c => !c.GetProperty("legendary").GetBoolean());
                    await Reject(() => Change(f.Service, f.Token, state, "shop/arcade/pick", new { playId, index, featureId = "Fish" }), "invalid_request");
                    await Reject(() => Change(f.Service, f.Token, state, "shop/arcade/pick", new { playId, index = 3 }), "invalid_request");
                    state = await Change(f.Service, f.Token, state, "shop/arcade/pick", new { playId, index });
                    Assert(state.GetProperty("shop").GetProperty("useSession").GetProperty("featureId").GetString() == pack[index].GetProperty("key").GetString(), "Picked card must start its use session.");
                    await RejectUnchanged(f, state, "shop/arcade/pick", new { playId, index });
                }
                else
                {
                    Assert(!resultView.GetProperty("keep").GetProperty("enabled").GetBoolean() && resultView.GetProperty("use").GetProperty("enabled").GetBoolean(), "Full storage must still allow Use Now.");
                    Assert(!resultView.GetProperty("pick").GetProperty("enabled").GetBoolean(), "A Tarot prize has no pack to open.");
                    state = await Change(f.Service, f.Token, state, "shop/arcade/use", new { playId });
                }
                Assert(state.GetProperty("shop").GetProperty("offers").GetArrayLength() == 4, "Hidden prize leaked onto the shop shelf.");
                var session = state.GetProperty("shop").GetProperty("useSession");
                var ids = session.GetProperty("targetCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
                Assert(ids.Length == 6 && ids.Distinct().Count() == 6, "Arcade prize lost its six-card sample.");
                await RejectUnchanged(f, state, "shop/finish-use", new { });
                state = await Change(f.Service, f.Token, state, "items/use", new { category = "Tarot", slot = 0, targetIds = Array.Empty<int>() });
                Assert(!state.GetProperty("shop").GetProperty("useSession").GetProperty("purchasedItemUsed").GetBoolean(), "Held Gambler consumed the mandatory prize.");
                state = await Change(f.Service, f.Token, state, "shop/use-purchased", new { targetIds = ids.Take(session.GetProperty("targetCount").GetInt32()).ToArray() });
                state = await Change(f.Service, f.Token, state, "shop/finish-use");
            }
            await RejectUnchanged(f, state, "shop/arcade/" + game, selection);
            state = await Change(f.Service, f.Token, state, "shop/reroll");
            await RejectUnchanged(f, state, "shop/arcade/" + game, selection);
        }
        Console.WriteLine("PASS  Arcade price/limits, three prize tiers, strict versioned routes, durable outcomes, full storage Use Now and held Gambler in mandatory six-card sessions");
    }
    private static async Task ArcadeSaveFailure()
    {
        await using var f = new Fixture();
        CampaignRun? run = null;
        for (ulong seed = 1; seed < 1000; seed++)
        {
            var candidate = CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop)
            { Resources = CampaignSnapshot.Create(seed) with { Inventory = new([], [TarotId.Gambler], []) } });
            if (CampaignArcade.Available(candidate) && CampaignArcade.PlayPlinko(candidate, 4).Shop!.Arcade!.Pending!.PrizeCategory == FeatureCategory.Sigil) { run = candidate; break; }
        }
        Assert(run is not null, "Missing durable Arcade fixture."); f.Save(run!);
        var state = await State(f.Service, f.Token);
        async Task FailedWrite(string action, object fields)
        {
            state = await State(f.Service, f.Token); var before = state.GetRawText();
            var backup = f.SavePath + ".blocked-write-backup"; File.Move(f.SavePath, backup); Directory.CreateDirectory(f.SavePath);
            try
            {
                await Reject(() => Change(f.Service, f.Token, state, action, fields), "save_unavailable");
                Assert((await State(f.Service, f.Token)).GetRawText() == before, "A failed Arcade save exposed a roll, payment, prize, inventory change or revised state: " + action);
            }
            finally { Directory.Delete(f.SavePath); File.Move(backup, f.SavePath); }
            await f.Restart();
            Assert((await State(f.Service, f.Token)).GetRawText() == before, "Restart changed a rejected Arcade action: " + action);
        }
        await FailedWrite("shop/arcade/plinko", new { lane = 4 });
        state = await Change(f.Service, f.Token, state, "shop/arcade/plinko", new { lane = 4 });
        Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 88, "Retry charged more than once.");
        var prize = state.GetProperty("shop").GetProperty("arcade").GetProperty("pending"); var playId = prize.GetProperty("playId").GetString();
        await FailedWrite("items/discard", new { category = "Tarot", slot = 0 });
        await FailedWrite("shop/arcade/tear", new { playId });
        state = await Change(f.Service, f.Token, state, "shop/arcade/tear", new { playId });
        prize = state.GetProperty("shop").GetProperty("arcade").GetProperty("pending");
        var sigilIndex = Array.FindIndex(prize.GetProperty("pack").EnumerateArray().ToArray(), c => !c.GetProperty("legendary").GetBoolean());
        await FailedWrite("shop/arcade/pick", new { playId, index = sigilIndex });
        state = await Change(f.Service, f.Token, state, "shop/arcade/pick", new { playId, index = sigilIndex });
        var ids = state.GetProperty("shop").GetProperty("useSession").GetProperty("targetCards").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
        await FailedWrite("items/use", new { category = "Tarot", slot = 0, targetIds = Array.Empty<int>() });
        state = await Change(f.Service, f.Token, state, "items/use", new { category = "Tarot", slot = 0, targetIds = Array.Empty<int>() });
        await FailedWrite("shop/use-purchased", new { targetIds = ids.Take(1).ToArray() });
        state = await Change(f.Service, f.Token, state, "shop/use-purchased", new { targetIds = ids.Take(1).ToArray() });
        await FailedWrite("shop/finish-use", new { });
        state = await Change(f.Service, f.Token, state, "shop/finish-use");
        Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 88 && state.GetProperty("inventory").GetProperty("tarot").GetArrayLength() == 0, "Durable retries changed charges or duplicated held Tarot use.");
        Console.WriteLine("PASS  Failed Arcade writes reveal no outcome, charge, grant or consumption; same-version retries commit once and restart preserves every step");
    }
    private static async Task RetiredGumballShop()
    {
        await using var f = new Fixture();
        var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 30, Phase: CampaignPhase.Shop));
        var machine = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Gumball);
        f.Save(run with { Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == machine.Id ? o with { Sold = true, FeatureId = "" } : o).ToArray() } });
        var state = await State(f.Service, f.Token);
        var catalog = state.GetProperty("catalog").EnumerateArray().ToArray();
        var gumballs = catalog.Where(f => f.GetProperty("category").GetString() == "Gumball")
            .Select(f => f.GetProperty("key").GetString()).ToHashSet();
        Assert(gumballs.SetEquals(Enum.GetNames<GumballId>()), "The catalogue must project exactly the active gumballs.");
        Assert(catalog.All(f => f.GetProperty("key").GetString() != "PanicButton"), "The retired bid must not appear in the catalogue.");
        void Sold(JsonElement current)
        {
            var offer = current.GetProperty("shop").GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("category").GetString() == "Gumball");
            Assert(offer.GetProperty("sold").GetBoolean() && offer.GetProperty("featureId").GetString() == "", "Retired purchase identity must remain sold without exposing the removed feature.");
            Assert(!offer.GetProperty("buy").GetProperty("enabled").GetBoolean(), "A retired purchase must not unlock a free pull.");
        }
        Sold(state);
        await RejectUnchanged(f, state, "shop/pull", new { offerId = machine.Id });
        state = await Change(f.Service, f.Token, state, "shop/reroll");
        Sold(state);
        await f.Restart();
        Sold(await State(f.Service, f.Token));
        Console.WriteLine("PASS  Retired gumball stock stays sold across projection, reroll and restart while active catalogue excludes the removed bid");
    }

    private static async Task RejectUnchanged(Fixture fixture, JsonElement state, string action, object fields)
    {
        var persisted = File.ReadAllText(fixture.SavePath);
        try { await Change(fixture.Service, fixture.Token, state, action, fields); throw new Exception("Invalid item action accepted: " + action); }
        catch (GameRuleException) { }
        Assert(Version(await State(fixture.Service, fixture.Token)) == Version(state), "Rejected action advanced its version: " + action);
        Assert(File.ReadAllText(fixture.SavePath) == persisted, "Rejected action changed save, random state, money or revision: " + action);
    }
    private static async Task Tickets()
    {
        await using var f = new Fixture();
        var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop) { Resources = CampaignSnapshot.Create(12) });
        f.Save(run);
        var state = await State(f.Service, f.Token);
        JsonElement Teller(JsonElement value) => value.GetProperty("shop").GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("category").GetString() == "Tarot");
        var offerId = Teller(state).GetProperty("id").GetString();
        Assert(Teller(state).GetProperty("targetCards").GetArrayLength() == 0, "Unpaid ticket leaked a sample.");
        state = await Change(f.Service, f.Token, state, "shop/print", new { offerId, count = 3 });
        Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 94, "Print charged wrong amount.");
        var offer = Teller(state); var sample = offer.GetProperty("targetCards").Clone();
        Assert(sample.GetArrayLength() == 6 && offer.GetProperty("tickets").GetArrayLength() == 3, "Printed spread missing tickets or six-card sample.");
        Assert(!state.GetProperty("capabilities").GetProperty("shop-leave").GetProperty("enabled").GetBoolean() && !state.GetProperty("shop").GetProperty("reroll").GetProperty("enabled").GetBoolean(), "Unresolved paid tickets must block leaving or rerolling.");
        await Reject(() => Change(f.Service, f.Token, state, "shop/keep-ticket", new { offerId, ticketId = "99" }), "invalid_request");
        await RejectUnchanged(f, state, "shop/print", new { offerId, count = 1 });
        await f.Restart(); state = await State(f.Service, f.Token);
        Assert(Teller(state).GetProperty("targetCards").GetRawText() == sample.GetRawText(), "Restart changed paid sample.");
        var ticket = Teller(state).GetProperty("tickets").EnumerateArray().First(t => t.GetProperty("targetCount").GetInt32() > 0);
        var count = ticket.GetProperty("targetCount").GetInt32(); var ids = sample.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
        var valid = ids.Take(count).ToArray(); var invalid = valid.ToArray(); invalid[0] = Enumerable.Range(0, 24).First(id => !ids.Contains(id));
        await RejectUnchanged(f, state, "shop/use-ticket", new { offerId, ticketId = ticket.GetProperty("id").GetString(), targetIds = invalid });
        var printedState = state;
        state = await Change(f.Service, f.Token, state, "shop/use-ticket", new { offerId, ticketId = ticket.GetProperty("id").GetString(), targetIds = valid });
        Assert(Teller(state).GetProperty("sold").GetBoolean() && Teller(state).GetProperty("tickets").GetArrayLength() == 0, "Used ticket did not resolve the spread.");
        await Reject(() => Change(f.Service, f.Token, printedState, "shop/use-ticket", new { offerId, ticketId = ticket.GetProperty("id").GetString(), targetIds = valid }), "stale_state");
        state = await Change(f.Service, f.Token, state, "shop/reroll"); offerId = Teller(state).GetProperty("id").GetString();
        state = await Change(f.Service, f.Token, state, "shop/print", new { offerId, count = 1 });
        var kept = Teller(state).GetProperty("tickets")[0].GetProperty("id").GetString();
        state = await Change(f.Service, f.Token, state, "shop/keep-ticket", new { offerId, ticketId = kept });
        Assert(state.GetProperty("inventory").GetProperty("tarot")[0].GetProperty("id").GetString() == kept, "Keep did not store printed ticket.");
        Console.WriteLine("PASS  Fortune Teller print/use/keep transactions, durable six-card sample, stale gates and atomic target rejection");
    }
    private static async Task LegacyShop()
    {
        await using var f = new Fixture();
        var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop) { Resources = CampaignSnapshot.Create(12) });
        var teller = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
        run = CampaignShop.PrintTickets(run, teller.Id, 3);
        run = run with { Shop = run.Shop! with { Offers = run.Shop.Offers.Select(o => o with { TargetCards = null }).ToArray() } };
        f.Save(run);
        var states = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => State(f.Service, f.Token)));
        Assert(states.All(s => Version(s) == "fixture:6"), "Legacy upgrade must advance the web version exactly once.");
        var state = states[0];
        foreach (var offer in state.GetProperty("shop").GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("category").GetString() is "Sigil" or "Tarot"))
            Assert(offer.GetProperty("targetCards").GetArrayLength() == 6, "Loaded legacy purchase needs six target cards.");
        var saved = CampaignStore.LoadSave(f.SavePath).Save!;
        Assert(saved.Run.Resources.Revision == run.Resources.Revision + 1 && saved.Metadata!.RunId == "fixture" && saved.Metadata.StateRevision == 6, "Legacy upgrade lost metadata or changed the resource revision more than once.");
        Assert(saved.Run.Money == run.Money && saved.Run.Shop!.Offers.Single(o => o.Id == teller.Id).Printed!.SequenceEqual(run.Shop.Offers.Single(o => o.Id == teller.Id).Printed!), "Legacy upgrade changed paid tickets or money.");
        var shop = state.GetProperty("shop").GetRawText();
        await f.Restart(); state = await State(f.Service, f.Token);
        Assert(Version(state) == "fixture:6" && state.GetProperty("shop").GetRawText() == shop, "Legacy shop upgrade was not persisted idempotently.");
        var printed = state.GetProperty("shop").GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("id").GetString() == teller.Id);
        var ticket = printed.GetProperty("tickets").EnumerateArray().First(t => t.GetProperty("targetCount").GetInt32() > 0);
        var targets = printed.GetProperty("targetCards").EnumerateArray().Take(ticket.GetProperty("targetCount").GetInt32()).Select(c => c.GetProperty("id").GetInt32()).ToArray();
        state = await Change(f.Service, f.Token, state, "shop/use-ticket", new { offerId = teller.Id, ticketId = ticket.GetProperty("id").GetString(), targetIds = targets });
        Assert(state.GetProperty("shop").GetProperty("offers").EnumerateArray().All(o => o.GetProperty("category").GetString() != "Sigil"), "Legacy shop should replace unsold Sigils with Arcade access.");
        Console.WriteLine("PASS  Loaded legacy shop upgrades paid-ticket samples once and preserves paid tickets while exposing the Arcade");
    }
    private static async Task LegacyCommittedArcadeUpgrade()
    {
        await using var f = new Fixture();
        var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop)
        { Resources = CampaignSnapshot.Create(12) });
        var targets = run.Resources.Deck.Take(6).Select(c => c.Id).ToArray();
        var committed = new ShopOffer("legacy-paid-sigil", FeatureCategory.Sigil, nameof(SigilId.Bear), 4, true) { TargetCards = targets };
        var unsold = new ShopOffer("legacy-unsold-sigil", FeatureCategory.Sigil, nameof(SigilId.Fish), 4) { TargetCards = targets };
        run = run with { Shop = run.Shop! with
        {
            Offers = [.. run.Shop.Offers, committed, unsold], Arcade = null,
            UseSession = new(committed.Id, committed.Category, committed.FeatureId, false)
        } };
        f.Save(run);
        var state = await State(f.Service, f.Token);
        Assert(state.GetProperty("shop").GetProperty("useSession").GetProperty("offerId").GetString() == committed.Id, "Legacy committed purchase was lost during migration.");
        state = await Change(f.Service, f.Token, state, "shop/use-purchased", new { targetIds = new[] { targets[0].Value } });
        state = await Change(f.Service, f.Token, state, "shop/finish-use");
        Assert(state.GetProperty("shop").GetProperty("offers").EnumerateArray().All(o => o.GetProperty("id").GetString() != unsold.Id), "Legacy unsold Sigil survived the completed session upgrade.");
        Assert(state.GetProperty("shop").GetProperty("arcade").GetProperty("plinko").GetProperty("enabled").GetBoolean(), "Finishing a legacy session failed to open the Arcade.");
        state = await Change(f.Service, f.Token, state, "shop/arcade/plinko", new { lane = 4 });
        Assert(state.GetProperty("run").GetProperty("money").GetInt32() == 88, "Upgraded Arcade failed to accept its first paid play.");
        await f.Restart();
        Assert((await State(f.Service, f.Token)).GetProperty("shop").GetProperty("arcade").GetProperty("pending").ValueKind == JsonValueKind.Object, "Legacy upgraded Arcade outcome did not persist.");
        Console.WriteLine("PASS  Completing a legacy paid six-card session upgrades Arcade access and removes unsold Sigils before accepting plays");
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
