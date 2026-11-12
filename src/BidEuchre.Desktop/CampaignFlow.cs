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

    private void ShowIntro() => show(CampaignScreens.Intro(Run!, BeginMatch, showMenu, TakeNote(), motion));

    private async void BeginMatch()
    {
        if (_starting || Run is not { Phase: CampaignPhase.Intro } run) return;
        _starting = true;
        try
        {
            Persist(run.StartMatch());
            await startMatch(run.NextMatch());
        }
        catch (Exception)
        {
            // A table that never opened is not a forfeit; MainWindow has already reported why.
            Persist(run);
            _note = "The match could not start.";
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

    private void ShowShop() => show(CampaignScreens.Shop(Run!, () =>
    {
        if (Run is not { Phase: CampaignPhase.Shop } run) return;
        Persist(run.LeaveShop());
        ShowIntro();
    }, motion));

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
