using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Server;

/// <summary>Cookie-private, leased campaign runtimes. Disk saves outlive resident sessions.</summary>
public sealed class WebCampaigns : IAsyncDisposable
{
    private sealed class Entry(string path)
    {
        public readonly string Path = path;
        public readonly SemaphoreSlim Requests = new(1, 1);
        public readonly object State = new();
        public CampaignRun? Run;
        public CampaignSaveMetadata Metadata = new(Guid.NewGuid().ToString("N"), 0);
        public GameSession? Session;
        public bool Loaded, Corrupt, Dirty, Evicting;
        public int Leases, Generation;
        public DateTimeOffset Access = DateTimeOffset.UtcNow;
        public string? Notice;
        public string Version => $"{Metadata.RunId}:{Metadata.StateRevision}";
    }
    private readonly object _registry = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly EngineCatalog _catalog;
    private readonly ILogger<WebCampaigns> _logger;
    private readonly FileStream _ownership;
    private readonly string _root;
    private readonly int _maximum;
    private bool _closing;
    public string CookieName { get; }
    public TimeSpan IdleTimeout { get; }

    public WebCampaigns(EngineCatalog catalog, IConfiguration configuration, ILogger<WebCampaigns> logger)
    {
        _catalog = catalog; _logger = logger;
        _maximum = configuration.GetValue("WebCampaigns:MaxSessions", 32);
        var seconds = configuration.GetValue("WebCampaigns:IdleTimeoutSeconds", 1800);
        if (_maximum is < 1 or > 128 || seconds is < 1 or > 86400) throw new ArgumentException("Invalid web campaign capacity or idle timeout.");
        IdleTimeout = TimeSpan.FromSeconds(seconds);
        CookieName = configuration["WebCampaigns:CookieName"] ?? "bideuchre.campaign";
        if (CookieName.Length is < 1 or > 64 || CookieName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Invalid campaign cookie name.");
        _root = Path.GetFullPath(configuration["WebCampaigns:DataDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BidEuchre", "web-campaigns"));
        if (_root.Split(Path.DirectorySeparatorChar).Any(s => s.Equals("wwwroot", StringComparison.OrdinalIgnoreCase))
            || (_root + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Campaign data must be outside public assets and the application output directory.");
        Directory.CreateDirectory(_root);
        try { _ownership = new FileStream(Path.Combine(_root, ".owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("The campaign data directory is already owned by another server process."); }
    }
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool ValidToken(string? token)
    {
        if (token is not { Length: 43 } || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        // Require canonical base64url; otherwise several tokens could denote the same random bytes.
        try { return Convert.ToBase64String(Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=")).TrimEnd('=').Replace('+', '-').Replace('/', '_') == token; }
        catch (FormatException) { return false; }
    }
    private async Task<Entry> LeaseAsync(string token)
    {
        if (!ValidToken(token)) throw new CampaignApiException(409, "profile_required", "Open campaign first to establish a browser profile.");
        await ExpireAsync();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        lock (_registry)
        {
            if (_closing) throw new CampaignApiException(503, "unavailable", "The server is shutting down.");
            if (!_entries.TryGetValue(key, out var entry))
            {
                if (_entries.Count >= _maximum) throw new CampaignApiException(429, "capacity", "Too many active campaigns. Wait for an idle campaign to expire.");
                entry = new(Path.Combine(_root, key, "campaign.json")); _entries.Add(key, entry);
            }
            if (entry.Evicting) throw new CampaignApiException(429, "capacity", "This campaign is being saved. Refresh shortly.");
            entry.Leases++; entry.Access = DateTimeOffset.UtcNow;
            return entry;
        }
    }
    private void EnsureOpen()
    {
        lock (_registry) if (_closing) throw new CampaignApiException(503, "unavailable", "The server is shutting down.");
    }
    private void Release(Entry entry) { lock (_registry) { entry.Leases--; entry.Access = DateTimeOffset.UtcNow; } }
    private static void Load(Entry entry)
    {
        if (entry.Loaded) return;
        var loaded = CampaignStore.LoadSave(entry.Path);
        entry.Corrupt = loaded.Status == CampaignLoadStatus.Corrupt;
        entry.Run = loaded.Save?.Run;
        entry.Metadata = loaded.Save?.Metadata ?? entry.Metadata;
        entry.Notice = entry.Corrupt ? "This saved campaign could not be read. It has been preserved. Start a new run or delete it to recover."
            : entry.Run?.LegacyMatchRestart == true ? "This older save has no exact hand. Continue restarts this match without losing a life." : null;
        entry.Loaded = true;
        // Older shop saves lack eligible-card samples. Upgrade only an active visit;
        // result and victory continuations still own opening their future shops.
        lock (entry.State)
        {
            if (entry.Run is { Phase: CampaignPhase.Shop, IsOver: false } run && entry.Metadata.LastResult is null)
            {
                var opened = CampaignShop.Open(run);
                if (!ReferenceEquals(opened, run)) Commit(entry, opened);
            }
        }
    }
    private static void Check(Entry entry, string expected)
    { lock (entry.State) if (entry.Version != expected) throw CampaignApiException.Stale(); }
    private static CampaignRun Run(Entry entry) => entry.Run ?? throw new CampaignApiException(404, "no_campaign", "No campaign exists in this browser.");
    private static void Advance(Entry entry) => entry.Metadata = entry.Metadata with { StateRevision = checked(entry.Metadata.StateRevision + 1) };
    private static bool Save(Entry entry)
    {
        if (entry.Run is null) return !entry.Dirty;
        entry.Dirty = !CampaignStore.Save(new CampaignSave(3, entry.Run) { Metadata = entry.Metadata }, entry.Path);
        return !entry.Dirty;
    }
    private static void Commit(Entry entry, CampaignRun run)
    { entry.Run = run; Advance(entry); Save(entry); }
    // Arcade results are revealed only after the complete transaction is durable.
    // A failed write must not expose a roll, grant a prize, or consume a use session.
    private static bool NeedsDurableArcadeCommit(CampaignRun before, string action)
        => action.StartsWith("shop/arcade/", StringComparison.Ordinal) || before.Shop?.Arcade?.Pending is not null
            || before.Shop?.UseSession is { } session && before.Shop.Offers.Any(o => o.Id == session.OfferId && o.IsArcadePrize);
    private static void CommitArcade(Entry entry, CampaignRun candidate)
    {
        var metadata = entry.Metadata with { StateRevision = checked(entry.Metadata.StateRevision + 1) };
        if (!CampaignStore.Save(new CampaignSave(3, candidate) { Metadata = metadata }, entry.Path))
            throw new CampaignApiException(503, "save_unavailable", "Could not save the Arcade action. Nothing was charged or changed. Try again once saving is available.");
        entry.Run = candidate; entry.Metadata = metadata; entry.Dirty = false;
    }
    private static bool Checkpoint(Entry entry, int generation, CampaignCheckpoint checkpoint)
    {
        lock (entry.State)
        {
            if (entry.Generation != generation || entry.Run?.Phase != CampaignPhase.InMatch) return true;
            entry.Run = entry.Run.WithCheckpoint(checkpoint);
            if (checkpoint.Match.Phase == GamePhase.GameComplete)
            {
                var match = checkpoint.Match;
                var (finished, outcome) = entry.Run.ApplyResult(match.GameWinner == 0, match.Scores[0], match.Scores[1]);
                entry.Run = finished;
                entry.Metadata = entry.Metadata with { LastResult = new CampaignSavedResult(outcome, match.Scores.ToArray())
                { Summary = new CampaignResultSummary(match.PlayerNames.ToArray(), match.HandHistory.ToArray(),
                    match.PriorHandEvents.Concat(match.Events).ToArray(), match.CompletionReason) } };
            }
            Advance(entry); return Save(entry);
        }
    }
    private static object Project(Entry entry, GameView? view = null, string? error = null, CampaignFeedback? feedback = null)
    {
        lock (entry.State) return CampaignProjection.State(entry.Run, entry.Version,
            entry.Run?.Phase == CampaignPhase.InMatch && entry.Session is null, entry.Corrupt, entry.Dirty,
            entry.Metadata.LastResult, view, error, entry.Notice, feedback);
    }
    private static Task<object> Read(Entry entry, CampaignFeedback? feedback = null) => entry.Session is { } session
        ? session.ReadCampaignAsync((view, _, error) => Project(entry, view, error, feedback)) : Task.FromResult(Project(entry, feedback: feedback));
    public async Task<object> GetAsync(string token)
    {
        var entry = await LeaseAsync(token);
        try
        {
            await entry.Requests.WaitAsync();
            try { EnsureOpen(); Load(entry); return await Read(entry); }
            finally { entry.Requests.Release(); }
        }
        finally { Release(entry); }
    }
    public async Task<object> MutateAsync(string token, string action, JsonElement body)
    {
        var fields = action switch
        {
            "commands" => new[] { "expectedRevision", "expectedDealSerial", "command" },
            "next-hand" => ["expectedRevision", "expectedDealSerial"],
            "arrange-hand" => ["expectedRevision", "expectedDealSerial", "orderedCardIds"],
            "shop/arcade/plinko" => ["lane"], "shop/arcade/roulette" => ["pocket"],
            "shop/arcade/keep" or "shop/arcade/use" or "shop/arcade/dismiss" => ["playId"],
            "shop/arcade/pick" => ["playId", "index"],
            "shop/begin-sigil-use" => ["offerId"], "shop/begin-ticket-use" => ["offerId", "ticketId"], "shop/use-purchased" => ["targetIds"],
            "shop/buy" or "shop/pull" => ["offerId"], "shop/buy-use" => ["offerId", "targetIds"],
            "shop/print" => ["offerId", "count"], "shop/keep-ticket" => ["offerId", "ticketId"], "shop/use-ticket" => ["offerId", "ticketId", "targetIds"],
            "shop/sell" => ["jokerId"], "items/use" => ["category", "slot", "targetIds"], "items/discard" => ["category", "slot"],
            "new" or "continue" or "delete" or "start-match" or "forfeit" or "shop/finish-use" or "shop/reroll" or "shop/leave" or "result/continue" or "victory/continue" => [],
            _ => throw CampaignApiException.Invalid("Unknown campaign action.")
        };
        CampaignWire.Fields(body, ["expectedVersion", .. fields]);
        var expected = CampaignWire.Text(body, "expectedVersion");
        var entry = await LeaseAsync(token);
        try
        {
            await entry.Requests.WaitAsync();
            try
            {
                EnsureOpen(); Load(entry);
                if (action is "commands" or "next-hand" or "arrange-hand")
                    return await Live(entry, expected, action, body);
                if (action is "new" or "delete" or "forfeit")
                    return await ReplaceOrForfeit(entry, expected, action);
                Check(entry, expected);
                if (entry.Corrupt) throw CampaignApiException.Phase();
                if (action == "continue")
                {
                    var run = Run(entry);
                    if (run.Phase == CampaignPhase.InMatch && entry.Session is null) await Start(entry, false);
                    return await Read(entry);
                }
                if (action == "start-match")
                {
                    if (Run(entry).Phase != CampaignPhase.Intro || Run(entry).IsOver || entry.Metadata.LastResult is not null) throw CampaignApiException.Phase();
                    await StopSession(entry); await Start(entry, true); return await Read(entry);
                }
                CampaignFeedback? feedback;
                lock (entry.State)
                {
                    Check(entry, expected);
                    var before = Run(entry);
                    if (before.Phase == CampaignPhase.InMatch) throw CampaignApiException.Phase();
                    if (action == "result/continue")
                    {
                        if (entry.Metadata.LastResult is null) throw CampaignApiException.Phase();
                        entry.Metadata = entry.Metadata with { LastResult = null };
                        Commit(entry, !before.IsOver && before.Phase == CampaignPhase.Shop ? CampaignShop.Open(before) : before);
                        feedback = null;
                    }
                    else
                    {
                        if (entry.Metadata.LastResult is not null || before.IsOver) throw CampaignApiException.Phase();
                        var after = BetweenMatches(before, action, body);
                        feedback = CampaignProjection.Feedback(before.Resources.Inventory, after.Resources.Inventory, "Campaign updated.");
                        if (NeedsDurableArcadeCommit(before, action)) CommitArcade(entry, after);
                        else Commit(entry, after);
                    }
                }
                if (entry.Run?.Phase != CampaignPhase.InMatch) await StopSession(entry);
                return await Read(entry, feedback);
            }
            finally { entry.Requests.Release(); }
        }
        finally { Release(entry); }
    }
    private static CampaignRun BetweenMatches(CampaignRun run, string action, JsonElement body)
    {
        string Offer() => CampaignWire.Text(body, "offerId");
        CardId[] Targets() => CampaignWire.Cards(body, "targetIds");
        int Slot() => (int)CampaignWire.Number(body, "slot", 0, CampaignInventory.ItemSlots - 1);
        FeatureCategory Category()
        {
            var category = CampaignWire.EnumValue<FeatureCategory>(body, "category");
            return category is FeatureCategory.Tarot or FeatureCategory.Sigil ? category : throw CampaignApiException.Invalid("Use Tarot or Sigil.");
        }
        switch (action)
        {
            case "shop/arcade/plinko": return CampaignArcade.PlayPlinko(run, (int)CampaignWire.Number(body, "lane", 0, CampaignArcade.PlinkoLaneCount - 1));
            case "shop/arcade/roulette": return CampaignArcade.PlayRoulette(run, (int)CampaignWire.Number(body, "pocket", 1, CampaignArcade.RoulettePocketCount));
            case "shop/arcade/keep": return CampaignArcade.KeepArcadePrize(run, CampaignWire.Text(body, "playId"));
            case "shop/arcade/use": return CampaignArcade.BeginArcadePrizeUse(run, CampaignWire.Text(body, "playId"));
            case "shop/arcade/pick": return CampaignArcade.PickPackCard(run, CampaignWire.Text(body, "playId"), (int)CampaignWire.Number(body, "index", 0, CampaignArcade.PackSize - 1));
            case "shop/arcade/dismiss": return CampaignArcade.DismissArcadeLoss(run, CampaignWire.Text(body, "playId"));
            case "shop/begin-sigil-use": return CampaignShop.BeginSigilUse(run, Offer());
            case "shop/begin-ticket-use": return CampaignShop.BeginTicketUse(run, Offer(), CampaignWire.EnumValue<TarotId>(body, "ticketId"));
            case "shop/use-purchased": return CampaignShop.UsePurchased(run, Targets());
            case "shop/finish-use": return CampaignShop.Open(CampaignShop.FinishUse(run));
            case "shop/buy": return CampaignShop.Buy(run, Offer());
            case "shop/pull":
                if (run.Shop?.Offers.SingleOrDefault(o => o.Id == Offer())?.Category != FeatureCategory.Gumball) throw CampaignApiException.Invalid("Choose a gumball machine.");
                return CampaignShop.Buy(run, Offer());
            case "shop/buy-use": return CampaignShop.BuyAndUse(run, Offer(), Targets());
            case "shop/print": return CampaignShop.PrintTickets(run, Offer(), (int)CampaignWire.Number(body, "count", 1, CampaignShop.TicketSpread));
            case "shop/keep-ticket": return CampaignShop.KeepTicket(run, Offer(), CampaignWire.EnumValue<TarotId>(body, "ticketId"));
            case "shop/use-ticket": return CampaignShop.UseTicket(run, Offer(), CampaignWire.EnumValue<TarotId>(body, "ticketId"), Targets());
            case "shop/sell": return CampaignShop.SellJoker(run, CampaignWire.EnumValue<JokerId>(body, "jokerId"));
            case "shop/reroll": return CampaignShop.Reroll(run);
            case "items/use":
                var targets = Targets(); var slot = Slot();
                if (Category() == FeatureCategory.Tarot) return CampaignShop.UseTarot(run, slot, targets);
                if (targets.Length != 1) throw CampaignApiException.Invalid("Choose one card.");
                return CampaignShop.UseSigil(run, slot, targets[0]);
            case "items/discard": return Category() == FeatureCategory.Tarot ? CampaignShop.DiscardTarot(run, Slot()) : CampaignShop.DiscardSigil(run, Slot());
            case "victory/continue":
                if (run.Phase != CampaignPhase.Victory) throw CampaignApiException.Phase();
                return CampaignShop.Open(run.AcknowledgeVictory());
            case "shop/leave":
                if (run.Phase != CampaignPhase.Shop) throw CampaignApiException.Phase();
                return run.LeaveShop();
            default: throw CampaignApiException.Phase();
        }
    }
    private static async Task<object> Live(Entry entry, string expected, string action, JsonElement body)
    {
        var revision = CampaignWire.Number(body, "expectedRevision"); var serial = CampaignWire.Number(body, "expectedDealSerial", 1);
        var command = action == "commands" ? CampaignWire.ParseCommand(CampaignWire.Required(body, "command")) : null;
        var cards = action == "arrange-hand" ? CampaignWire.Cards(body, "orderedCardIds") : null;
        var session = entry.Session ?? throw CampaignApiException.Phase();
        void Validate()
        {
            lock (entry.State)
            {
                Check(entry, expected);
                if (entry.Run?.Phase != CampaignPhase.InMatch || entry.Run.LiveMatch is not { } match) throw CampaignApiException.Phase();
                if (match.Resources.Revision != revision || match.DealSerial != serial) throw CampaignApiException.Stale();
            }
        }
        CampaignInventory before;
        lock (entry.State) before = Run(entry).Resources.Inventory.Copy();
        if (action == "commands") await session.ExecuteCampaignAsync(new(0, revision, serial, command!), precondition: Validate);
        else if (action == "next-hand") await session.StartNextHandAsync(precondition: Validate);
        else await session.ArrangeCampaignHandAsync(0, cards!, precondition: Validate);
        return await session.ReadCampaignAsync((view, _, error) => Project(entry, view, error,
            CampaignProjection.Feedback(before, Run(entry).Resources.Inventory, "Action accepted.")));
    }
    private async Task Start(Entry entry, bool fresh)
    {
        var previous = Run(entry); var previousMetadata = entry.Metadata; var previousDirty = entry.Dirty;
        var candidate = fresh ? previous.StartMatch() : previous;
        var generation = ++entry.Generation;
        var seats = Enumerable.Range(0, 4).Select(i => new SeatConfiguration(i,
            i == 0 ? "You" : i == 2 ? "Partner" : $"Opponent {i}", i == 0 ? PlayerKind.Human : PlayerKind.Bot, null)).ToArray();
        var session = new GameSession(Guid.NewGuid().ToString("N"), "Campaign", seats, _catalog, null,
            matchOptions: candidate.NextMatch(), campaign: new CampaignContext(candidate.Resources),
            liveCheckpoint: checkpoint => Checkpoint(entry, generation, checkpoint));
        entry.Run = candidate; entry.Session = session;
        try
        {
            if (candidate.LiveMatch is { } match) await session.RestoreCampaignAsync(new(match, candidate.Arrangement));
            else await session.StartAsync(waitForDeal: false);
        }
        catch
        {
            ++entry.Generation; await session.DisposeAsync(); entry.Session = null;
            entry.Run = previous; entry.Metadata = previousMetadata; entry.Dirty = previousDirty;
            throw;
        }
    }
    private static async Task StopSession(Entry entry)
    {
        if (entry.Session is not { } session) return;
        ++entry.Generation; entry.Session = null; await session.DisposeAsync();
    }
    private static async Task<object> ReplaceOrForfeit(Entry entry, string expected, string action)
    {
        var session = entry.Session;
        if (session is not null) await session.PauseAsync();
        try
        {
            lock (entry.State)
            {
                Check(entry, expected);
                if (action == "delete")
                {
                    if (!CampaignStore.Delete(entry.Path)) throw new CampaignApiException(409, "save_unavailable", "Could not delete the saved campaign. It is unchanged.");
                    entry.Run = null; entry.Corrupt = entry.Dirty = false; entry.Notice = null;
                    entry.Metadata = new(Guid.NewGuid().ToString("N"), 0);
                }
                else if (action == "new")
                {
                    entry.Corrupt = false; entry.Notice = null; entry.Metadata = new(Guid.NewGuid().ToString("N"), 0);
                    Commit(entry, new CampaignRun());
                }
                else
                {
                    var run = Run(entry);
                    // Dormant terminal saves settle through the same checkpoint path, never as a loss.
                    if (run.LiveMatch is { Phase: GamePhase.GameComplete } terminal)
                        Checkpoint(entry, entry.Generation, new(terminal, run.Arrangement));
                    else if (run.Phase == CampaignPhase.InMatch)
                    {
                        var (next, outcome) = run.Forfeit();
                        entry.Metadata = entry.Metadata with { LastResult = new(outcome, []) }; Commit(entry, next);
                    }
                    else if (entry.Metadata.LastResult is null) throw CampaignApiException.Phase();
                }
            }
            await StopSession(entry); return await Read(entry);
        }
        catch { if (session is not null && entry.Session == session) await session.ResumeAsync(); throw; }
    }
    public async Task ExpireAsync()
    {
        KeyValuePair<string, Entry>[] victims;
        lock (_registry)
        {
            if (_closing) return;
            victims = _entries.Where(p => !p.Value.Evicting && p.Value.Leases == 0 && DateTimeOffset.UtcNow - p.Value.Access >= IdleTimeout).ToArray();
            foreach (var pair in victims) pair.Value.Evicting = true;
        }
        foreach (var pair in victims)
        {
            var entry = pair.Value;
            await entry.Requests.WaitAsync();
            var remove = false;
            try
            {
                if (entry.Session is { } session) await session.PauseAsync();
                lock (entry.State) remove = !entry.Dirty || Save(entry);
                if (remove) await StopSession(entry);
                else if (entry.Session is { } live) await live.ResumeAsync();
            }
            finally
            {
                entry.Requests.Release();
                lock (_registry) { if (remove) _entries.Remove(pair.Key); else entry.Evicting = false; }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_registry) { if (_closing) return; _closing = true; entries = _entries.Values.ToArray(); }
        foreach (var entry in entries)
        {
            await entry.Requests.WaitAsync();
            try
            {
                if (entry.Session is { } session) await session.PauseAsync();
                lock (entry.State) if (entry.Dirty && !Save(entry)) _logger.LogError("A campaign has unsaved progress at shutdown.");
                await StopSession(entry);
            }
            finally { entry.Requests.Release(); }
        }
        _ownership.Dispose();
    }
}

public sealed class WebCampaignReaper(WebCampaigns campaigns) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Min(60, campaigns.IdleTimeout.TotalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await campaigns.ExpireAsync();
    }
}
