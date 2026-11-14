using Avalonia.Controls;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// Moves a campaign run between its screens and keeps the save file in step with it.
internal sealed class CampaignFlow(
    string savePath,
    Action<Control> show,
    Func<MatchOptions, Task> startMatch,
    Action showMenu,
    PixelMotion motion)
{
    private string? _note;
    private bool _starting;
    private (MatchOutcome Outcome, int[] Scores)? _pendingResult;

    public CampaignRun? Run { get; private set; }

    public bool InMatch => Run?.Phase is CampaignPhase.InMatch;

    public string Strip => Run is null
        ? ""
        : $"♥ {Run.Lives} · ${Run.Money} · ANTE {Run.Ante} · MATCH {Run.MatchInAnte}/3{(Run.IsBoss ? " · BOSS" : "")}";

    public void Open()
    {
        _pendingResult = null;
        var saved = CampaignStore.Load(savePath);
        if (saved is null)
        {
            StartNewRun();
            return;
        }

        show(CampaignScreens.PlayChoice(saved, () => Resume(saved), StartNewRun, showMenu, motion));
    }

    // The result is recorded the moment the match ends, so leaving the finished table cannot turn it into a
    // forfeit. A second report finds the run out of InMatch and is ignored.
    public void MatchCompleted(bool won, int[] scores)
    {
        if (Run is not { Phase: CampaignPhase.InMatch } run) return;
        var (next, outcome) = run.ApplyResult(won, scores[0], scores[1]);
        Record(next);
        _pendingResult = (outcome, scores);
    }

    // Shown a moment later so the last trick can be seen before the table is replaced.
    public void ShowPendingResult()
    {
        if (_pendingResult is not { } pending || Run is not { } run) return;
        _pendingResult = null;
        show(CampaignScreens.Result(run, pending.Outcome, pending.Scores, AfterResult, TakeNote(), motion));
    }

    private void StartNewRun()
    {
        Persist(new CampaignRun());
        ShowIntro();
    }

    private void Resume(CampaignRun saved)
    {
        Run = saved;
        switch (saved.Phase)
        {
            case CampaignPhase.InMatch:
                // The match was abandoned by leaving or quitting, which counts as a loss.
                var (next, outcome) = saved.Forfeit();
                ShowResult(next, outcome, null);
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

    private void ShowIntro()
    {
        var revision = Run!.Resources.Revision;
        show(CampaignAnteScreen.Build(Run, BeginMatch, showMenu, TakeNote(), motion,
            (category, slot) => UseItem(category, slot, revision), (category, slot) => Discard(category, slot, revision)));
    }

    // Stored cards are played from the shop and from the screen before a match; both come back to where they were.
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
            Persist(run.StartMatch());
            await startMatch(run.NextMatch());
        }
        catch (Exception error)
        {
            // A table that never opened is not a forfeit; MainWindow has already reported why.
            Persist(run);
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
        show(CampaignScreens.Result(next, outcome, scores, AfterResult, TakeNote(), motion));
    }

    // A lost run has nothing left to continue, so its save is removed instead of rewritten.
    private void Record(CampaignRun next)
    {
        if (next.IsOver)
        {
            Run = next;
            CampaignStore.Delete(savePath);
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
            Run = null;
            show(CampaignScreens.RunOver(run, showMenu, motion));
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

    private void ShowVictory() => show(CampaignScreens.Victory(Run!, () =>
    {
        if (Run is not { Phase: CampaignPhase.Victory } run) return;
        Persist(run.AcknowledgeVictory());
        ShowShop();
    }, showMenu, motion));

    public bool Checkpoint(CampaignSnapshot snapshot)
    {
        if (Run is not { Phase: CampaignPhase.InMatch } run || snapshot.Revision < run.LastAppliedRevision) return false;
        Run = run with { Resources = snapshot.Copy(), Money = snapshot.Wallets[0], LastAppliedRevision = snapshot.Revision };
        var saved = CampaignStore.Save(Run, savePath); if (!saved) _note = "Could not save progress."; return saved;
    }

    private void ShowShop()
    {
        if (Run is null) return;
        Persist(CampaignShop.Open(Run));
        var revision = Run!.Resources.Revision;
        show(CampaignShopScreen.Build(Run!, new ShopActions(Buy, BuyAndUse, Sell,
            (category, slot) => UseItem(category, slot, revision), (category, slot) => Discard(category, slot, revision), InspectDeck,
            () => { Persist(Run!.LeaveShop()); ShowIntro(); }) { Pull = Pull, Refresh = ShowShop }, motion, TakeNote()));
    }
    private void InspectDeck()
    {
        if (Run is null) return;
        show(CampaignDeckPicker.Build(Run.Resources.Deck, 0, _ => { }, ShowShop, motion, "Shared deck · changes last for this run"));
    }
    private void Buy(string id) => Transact(() => CampaignShop.Buy(Run!, id));
    // The run is saved before the machine plays, so a pull is never lost or repeated. The shop is redrawn afterwards.
    private GumballId? Pull(string id)
    {
        if (Run is not { Phase: CampaignPhase.Shop } before) return null;
        try
        {
            var after = CampaignShop.Buy(before, id);
            var gumball = after.Resources.Inventory.Unlocks.Except(before.Resources.Inventory.Unlocks).Single();
            _note = $"The gumball unlocked the {FeatureCatalog.Get(FeatureCategory.Gumball, gumball.ToString()).Name} bid!";
            Persist(after);
            return gumball;
        }
        catch (GameRuleException error) { _note = error.Message; return null; }
    }
    // Items used from the shop are aimed at the shared deck, since there is no hand to aim at.
    private void BuyAndUse(string id)
    {
        if (Run?.Shop?.Offers.SingleOrDefault(o => o.Id == id) is not { } offer || offer.Category is not (FeatureCategory.Tarot or FeatureCategory.Sigil)) return;
        var definition = FeatureCatalog.Get(offer.Category, offer.FeatureId);
        var required = offer.Category == FeatureCategory.Sigil ? 1 : CampaignShop.TarotTargets(Enum.Parse<TarotId>(offer.FeatureId));
        CampaignRun Use(CardId[] targets)
        {
            var before = Run!;
            var after = CampaignShop.BuyAndUse(before, id, targets);
            _note = UseReport(definition.Name, before, after);
            return after;
        }
        if (required == 0) Transact(() => Use([]));
        else ChooseFromDeck(required, $"{definition.Name} · {definition.Description}", $"Buy & Use ${offer.Price}", Use);
    }
    // Outside a match a stored card is aimed at the shared deck, as one bought and used at once is.
    private void UseItem(FeatureCategory category, int slot, long revision)
    {
        if (Run?.Resources.Revision != revision) { _note = "Your cards changed. Choose again."; ShowHome(); return; }
        var inventory = Run.Resources.Inventory;
        var sigil = category == FeatureCategory.Sigil;
        var definition = FeatureCatalog.Get(category, sigil ? inventory.Sigils[slot].ToString() : inventory.Tarot[slot].ToString());
        var required = sigil ? 1 : CampaignShop.TarotTargets(inventory.Tarot[slot]);
        CampaignRun Use(CardId[] targets)
        {
            var before = Run!;
            var after = sigil ? CampaignShop.UseSigil(before, slot, targets[0], revision) : CampaignShop.UseTarot(before, slot, targets, revision);
            _note = UseReport(definition.Name, before, after);
            return after;
        }
        if (required == 0) Transact(() => Use([]));
        else ChooseFromDeck(required, $"{definition.Name} · {definition.Description}", sigil ? "Use Sigil" : "Use Tarot", Use);
    }
    private void ChooseFromDeck(int required, string title, string confirm, Func<CardId[], CampaignRun> update)
    {
        var revision = Run!.Resources.Revision;
        show(CampaignDeckPicker.Build(Run.Resources.Deck, required, targets =>
        {
            if (Run?.Resources.Revision != revision) { _note = "Your cards changed. Choose again."; ShowHome(); return; }
            Transact(() => update(targets));
        }, ShowHome, motion, title, confirm));
    }
    // Says what a Tarot did, naming the outcome of the ones that draw at random. Good news ends in "!".
    private static string UseReport(string name, CampaignRun before, CampaignRun after)
    {
        static string Card(CardState card) => $"{Core.Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}";
        var gumballs = after.Resources.Inventory.Unlocks.Except(before.Resources.Inventory.Unlocks)
            .Select(id => FeatureCatalog.Get(FeatureCategory.Gumball, id.ToString()).Name).ToArray();
        if (gumballs.Length > 0) return $"{name} unlocked the {gumballs[0]} bid!";
        // Card ids keep their place in the deck, so the same index is the same physical card.
        var changes = after.Resources.Deck.Select((card, index) => (Old: before.Resources.Deck[index], New: card)).Where(pair => pair.Old != pair.New)
            .Select(pair => pair.New.Sigil is { } sigil && pair.Old.Sigil != sigil
                ? $"{Card(pair.Old)} now carries {sigil}"
                : pair.New.Enhancement is { } enhancement && pair.Old.Enhancement != enhancement
                ? $"{Card(pair.Old)} is now {enhancement}" : $"{Card(pair.Old)} is now the {Card(pair.New)}").ToArray();
        if (changes.Length > 0) return $"{name}: {string.Join(", ", changes)}!";
        return $"Used {name}!";
    }
    private void Sell(JokerId id) => Transact(() => CampaignShop.SellJoker(Run!, id));
    private void Discard(FeatureCategory category, int slot, long revision) => Transact(() => category == FeatureCategory.Sigil
        ? CampaignShop.DiscardSigil(Run!, slot, revision) : CampaignShop.DiscardTarot(Run!, slot, revision));
    private void Transact(Func<CampaignRun> update)
    {
        try { Persist(update()); } catch (GameRuleException error) { _note = error.Message; }
        ShowHome();
    }

    // A failed save never stops play; the next screen says progress was not saved.
    private void Persist(CampaignRun run)
    {
        Run = run;
        if (!CampaignStore.Save(run, savePath)) _note = "Could not save progress.";
    }

    private string? TakeNote()
    {
        var note = _note;
        _note = null;
        return note;
    }
}
