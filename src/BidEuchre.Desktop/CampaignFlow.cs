using Avalonia.Controls;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// Moves a campaign run between its screens and keeps the save file in step with it.
internal sealed class CampaignFlow(
    string savePath,
    Action<Control> showScreen,
    Func<MatchOptions, Task> startMatch,
    Action showMenu,
    PixelMotion motion)
{
    private string? _note;
    // Jokers a Legendary pack card just granted, shown once on the next Arcade screen.
    private JokerId[]? _granted;
    private JokerId[]? TakeGranted() { var granted = _granted; _granted = null; return granted; }
    // The ante or shop screen last shown, so a developer change can redraw it.
    private Control? _home;
    private readonly ShopPurse _purse = new();
    private bool _starting;
    private bool _saveFailed;
    public string? ResumeNotice { get; private set; }
    private (MatchOutcome Outcome, int[] Scores, GameView? Summary)? _pendingResult;

    private CancellationTokenSource? _artworkCue;
    private CancellationTokenSource? _artworkRequest;
    private bool _closed;

    private void Present(Control screen)
    {
        if (_closed) return;
        _artworkRequest?.Cancel();
        if (Run is { } run && screen is not CampaignInventoryScreen)
        {
            var framed = CampaignInventoryScreen.Build(screen, run.Resources.Inventory, motion);
            if (ReferenceEquals(screen, _home)) _home = framed;
            screen = framed;
        }
        showScreen(screen);
    }

    private void ReturnToMenu()
    {
        CancelArtwork();
        showMenu();
    }

    public void Close()
    {
        _closed = true;
        CancelArtwork();
    }

    private void CancelArtwork()
    {
        _artworkRequest?.Cancel();
        _artworkCue?.Cancel();
        _artworkCue?.Dispose();
        _artworkCue = null;
    }

    private async Task<bool> PrepareArtworkAsync(IEnumerable<string> artwork)
    {
        if (_closed) return false;
        _artworkRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _artworkRequest = request;
        try
        {
            await PixelArt.PreloadAsync(artwork, request.Token);
            return !request.IsCancellationRequested && !_closed;
        }
        finally
        {
            if (ReferenceEquals(_artworkRequest, request)) _artworkRequest = null;
        }
    }

    private void WarmUpcomingArtwork(CampaignRun run)
    {
        if (_closed) return;
        _artworkCue?.Cancel();
        _artworkCue?.Dispose();
        _artworkCue = new CancellationTokenSource();
        var artwork = run.Phase == CampaignPhase.Shop && !run.IsOver
            ? ScreenArtwork.Shop(CampaignShop.Open(run))
            : run.Phase is CampaignPhase.Intro or CampaignPhase.InMatch
                ? ScreenArtwork.Match(run)
                : [];
        _ = PixelArt.PreloadAsync(artwork, _artworkCue.Token);
    }

    public CampaignRun? Run { get; private set; }

    public bool InMatch => Run?.Phase is CampaignPhase.InMatch;

    public string Strip => Run is null
        ? ""
        : $"♥ {Run.Lives} · ${Run.Money} · ANTE {Run.Ante} · MATCH {Run.MatchInAnte}/3{(Run.IsBoss ? " · BOSS" : "")}";

    public void Open()
    {
        if (_closed) return;
        CancelArtwork();
        _pendingResult = null;
        var loaded = CampaignStore.LoadSave(savePath);
        if (!_saveFailed && loaded.Status == CampaignLoadStatus.Corrupt)
        {
            Present(CampaignScreens.Recovery(loaded.Error!, StartNewRun, ReturnToMenu, motion));
            return;
        }
        var saved = _saveFailed ? Run : loaded.Save?.Run;
        if (saved is null || saved.IsOver)
        {
            StartNewRun();
            return;
        }

        Present(CampaignInventoryScreen.Build(CampaignScreens.PlayChoice(saved, () => Resume(saved), StartNewRun, ReturnToMenu, motion), saved.Resources.Inventory, motion));
        WarmUpcomingArtwork(saved);
    }

    // The result is recorded the moment the match ends, so leaving the finished table cannot turn it into a
    // forfeit. A second report finds the run out of InMatch and is ignored.
    public void MatchCompleted(bool won, int[] scores, GameView? summary = null)
    {
        if (Run is not { Phase: CampaignPhase.InMatch } run) return;
        var (next, outcome) = run.ApplyResult(won, scores[0], scores[1]);
        Record(next);
        _pendingResult = (outcome, scores.ToArray(), summary);
        WarmUpcomingArtwork(next);
    }

    // Shown a moment later so the last trick can be seen before the table is replaced.
    public void ShowPendingResult()
    {
        if (_pendingResult is not { } pending || Run is not { } run) return;
        _pendingResult = null;
        Present(CampaignScreens.Result(run, pending.Outcome, pending.Scores, AfterResult, TakeNote(), motion, pending.Summary));
    }

    private void StartNewRun()
    {
        ResumeNotice = null;
        Persist(new CampaignRun());
        ShowIntro();
    }

    private void Resume(CampaignRun saved)
    {
        Run = saved;
        switch (saved.Phase)
        {
            case CampaignPhase.InMatch:
                ResumeMatch(saved);
                break;
            case CampaignPhase.Shop:
                ShowShop();
                break;
            case CampaignPhase.Victory:
                ShowVictory();
                break;
            default:
                ShowIntro();
                break;
        }
    }

    private async void ResumeMatch(CampaignRun saved)
    {
        if (_starting) return;
        _starting = true;
        ResumeNotice = saved.LegacyMatchRestart
            ? "This older save did not contain the hand. The same match has restarted without losing a life."
            : null;
        try
        {
            if (await PrepareArtworkAsync(ScreenArtwork.Match(saved)) && ReferenceEquals(Run, saved))
                await startMatch(saved.NextMatch());
        }
        catch (Exception error)
        {
            Run = saved;
            Present(CampaignScreens.Recovery($"The saved match could not open: {error.Message}", () => ResumeMatch(saved), ReturnToMenu, motion, "Retry"));
        }
        finally { _starting = false; }
    }

    public void ForfeitMatch()
    {
        if (Run is not { Phase: CampaignPhase.InMatch } run) return;
        var (next, _) = run.Forfeit();
        _pendingResult = null;
        Record(next);
    }

    public bool ShowSaveFailure()
    {
        if (!_saveFailed || Run is null) return false;
        Present(CampaignScreens.Recovery("Could not save progress. Changes are retained in this session only.", () =>
        {
            Record(Run!);
            if (!ShowSaveFailure()) ReturnToMenu();
        }, ReturnToMenu, motion, "Retry save"));
        return true;
    }

    private void ShowIntro()
    {
        var revision = Run!.Resources.Revision;
        Present(_home = CampaignAnteScreen.Build(Run, BeginMatch, ReturnToMenu, TakeNote(), motion,
            (category, slot) => Discard(category, slot, revision)));
        WarmUpcomingArtwork(Run);
    }

    // Inventory changes return to the shop or to the screen before a match.
    private void ShowHome()
    {
        if (Run?.Phase is CampaignPhase.Intro) ShowIntro(); else ShowShop();
    }

    private async void BeginMatch()
    {
        if (_starting || Run is not { Phase: CampaignPhase.Intro } run) return;
        _starting = true;
        try
        {
            if (!await PrepareArtworkAsync(ScreenArtwork.Match(run)) || !ReferenceEquals(Run, run)) return;
            ResumeNotice = null;
            // Persisting InMatch before a complete engine checkpoint would make a crash
            // indistinguishable from corruption. The previous Intro remains durable until start.
            Run = run.StartMatch();
            await startMatch(run.NextMatch());
        }
        catch (Exception error)
        {
            // A table that never opened is not a forfeit; MainWindow has already reported why.
            Run = run;
            _note = $"The match could not start: {error.Message}";
            ShowIntro();
        }
        finally
        {
            _starting = false;
        }
    }

    private void ShowResult(CampaignRun next, MatchOutcome outcome, int[]? scores)
    {
        Record(next);
        Present(CampaignScreens.Result(next, outcome, scores, AfterResult, TakeNote(), motion));
        WarmUpcomingArtwork(next);
    }

    // A lost run has nothing left to continue, so its save is removed instead of rewritten.
    private void Record(CampaignRun next)
    {
        if (next.IsOver)
        {
            Run = next;
            _saveFailed = !CampaignStore.Delete(savePath);
            if (_saveFailed) _note = "Could not save progress. Changes are retained in this session only.";
        }
        else
        {
            Persist(next);
        }
    }

    private void AfterResult()
    {
        if (Run is not { } run) return;
        if (run.IsOver)
        {
            Present(CampaignScreens.RunOver(run, ReturnToMenu, motion));
            Run = null;
        }
        else if (run.Phase is CampaignPhase.Victory)
        {
            ShowVictory();
        }
        else
        {
            ShowShop();
        }
    }

    private void ShowVictory() => Present(CampaignScreens.Victory(Run!, () =>
    {
        if (Run is not { Phase: CampaignPhase.Victory } run) return;
        Persist(run.AcknowledgeVictory());
        ShowShop();
    }, ReturnToMenu, motion));

    public bool Checkpoint(CampaignCheckpoint checkpoint)
    {
        if (Run is not { Phase: CampaignPhase.InMatch } run || checkpoint.Match.Resources.Revision < run.LastAppliedRevision) return false;
        Run = run.WithCheckpoint(checkpoint);
        _saveFailed = !CampaignStore.Save(Run, savePath);
        if (_saveFailed) _note = "Could not save progress. Changes are retained in this session only.";
        else if (_note?.StartsWith("Could not save progress") is true) _note = null;
        return !_saveFailed;
    }

    private void ShowShop() => ShowShop(refreshPaidAction: false);

    private async void ShowShop(bool refreshPaidAction, bool revealBooster = false, bool showSoda = false)
    {
        if (Run is not { } run || _closed) return;
        var shopRun = CampaignShop.Open(run);
        if (!IsArcadeUseSession() && (!refreshPaidAction || !ReferenceEquals(shopRun, run))) Persist(shopRun);
        if (!await PrepareArtworkAsync(ScreenArtwork.Shop(shopRun)) || !ReferenceEquals(Run, shopRun)) return;
        if (Run!.Shop?.UseSession is not null) { ShowUseSession(); return; }
        if (Run.Shop?.Arcade?.Pending is not null) { ShowArcade(); return; }
        var revision = Run!.Resources.Revision;
        if (Run.Shop?.PendingBooster is { } booster)
        {
            Present(_home = CampaignShopScreen.OpenedBooster(booster, Run.Resources.Inventory,
                () => Transact(() => CampaignShop.TearBooster(Run!, revision), booster: true), id =>
                Transact(() => CampaignShop.ResolveBooster(Run!, id, revision), booster: true),
                id => Transact(() => CampaignShop.SellJoker(Run!, id, revision), booster: true), motion, TakeNote(), revealBooster));
            return;
        }
        // A new visit starts from its own balance; redraws within a visit count from the last one shown.
        var visit = (Run.Ante, Run.Match);
        if (!Equals(_purse.Visit, visit)) { _purse.Visit = visit; _purse.Shown = null; }
        Present(_home = CampaignShopScreen.Build(Run!, new ShopActions(id => Buy(id, revision), id => BuyAndUse(id, revision), id => Sell(id, revision),
            (category, slot) => Discard(category, slot, revision), InspectDeck,
            () => Transact(() =>
            {
                if (Run is not { Phase: CampaignPhase.Shop } run || run.Resources.Revision != revision)
                    throw new GameRuleException("The shop changed. Choose again.");
                return run.LeaveShop();
            })) { OpenSoda = () =>
                {
                    if (Run is { Shop: { SodaPurchased: true, SodaOpened: false } shop } current && current.Resources.Revision == revision)
                        Persist(current with { Shop = shop with { SodaOpened = true } });
                }, BuySoda = () => BuySoda(revision), Pull = id => Pull(id, revision), Refresh = ShowShop, Reroll = () => Transact(() => CampaignShop.Reroll(Run!, revision)), Teller = ShowTeller, Arcade = ShowArcade,
                OpenBooster = index => Transact(() => CampaignShop.OpenBooster(Run!, revision, index), booster: true) }, motion, TakeNote(), _purse, showSoda));
    }
    private void BuySoda(long revision)
    {
        var bought = false;
        try
        {
            bought = PersistPaidAction(CampaignShop.BuySoda(Run!, revision), "vending machine");
        }
        catch (GameRuleException error) { _note = error.Message; }
        ShowShop(refreshPaidAction: true, showSoda: bought);
    }

    private void ShowArcade()
    {
        if (Run is not { Phase: CampaignPhase.Shop } run) { ShowHome(); return; }
        if (run.Shop?.UseSession is not null) { ShowUseSession(); return; }
        if (run.Shop?.PendingBooster is not null) { ShowShop(); return; }
        var revision = run.Resources.Revision;
        ArcadeResult? Play(ArcadeGame game, int choice)
        {
            try
            {
                var after = game == ArcadeGame.Plinko ? CampaignArcade.PlayPlinko(Run!, choice, revision)
                    : CampaignArcade.PlayRoulette(Run!, choice, revision);
                if (!PersistArcade(after)) return null;
                revision = after.Resources.Revision;
                return after.Shop!.Arcade!.Pending;
            }
            catch (GameRuleException error) { _note = error.Message; return null; }
        }
        void Resolve(Func<CampaignRun> update, bool use = false)
        {
            try { PersistArcade(update()); }
            catch (GameRuleException error) { _note = error.Message; }
            if (use && Run?.Shop?.UseSession is not null) ShowUseSession(); else ShowArcade();
        }
        Present(_home = ArcadeScreen.Build(run, new ArcadeActions(Play,
            id => Resolve(() => CampaignArcade.KeepArcadePrize(Run!, id, revision)),
            id => Resolve(() => CampaignArcade.BeginArcadePrizeUse(Run!, id, revision), true),
            id => Resolve(() => CampaignArcade.DismissArcadeLoss(Run!, id, revision)), ShowShop, ShowArcade)
        {
            Discard = (category, slot) => Resolve(() => category == FeatureCategory.Sigil
                ? CampaignShop.DiscardSigil(Run!, slot, revision) : CampaignShop.DiscardTarot(Run!, slot, revision)),
            Tear = id =>
            {
                try
                {
                    var after = CampaignArcade.TearSpectralPack(Run!, id, revision);
                    if (PersistArcade(after))
                    {
                        revision = after.Resources.Revision;
                        return true;
                    }
                }
                catch (GameRuleException error) { _note = error.Message; }
                ShowArcade();
                return false;
            },
            SkipPack = id => Resolve(() => CampaignArcade.SkipSpectralPack(Run!, id, revision)),
            Pick = (id, index) =>
            {
                var before = Run!.Resources.Inventory.Jokers.Select(j => j.Id).ToArray();
                try { PersistArcade(CampaignArcade.PickPackCard(Run!, id, index, revision)); }
                catch (GameRuleException error) { _note = error.Message; }
                if (Run?.Shop?.UseSession is not null) { ShowUseSession(); return; }
                var granted = Run!.Resources.Inventory.Jokers.Select(j => j.Id).Except(before).ToArray();
                if (granted.Length > 0) _granted = granted;
                ShowArcade();
            }
        }, motion, TakeNote(), TakeGranted()));
    }
    // The Fortune Teller's own menu. A print is paid and saved before the booth plays, so it is never lost or repeated;
    // the printed tickets wait on her offer until one is kept or used.
    private void ShowTeller(string offerId)
    {
        if (Run?.Shop is { UseSession: not null } or { PendingBooster: not null }) { ShowShop(); return; }
        if (Run is not { Phase: CampaignPhase.Shop } run || run.Shop?.Offers.SingleOrDefault(o => o.Id == offerId) is not { Category: FeatureCategory.Tarot, Sold: false } offer)
        {
            ShowShop(); return;
        }
        var revision = run.Resources.Revision;
        string[]? Print(int count)
        {
            try
            {
                var after = CampaignShop.PrintTickets(Run!, offerId, count, revision);
                if (!PersistPaidAction(after, "Fortune Teller")) return null;
                revision = after.Resources.Revision;
                return after.Shop!.Offers.Single(o => o.Id == offerId).Printed!;
            }
            catch (GameRuleException error) { _note = error.Message; return null; }
        }
        void Keep(string ticket) => Transact(() =>
        {
            var after = CampaignShop.KeepTicket(Run!, offerId, ticket, revision);
            _note = $"You kept {CampaignShop.PrintedCard(ticket)!.Name}!";
            return after;
        });
        void Use(string ticket) => Transact(() => CampaignShop.BeginTicketUse(Run!, offerId, ticket, revision));
        Present(FortuneTellerScreen.Build(run, offer, new TellerActions(Print, Keep, Use, ShowShop, () => ShowTeller(offerId)), motion, TakeNote()));
    }
    private void InspectDeck()
    {
        if (Run is null) return;
        if (Run.Shop is { UseSession: not null } or { PendingBooster: not null }) { ShowShop(); return; }
        Present(CampaignDeckPicker.Build(Run.Resources.Deck, 0, _ => { }, ShowShop, motion, "Shared deck · changes last for this run"));
    }
    private void Buy(string id, long revision) => Transact(() => CampaignShop.Buy(Run!, id, expectedRevision: revision));
    // The run is saved before the machine plays, so a pull is never lost or repeated. The shop is redrawn afterwards.
    private GumballId? Pull(string id, long revision)
    {
        if (Run is not { Phase: CampaignPhase.Shop } before) return null;
        try
        {
            var after = CampaignShop.Buy(before, id, expectedRevision: revision);
            var gumball = Enum.Parse<GumballId>(after.Shop!.Offers.Single(offer => offer.Id == id).FeatureId);
            _note = GumballReport("The gumball", before.Resources.Inventory, after.Resources.Inventory, gumball);
            if (before.Resources.Inventory.Has(SodaId.DoubleBubble) && before.Shop!.GumballPulls == 0)
            {
                var other = Enum.GetValues<GumballId>().First(id => after.Resources.Inventory.GumballCopies(id)
                    - before.Resources.Inventory.GumballCopies(id) > (id == gumball ? 1 : 0));
                _note += $" Double Bubble also granted {FeatureCatalog.Get(FeatureCategory.Gumball, other.ToString()).Name}!";
            }
            Persist(after);
            return gumball;
        }
        catch (GameRuleException error) { _note = error.Message; return null; }
    }
    // The purchase is paid and saved before the six-card screen opens. Its session survives closing the app.
    private void BuyAndUse(string id, long revision) => Transact(() => CampaignShop.BeginSigilUse(Run!, id, revision));
    private void ShowUseSession()
    {
        if (Run?.Shop?.UseSession is not { } session) { ShowHome(); return; }
        var revision = Run!.Resources.Revision;
        void Use(CardId[] targets, int? slot) => Transact(() =>
        {
            var before = Run!;
            var after = slot is { } index ? CampaignShop.UseHeldTarot(before, index, targets, revision)
                : CampaignShop.UsePurchased(before, targets, revision);
            var definition = slot is { } heldSlot ? FeatureCatalog.Get(FeatureCategory.Tarot, before.Resources.Inventory.Tarot[heldSlot].ToString())
                : FeatureCatalog.Get(session.Category, session.FeatureId);
            _note = UseReport(definition.Name, before, after);
            return after;
        });
        Present(_home = CampaignShopUseScreen.Build(Run, new ShopUseActions(targets => Use(targets, null),
            (slot, targets) => Use(targets, slot), () => Transact(() => CampaignShop.FinishUse(Run!, revision))), motion, TakeNote()));
    }
    // Says what a Tarot did, naming the outcome of the ones that draw at random. Good news ends in "!".
    private static string UseReport(string name, CampaignRun before, CampaignRun after)
    {
        static string Card(CardState card) => $"{Core.Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}";
        var gumballs = Enum.GetValues<GumballId>().Where(id => after.Resources.Inventory.GumballCopies(id) > before.Resources.Inventory.GumballCopies(id)).ToArray();
        if (gumballs.Length > 0) return GumballReport(name, before.Resources.Inventory, after.Resources.Inventory, gumballs[0]);
        // Card ids keep their place in the deck, so the same index is the same physical card.
        var changes = after.Resources.Deck.Select((card, index) => (Old: before.Resources.Deck[index], New: card)).Where(pair => pair.Old != pair.New)
            .Select(pair => pair.New.Sigil is { } sigil && pair.Old.Sigil != sigil
                ? $"{Card(pair.Old)} now carries {sigil}"
                : pair.New.Enhancement is { } enhancement && pair.Old.Enhancement != enhancement
                ? $"{Card(pair.Old)} is now {enhancement}" : $"{Card(pair.Old)} is now the {Card(pair.New)}").ToArray();
        if (changes.Length > 0) return $"{name}: {string.Join(", ", changes)}!";
        return $"Used {name}!";
    }
    private static string GumballReport(string source, CampaignInventory before, CampaignInventory after, GumballId id)
    {
        var name = FeatureCatalog.Get(FeatureCategory.Gumball, id.ToString()).Name;
        if (!before.Unlocks.Contains(id)) return $"{source} unlocked the {name} bid!";
        var reward = $"+{after.GumballExtraCopies.GetValueOrDefault(id)} points on your successful matching bid, after hand multipliers!";
        return $"{source} granted {name} copy {after.GumballCopies(id)}! {reward}";
    }
    private void Sell(JokerId id, long revision) => Transact(() => CampaignShop.SellJoker(Run!, id, revision));
    private void Discard(FeatureCategory category, int slot, long revision) => Transact(() => category == FeatureCategory.Sigil
        ? CampaignShop.DiscardSigil(Run!, slot, revision) : CampaignShop.DiscardTarot(Run!, slot, revision));
    private void Transact(Func<CampaignRun> update, bool booster = false)
    {
        var revealBooster = false;
        try
        {
            var arcade = IsArcadeUseSession();
            var after = update();
            if (booster)
            {
                var opening = Run?.Shop?.PendingBooster?.AwaitingOpen == true && after.Shop?.PendingBooster?.AwaitingOpen == false;
                revealBooster = PersistPaidAction(after, "booster") && opening;
            }
            else if (arcade) PersistArcade(after); else Persist(after);
        }
        catch (GameRuleException error) { _note = error.Message; }
        if (booster && Run?.Phase == CampaignPhase.Shop) ShowShop(refreshPaidAction: true, revealBooster); else ShowHome();
    }

    private bool IsArcadeUseSession() => Run?.Shop?.UseSession is { } session
        && Run.Shop.Offers.Any(offer => offer.Id == session.OfferId && offer.IsArcadePrize);

    // A paid Arcade result, its claim and its card effects become visible only after their save succeeds.
    private bool PersistArcade(CampaignRun after) => PersistPaidAction(after, "Arcade");

    private bool PersistPaidAction(CampaignRun after, string action)
    {
        if (!CampaignStore.Save(after, savePath))
        {
            _saveFailed = true;
            _note = $"Could not save the {action} action. Nothing changed. Try again.";
            return false;
        }
        Run = after;
        _saveFailed = false;
        return true;
    }

    // A failed save never stops play; the next screen says progress was not saved.
    private void Persist(CampaignRun run)
    {
        Run = run;
        _saveFailed = !CampaignStore.Save(run, savePath);
        if (_saveFailed) _note = "Could not save progress. Changes are retained in this session only.";
    }

    /// <summary>Applies a developer-console change between matches, saves it, and redraws the
    /// ante or shop screen when that is what is showing.</summary>
    public void DeveloperApply(Func<CampaignRun, CampaignRun> update, Control? showing)
    {
        if (Run is not { } run) throw new GameRuleException("No campaign run is loaded.");
        if (run.Phase is CampaignPhase.InMatch) throw new GameRuleException("A match is in progress.");
        var next = update(run with { Resources = run.Resources with { Wallets = [run.Money, .. run.Resources.Wallets[1..]] } });
        next = next with { Money = next.Resources.Wallets[0], Resources = next.Resources with { Revision = next.Resources.Revision + 1 } };
        if (!next.Resources.IsValid()) throw new GameRuleException("Those campaign resources are not valid.");
        Persist(next);
        if (showing is not null && ReferenceEquals(showing, _home)) ShowHome();
    }

    private string? TakeNote()
    {
        var note = _note;
        _note = null;
        return note;
    }
}
