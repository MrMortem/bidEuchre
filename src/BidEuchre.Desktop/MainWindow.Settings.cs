using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    // Set before awaiting the session lock: timer refreshes and retained input must stop at once.
    private bool _liveSettingsOpen;

    private Control BuildTableNavigation(Button menu)
    {
        menu.Margin = default;
        var settings = QuietButton("Settings");
        settings.Name = "TableSettings";
        settings.FontSize = 11;
        settings.HorizontalAlignment = HorizontalAlignment.Right;
        settings.Click += async (_, _) => await OpenTableSettingsAsync();
        return new StackPanel
        {
            Name = "TableNavigation", Spacing = 4, Margin = new Thickness(0, 6, 20, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Children = { menu, settings }
        };
    }

    private async Task OpenTableSettingsAsync()
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (_tableTransitioning || _liveSettingsOpen) return;
        if (session is null) { ShowSettings(); return; }
        if (InCampaignMatch && !_campaign.InMatch) return;
        _tableTransitioning = true;
        _liveSettingsOpen = true;
        _input.BeginViewChange();
        SyncNativeInput();
        try
        {
            await session.PauseAsync();
            if (generation != _sessionGeneration || !ReferenceEquals(session, _session)) return;
            StopTablePresentation();
            var view = await session.GetViewAsync(_viewer);
            // The last in-flight bot move may have completed the match while pause awaited the gate.
            if (view?.Phase == GamePhase.GameComplete && InCampaignMatch)
            {
                _liveSettingsOpen = false;
                await session.ResumeAsync();
                _renderedView = null;
                await RefreshGameAsync();
                return;
            }
            _menuButton.Content = MenuLabel;
            _screenHost.Content = SettingsPage.Build(_preferences, ChangePreferences,
                () => _ = ReturnFromTableSettingsAsync(session, generation), _motion, matchPaused: true);
        }
        catch (ObjectDisposedException) { _liveSettingsOpen = false; }
        catch (Exception error)
        {
            _liveSettingsOpen = false;
            try { await session.ResumeAsync(); } catch (ObjectDisposedException) { }
            _renderedView = null;
            await RefreshGameAsync();
            ShowError(error.Message);
        }
        finally { _tableTransitioning = false; }
    }

    private async Task ReturnFromTableSettingsAsync(GameSession session, long generation)
    {
        if (_tableTransitioning || !_liveSettingsOpen || generation != _sessionGeneration ||
            !ReferenceEquals(session, _session)) return;
        _tableTransitioning = true;
        try
        {
            StopTablePresentation();
            var view = await session.GetViewAsync(_viewer);
            // Seed presentation with the current public state; returning must not replay its history.
            _motionTracker.Update(view, $"{session.Id}:{_viewer}");
            _motionCue = TableMotionCue.None;
            _input.BeginViewChange();
            _bidLevel = null;
            _bidStepKey = null;
            _targeting = null;
            _actionView = null;
            _inventoryKey = null;
            _renderedView = null;
            _liveSettingsOpen = false;
            // Hide the previous private view before the first asynchronous return refresh.
            _tableHost.IsVisible = false;
            _centerPanel.IsVisible = false;
            ShowTable();
            await RefreshGameAsync();
            await session.ResumeAsync();
            // A pre-resume refresh may have rejected the contract-dependent hand sort.
            _renderedView = null;
            await RefreshGameAsync();
        }
        catch (ObjectDisposedException) { _liveSettingsOpen = false; }
        catch (Exception error) { ShowError(error.Message); }
        finally { _tableTransitioning = false; }
    }

    private void StopTablePresentation()
    {
        CancelContractAnnouncement();
        _pokerReveal = null;
        _motion.Cancel();
        _bidAnnouncement?.Clear();
        _scoreJolts.Cancel();
        _cardFlights?.Cancel();
        _deckDeal?.Cancel();
        _handGlide?.Reset();
        _trickCollection.Complete(_trickCollection.Key);
    }
}
