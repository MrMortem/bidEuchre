namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    private readonly string[] _playerNames;
    private readonly List<Card>[] _hands = [[], [], [], []];
    private readonly List<AuctionAction> _auction = [];
    private readonly List<CardPlay> _currentTrick = [];
    private readonly List<CompletedTrick> _completedTricks = [];
    private readonly List<HandResult> _handHistory = [];
    private readonly List<string> _events = [];
    private readonly int[] _scores;
    private readonly int _targetScore;
    private readonly MatchOptions _matchOptions;
    private readonly int[] _tricksByTeam = [0, 0];
    private Random _random;
    private int _biddingTurns;
    private Card? _pendingExchangeCard;

    public GameEngine(IReadOnlyList<string> playerNames, int? randomSeed = null, MatchOptions? options = null, CampaignContext? campaign = null)
    {
        if (playerNames.Count != 4)
        {
            throw new ArgumentException("Exactly four player names are required.", nameof(playerNames));
        }

        if (playerNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Player names cannot be blank.", nameof(playerNames));
        }

        _playerNames = playerNames.Select(name => name.Trim()).ToArray();
        _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);

        options ??= new MatchOptions();
        if (options.TargetScore < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The target score must be at least 1.");
        }

        if (options.HandLimit is <= 0 || options.NegativeThreshold is >= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        _matchOptions = options;
        _campaign = campaign;
        if (campaign is not null)
        {
            _resources = campaign.Snapshot(); _campaignRandom = new(_resources.RandomState);
            _dejaValue = _resources.Inventory.Jokers.FirstOrDefault(j => j.Id == JokerId.DejaVu)?.Value ?? 0;
        }
        _targetScore = options.TargetScore;
        _scores = [options.StartingScoreTeam0, options.StartingScoreTeam1];
    }

    public GamePhase Phase { get; private set; } = GamePhase.NotStarted;
    public int HandNumber { get; private set; }
    public long DealSerial { get; private set; }
    public int Dealer { get; private set; }
    public int? CurrentSeat { get; private set; }
    public BidLevel? HighBid { get; private set; }
    public int? Bidder { get; private set; }
    public Contract? Contract { get; private set; }
    public int? GameWinner { get; private set; }
    public IReadOnlyList<int> Scores => _scores;
    public IReadOnlyList<int> TricksByTeam => _tricksByTeam;
    public IReadOnlyList<AuctionAction> Auction => _auction;
    public IReadOnlyList<CardPlay> CurrentTrick => _currentTrick;
    public IReadOnlyList<CompletedTrick> CompletedTricks => _completedTricks;
    public IReadOnlyList<HandResult> HandHistory => _handHistory;

    public void StartGame(int? dealer = null)
    {
        EnsurePhase(GamePhase.NotStarted);
        Dealer = dealer ?? _random.Next(4);
        if (IsCampaign && Dealer == 0 && Has(JokerId.SlickWilly)) Dealer = CampaignNextDealer(Dealer);
        GameRules.ValidateSeat(Dealer);
        DealHand();
    }

    public void StartNextHand()
    {
        EnsurePhase(GamePhase.HandComplete);
        if (GameWinner is not null)
        {
            throw new GameRuleException("The game has already been won.");
        }

        Dealer = IsCampaign ? CampaignNextDealer(Dealer) : GameRules.NextSeat(Dealer);
        DealHand();
    }

    public void Redeal()
    {
        if (Phase is GamePhase.NotStarted or GamePhase.GameComplete)
        {
            throw new GameRuleException("There is no active hand to redeal.");
        }

        HandNumber--;
        DealHand();
        _events.Add("The hand was redealt after a misdeal.");
    }

    public IReadOnlyList<BidLevel> GetLegalBids() =>
        Phase is GamePhase.Bidding ? IsCampaign ? CampaignBids(CurrentSeat!.Value).Select(b => b.Level).Distinct().ToArray() : GameRules.LegalRaises(HighBid) : [];

    public bool CanPass =>
        Phase is GamePhase.Bidding && !(CurrentSeat == Dealer && HighBid is null);

    public void PlaceBid(int seat, BidLevel? bid)
    {
        if (IsCampaign && !_dispatchCampaign) throw new GameRuleException("Use a campaign bidding action.");
        EnsureTurn(GamePhase.Bidding, seat);

        if (bid is null)
        {
            if (!CanPass)
            {
                throw new GameRuleException("The dealer must bid when everyone else passes.");
            }
        }
        else if (!GetLegalBids().Contains(bid.Value))
        {
            throw new GameRuleException($"{bid} is not a legal raise over {HighBid?.ToString() ?? "no bid"}.");
        }

        _auction.Add(new AuctionAction(seat, bid));
        _biddingTurns++;
        if (bid is not null)
        {
            if (IsCampaign) { _variant = _pendingBidVariant; _pendingBidVariant = null; }
            HighBid = bid;
            Bidder = seat;
            _events.Add($"{_playerNames[seat]} bid {DisplayBid(bid.Value)}.");
        }
        else
        {
            _events.Add($"{_playerNames[seat]} passed.");
        }

        if (_biddingTurns == 4)
        {
            if (Bidder is null || HighBid is null)
            {
                throw new GameRuleException("The auction ended without the dealer's required bid.");
            }

            Phase = GamePhase.ChoosingContract;
            CurrentSeat = Bidder;
            _events.Add($"{_playerNames[Bidder.Value]} won the auction.");
            if (IsCampaign) CampaignAfterBid();
            return;
        }

        CurrentSeat = GameRules.NextSeat(seat);
    }

    public void ChooseContract(int seat, ContractMode mode, Suit? trump = null)
    {
        EnsureTurn(GamePhase.ChoosingContract, seat);
        if (seat != Bidder)
        {
            throw new GameRuleException("Only the auction winner may choose the contract.");
        }

        Contract = IsCampaign && (int)HighBid! == 2
            ? mode == ContractMode.Trump && trump is not null ? new Contract(HighBid.Value, mode, trump) : throw new GameRuleException("Bid 2 requires trump.")
            : Contract.Create(HighBid!.Value, mode, trump);
        if (IsCampaign) CampaignContractSelected();
        _events.Add($"{_playerNames[seat]} chose {DisplayContract(Contract)}.");

        if (Contract.IsPartnersBest)
        {
            Phase = GamePhase.ExchangingBidderCard;
            CurrentSeat = Bidder;
            return;
        }

        BeginTrickPlay();
    }

    public void ExchangeCard(int seat, Card card)
    {
        if (IsCampaign && !_dispatchCampaign) throw new GameRuleException("Select a physical campaign card.");
        if (Contract?.IsPartnersBest is not true || Bidder is null)
        {
            throw new GameRuleException("Cards can only be exchanged during Partners Best.");
        }

        if (Phase is GamePhase.ExchangingBidderCard)
        {
            EnsureTurn(GamePhase.ExchangingBidderCard, seat);
            EnsureCardInHand(seat, card);
            _hands[seat].Remove(card);
            var partner = GameRules.PartnerOf(seat);
            _hands[partner].Add(card);
            _pendingExchangeCard = card;
            Phase = GamePhase.ExchangingPartnerCard;
            CurrentSeat = partner;
            return;
        }

        if (Phase is GamePhase.ExchangingPartnerCard)
        {
            EnsureTurn(GamePhase.ExchangingPartnerCard, seat);
            if (seat != GameRules.PartnerOf(Bidder.Value))
            {
                throw new GameRuleException("Only the bidder's partner may return a card.");
            }

            EnsureCardInHand(seat, card);
            _hands[seat].Remove(card);
            _hands[Bidder.Value].Add(card);
            _pendingExchangeCard = null;
            _events.Add("The Partners Best cards were exchanged privately.");
            BeginTrickPlay();
            return;
        }

        throw new GameRuleException("The game is not waiting for a Partners Best exchange.");
    }

    public IReadOnlyList<Card> GetLegalCards(int seat)
    {
        if (Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard)
        {
            return CurrentSeat == seat ? _hands[seat].OrderBy(card => card.Code).ToArray() : [];
        }

        if (Phase is not GamePhase.Playing || CurrentSeat != seat || Contract is null)
        {
            return [];
        }

        return IsCampaign ? CampaignLegalPlays(seat).Select(p => TokenFor(p.Id)).Distinct().ToArray() : GameRules.LegalCards(_hands[seat], _currentTrick, Contract);
    }

    public void PlayCard(int seat, Card card)
    {
        if (IsCampaign && !_dispatchCampaign) throw new GameRuleException("Select a physical campaign card.");
        EnsureTurn(GamePhase.Playing, seat);
        if (!GetLegalCards(seat).Contains(card))
        {
            throw new GameRuleException($"{card} is not a legal play.");
        }

        _hands[seat].Remove(card);
        _currentTrick.Add(new CardPlay(seat, card));
        _events.Add($"{_playerNames[seat]} played {(IsCampaign ? StateFor(card).Face.Code : card.Code)}.");
        if (IsCampaign) CampaignOnPlay(seat, card);

        var activePlayers = IsCampaign ? Enumerable.Range(0, 4).Count(CampaignActive) : Contract!.PartnerSitsOut ? 3 : 4;
        if (_currentTrick.Count < activePlayers)
        {
            CurrentSeat = NextActiveSeat(seat);
            return;
        }

        var winner = IsCampaign ? CampaignTrickWinner() : GameRules.DetermineTrickWinner(_currentTrick, Contract!);
        var completed = new CompletedTrick(
            _completedTricks.Count + 1,
            _currentTrick[0].Seat,
            winner,
            _currentTrick.ToArray());
        _completedTricks.Add(completed);
        _tricksByTeam[GameRules.TeamForSeat(winner)]++;
        _events.Add($"{_playerNames[winner]} won trick {completed.Number}.");
        if (IsCampaign) CampaignOnTrick(winner, _currentTrick.ToArray());
        _currentTrick.Clear();
        _wildSuits.Clear();

        if (_completedTricks.Count == 6)
        {
            ScoreCompletedHand("All six tricks were played.");
            return;
        }

        if (IsCampaign && _variant == PlanetId.Chaos) RollChaos();
        CurrentSeat = winner;
    }

    public void ApplyIllegalPlayPenalty(int offendingSeat)
    {
        if (Phase is not GamePhase.Playing || Bidder is null || Contract is null)
        {
            throw new GameRuleException("Illegal-play penalties require an active contract in trick play.");
        }

        GameRules.ValidateSeat(offendingSeat);
        var biddingTeam = GameRules.TeamForSeat(Bidder.Value);
        var offendingTeam = GameRules.TeamForSeat(offendingSeat);
        var deltas = new int[2];

        if (offendingTeam == biddingTeam)
        {
            deltas[biddingTeam] = FailureScore(Contract);
            deltas[1 - biddingTeam] = 6;
            CompleteHandWithDeltas(deltas, "The bidding team made an illegal play.");
        }
        else
        {
            deltas[biddingTeam] = MaximumSuccessScore(Contract);
            deltas[1 - biddingTeam] = 0;
            CompleteHandWithDeltas(deltas, "The defending team made an illegal play.");
        }
    }

    public GameView CreateView(int? viewerSeat = null, bool revealAllCards = false)
    {
        if (viewerSeat is not null)
        {
            GameRules.ValidateSeat(viewerSeat.Value);
        }

        var sittingOut = Contract?.PartnerSitsOut is true &&
            Bidder is not null &&
            Phase is GamePhase.Playing or GamePhase.HandComplete or GamePhase.GameComplete
            ? GameRules.PartnerOf(Bidder.Value)
            : -1;
        var players = Enumerable.Range(0, 4)
            .Select(seat => new PlayerView(
                seat,
                _playerNames[seat],
                GameRules.TeamForSeat(seat),
                _hands[seat].Count,
                revealAllCards || viewerSeat == seat ? _hands[seat].OrderBy(card => card.Code).ToArray() : null,
                seat == sittingOut))
            .ToArray();

        var isViewingCurrentSeat = viewerSeat is not null && viewerSeat == CurrentSeat;
        var legalBids = isViewingCurrentSeat && Phase is GamePhase.Bidding ? GetLegalBids() : [];
        var modes = isViewingCurrentSeat && Phase is GamePhase.ChoosingContract
            ? GetContractModes()
            : [];
        var suits = isViewingCurrentSeat && Phase is GamePhase.ChoosingContract
            ? Enum.GetValues<Suit>()
            : [];
        var cards = isViewingCurrentSeat ? GetLegalCards(viewerSeat!.Value) : [];

        var result = new GameView(
            Phase,
            HandNumber,
            Dealer,
            CurrentSeat,
            _scores.ToArray(),
            players,
            _auction.ToArray(),
            HighBid,
            Bidder,
            Contract,
            _currentTrick.ToArray(),
            _completedTricks.ToArray(),
            _tricksByTeam.ToArray(),
            GameWinner,
            new LegalActionView(
                isViewingCurrentSeat && CanPass,
                legalBids,
                modes,
                suits,
                cards),
            _events.ToArray(),
            _targetScore)
        {
            DealSerial = DealSerial
        };
        if (IsCampaign)
        {
            var cv = CreateCampaignView(viewerSeat);
            return result with
            {
                Players = result.Players.Select(p => p with { Cards = p.Cards is null ? null : p.Cards.Select(c => StateFor(c).Face).ToArray(), IsSittingOut = !CampaignActive(p.Seat) }).ToArray(),
                CurrentTrick = result.CurrentTrick.Select(p => p with { Card = StateFor(p.Card).Face }).ToArray(),
                CompletedTricks = result.CompletedTricks.Select(t => t with { Plays = t.Plays.Select((p, i) => p with { Card = _campaignTrickHistory.TryGetValue(t.Number, out var recorded) ? recorded[i].Card.Face : StateFor(p.Card).Face }).ToArray() }).ToArray(),
                Campaign = cv
            };
        }
        return result;
    }

    private IReadOnlyList<ContractMode> GetContractModes()
    {
        if (HighBid is BidLevel.Three or BidLevel.PartnersBest || (int?)HighBid == 2 || IsCampaign && _variant is PlanetId.Chaos or PlanetId.Telepath)
        {
            return [ContractMode.Trump];
        }

        return [ContractMode.High, ContractMode.Low, ContractMode.Trump];
    }

    private void DealHand()
    {
        HandNumber++;
        DealSerial++;
        HighBid = null;
        Bidder = null;
        Contract = null;
        CurrentSeat = GameRules.NextSeat(Dealer);
        GameWinner = null;
        _biddingTurns = 0;
        _pendingExchangeCard = null;
        _auction.Clear();
        _currentTrick.Clear();
        _completedTricks.Clear();
        Array.Clear(_tricksByTeam);
        foreach (var hand in _hands)
        {
            hand.Clear();
        }

        if (IsCampaign)
        {
            Phase = GamePhase.Bidding; _events.Clear(); _events.Add($"Hand {HandNumber}: dealer {_playerNames[Dealer]}.");
            CampaignDealHand(); return;
        }
        var deck = GameRules.CreateDeck().ToArray();
        _random.Shuffle(deck);
        for (var index = 0; index < deck.Length; index++)
        {
            _hands[index % 4].Add(deck[index]);
        }

        Phase = GamePhase.Bidding;
        _events.Clear();
        _events.Add($"Hand {HandNumber} began. {_playerNames[Dealer]} is the dealer.");
    }

    private void BeginTrickPlay()
    {
        Phase = GamePhase.Playing;
        CurrentSeat = NextActiveSeat(Dealer);
    }

    private int NextActiveSeat(int seat)
    {
        var next = GameRules.NextSeat(seat);
        if (IsCampaign) { while (!CampaignActive(next)) next = GameRules.NextSeat(next); return next; }
        if (Contract?.PartnerSitsOut is true && Bidder is not null && next == GameRules.PartnerOf(Bidder.Value))
        {
            next = GameRules.NextSeat(next);
        }

        return next;
    }

    private void ScoreCompletedHand(string reason)
    {
        var biddingTeam = GameRules.TeamForSeat(Bidder!.Value);
        var defendingTeam = 1 - biddingTeam;
        var deltas = new int[2];
        var biddingTricks = _tricksByTeam[biddingTeam];
        var defendingTricks = _tricksByTeam[defendingTeam];

        if (Contract!.IsPartnersBest)
        {
            deltas[biddingTeam] = biddingTricks == 6 ? 12 : -12;
        }
        else if (Contract.IsAlone)
        {
            deltas[biddingTeam] = biddingTricks == 6 ? 24 : -24;
        }
        else
        {
            deltas[biddingTeam] = biddingTricks >= Contract.RequiredTricks
                ? biddingTricks
                : -Contract.RequiredTricks;
        }

        deltas[defendingTeam] = defendingTricks;
        if (IsCampaign) deltas = CampaignScore(deltas);
        CompleteHandWithDeltas(deltas, reason);
    }

    private void CompleteHandWithDeltas(int[] deltas, string reason)
    {
        var biddingTeam = GameRules.TeamForSeat(Bidder!.Value);
        _scores[0] += deltas[0];
        _scores[1] += deltas[1];
        _handHistory.Add(new HandResult(
            HandNumber,
            Bidder.Value,
            Contract!,
            _tricksByTeam[biddingTeam],
            _tricksByTeam[1 - biddingTeam],
            deltas[0],
            deltas[1],
            reason));
        _events.Add($"Hand complete: Team 1 {Signed(deltas[0])}, Team 2 {Signed(deltas[1])}.");
        CurrentSeat = null;

        GameWinner = DetermineGameWinner();
        Phase = GameWinner is null ? GamePhase.HandComplete : GamePhase.GameComplete;
        if (GameWinner is not null)
        {
            if (IsCampaign) CampaignMatchEnd();
            _events.Add($"Team {GameWinner.Value + 1} won the game.");
        }
    }

    private int? DetermineGameWinner()
    {
        if (_matchOptions.HandLimit is { } limit || _matchOptions.NegativeThreshold is not null)
        {
            if (_scores[0] == _scores[1]) return null;
            var ended = (_matchOptions.HandLimit is { } hands && HandNumber >= hands)
                || _scores.Max() >= _targetScore
                || (_matchOptions.NegativeThreshold is { } floor && _scores.Min() <= floor);
            return ended ? (_scores[0] > _scores[1] ? 0 : 1) : null;
        }
        if (_scores[0] < _targetScore && _scores[1] < _targetScore)
        {
            return null;
        }

        if (_scores[0] == _scores[1])
        {
            return null;
        }

        return _scores[0] > _scores[1] ? 0 : 1;
    }

    private static int FailureScore(Contract contract) => contract.Bid switch
    {
        BidLevel.PartnersBest => -12,
        BidLevel.Alone => -24,
        _ => -contract.RequiredTricks
    };

    private static int MaximumSuccessScore(Contract contract) => contract.Bid switch
    {
        BidLevel.PartnersBest => 12,
        BidLevel.Alone => 24,
        _ => 6
    };

    private void EnsureTurn(GamePhase expectedPhase, int seat)
    {
        GameRules.ValidateSeat(seat);
        EnsurePhase(expectedPhase);
        if (CurrentSeat != seat)
        {
            throw new GameRuleException($"It is not {_playerNames[seat]}'s turn.");
        }
    }

    private void EnsurePhase(GamePhase expected)
    {
        if (Phase != expected)
        {
            throw new GameRuleException($"Expected phase {expected}, but the game is in {Phase}.");
        }
    }

    private void EnsureCardInHand(int seat, Card card)
    {
        if (!_hands[seat].Contains(card))
        {
            throw new GameRuleException($"{_playerNames[seat]} does not hold {card}.");
        }
    }

    private static string DisplayBid(BidLevel bid) => bid switch
    {
        BidLevel.PartnersBest => "Partners Best",
        _ => bid.ToString()
    };

    private static string DisplayContract(Contract contract)
    {
        var prefix = contract.Bid switch
        {
            BidLevel.PartnersBest => "Partners Best",
            BidLevel.Alone => "Alone",
            _ => ((int)contract.Bid).ToString()
        };
        return contract.Mode is ContractMode.Trump
            ? $"{prefix} in {contract.Trump}"
            : $"{prefix} {contract.Mode}";
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
}
