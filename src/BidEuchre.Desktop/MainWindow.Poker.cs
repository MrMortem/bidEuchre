using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    private static readonly TimeSpan PokerRevealDuration = TimeSpan.FromSeconds(3);
    private (GameSession Session, long Deal, long Revision, long? Since)? _pokerReveal;

    private void RenderPokerReveal(GameView view, CampaignGameView campaign, PokerRoundResult poker)
    {
        _actionTitle.Text = "Poker showdown";
        var winners = ScoreHistoryControls.PokerWinners(view, poker);
        var awards = poker.TeamPoints.Select((points, team) => points > 0 ? $"Team {team + 1} +{points} points" : null).OfType<string>();
        var hand = view.HandHistory.LastOrDefault(hand => hand.HandNumber == view.HandNumber);
        var copies = (hand?.ScoreBreakdowns ?? []).SelectMany(part => part.GumballBonuses
            .Where(bonus => bonus.Id == GumballId.Poker)
            .Select(bonus => $"Team {part.Team + 1} +{bonus.Points} Poker copies"));
        _actionHelp.Text = string.Join("\n", winners.Concat(awards).Concat(copies));
        if (campaign.LegalActions.Any(command => command is CampaignCommand.CompletePoker))
            AddAction("Continue", () => SendCampaignActionAsync(campaign, new CampaignCommand.CompletePoker()));
    }

    private void UpdatePokerReveal(GameSession session, GameView view)
    {
        if (view.Campaign is not { } campaign || !CanCompletePoker(session, campaign))
        {
            _pokerReveal = null;
            return;
        }
        var current = (session, campaign.DealSerial, campaign.Revision);
        if (_pokerReveal is not { } reveal || (reveal.Session, reveal.Deal, reveal.Revision) != current)
        {
            _pokerReveal = (session, campaign.DealSerial, campaign.Revision, Stopwatch.GetTimestamp());
            return;
        }
        if (_input.Locked || reveal.Since is not { } since || Stopwatch.GetElapsedTime(since) < PokerRevealDuration) return;

        // Submit outside the refresh so the action can immediately render the next deal or match result.
        var submitted = (session, campaign.DealSerial, campaign.Revision, (long?)null);
        _pokerReveal = submitted;
        Dispatcher.UIThread.Post(async () =>
        {
            if (_pokerReveal == submitted && CanCompletePoker(session, campaign))
                await SendCampaignActionAsync(campaign, new CampaignCommand.CompletePoker());
        });
    }

    private bool CanCompletePoker(GameSession session, CampaignGameView campaign) =>
        IsActive && WindowState != WindowState.Minimized && TableShown && !_tableTransitioning && _viewer == 0 &&
        ReferenceEquals(session, _session) &&
        _actionView is { Phase: GamePhase.PokerReveal, Campaign: { ViewerSeat: 0 } current } &&
        current.DealSerial == campaign.DealSerial && current.Revision == campaign.Revision &&
        current.LegalActions.Any(command => command is CampaignCommand.CompletePoker);
}
