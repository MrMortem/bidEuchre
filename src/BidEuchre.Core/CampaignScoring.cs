namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    private int[] LiveCampaignScores()
    {
        // Saves and hand settlement use the settled base score. The trick ledgers already
        // persist everything needed to reconstruct the score shown during play.
        var team0 = (long)_scores[0] + _cardPoints[0] + _jokerPoints[0] + RoundJokerPoints();
        var team1 = (long)_scores[1] + _cardPoints[1] + _jokerPoints[1];
        foreach (var point in _trickPoints)
        {
            if (point.Team == 0) team0 += point.Amount;
            else team1 += point.Amount;
        }
        return [(int)Math.Clamp(team0, int.MinValue, int.MaxValue), (int)Math.Clamp(team1, int.MinValue, int.MaxValue)];
    }

    private int RoundJokerPoints()
    {
        var contract = Contract!;
        var points = 0;
        if (Bidder % 2 == 0 && _tricksByTeam[0] >= contract.RequiredTricks)
        {
            if (Has(JokerId.ThreesCompany) && contract.Bid == BidLevel.Three) points++;
            if (Has(JokerId.HighRoller) && contract.Bid >= BidLevel.Five) points += 3;
        }
        if (_repeatsPreviousTrump && Has(JokerId.DejaVu)) points += _tricksByTeam[0];
        if (Has(JokerId.RollerCoaster) && contract.Mode is ContractMode.High or ContractMode.Low) points += _rollerValue;
        return points;
    }

    private void ScoreCampaignTrick(int winner, IReadOnlyList<CardPlay> plays, int lastLaugh)
    {
        var team = winner % 2;
        var glass = 1 << plays.Count(play => StateFor(play.Card).Enhancement == EnhancementId.Glass);
        var trickCard = new int[2];
        foreach (var point in _trickPoints) trickCard[point.Team] += point.Amount * glass;

        var extra = team == 0 ? TrickJokerPoints(winner, plays) * lastLaugh : 0;
        var multiplier = TrickMultiplier(winner, plays, lastLaugh);
        // Last Laugh already repeated card activations. Only the other multipliers apply to this ledger.
        var nonLast = multiplier / lastLaugh;
        for (var t = 0; t < 2; t++) _cardPoints[t] += trickCard[t] * (t == team ? nonLast : 1);
        _jokerPoints[team] += extra * nonLast;
        _modifierPoints[team] += multiplier - 1;
        if (Contract!.Mode is ContractMode.High or ContractMode.Low) _rollerValue += team == 0 ? 1 : -1;
    }

    private int TrickJokerPoints(int winner, IReadOnlyList<CardPlay> plays)
    {
        var cards = plays.Select(play => StateFor(play.Card)).ToArray();
        var extra = 0;
        if (Has(JokerId.LoverBoy) && Bidder == 0 && Contract!.Trump == Suit.Hearts)
            extra += cards.Count(card => MatchesEffectSuit(card, Suit.Hearts));
        if (Has(JokerId.SnakeEyes) && cards.Count(card => card.Face.Rank == Rank.Ace) >= 2) extra += 4;

        var us = plays.FirstOrDefault(play => play.Seat == 0);
        var partner = plays.FirstOrDefault(play => play.Seat == 2);
        if (Has(JokerId.TwinFlame) && us is not null && partner is not null &&
            IsRed(StateFor(us.Card).Face.Suit) != IsRed(StateFor(partner.Card).Face.Suit)) extra++;
        if (Has(JokerId.Clubhouse) && cards.Any(card => MatchesEffectSuit(card, Suit.Clubs))) extra++;
        if (Has(JokerId.TheUndertaker)) extra += cards.Count(card => card.Face.Rank == Rank.Nine);
        if (winner == 0 && Has(JokerId.TrendSetter) && cards.Length == 4 &&
            Enum.GetValues<Suit>().Any(suit => cards.All(card => MatchesEffectSuit(card, suit)))) extra += 2;
        if (winner == 0 && Has(JokerId.ConsolationPrize) && Bidder % 2 != 0) extra++;
        return extra;
    }

    private int TrickMultiplier(int winner, IReadOnlyList<CardPlay> plays, int lastLaugh)
    {
        var multiplier = lastLaugh;
        if (winner == 0 && Has(JokerId.VowofSilence)) multiplier *= 2;
        if (winner == 0 && Has(JokerId.Contrarian))
        {
            var own = plays.Single(play => play.Seat == winner);
            var ownSuit = EffectiveCampaignSuit(own.Card);
            if (plays.Where(play => play.Seat != winner).All(play => EffectiveCampaignSuit(play.Card) != ownSuit))
                multiplier *= 2;
        }
        if (winner % 2 == 0 && Has(JokerId.RoyalCouncil) &&
            new[] { Rank.Ace, Rank.King, Rank.Queen, Rank.Jack }.All(rank => plays.Any(play => StateFor(play.Card).Face.Rank == rank)))
            multiplier *= 2;
        return multiplier;
    }

    private static bool IsRed(Suit suit) => suit is Suit.Hearts or Suit.Diamonds;

    private TeamScoreBreakdown[] SettleCampaignHand(int[] normal)
    {
        var bidderTeam = Bidder!.Value % 2;
        var success = _tricksByTeam[bidderTeam] >= Contract!.RequiredTricks;
        if (!success && bidderTeam == 0 && Has(JokerId.ProtectiveBubble))
        {
            normal[0] = 0;
            var remaining = _resources.Inventory.Jokers.Where(joker => joker.Id != JokerId.ProtectiveBubble).ToArray();
            _resources = _resources with { Inventory = _resources.Inventory with { Jokers = remaining } };
        }
        else if (!success && _variant == GumballId.Duelist)
        {
            normal[bidderTeam] -= 2;
        }

        _jokerPoints[0] += RoundJokerPoints();
        if (Contract.Bid <= BidLevel.Six) _matchOvertricks[bidderTeam] += Math.Max(0, _tricksByTeam[bidderTeam] - Contract.RequiredTricks);

        var factor = _variant == GumballId.Chaos ? 2 : 1;
        var opponentBonus = success && bidderTeam == 1 ? _matchOptions.OpponentSuccessfulBidBonus : 0;
        var gumballBonuses = GumballRewards.SuccessfulBidBonuses(_resources.Inventory, Bidder.Value, success, Contract.Bid, Contract.Mode, _variant);
        foreach (var reward in gumballBonuses)
            _events.Add($"{FeatureCatalog.Get(FeatureCategory.Gumball, reward.Id.ToString()).Name} duplicates: +{reward.Points} points for team {reward.Team + 1} after hand multiplier.");
        var breakdowns = new TeamScoreBreakdown[2];
        for (var team = 0; team < 2; team++)
        {
            _events.Add($"Team {team + 1}: contract {Signed(normal[team])}; cards {Signed(_cardPoints[team])}; Jokers {Signed(_jokerPoints[team])}; trick multipliers {Signed(_modifierPoints[team])}; hand ×{factor}; opponent bid bonus {Signed(team == 1 ? opponentBonus : 0)} after hand multiplier.");
            // Settlement and its public explanation share the same arithmetic and reward entries.
            breakdowns[team] = new(team, normal[team], _cardPoints[team], _jokerPoints[team], _modifierPoints[team], factor, team == 1 ? opponentBonus : 0)
            { GumballBonuses = gumballBonuses.Where(reward => reward.Team == team).ToArray() };
        }
        return breakdowns;
    }

    private void ResolveMatchEnd()
    {
        if (Has(JokerId.PennyPincher)) Money(0, _matchOvertricks[0], CampaignScoringEffect.PennyPincher);
        if (Has(JokerId.Bully) && GameWinner == 0 && _scores[0] - _scores[1] >= 6) Money(0, 3, CampaignScoringEffect.Bully);
        if (Has(JokerId.Medusa) && _lastMatchCard is { } last) ChangeCard(last, c => c with { Enhancement = EnhancementId.Stone });
        _overlays.Clear();
    }
}
