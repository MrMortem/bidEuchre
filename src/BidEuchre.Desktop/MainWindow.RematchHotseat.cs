using Avalonia.Controls;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    // This is the table that actually opened, independent of later setup edits.
    private LastTableSetup? _freePlayRematch;
    private int? _hotseatPending;
    private long _hotseatRevision;
    private bool _settingViewer;
    private bool AutomaticHotseat => _preferences.AutomaticHotseatPrivacy && !InCampaignMatch
        && _session?.Seats.Count(seat => seat.Kind == PlayerKind.Human) > 1;

    private bool AcceptTableInput(long revision, long generation, int? viewer, GameView? view) =>
        revision == _input.Revision && generation == _sessionGeneration && viewer == _viewer
        && ReferenceEquals(view, _actionView) && _hotseatPending is null && !_liveSettingsOpen && !_tableTransitioning;

    private void SetViewerSelection(int? seat)
    {
        _settingViewer = true;
        try { _viewerSeat.SelectedItem = _viewerSeat.Items.OfType<ViewerOption>().FirstOrDefault(option => option.Seat == seat); }
        finally { _settingViewer = false; }
    }

    private void ResetHotseat() => _hotseatPending = null;

    // Called before building any hand or choice. All changes in this block are synchronous:
    // neither an old face nor a private flying card survives a handoff to the renderer.
    private bool RenderHotseatHandoff(GameSession session, GameView view)
    {
        var next = AutomaticHotseat && view.CurrentSeat is { } actor && session.Seats[actor].Kind == PlayerKind.Human
            && actor != _viewer ? view.CurrentSeat : null;
        if (next is null)
        {
            if (_hotseatPending is not null)
            {
                _hotseatPending = null;
                _renderKey = null;
            }
            return false;
        }
        if (_hotseatPending != next || _hotseatRevision != _input.Revision || _actionButtons.Children.Count == 0)
        {
            _hotseatPending = next;
            var revision = _hotseatRevision = _input.BeginViewChange();
            _tableHost.Content = null;
            _actionButtons.Children.Clear();
            _actionView = null;
            StopTablePresentation();
            _motion.SetContract(null);
            _motionTracker.Update(null, $"{session.Id}:handoff");
            _renderKey = null;
            _teamZeroScore.Text = view.Scores[0].ToString();
            _teamOneScore.Text = view.Scores[1].ToString();
            _dealerText.Text = $"HAND {view.HandNumber} · TRICKS {view.TricksByTeam[0]}–{view.TricksByTeam[1]}";
            _contractText.Text = ContractName(view);
            _contractText.IsVisible = true;
            _actionTitle.Text = $"Pass the device to {view.Players[next.Value].Name}";
            _actionHelp.Text = "Press Reveal when you are ready to see your hand.";
            var reveal = ActionButton("Reveal hand");
            reveal.Name = "HotseatReveal";
            var generation = _sessionGeneration;
            reveal.Click += async (_, _) => await RevealHotseatAsync(session, next.Value, revision, generation);
            _actionButtons.Children.Add(reveal);
            _tableHost.Content = BuildWaitingTable(view.Players);
            ShowCenter(always: true);
            _input.CompleteView(revision);
        }
        // A handoff replaces these optional presentations and must release their engine gates.
        if (session.WaitingForDeal) _ = FinishDealAsync(session, view.HandNumber);
        if (session.WaitingForContractPresentation) _ = AcknowledgeContractAsync(session, view.DealSerial ?? view.HandNumber);
        SyncNativeInput();
        return true;
    }

    private async Task RevealHotseatAsync(GameSession session, int seat, long revision, long generation)
    {
        if (generation != _sessionGeneration || !ReferenceEquals(session, _session) || _liveSettingsOpen
            || _tableTransitioning || _hotseatPending != seat || revision != _input.Revision) return;
        GameView? current;
        try { current = await session.GetViewAsync(null); }
        catch (ObjectDisposedException) { return; }
        if (generation != _sessionGeneration || !ReferenceEquals(session, _session) || _liveSettingsOpen
            || _tableTransitioning || revision != _input.Revision || _hotseatPending != seat) return;
        if (current?.CurrentSeat != seat || !AutomaticHotseat) { _renderKey = null; await RefreshGameAsync(); return; }
        _input.BeginViewChange();
        _hotseatPending = null;
        _viewer = seat;
        SetViewerSelection(seat);
        _renderKey = null;
        SyncNativeInput();
        await RefreshGameAsync();
    }

    private void RenderFreePlayRematch()
    {
        var session = _session;
        if (session is null || InCampaignMatch || session.Game?.IsCampaign != false) return;
        var rematch = ActionButton("Rematch"); rematch.Name = "FreePlayRematch";
        rematch.Click += async (_, _) => await RematchFreePlayAsync(session, rematch);
        var setup = ActionButton("New table"); setup.Name = "FreePlayNewTable";
        setup.Click += async (_, _) =>
        {
            if (!ReferenceEquals(session, _session) || InCampaignMatch || session.Game?.Phase != GamePhase.GameComplete || _liveSettingsOpen) return;
            await LeaveTableAsync(setup);
        };
        _actionButtons.Children.Add(rematch);
        _actionButtons.Children.Add(setup);
    }

    private async Task RematchFreePlayAsync(GameSession previous, Button button)
    {
        if (_tableTransitioning || _liveSettingsOpen || !ReferenceEquals(previous, _session) || InCampaignMatch
            || previous.Game is not { IsCampaign: false, Phase: GamePhase.GameComplete } || _freePlayRematch is not { } setup) return;
        _tableTransitioning = true;
        button.IsEnabled = false;
        ClearStatus();
        try
        {
            DetachSession();
            await previous.DisposeAsync();
            await OpenSessionAtPaceAsync(setup.Seats, setup.TableName, null, setup.BotDelayMilliseconds);
            await StartSessionAsync();
        }
        catch (Exception error)
        {
            if (_session is null) ShowSetup();
            ShowError(error.Message);
        }
        finally { _tableTransitioning = false; button.IsEnabled = true; }
    }
}
