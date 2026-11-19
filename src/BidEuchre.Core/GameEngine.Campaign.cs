namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    private CampaignContext? _campaign;
    private CampaignSnapshot _resources = CampaignSnapshot.Create(1);
    private CampaignRandom _campaignRandom = new(1);
    private readonly Card[] _identityTokens = GameRules.CreateDeck().ToArray();
    private readonly Dictionary<CardId, CardOverlay> _overlays = [];
    private readonly Dictionary<CardId, Suit> _wildSuits = [];
    private readonly HashSet<(int Viewer, CardId Id)> _reveals = [];
    private bool _dispatchCampaign;
    private GumballId? _variant;
    private GumballId? _pendingBidVariant;
    private string? _pendingChoice;
    private int? _pendingActor;
    private int? _duelOpponent;
    private bool _duelistUsedThisMatch;
    private bool _pokerRevealed;
    private PokerRoundResult? _pokerResult;
    private bool _telepathDone;
    private readonly int[] _cardPoints = new int[2];
    private readonly int[] _jokerPoints = new int[2];
    private readonly int[] _modifierPoints = new int[2];
    private readonly int[] _matchOvertricks = new int[2];
    private readonly HashSet<int> _stickyTriggered = [];
    private bool _firstMatchCard;
    private bool _firstHumanRoundCard;
    private CardId? _lastMatchCard;
    private readonly Dictionary<int, CampaignTrickPlay[]> _campaignTrickHistory = [];
    private int _rollerValue;
    private bool _repeatsPreviousTrump;
    private int[] _handWalletStart = new int[4];
    public int? CampaignActor => _pendingActor ?? CurrentSeat;
    public bool IsCampaign => _campaign is not null;
    public CampaignSnapshot ExportCampaignSnapshot() => _resources with
    {
        Deck = _resources.Deck.ToArray(),
        Wallets = _resources.Wallets.ToArray(),
        Inventory = _resources.Inventory.Copy(),
        RandomState = _campaignRandom.State,
        GeneratedTarot = _resources.GeneratedTarot.Select(cards => cards.ToArray()).ToArray()
    };
    private bool Has(JokerId id) => _resources.Inventory.Has(id);
    private CardId IdFor(Card token) => new(Array.IndexOf(_identityTokens, token));
    private Card TokenFor(CardId id) => id.Value is >= 0 and < 24 ? _identityTokens[id.Value] : throw new GameRuleException("Unknown physical card.");
    private CardState StateFor(CardId id)
    {
        var state = _resources.Deck.Single(c => c.Id == id);
        return _overlays.TryGetValue(id, out var overlay) ? state with { Face = overlay.Face, Enhancement = overlay.Enhancement, Sigil = overlay.Sigil } : state;
    }
    private CardState StateFor(Card token) => StateFor(IdFor(token));
    private CampaignCardView CardView(Card token)
    { var c = StateFor(token); return new(c.Id, c.Face, c.Enhancement, c.Sigil); }
    private void ChangeCard(CardId id, Func<CardState, CardState> update)
    {
        var deck = _resources.Deck.ToArray(); var index = Array.FindIndex(deck, c => c.Id == id);
        if (index < 0) throw new GameRuleException("Unknown physical card.");
        deck[index] = update(deck[index]);
        _resources = _resources with { Deck = deck };
        // A card showing a copied face this hand takes the change on that face too, so it is seen at once.
        if (_overlays.TryGetValue(id, out var shown))
        { var changed = update(new(id, shown.Face, shown.Enhancement, shown.Sigil)); _overlays[id] = new(id, changed.Face, changed.Enhancement, changed.Sigil); }
    }
    private int CampaignNextDealer(int seat)
    { do seat = GameRules.NextSeat(seat); while (seat == 0 && Has(JokerId.SlickWilly)); return seat; }
    private void CampaignDealHand()
    {
        _variant = null; _pendingBidVariant = null; _pendingChoice = null; _pendingActor = null; _duelOpponent = null;
        _campaignTrickHistory.Clear(); _trickPoints.Clear();
        _rollerValue = 0; _repeatsPreviousTrump = false; _firstHumanRoundCard = false;
        _pokerRevealed = false; _pokerResult = null; _telepathDone = false; _reveals.Clear(); _wildSuits.Clear(); _stickyTriggered.Clear();
        Array.Clear(_cardPoints); Array.Clear(_jokerPoints); Array.Clear(_modifierPoints);
        _handWalletStart = _resources.Wallets.ToArray();
        var available = _resources.Deck.Select(c => c.Id).ToList();
        _campaignRandom.Shuffle(available);
        var human = available.Take(6).ToArray();
        var others = _resources.Deck.Select(c => c.Id).Except(human).ToList(); _campaignRandom.Shuffle(others);
        _hands[0].AddRange(human.Select(TokenFor));
        for (var seat = 1; seat < 4; seat++) _hands[seat].AddRange(others.Skip((seat - 1) * 6).Take(6).Select(TokenFor));
        if (Has(JokerId.OneUp)) { _pendingChoice = "Swap one card with your partner"; _pendingActor = 0; }
        else if (Has(JokerId.Cheater)) { _pendingChoice = "Choose a player to reveal a card"; _pendingActor = 0; }
        _resources = _resources with { Revision = _resources.Revision + 1 };
        AutoPassVow();
    }
    private bool CanRaiseCampaignBid(int seat, BidLevel bid) => HighBid is null || bid > HighBid
        || (seat == 0 && Has(JokerId.Alpha) && Bidder == 2 && bid == HighBid);

    private IEnumerable<CampaignCommand.Bid> CampaignBids(int seat)
    {
        if (seat == 0 && Has(JokerId.VowofSilence)) yield break;
        foreach (var bid in GameRules.LegalRaises(null))
        {
            if (!CanRaiseCampaignBid(seat, bid)) continue;
            if (bid == BidLevel.PartnersBest && !_resources.Inventory.Unlocks.Contains(GumballId.PartnersBest)) continue;
            if (bid == BidLevel.Alone && !_resources.Inventory.Unlocks.Contains(GumballId.Alone)) continue;
            yield return new(bid);
            if (bid <= BidLevel.Six)
                foreach (var variant in _resources.Inventory.Unlocks)
                    if (CampaignBidVariantAvailable(seat, bid, variant))
                        yield return new(bid, variant);
        }
        if (_resources.Inventory.Unlocks.Contains(GumballId.Lowballer) && CanRaiseCampaignBid(seat, (BidLevel)2))
            yield return new((BidLevel)2);
    }

    private bool CampaignBidVariantAvailable(int seat, BidLevel bid, GumballId variant) => variant switch
    {
        GumballId.OverAchiever => bid == BidLevel.Three,
        GumballId.RollerCoaster => bid >= BidLevel.Four && _resources.Inventory.Unlocks.Contains(GumballId.HighLow),
        GumballId.Chaos => seat == 0 && bid >= BidLevel.Four,
        GumballId.Poker => seat == 0 && bid == BidLevel.Four,
        GumballId.Duelist => seat == 0 && !_duelistUsedThisMatch && bid is BidLevel.Four or BidLevel.Five,
        GumballId.Telepath => seat == 0 && bid >= BidLevel.Five,
        _ => false
    };

    private int CampaignLeader(int intended)
    {
        var leader = GameRules.CampaignLeader(_variant, Bidder, intended);
        if (leader != intended) _events.Add($"Over Achiever: the lead passes left to {_playerNames[leader]}.");
        return leader;
    }
    private List<CampaignCommand> CampaignLegal(int seat)
    {
        var actions = new List<CampaignCommand>();
        if (_pendingChoice is not null)
        {
            if (seat != _pendingActor) return actions;
            if (_pendingChoice.StartsWith("Swap")) actions.AddRange(_hands[seat].Select(c => new CampaignCommand.SwapPartnerCard(IdFor(c))));
            else if (_pendingChoice.StartsWith("Choose a player")) actions.AddRange(Enumerable.Range(0, 4).Where(s => s != seat).Select(s => new CampaignCommand.ChooseRevealSeat(s)));
            else if (_pendingChoice.StartsWith("Reveal")) actions.AddRange(_hands[seat].Select(c => new CampaignCommand.RevealPartnerCard(IdFor(c))));
            else actions.AddRange(new[] { 1, 3 }.Select(s => new CampaignCommand.SelectOpponent(s)));
            return actions;
        }
        if (CurrentSeat != seat) return actions;
        switch (Phase)
        {
            case GamePhase.Bidding:
                if (CanPass) actions.Add(new CampaignCommand.Pass());
                actions.AddRange(CampaignBids(seat));
                break;
            case GamePhase.ChoosingContract:
                foreach (var mode in GetContractModes())
                {
                    if (mode == ContractMode.Trump) actions.AddRange(Enum.GetValues<Suit>().Select(s => new CampaignCommand.ChooseContract(mode, s)));
                    else actions.Add(new CampaignCommand.ChooseContract(mode));
                }
                break;
            case GamePhase.ExchangingBidderCard:
            case GamePhase.ExchangingPartnerCard:
                actions.AddRange(_hands[seat].Select(c => new CampaignCommand.Exchange(IdFor(c)))); break;
            case GamePhase.Playing:
                actions.AddRange(CampaignLegalPlays(seat)); break;
            case GamePhase.PokerReveal:
                if (seat == 0) actions.Add(new CampaignCommand.CompletePoker());
                break;
        }
        return actions;
    }
    public CampaignGameView CreateCampaignView(int? viewerSeat)
    {
        if (!IsCampaign) throw new GameRuleException("This is not a campaign table.");
        if (viewerSeat is { } v) GameRules.ValidateSeat(v);
        var players = Enumerable.Range(0, 4).Select(seat => new CampaignPlayerView(seat, _hands[seat].Count,
            _hands[seat].Where(c => viewerSeat == seat || _pokerRevealed || _pokerResult is not null || viewerSeat is { } who && _reveals.Contains((who, IdFor(c))))
                .Select(CardView).ToArray(), !CampaignActive(seat))).ToArray();
        return new(_resources.Revision, DealSerial, viewerSeat, players, viewerSeat is { } actor ? CampaignLegal(actor) : [],
            _currentTrick.Select(p => new CampaignTrickPlay(p.Seat, CardView(p.Card), EffectiveCampaignSuit(p.Card))).ToArray(), _events.ToArray(),
            viewerSeat == 0 ? _resources.Inventory.Copy() : CampaignInventory.Empty, viewerSeat is { } wallet ? _resources.Wallets[wallet] : 0, HandNumber, _targetScore, _pendingChoice)
        {
            LastTrick = _campaignTrickHistory.GetValueOrDefault(_completedTricks.Count) ?? [],
            HandLimit = _matchOptions.HandLimit,
            EffectSequence = _effectSequence,
            ScoringEffects = Array.AsReadOnly(_scoringEffects.ToArray()),
            PokerResult = _pokerResult
        };
    }
    /// <summary>Puts a hand in the order its player arranged it. Arranging never costs a turn, and only
    /// Sticky Fingers reads the order: its "leftmost remaining card" is the first card here.</summary>
    public void ArrangeCampaignHand(int seat, IReadOnlyList<CardId> order)
    {
        ViewRevision++;
        if (!IsCampaign) throw new GameRuleException("This is not a campaign table.");
        if (Phase == GamePhase.PokerReveal) throw new GameRuleException("The Poker hands have already been revealed.");
        GameRules.ValidateSeat(seat);
        var hand = _hands[seat];
        var arranged = order?.Select(TokenFor).ToArray() ?? throw new GameRuleException("Arrange the cards in your hand.");
        if (arranged.Length != hand.Count || arranged.Distinct().Count() != hand.Count || arranged.Any(card => !hand.Contains(card)))
            throw new GameRuleException("Arrange exactly the cards in your hand.");
        hand.Clear(); hand.AddRange(arranged);
    }
    public void ExecuteCampaignAction(int seat, CampaignCommand command)
    {
        ViewRevision++;
        if (!IsCampaign) throw new GameRuleException("Campaign actions require a campaign table.");
        GameRules.ValidateSeat(seat);
        if (command is CampaignCommand.UseTarot tarot) { UseItem(() => ResolveTarot(seat, tarot)); CommitCampaign(); return; }
        if (command is CampaignCommand.UseSigil sigil) { UseItem(() => ResolveSigil(seat, sigil)); CommitCampaign(); return; }
        if (!CampaignLegal(seat).Contains(command)) throw new GameRuleException("That campaign action is no longer legal.");
        _dispatchCampaign = true;
        try
        {
            switch (command)
            {
                case CampaignCommand.Bid b: _pendingBidVariant = b.Variant; PlaceBid(seat, b.Level); break;
                case CampaignCommand.Pass: PlaceBid(seat, null); break;
                case CampaignCommand.ChooseContract c: ChooseContract(seat, c.Mode, c.Trump); break;
                case CampaignCommand.Play p:
                    if (p.EffectiveSuit is { } suit) _wildSuits[p.Id] = suit;
                    PlayCard(seat, TokenFor(p.Id)); break;
                case CampaignCommand.Exchange e: ExchangeCard(seat, TokenFor(e.Id)); break;
                case CampaignCommand.SwapPartnerCard swap:
                    var other = _hands[2][_campaignRandom.NextInt(_hands[2].Count)]; var token = TokenFor(swap.Id);
                    _hands[0].Remove(token); _hands[2].Remove(other); _hands[0].Add(other); _hands[2].Add(token);
                    _pendingChoice = Has(JokerId.Cheater) ? "Choose a player to reveal a card" : null; _pendingActor = _pendingChoice is null ? null : 0; break;
                case CampaignCommand.ChooseRevealSeat reveal:
                    var chosen = _hands[reveal.Seat][_campaignRandom.NextInt(_hands[reveal.Seat].Count)]; _reveals.Add((0, IdFor(chosen)));
                    _events.Add("Cheater revealed one card privately."); _pendingChoice = null; _pendingActor = null; break;
                case CampaignCommand.RevealPartnerCard reveal:
                    _reveals.Add((Bidder!.Value, reveal.Id)); _pendingChoice = null; _pendingActor = null; _telepathDone = true; break;
                case CampaignCommand.SelectOpponent duel: _duelOpponent = duel.Seat; _pendingChoice = null; _pendingActor = null; break;
                case CampaignCommand.CompletePoker: CompletePoker(); break;
            }
            AutoPassVow(); CommitCampaign();
        }
        finally { _dispatchCampaign = false; _pendingBidVariant = null; }
    }
    private void CommitCampaign()
    { _resources = _resources with { Revision = _resources.Revision + 1, RandomState = _campaignRandom.State }; _campaign!.Replace(ExportCampaignSnapshot()); }
    private bool CampaignActive(int seat) => _variant == GumballId.Duelist && _duelOpponent is { } opponent
        ? seat == Bidder || seat == opponent : !(Contract?.PartnerSitsOut is true && seat == GameRules.PartnerOf(Bidder!.Value));
    private void CampaignAfterBid()
    {
        if (Phase != GamePhase.ChoosingContract) return;
        if (_variant == GumballId.Chaos)
        {
            // Chaos rolls its own trump every trick, so there is nothing for the bidder to choose.
            Contract = Contract.Create(HighBid!.Value, ContractMode.Trump, (Suit)_campaignRandom.NextInt(4));
            _events.Add($"Chaos trump: {Contract.Trump}.");
            RecordContract();
            BeginTrickPlay();
            return;
        }
        if (_variant == GumballId.Duelist && _duelOpponent is null)
        {
            // A winning auction activates Duelist. An overcalled or rejected bid spends no use.
            _duelistUsedThisMatch = true;
            _pendingChoice = "Choose an opponent for Duelist"; _pendingActor = Bidder;
        }
        if (_variant == GumballId.Telepath && !_telepathDone) { _pendingChoice = "Reveal a partner card"; _pendingActor = GameRules.PartnerOf(Bidder!.Value); }
    }
    private void AutoPassVow()
    {
        if (Phase != GamePhase.Bidding || CurrentSeat != 0 || !Has(JokerId.VowofSilence) || _pendingChoice is not null) return;
        if (HighBid is null && _biddingTurns == 3)
        { var forced = GameRules.NextSeat(0); _auction.Add(new(forced, BidLevel.Three)); HighBid = BidLevel.Three; Bidder = forced; _biddingTurns = 4; Phase = GamePhase.ChoosingContract; CurrentSeat = forced; }
        else { _dispatchCampaign = true; try { PlaceBid(0, null); } finally { _dispatchCampaign = false; } }
    }
    private IEnumerable<CampaignCommand.Play> CampaignLegalPlays(int seat)
    {
        var hand = _hands[seat];
        var lead = _currentTrick.FirstOrDefault();
        var ledSuit = lead is null ? (Suit?)null : EffectiveCampaignSuit(lead.Card);
        var following = ledSuit is { } led
            ? hand.Where(card => StateFor(card).Enhancement == EnhancementId.Wild || EffectiveCampaignSuit(card) == led).ToArray()
            : [];
        var offSuit = hand.Except(following).ToArray();
        var chameleon = lead is not null && StateFor(lead.Card).Sigil == SigilId.Chameleon;
        // Chameleon requires an off-suit card when one is available. Wild counts as following.
        var legal = chameleon && offSuit.Length > 0 ? offSuit : following.Length > 0 ? following : hand.ToArray();
        foreach (var token in legal)
        {
            var state = StateFor(token);
            if (state.Enhancement == EnhancementId.Wild)
            {
                var suits = ledSuit is { } suit ? [suit] : Enum.GetValues<Suit>();
                foreach (var choice in suits) yield return new(state.Id, choice);
            }
            else yield return new(state.Id);
        }
    }

    private Suit? DeclaredWildSuit(CardId id) => _wildSuits.TryGetValue(id, out var suit) ? suit : null;

    private Suit EffectiveCampaignSuit(Card token)
    {
        var card = StateFor(token);
        return CampaignCardRules.EffectiveSuit(card.Face, card.Enhancement, card.Sigil, Contract, DeclaredWildSuit(card.Id));
    }

    private int CampaignTrickWinner()
    {
        var led = EffectiveCampaignSuit(_currentTrick[0].Card);
        return _currentTrick.MaxBy(play =>
        {
            var card = StateFor(play.Card);
            return CampaignCardRules.TrickPower(card.Face, card.Enhancement, card.Sigil, Contract!, led, DeclaredWildSuit(card.Id));
        })!.Seat;
    }

    private void RecordContract()
    {
        var key = new ContractKey(Contract!.Mode, Contract.Trump);
        _repeatsPreviousTrump = _resources.PreviousContract == key;
        _resources = _resources with { PreviousContract = key };
    }
    private void RollChaos() { Contract = Contract! with { Mode = ContractMode.Trump, Trump = (Suit)_campaignRandom.NextInt(4) }; _events.Add($"Chaos trump: {Contract.Trump}."); }
    private void CampaignOnTrick(int winner, IReadOnlyList<CardPlay> plays)
    {
        _campaignTrickHistory[_completedTricks.Count] = plays.Select(p => new CampaignTrickPlay(p.Seat, CardView(p.Card), EffectiveCampaignSuit(p.Card))).ToArray();
        ResolveWonTrick(winner, plays);
    }
    private TeamScoreBreakdown[] CampaignScore(int[] normal) => SettleCampaignHand(normal);
    private void CampaignMatchEnd() => ResolveMatchEnd();
    // A stored Tarot or Sigil is played on any of the human's own turns; a failed one leaves nothing behind.
    private void UseItem(Action resolve)
    {
        var before = ExportCampaignSnapshot().Copy(); var eventCount = _events.Count; var sequence = _effectSequence;
        try { resolve(); }
        catch
        {
            _resources = before; _campaignRandom = new(before.RandomState); _effectSequence = sequence;
            _scoringEffects.RemoveAll(effect => effect.Sequence > sequence);
            _events.RemoveRange(eventCount, _events.Count - eventCount); throw;
        }
    }
    private sealed record PokerSettlement(PokerRoundResult Result, TeamScoreBreakdown[] Breakdowns,
        (int[] Deltas, int Team0, int Team1) Score);

    private PokerSettlement PreparePokerShowdown()
    {
        var result = PokerEvaluator.Showdown(_hands.Select(hand => hand.Select(StateFor)));
        var bonus = result.TeamPoints[0] > 0 ? _resources.Inventory.GumballExtraCopies.GetValueOrDefault(GumballId.Poker) : 0;
        TeamScoreBreakdown[] breakdowns =
        [
            new(0, 0, 0, 0, 0, 1, 0)
            {
                PokerPoints = result.TeamPoints[0],
                GumballBonuses = bonus > 0 ? [new(0, GumballId.Poker, bonus)] : []
            },
            new(1, 0, 0, 0, 0, 1, 0) { PokerPoints = result.TeamPoints[1] }
        ];
        return new(result, breakdowns, CalculateHandScore(breakdowns));
    }

    private void RevealPoker(PokerSettlement poker)
    {
        _resources = _resources with { PreviousContract = null };
        _pokerResult = poker.Result;
        foreach (var winner in poker.Result.Winners)
            _events.Add($"Poker: {_playerNames[winner.Seat]} revealed {winner.Category}.");
        foreach (var reward in poker.Breakdowns[0].GumballBonuses)
            _events.Add($"Poker duplicates: +{reward.Points} points for team 1.");
        RecordHandScore(poker.Breakdowns, poker.Score, "Poker showdown.", madeContract: false, poker.Result);
        Phase = GamePhase.PokerReveal;
        CurrentSeat = 0;
    }

    private void ClearPokerReveal()
    {
        _pokerResult = null;
        foreach (var hand in _hands) hand.Clear();
    }

    private void CompletePoker()
    {
        ClearPokerReveal();
        FinishScoredHand();
        if (Phase == GamePhase.HandComplete) StartNextHand();
    }
}
