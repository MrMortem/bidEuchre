using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    private ContractAnnouncement? _contractAnnouncement;
    private CancellationTokenSource? _contractAnnouncementSource;
    private GameSession? _announcingSession;
    private long _announcingDeal;

    private static string ContractCaller(GameView view) => view.Players.FirstOrDefault(p => p.Seat == view.Bidder)?.Name
        ?? (view.Bidder is { } seat ? $"Seat {seat + 1}" : "Caller");
    private static string ContractValue(Contract contract) => contract.Mode == ContractMode.Trump
        ? $"{SuitSymbol(contract.Trump!.Value)} {contract.Trump}" : contract.Mode.ToString();

    private void UpdateContractAnnouncement(GameSession session, GameView view)
    {
        if (!session.WaitingForContractPresentation || view.Contract is null) return;
        if (!TableShown || WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            CancelContractAnnouncement();
            _ = AcknowledgeContractAsync(session, (view.DealSerial ?? view.HandNumber));
            return;
        }
        if (ReferenceEquals(_announcingSession, session) && _announcingDeal == (view.DealSerial ?? view.HandNumber)) return;
        CancelContractAnnouncement();
        var source = new CancellationTokenSource();
        _contractAnnouncementSource = source;
        _announcingSession = session;
        _announcingDeal = (view.DealSerial ?? view.HandNumber);
        _ = PresentContractAsync(session, view, source);
    }

    private async Task PresentContractAsync(GameSession session, GameView view, CancellationTokenSource source)
    {
        try
        {
            if (_contractAnnouncement is not null)
                await _contractAnnouncement.ShowAsync(ContractValue(view.Contract!), ContractCaller(view), _motion.Enabled, source.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            if (ReferenceEquals(_contractAnnouncementSource, source))
            {
                _contractAnnouncementSource = null;
                _announcingSession = null;
            }
            source.Dispose();
            await AcknowledgeContractAsync(session, (view.DealSerial ?? view.HandNumber));
        }
    }

    private async Task AcknowledgeContractAsync(GameSession session, long deal)
    {
        try
        {
            await session.CompleteContractPresentationAsync(deal);
            if (!ReferenceEquals(session, _session) || _liveSettingsOpen) return;
            _renderedView = null;
            await RefreshGameAsync();
            SyncNativeInput();
        }
        catch (ObjectDisposedException) { }
    }

    private void CancelContractAnnouncement()
    {
        var source = _contractAnnouncementSource;
        _contractAnnouncementSource = null;
        _announcingSession = null;
        source?.Cancel();
    }
}
