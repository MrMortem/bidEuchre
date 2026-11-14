namespace BidEuchre.Core;

public sealed partial record CampaignMatchSnapshot
{
    // Validate before hydrating. Historical IDs may repeat (Eagle); historical faces and
    // effective suits deliberately need not agree with today's deck/trump (Chaos/overlays).
    public bool IsValid()
    {
        try { return Validate(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or IndexOutOfRangeException) { return false; }
    }
    private static bool Seat(int seat) => seat is >= 0 and < 4;
    private static bool Id(CardId id) => id.Value is >= 0 and < 24;
    private static bool Face(Card face) => Enum.IsDefined(face.Suit) && Enum.IsDefined(face.Rank);
    private static bool ValidContract(Contract? c) => c is not null && Enum.IsDefined(c.Bid) && Enum.IsDefined(c.Mode)
        && (c.Mode == ContractMode.Trump ? c.Trump is { } suit && Enum.IsDefined(suit) : c.Trump is null)
        && (c.Bid is not (BidLevel.Two or BidLevel.Three or BidLevel.PartnersBest) || c.Mode == ContractMode.Trump);
    private bool Active(int seat) => Variant == GumballId.Duelist && DuelOpponent is { } opponent
        ? seat == Bidder || seat == opponent : !(Contract is { IsPartnersBest: true } or { IsAlone: true }) || seat != (Bidder!.Value + 2) % 4;
    private int Next(int seat) { do seat = (seat + 1) % 4; while (!Active(seat)); return seat; }
    private bool Plays(SnapshotPlay[] plays, bool completed)
    {
        if (plays is null || plays.Any(p => p is null || !Seat(p.Seat) || !Id(p.Id)) || plays.Select(p => p.Seat).Distinct().Count() != plays.Length
            || plays.Select(p => p.Id).Distinct().Count() != plays.Length) return false;
        if (Contract is null) return plays.Length == 0;
        if (plays.Any(p => !Active(p.Seat))) return false;
        var count = Enumerable.Range(0, 4).Count(Active);
        if (completed ? plays.Length != count : plays.Length >= count) return false;
        for (var i = 1; i < plays.Length; i++) if (plays[i].Seat != Next(plays[i - 1].Seat)) return false;
        return true;
    }
    private bool Validate()
    {
        if (Version != 1 || PlayerNames is not { Length: 4 } || PlayerNames.Any(string.IsNullOrWhiteSpace)
            || Options is null || Options.TargetScore < 1 || Options.HandLimit is <= 0 || Options.NegativeThreshold is >= 0
            || Resources is null || !Resources.IsValid() || Resources.PreviousContract is { } previous && !ValidContract(new(BidLevel.Four, previous.Mode, previous.Trump))
            || !Enum.IsDefined(Phase) || Phase == GamePhase.NotStarted || HandNumber < 1 || DealSerial < HandNumber
            || !Seat(Dealer) || CurrentSeat is { } current && !Seat(current) || Bidder is { } bidder && !Seat(bidder)
            || HighBid is { } bid && !Enum.IsDefined(bid) || GameWinner is { } winner && winner is not (0 or 1)
            || Contract is not null && (!ValidContract(Contract) || Contract.Bid != HighBid || Bidder is null)
            || (Bidder is null) != (HighBid is null) || BiddingTurns is < 0 or > 4
            || Variant is { } variant && variant is not (GumballId.Chaos or GumballId.Poker or GumballId.Telepath or GumballId.Duelist)
            || DuelOpponent is { } opponent && (opponent is not (1 or 3) || Variant != GumballId.Duelist)
            || LastMatchCard is { } last && !Id(last) || EffectSequence < 0) return false;
        if (Scores is not { Length: 2 } || TricksByTeam is not { Length: 2 } || CardPoints is not { Length: 2 }
            || JokerPoints is not { Length: 2 } || ModifierPoints is not { Length: 2 } || MatchOvertricks is not { Length: 2 }
            || HandWalletStart is not { Length: 4 } || HandWalletStart.Any(x => x < 0) || MatchOvertricks.Any(x => x < 0)
            || Hands is not { Length: 4 } || Hands.Any(h => h is null || h.Length > 24 || h.Any(id => !Id(id)))
            || Auction is null || Auction.Length != BiddingTurns || Auction.Any(a => a is null || !Seat(a.Seat) || a.Bid is { } b && !Enum.IsDefined(b))
            || !Plays(CurrentTrick, false) || CompletedTricks is null || CompletedTricks.Length > 6
            || Events is null || PriorHandEvents is null || Events.Any(e => e is null) || PriorHandEvents.Any(e => e is null)) return false;
        var winningBid = Auction.LastOrDefault(a => a.Bid is not null);
        if (HighBid != winningBid?.Bid || Bidder != winningBid?.Seat) return false;
        if (Phase == GamePhase.Bidding && CurrentSeat != (Dealer + BiddingTurns + 1) % 4) return false;
        var held = Hands.SelectMany(h => h).Concat(CurrentTrick.Select(p => p.Id)).ToArray();
        if (held.Distinct().Count() != held.Length) return false;
        for (var i = 0; i < CompletedTricks.Length; i++)
        {
            var t = CompletedTricks[i];
            if (t is null || t.Number != i + 1 || !Plays(t.Plays, true) || t.Leader != t.Plays[0].Seat || !t.Plays.Any(p => p.Seat == t.Winner)) return false;
        }
        if (!TricksByTeam.SequenceEqual(new[] { CompletedTricks.Count(t => t.Winner % 2 == 0), CompletedTricks.Count(t => t.Winner % 2 == 1) })) return false;
        if (TrickHistory is null || TrickHistory.Length != CompletedTricks.Length || TrickHistory.Select(t => t.Number).Distinct().Count() != TrickHistory.Length) return false;
        foreach (var t in TrickHistory)
        {
            if (t is null || t.Number < 1 || t.Number > CompletedTricks.Length || t.Plays is null) return false;
            var logical = CompletedTricks[t.Number - 1].Plays;
            if (t.Plays.Length != logical.Length) return false;
            for (var i = 0; i < logical.Length; i++)
            {
                var p = t.Plays[i];
                if (p is null || p.Card is null || p.Seat != logical[i].Seat || p.Card.Id != logical[i].Id || !Face(p.Card.Face)
                    || !Enum.IsDefined(p.EffectiveSuit) || p.Card.Enhancement is { } e && !Enum.IsDefined(e) || p.Card.Sigil is { } s && !Enum.IsDefined(s)) return false;
            }
        }
        if (Overlays is null || Overlays.Any(o => o is null || !Id(o.Id) || !Face(o.Face) || o.Enhancement is { } e && !Enum.IsDefined(e) || o.Sigil is { } s && !Enum.IsDefined(s))
            || Overlays.Select(o => o.Id).Distinct().Count() != Overlays.Length
            || Reveals is null || Reveals.Any(r => r is null || !Seat(r.Viewer) || !Id(r.Id)) || Reveals.Distinct().Count() != Reveals.Length
            || WildSuits is null || WildSuits.Any(w => w is null || !Id(w.Id) || !Enum.IsDefined(w.Suit) || !CurrentTrick.Any(p => p.Id == w.Id))
            || WildSuits.Select(w => w.Id).Distinct().Count() != WildSuits.Length
            || StickyTriggered is null || StickyTriggered.Any(s => !Seat(s)) || StickyTriggered.Distinct().Count() != StickyTriggered.Length
            || TrickPoints is null || TrickPoints.Any(p => p is null || p.Team is not (0 or 1) || string.IsNullOrWhiteSpace(p.Source))) return false;
        if (HandHistory is null) return false;
        var previousHand = 0;
        foreach (var h in HandHistory)
        {
            if (h is null || h.HandNumber <= previousHand || h.HandNumber > HandNumber || !Seat(h.Bidder) || !ValidContract(h.Contract)
                || h.BiddingTeamTricks < 0 || h.DefendingTeamTricks < 0 || h.BiddingTeamTricks + h.DefendingTeamTricks > 6 || h.Reason is null
                || h.ScoreBreakdowns is not { Count: 2 }) return false;
            previousHand = h.HandNumber;
            for (var team = 0; team < 2; team++)
            {
                var b = h.ScoreBreakdowns[team];
                if (b is null || b.Team != team || b.HandMultiplier < 1 || b.GumballBonuses is null
                    || b.GumballBonuses.Any(g => g is null || !Enum.IsDefined(g.Id) || g.Points < 0)
                    || b.FinalDelta != (team == 0 ? h.TeamZeroDelta : h.TeamOneDelta)) return false;
            }
        }
        var terminal = Phase is GamePhase.HandComplete or GamePhase.GameComplete;
        // DeveloperEndMatch intentionally leaves the pending description/exchange identity
        // visible while removing its actor. Preserve that terminal evidence, never resume it.
        if (!terminal && (PendingChoice is null) != (PendingActor is null)) return false;
        if (PendingChoice is not null && PendingChoice is not ("Swap one card with your partner" or "Choose a player to reveal a card" or "Reveal a partner card" or "Choose an opponent for Duelist")) return false;
        if (!terminal && PendingChoice is not null && !(PendingChoice switch
        {
            "Swap one card with your partner" or "Choose a player to reveal a card" => Phase == GamePhase.Bidding && BiddingTurns == 0 && PendingActor == 0,
            "Reveal a partner card" => Phase == GamePhase.ChoosingContract && Variant == GumballId.Telepath && Bidder is { } b && PendingActor == (b + 2) % 4 && !TelepathDone,
            "Choose an opponent for Duelist" => Phase == GamePhase.ChoosingContract && Variant == GumballId.Duelist && PendingActor == Bidder && DuelOpponent is null && DuelistUsedThisMatch,
            _ => false
        })) return false;
        if (terminal ? CurrentSeat is not null || PendingActor is not null : CurrentSeat is null || GameWinner is not null) return false;
        if ((Phase == GamePhase.GameComplete) != (GameWinner is not null)) return false;
        if (Phase == GamePhase.Bidding && (Contract is not null || BiddingTurns >= 4 || CompletedTricks.Length != 0 || CurrentTrick.Length != 0)) return false;
        if (Phase == GamePhase.ChoosingContract && (Contract is not null || Bidder is null || CurrentSeat != Bidder || BiddingTurns != 4)) return false;
        if (Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard or GamePhase.Playing)
            if (Contract is null || Bidder is null || BiddingTurns != 4) return false;
        if (Phase is GamePhase.Bidding or GamePhase.ChoosingContract or GamePhase.ExchangingBidderCard)
            if (Hands.Any(h => h.Length != 6)) return false;
        if (Phase == GamePhase.ExchangingBidderCard && (Contract?.IsPartnersBest != true || CurrentSeat != Bidder)) return false;
        if (Phase == GamePhase.ExchangingPartnerCard)
        {
            if (Contract?.IsPartnersBest != true || Bidder is not { } b || CurrentSeat != (b + 2) % 4 || PendingExchangeCard is not { } id || !Hands[CurrentSeat.Value].Contains(id)) return false;
            for (var seat = 0; seat < 4; seat++) if (Hands[seat].Length != (seat == b ? 5 : seat == CurrentSeat ? 7 : 6)) return false;
        }
        else if (PendingExchangeCard is { } exchanged && (!terminal || Contract?.IsPartnersBest != true || Bidder is not { } exchangeBidder || !Hands[(exchangeBidder + 2) % 4].Contains(exchanged))) return false;
        if (Phase == GamePhase.Playing && (!Active(CurrentSeat!.Value) || CurrentTrick.Length > 0 && Next(CurrentTrick[^1].Seat) != CurrentSeat)) return false;
        return true;
    }
}
