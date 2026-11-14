using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// Wires the developer console (` key) to the campaign: live matches go through the session, everything else through the saved run.
public sealed partial class MainWindow
{
    private DeveloperConsoleView? _developerConsole;
    private DeveloperHost? _developerHost;

    private Control WithDeveloperConsole(Control content)
    {
        _developerHost = new DeveloperHost(this);
        _developerConsole = new DeveloperConsoleView(new DeveloperConsole(_developerHost), _developerHost.RefreshAsync);
        SizeChanged += (_, args) => _developerConsole.Fit(args.NewSize.Height);
        _developerConsole.Fit(Height);
        // Tunnelling catches the key before any focused text box types it.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.OemTilde || _developerConsole.IsOpen) return;
            e.Handled = true;
            _developerConsole.Toggle();
        }, RoutingStrategies.Tunnel);
        return new Grid { Children = { content, _developerConsole } };
    }

    private sealed class DeveloperHost(MainWindow window) : IDeveloperHost
    {
        private GameSession? LiveSession => window._campaign.InMatch && window.InCampaignMatch ? window._campaignSession : null;

        public CampaignRun? Run => window._campaign.Run;
        public bool InMatch => LiveSession is not null;
        public IReadOnlyList<int>? Scores { get; private set; }
        public IReadOnlyList<CardId>? HumanHand { get; private set; }

        /// <summary>Reads scores and the human hand from the live match, under the session's lock.</summary>
        public async Task RefreshAsync()
        {
            Scores = null; HumanHand = null;
            if (LiveSession is not { } session) return;
            try
            {
                var view = await session.GetViewAsync(0);
                Scores = view?.Scores;
                HumanHand = view?.Campaign?.Players[0].VisibleCards.Select(c => c.Id).ToArray();
            }
            catch (ObjectDisposedException) { }
        }

        public async Task UpdateResourcesAsync(Func<CampaignSnapshot, CampaignSnapshot> update)
        {
            if (LiveSession is { } session) await session.DeveloperAsync(game => game.DeveloperReplaceResources(update));
            else window._campaign.DeveloperApply(run => run with { Resources = update(run.Resources) }, window._screenHost.Content as Control);
            await RefreshAsync();
        }

        public Task UpdateRunAsync(Func<CampaignRun, CampaignRun> update)
        {
            if (InMatch) throw new GameRuleException("That only works between matches.");
            window._campaign.DeveloperApply(update, window._screenHost.Content as Control);
            return Task.CompletedTask;
        }

        public async Task SetScoresAsync(int us, int them)
        {
            var session = LiveSession ?? throw new GameRuleException("There is no live match.");
            await session.DeveloperAsync(game => game.DeveloperSetScores(us, them));
            await RefreshAsync();
        }

        public async Task EndMatchAsync(bool won)
        {
            var session = LiveSession ?? throw new GameRuleException("There is no live match.");
            await session.DeveloperAsync(game => game.DeveloperEndMatch(won ? 0 : 1));
            await RefreshAsync();
        }
    }
}
