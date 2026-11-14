namespace BidEuchre.Core;

// Effect handlers live in the engine's partial lifecycle, never in rendering callbacks.
public sealed partial class GameEngine
{
    private sealed record EffectPoint(int Team, int Amount, string Source);
    private readonly List<EffectPoint> _trickPoints = [];
    private int _effectSequence;
    private void Point(int seat, int amount, string source)
    { _trickPoints.Add(new(seat % 2, amount, source)); _events.Add($"Effect {++_effectSequence}: {source} {Signed(amount)} points for team {seat % 2 + 1}."); }
    private void Money(int seat, int amount, string source)
    {
        _resources.Wallets[seat] = checked(_resources.Wallets[seat] + amount);
        _events.Add($"Effect {++_effectSequence}: {source} +${amount} for {_playerNames[seat]}.");
    }
    private void GenerateTarot(int seat)
    {
        if (seat != 0)
        {
            var held = _resources.GeneratedTarot[seat];
            if (held.Length >= 2) { _events.Add($"{_playerNames[seat]}'s Tarot inventory is full."); return; }
            var generated = (TarotId)_campaignRandom.NextInt(Enum.GetValues<TarotId>().Length);
            var inventories = _resources.GeneratedTarot.Select(cards => cards.ToArray()).ToArray();
            inventories[seat] = [.. held, generated]; _resources = _resources with { GeneratedTarot = inventories };
            _events.Add($"{_playerNames[seat]} generated a Tarot (stored; bots do not use items)."); return;
        }
        var inventory = _resources.Inventory;
        if (!inventory.HasItemRoom) { _events.Add("Tarot creation skipped: inventory full."); return; }
        var card = (TarotId)_campaignRandom.NextInt(Enum.GetValues<TarotId>().Length);
        _resources = _resources with { Inventory = inventory with { Tarot = [.. inventory.Tarot, card] } };
        _events.Add($"Created {FeatureCatalog.Get(FeatureCategory.Tarot, card.ToString()).Name}.");
    }
    private void OnCardEffects(int seat, Card token, bool replay = false, int? trickNumber = null, bool finalAlreadyApplied = false)
    {
        var state = StateFor(token);
        var number = trickNumber ?? (_completedTricks.Count + 1);
        var finalMultiplier = Has(JokerId.LastLaugh) && number == 6 && !finalAlreadyApplied ? 2 : 1;
        // Schedule independent activations once. A replay never schedules another replay bonus.
        var extra = replay ? 0 : (state.Enhancement == EnhancementId.Lucky && Has(JokerId.LuckyDuck) ? 1 : 0) + (state.Sigil == SigilId.Frog ? 1 : 0);
        var repeat = (1 + extra) * finalMultiplier;
        for (var activation = 0; activation < repeat; activation++)
        {
            switch (state.Enhancement)
            {
                case EnhancementId.Lucky:
                    if (_campaignRandom.NextInt(4) == 0) Point(seat, 4, "Lucky");
                    if (_campaignRandom.NextInt(3) == 0) Money(seat, 3, "Lucky"); break;
                case EnhancementId.Stone: Point(seat, 1, "Stone"); break;
                case EnhancementId.Bouncy when !replay:
                    foreach (var prior in _currentTrick.Take(_currentTrick.Count - 1).ToArray()) OnCardEffects(prior.Seat, prior.Card, true, number, finalAlreadyApplied: true);
                    break;
            }
            switch (state.Sigil)
            {
                case SigilId.Coin: Money(seat, 3, "Coin"); break;
                case SigilId.Fish: Point(seat, 2, "Fish"); break;
                case SigilId.Snake: Point(seat, 1, "Snake"); Point(1 - seat % 2, -1, "Snake"); break;
                case SigilId.Duck: if (_campaignRandom.NextInt(2) == 0) Point(seat, 4, "Duck"); break;
                case SigilId.Spider:
                    var previous = _currentTrick.Where(p => p.Card != token).ToArray();
                    if (previous.Length > 0) ChangeCard(IdFor(previous[_campaignRandom.NextInt(previous.Length)].Card), c => c with { Enhancement = EnhancementId.Cursed });
                    break;
            }
        }
    }

