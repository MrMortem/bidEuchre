namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    public CampaignMatchSnapshot ExportCampaignMatchSnapshot()
    {
        if (!IsCampaign || Phase == GamePhase.NotStarted || _dispatchCampaign || _pendingBidVariant is not null)
            throw new GameRuleException("No stable campaign match is available to save.");
        return new()
        {
            Version = 1,
            PlayerNames = _playerNames.ToArray(),
            Options = _matchOptions,
            Resources = ExportCampaignSnapshot(),
            Hands = _hands.Select(hand => hand.Select(IdFor).ToArray()).ToArray(),
            Auction = _auction.ToArray(),
            CurrentTrick = _currentTrick.Select(p => new SnapshotPlay(p.Seat, IdFor(p.Card))).ToArray(),
            CompletedTricks = _completedTricks.Select(t => new SnapshotTrick(t.Number, t.Leader, t.Winner, t.Plays.Select(p => new SnapshotPlay(p.Seat, IdFor(p.Card))).ToArray())).ToArray(),
            HandHistory = _handHistory.Select(CopyHandResult).ToArray(),
            Events = _events.ToArray(),
            PriorHandEvents = _priorHandEvents.ToArray(),
            PendingExchangeCard = _pendingExchangeCard is { } pending ? IdFor(pending) : null,
            Overlays = _overlays.Values.ToArray(),
            WildSuits = _wildSuits.Select(p => new SnapshotWildSuit(p.Key, p.Value)).ToArray(),
            Reveals = _reveals.Select(p => new SnapshotReveal(p.Viewer, p.Id)).ToArray(),
            StickyTriggered = _stickyTriggered.ToArray(),
            TrickHistory = _campaignTrickHistory.Select(p => new SnapshotTrickHistory(p.Key, p.Value.ToArray())).ToArray(),
            TrickPoints = _trickPoints.Select(p => new SnapshotEffectPoint(p.Team, p.Amount, p.Source)).ToArray(),
            Phase = Phase,
            HandNumber = HandNumber,
            DealSerial = DealSerial,
            Dealer = Dealer,
            CurrentSeat = CurrentSeat,
            HighBid = HighBid,
            Bidder = Bidder,
            Contract = Contract,
            GameWinner = GameWinner,
            CompletionReason = CompletionReason,
            BiddingTurns = _biddingTurns,
            Variant = _variant,
            PendingChoice = _pendingChoice,
            PendingActor = _pendingActor,
            DuelOpponent = _duelOpponent,
            DuelistUsedThisMatch = _duelistUsedThisMatch,
            PokerRevealed = _pokerRevealed,
            TelepathDone = _telepathDone,
            FirstMatchCard = _firstMatchCard,
            FirstHumanRoundCard = _firstHumanRoundCard,
            LastMatchCard = _lastMatchCard,
            RollerValue = _rollerValue,
            DejaValue = _dejaValue,
            EffectSequence = _effectSequence,
            Scores = _scores.ToArray(),
            TricksByTeam = _tricksByTeam.ToArray(),
            CardPoints = _cardPoints.ToArray(),
            JokerPoints = _jokerPoints.ToArray(),
            ModifierPoints = _modifierPoints.ToArray(),
            MatchOvertricks = _matchOvertricks.ToArray(),
            HandWalletStart = _handWalletStart.ToArray(),
        };
    }

    public static GameEngine RestoreCampaignMatch(CampaignMatchSnapshot snapshot)
    {
        if (snapshot is null || !snapshot.IsValid()) throw new GameRuleException("Invalid campaign match checkpoint.");
        var game = new GameEngine(snapshot.PlayerNames, options: snapshot.Options, campaign: new CampaignContext(snapshot.Resources));
        game.Phase = snapshot.Phase;
        game.HandNumber = snapshot.HandNumber;
        game.DealSerial = snapshot.DealSerial;
        game.Dealer = snapshot.Dealer;
        game.CurrentSeat = snapshot.CurrentSeat;
        game.HighBid = snapshot.HighBid;
        game.Bidder = snapshot.Bidder;
        game.Contract = snapshot.Contract;
        game.GameWinner = snapshot.GameWinner;
        game.CompletionReason = snapshot.CompletionReason;
        game._biddingTurns = snapshot.BiddingTurns;
        game._variant = snapshot.Variant;
        game._pendingChoice = snapshot.PendingChoice;
        game._pendingActor = snapshot.PendingActor;
        game._duelOpponent = snapshot.DuelOpponent;
        game._duelistUsedThisMatch = snapshot.DuelistUsedThisMatch;
        game._pokerRevealed = snapshot.PokerRevealed;
        game._telepathDone = snapshot.TelepathDone;
        game._firstMatchCard = snapshot.FirstMatchCard;
        game._firstHumanRoundCard = snapshot.FirstHumanRoundCard;
        game._lastMatchCard = snapshot.LastMatchCard;
        game._rollerValue = snapshot.RollerValue;
        game._dejaValue = snapshot.DejaValue;
        game._effectSequence = snapshot.EffectSequence;
        snapshot.Scores.CopyTo(game._scores, 0);
        snapshot.TricksByTeam.CopyTo(game._tricksByTeam, 0);
        snapshot.CardPoints.CopyTo(game._cardPoints, 0);
        snapshot.JokerPoints.CopyTo(game._jokerPoints, 0);
        snapshot.ModifierPoints.CopyTo(game._modifierPoints, 0);
        snapshot.MatchOvertricks.CopyTo(game._matchOvertricks, 0);
        snapshot.HandWalletStart.CopyTo(game._handWalletStart, 0);
        for (var seat = 0; seat < 4; seat++) game._hands[seat].AddRange(snapshot.Hands[seat].Select(game.TokenFor));
        game._auction.AddRange(snapshot.Auction);
        game._currentTrick.AddRange(snapshot.CurrentTrick.Select(p => new CardPlay(p.Seat, game.TokenFor(p.Id))));
        game._completedTricks.AddRange(snapshot.CompletedTricks.Select(t => new CompletedTrick(t.Number, t.Leader, t.Winner,
            t.Plays.Select(p => new CardPlay(p.Seat, game.TokenFor(p.Id))).ToArray())));
        game._handHistory.AddRange(snapshot.HandHistory.Select(CopyHandResult));
        game._events.AddRange(snapshot.Events); game._priorHandEvents.AddRange(snapshot.PriorHandEvents);
        game._pendingExchangeCard = snapshot.PendingExchangeCard is { } id ? game.TokenFor(id) : null;
        foreach (var overlay in snapshot.Overlays) game._overlays.Add(overlay.Id, overlay);
        foreach (var wild in snapshot.WildSuits) game._wildSuits.Add(wild.Id, wild.Suit);
        foreach (var reveal in snapshot.Reveals) game._reveals.Add((reveal.Viewer, reveal.Id));
        foreach (var seat in snapshot.StickyTriggered) game._stickyTriggered.Add(seat);
        foreach (var trick in snapshot.TrickHistory) game._campaignTrickHistory.Add(trick.Number, trick.Plays.ToArray());
        game._trickPoints.AddRange(snapshot.TrickPoints.Select(p => new EffectPoint(p.Team, p.Amount, p.Source)));
        return game;
    }

    private static HandResult CopyHandResult(HandResult result) => result with
    { ScoreBreakdowns = result.ScoreBreakdowns.Select(score => score with { GumballBonuses = score.GumballBonuses.ToArray() }).ToArray() };
}
