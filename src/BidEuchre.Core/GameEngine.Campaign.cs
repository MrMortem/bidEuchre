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
    private PlanetId? _variant;
    private PlanetId? _pendingBidVariant;
    private string? _pendingChoice;
    private int? _pendingActor;
    private int? _duelOpponent;
    private bool _pokerRevealed;
    private bool _telepathDone;
    private readonly int[] _cardPoints = new int[2];
    private readonly int[] _jokerPoints = new int[2];
    private readonly int[] _modifierPoints = new int[2];
    private readonly int[] _matchOvertricks = new int[2];
    private readonly HashSet<int> _stickyTriggered = [];
    private bool _firstMatchCard;
    private CardId? _lastMatchCard;
    private readonly Dictionary<int, CampaignTrickPlay[]> _campaignTrickHistory = [];
    private int _rollerValue;
    private int _dejaValue;
    private int[] _handWalletStart = new int[4];
    public int? CampaignActor => _pendingActor ?? CurrentSeat;
    public bool IsCampaign => _campaign is not null;
    public CampaignSnapshot ExportCampaignSnapshot() => _resources with
    {
        Deck = _resources.Deck.ToArray(),
        Wallets = _resources.Wallets.ToArray(),
        Inventory = _resources.Inventory.Copy(),
        RandomState = _campaignRandom.State
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
        if (deck.Count(c => c.Enhancement == EnhancementId.Stinky) > 18) throw new GameRuleException("At least six non-Stinky cards must remain.");
        _resources = _resources with { Deck = deck };
    }
    private int CampaignNextDealer(int seat)
    { do seat = GameRules.NextSeat(seat); while (seat == 0 && Has(JokerId.SlickWilly)); return seat; }
    private void CampaignDealHand()
    {
        _variant = null; _pendingBidVariant = null; _pendingChoice = null; _pendingActor = null; _duelOpponent = null;
        _campaignTrickHistory.Clear(); _trickPoints.Clear();
        _pokerRevealed = false; _telepathDone = false; _reveals.Clear(); _wildSuits.Clear(); _stickyTriggered.Clear();
        Array.Clear(_cardPoints); Array.Clear(_jokerPoints); Array.Clear(_modifierPoints);
        _handWalletStart = _resources.Wallets.ToArray();
        var available = _resources.Deck.Where(c => c.Enhancement != EnhancementId.Stinky).Select(c => c.Id).ToList();
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
    private IEnumerable<CampaignCommand.Bid> CampaignBids(int seat)
    {
        if (seat == 0 && Has(JokerId.VowofSilence)) yield break;
        foreach (var bid in GameRules.LegalRaises(null))
        {
            if (HighBid is not null && bid <= HighBid && !(seat == 0 && Has(JokerId.Alpha) && Bidder == 2 && bid == HighBid && bid <= BidLevel.Six)) continue;
            if (seat == 0 && bid == BidLevel.PartnersBest && !_resources.Inventory.Unlocks.Contains(PlanetId.PartnersBest)) continue;
            if (seat == 0 && bid == BidLevel.Alone && !_resources.Inventory.Unlocks.Contains(PlanetId.Alone)) continue;
            yield return new(bid);
            if (seat == 0 && bid <= BidLevel.Six)
                foreach (var variant in _resources.Inventory.Unlocks)
                    if (variant is PlanetId.Chaos && bid >= BidLevel.Four || variant is PlanetId.Poker && bid == BidLevel.Four
                        || variant is PlanetId.Duelist && bid <= BidLevel.Five || variant is PlanetId.Telepath && bid >= BidLevel.Five)
                        yield return new(bid, variant);
        }
        if (seat == 0 && _resources.Inventory.Unlocks.Contains(PlanetId.Lowballer) && (HighBid is null || (int)HighBid < 2))
            yield return new((BidLevel)2);
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
                if (seat == 0 && !Has(JokerId.VowofSilence) && _resources.Inventory.Unlocks.Contains(PlanetId.PanicButton)) actions.Add(new CampaignCommand.PanicButton());
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
        }
        return actions;
    }
    public CampaignGameView CreateCampaignView(int? viewerSeat)
    {
        if (!IsCampaign) throw new GameRuleException("This is not a campaign table.");
        if (viewerSeat is { } v) GameRules.ValidateSeat(v);
        var players = Enumerable.Range(0, 4).Select(seat => new CampaignPlayerView(seat, _hands[seat].Count,
            _hands[seat].Where(c => viewerSeat == seat || _pokerRevealed || StateFor(c).Sigil == SigilId.Owl || viewerSeat is { } who && _reveals.Contains((who, IdFor(c))))
                .Select(CardView).ToArray(), !CampaignActive(seat))).ToArray();
        return new(_resources.Revision, DealSerial, viewerSeat, players, viewerSeat is { } actor ? CampaignLegal(actor) : [],
            _currentTrick.Select(p => new CampaignTrickPlay(p.Seat, CardView(p.Card), EffectiveCampaignSuit(p.Card))).ToArray(), _events.ToArray(),
            viewerSeat == 0 ? _resources.Inventory.Copy() : CampaignInventory.Empty, viewerSeat is { } wallet ? _resources.Wallets[wallet] : 0, HandNumber, _targetScore, _pendingChoice)
        { LastTrick = _campaignTrickHistory.GetValueOrDefault(_completedTricks.Count) ?? [] };
    }
    public void ExecuteCampaignAction(int seat, CampaignCommand command)
    {
        if (!IsCampaign) throw new GameRuleException("Campaign actions require a campaign table.");
        GameRules.ValidateSeat(seat);
        if (command is CampaignCommand.UseTarot tarot) { UseTarot(seat, tarot); CommitCampaign(); return; }
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
                case CampaignCommand.PanicButton:
                    _scores[0] -= 8; _events.Add("Panic Button: your team -8; hand consumed.");
                    CurrentSeat = null; GameWinner = DetermineGameWinner(); Phase = GameWinner is null ? GamePhase.HandComplete : GamePhase.GameComplete;
                    if (GameWinner is not null) CampaignMatchEnd(); break;
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
            }
            AutoPassVow(); CommitCampaign();
        }
        finally { _dispatchCampaign = false; }
    }
    private void CommitCampaign()
    { _resources = _resources with { Revision = _resources.Revision + 1, RandomState = _campaignRandom.State }; _campaign!.Replace(ExportCampaignSnapshot()); }
    private bool CampaignActive(int seat) => _variant == PlanetId.Duelist && _duelOpponent is { } opponent
        ? seat == Bidder || seat == opponent : !(Contract?.PartnerSitsOut is true && seat == GameRules.PartnerOf(Bidder!.Value));
    private void CampaignAfterBid()
    {
        if (Phase != GamePhase.ChoosingContract) return;
        if (_variant == PlanetId.Duelist && _duelOpponent is null) { _pendingChoice = "Choose an opponent for Duelist"; _pendingActor = Bidder; }
        if (_variant == PlanetId.Telepath && !_telepathDone) { _pendingChoice = "Reveal a partner card"; _pendingActor = GameRules.PartnerOf(Bidder!.Value); }
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
        var hand = _hands[seat]; var following = _currentTrick.Count == 0 ? [] : hand.Where(c => StateFor(c).Enhancement == EnhancementId.Wild || EffectiveCampaignSuit(c) == EffectiveCampaignSuit(_currentTrick[0].Card)).ToArray();
        var chameleon = _currentTrick.Count > 0 && StateFor(_currentTrick[0].Card).Sigil == SigilId.Chameleon;
        var off = hand.Except(following).ToArray();
        var legal = chameleon && off.Length > 0 ? off : following.Length > 0 && !chameleon ? following : hand.ToArray();
        foreach (var token in legal)
        {
            var state = StateFor(token);
            if (state.Enhancement == EnhancementId.Wild)
            {
                var suits = _currentTrick.Count > 0 && following.Contains(token) && !chameleon ? new[] { EffectiveCampaignSuit(_currentTrick[0].Card) } : Enum.GetValues<Suit>();
                foreach (var suit in suits) yield return new(state.Id, suit);
            }
            else yield return new(state.Id);
        }
    }
    private Card EffectiveCampaignCard(Card token)
    {
        var state = StateFor(token); var face = state.Face;
        if (state.Sigil == SigilId.Bear) face = face with { Rank = face.Rank == Rank.Ace ? Rank.Nine : (Rank)((int)face.Rank + 1) };
        if (state.Enhancement == EnhancementId.Wild && _wildSuits.TryGetValue(state.Id, out var suit)) face = face with { Suit = suit };
        return face;
    }
    private Suit EffectiveCampaignSuit(Card token)
    {
        var state = StateFor(token);
        if (state.Enhancement == EnhancementId.Wild && _wildSuits.TryGetValue(state.Id, out var selected)) return selected;
        return Contract is null ? EffectiveCampaignCard(token).Suit : GameRules.EffectiveSuit(EffectiveCampaignCard(token), Contract);
    }
    private int CampaignTrickWinner()
    {
        var led = EffectiveCampaignSuit(_currentTrick[0].Card);
        int Strength(CardPlay play)
        {
            var card = EffectiveCampaignCard(play.Card); var suit = EffectiveCampaignSuit(play.Card);
            if (Contract!.Mode == ContractMode.Trump && suit == Contract.Trump)
                return card.Rank == Rank.Jack ? StateFor(play.Card).Enhancement == EnhancementId.Wild || card.Suit == Contract.Trump ? 400 : 399 : 200 + (int)card.Rank;
            if (suit != led) return int.MinValue;
            return Contract.Mode == ContractMode.Low ? 100 - (int)card.Rank : 100 + (int)card.Rank;
        }
        return _currentTrick.MaxBy(Strength)!.Seat;
    }
    private void CampaignContractSelected()
    {
        var key = new ContractKey(Contract!.Mode, Contract.Trump);
        if (_resources.PreviousContract == key) _dejaValue++;
        _resources = _resources with { PreviousContract = key };
        if (_variant == PlanetId.Poker) ResolvePoker();
        if (_variant == PlanetId.Chaos) RollChaos();
    }
    private void RollChaos() { Contract = Contract! with { Mode = ContractMode.Trump, Trump = (Suit)_campaignRandom.NextInt(4) }; _events.Add($"Chaos trump: {Contract.Trump}."); }
    private void CampaignOnPlay(int seat, Card token) => ResolvePlayedCard(seat, token);
    private void CampaignOnTrick(int winner, IReadOnlyList<CardPlay> plays)
    {
        _campaignTrickHistory[_completedTricks.Count] = plays.Select(p => new CampaignTrickPlay(p.Seat, CardView(p.Card), EffectiveCampaignSuit(p.Card))).ToArray();
        ResolveWonTrick(winner, plays);
    }
    private int[] CampaignScore(int[] normal) => SettleCampaignHand(normal);
    private void CampaignMatchEnd() => ResolveMatchEnd();
    private void UseTarot(int seat, CampaignCommand.UseTarot tarot) => ResolveTarot(seat, tarot);
    private void ResolvePoker()
    {
        _pokerRevealed = true;
        var values = Enumerable.Range(0, 4).Select(s => PokerEvaluator.Best(_hands[s].Select(c => StateFor(c).Face).ToArray())).ToArray();
        var best = values.Max()!;
        foreach (var team in Enumerable.Range(0, 4).Where(s => values[s].CompareTo(best) == 0).Select(s => s % 2).Distinct()) _jokerPoints[team] += 4;
        _events.Add("Poker: all hands revealed; best hand awards 4 points per winning team.");
    }
}