    private void ResolvePlayedCard(int seat, Card token)
    {
        _lastMatchCard = IdFor(token);
        if (!_firstMatchCard)
        {
            _firstMatchCard = true;
            if (Has(JokerId.CheeseTouch)) ChangeCard(IdFor(token), c => c with { Enhancement = EnhancementId.Cursed });
        }
        OnCardEffects(seat, token);
        if (seat == 0 && Has(JokerId.KingMidas) && StateFor(token).Face.Rank == Rank.King && _campaignRandom.NextInt(3) == 0)
            ChangeCard(IdFor(token), c => c with { Enhancement = EnhancementId.Golden });
    }
    private bool MatchesSuit(CardState card, Suit suit) => card.Enhancement == EnhancementId.Wild || card.Face.Suit == suit;
    private void ResolveWonTrick(int winner, IReadOnlyList<CardPlay> plays)
    {
        var team = winner % 2; var final = _completedTricks.Count == 6; var lastDouble = Has(JokerId.LastLaugh) && final ? 2 : 1;
        // Deep Digger's additional on-play effects resolve only once the winner is known.
        if (winner == 0 && Has(JokerId.DeepDigger))
            foreach (var p in plays.Where(p => MatchesSuit(StateFor(p.Card), Suit.Spades))) OnCardEffects(p.Seat, p.Card, true, _completedTricks.Count);
        foreach (var p in plays)
        {
            var c = StateFor(p.Card); var count = lastDouble * (1 + (winner == 0 && Has(JokerId.DeepDigger) && MatchesSuit(c, Suit.Spades) ? 1 : 0));
            for (var i = 0; i < count; i++)
            {
                if (c.Enhancement == EnhancementId.Golden || p.Seat == 0 && Has(JokerId.Millionaire) && MatchesSuit(c, Suit.Diamonds)) Money(winner, 1, "Golden");
                if (c.Enhancement == EnhancementId.Cursed) Point(winner, winner == 0 && Has(JokerId.CureAll) ? 2 : -1, "Cursed");
                if (c.Enhancement == EnhancementId.Silver) Money(team == 0 ? 1 : 0, 5, "Silver");
                if (c.Enhancement == EnhancementId.Emerald && final && p == plays.Last()) Money(p.Seat, 10, "Emerald");
                if (c.Sigil == SigilId.Peacock) Point(winner, plays.Count(x => StateFor(x.Card).Sigil is not null), "Peacock");
                if (c.Sigil == SigilId.Vulture) GenerateTarot(winner);
            }
            if (c.Sigil == SigilId.Eagle && p.Seat != winner) _hands[p.Seat].Add(p.Card);
        }
        var glass = 1 << plays.Count(p => StateFor(p.Card).Enhancement == EnhancementId.Glass);
        var trickCard = new int[2]; foreach (var point in _trickPoints) trickCard[point.Team] += point.Amount * glass;
        var extra = 0;
        if (team == 0)
        {
            var cards = plays.Select(p => StateFor(p.Card)).ToArray();
            if (Has(JokerId.LoverBoy) && Bidder == 0 && Contract!.Trump == Suit.Hearts) extra += cards.Count(c => MatchesSuit(c, Suit.Hearts));
            if (Has(JokerId.SnakeEyes) && cards.Count(c => c.Face.Rank == Rank.Ace) >= 2) extra += 5;
            var us = plays.FirstOrDefault(p => p.Seat == 0); var partner = plays.FirstOrDefault(p => p.Seat == 2);
            if (Has(JokerId.TwinFlame) && us is not null && partner is not null && IsRed(StateFor(us.Card).Face.Suit) != IsRed(StateFor(partner.Card).Face.Suit)) extra++;
            if (Has(JokerId.Clubhouse) && cards.Any(c => MatchesSuit(c, Suit.Clubs))) extra++;
            if (Has(JokerId.TheUndertaker)) extra += cards.Count(c => c.Face.Rank == Rank.Nine);
            if (winner == 0 && Has(JokerId.TrendSetter) && cards.Length == 4 && Enum.GetValues<Suit>().Any(s => cards.All(c => MatchesSuit(c, s)))) extra += 2;
            if (winner == 0 && Has(JokerId.ConsolationPrize) && Bidder % 2 != 0) extra++;
        }
        extra *= lastDouble;
        var multiplier = lastDouble;
        if (winner == 0 && Has(JokerId.VowofSilence)) multiplier *= 2;
        if (winner == 0 && Has(JokerId.Contrarian))
        { var own = plays.Single(p => p.Seat == winner); var ownSuit = EffectiveCampaignSuit(own.Card); if (plays.Where(p => p.Seat != winner).All(p => EffectiveCampaignSuit(p.Card) != ownSuit)) multiplier *= 2; }
        if (team == 0 && Has(JokerId.RoyalCouncil) && new[] { Rank.Ace, Rank.King, Rank.Queen, Rank.Jack }.All(r => plays.Any(p => StateFor(p.Card).Face.Rank == r))) multiplier *= 2;
        // Card on-play/trick effects already doubled by Last Laugh; multiply its base and Joker contributions once.
        var nonLast = multiplier / lastDouble;
        for (var t = 0; t < 2; t++) _cardPoints[t] += trickCard[t] * (t == team ? nonLast : 1);
        _jokerPoints[team] += extra * nonLast;
        _modifierPoints[team] += multiplier - 1;
        if (Contract!.Mode is ContractMode.High or ContractMode.Low) _rollerValue += team == 0 ? 1 : -1;
        if (winner == 0 && Has(JokerId.PeoplePleaser)) Money(0, _hands[0].Count(c => StateFor(c).Face.Rank is Rank.Jack or Rank.Queen or Rank.King) * lastDouble, "People Pleaser");
        if (winner == 0 && Has(JokerId.StickyFingers) && _stickyTriggered.Add(0) && _hands[0].Count > 0)
        {
            var copy = StateFor(plays.Single(p => p.Seat == 0).Card); var id = IdFor(_hands[0][0]); var overlay = new CardOverlay(id, copy.Face, copy.Enhancement, copy.Sigil);
            if (CanDeal(_resources.Deck, overlay)) _overlays[id] = overlay;
            else _events.Add("Sticky Fingers copy skipped: at least six drawable cards must remain.");
        }
        _trickPoints.Clear();
    }
    private static bool IsRed(Suit s) => s is Suit.Hearts or Suit.Diamonds;
    private int[] SettleCampaignHand(int[] normal)
    {
        var bidderTeam = Bidder!.Value % 2; var success = _tricksByTeam[bidderTeam] >= Contract!.RequiredTricks;
        if (!success && Has(JokerId.ProtectiveBubble) && bidderTeam == 0)
        { normal[0] = 0; _resources = _resources with { Inventory = _resources.Inventory with { Jokers = _resources.Inventory.Jokers.Where(j => j.Id != JokerId.ProtectiveBubble).ToArray() } }; }
        else if (!success && _variant == GumballId.Duelist) normal[bidderTeam]--;
        if (success && bidderTeam == 0)
        {
            if (Has(JokerId.ThreesCompany) && Contract.Bid == BidLevel.Three) _jokerPoints[0] += 2;
            if (Has(JokerId.HighRoller) && Contract.Bid >= BidLevel.Five) _jokerPoints[0] += 3;
        }
        if (bidderTeam == 0 && !success) _dejaValue = 0;
        if (Has(JokerId.DejaVu)) _jokerPoints[0] += _dejaValue;
        if (Has(JokerId.RollerCoaster) && Contract.Mode is ContractMode.High or ContractMode.Low) _jokerPoints[0] += _rollerValue;
        if (Contract.Bid <= BidLevel.Six) _matchOvertricks[bidderTeam] += Math.Max(0, _tricksByTeam[bidderTeam] - Contract.RequiredTricks);
        var factor = _variant == GumballId.Chaos ? 2 : 1;
        for (var team = 0; team < 2; team++)
        {
            _events.Add($"Team {team + 1}: contract {Signed(normal[team])}; cards {Signed(_cardPoints[team])}; Jokers {Signed(_jokerPoints[team])}; trick multipliers {Signed(_modifierPoints[team])}; hand ×{factor}.");
            normal[team] = (normal[team] + _cardPoints[team] + _jokerPoints[team] + _modifierPoints[team]) * factor;
        }
        _resources = _resources with { Inventory = _resources.Inventory with { Jokers = _resources.Inventory.Jokers.Select(j => j.Id == JokerId.DejaVu ? j with { Value = _dejaValue } : j).ToArray() } };
        return normal;
    }
    private void ResolveMatchEnd()
    {
        if (Has(JokerId.PennyPincher)) Money(0, _matchOvertricks[0], "Penny Pincher");
        if (Has(JokerId.Bully) && GameWinner == 0 && _scores[0] - _scores[1] >= 6) Money(0, 3, "Bully");
        if (Has(JokerId.Medusa) && _lastMatchCard is { } last) ChangeCard(last, c => c with { Enhancement = EnhancementId.Stone });
        _overlays.Clear();
    }
}
