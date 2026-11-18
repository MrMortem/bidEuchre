namespace BidEuchre.Core;

// Effect handlers live in the engine's partial lifecycle, never in rendering callbacks.
public sealed partial class GameEngine
{
    private sealed record EffectPoint(int Team, int Amount, string Source);
    private readonly List<EffectPoint> _trickPoints = [];
    private int _effectSequence;
    private readonly List<CampaignEffectEvent> _scoringEffects = [];

    private void Point(int seat, int amount, CampaignScoringEffect effect, Card token, int sourceSeat, int trickNumber)
    {
        var team = seat % 2;
        _trickPoints.Add(new(team, amount, effect.ToString()));
        _scoringEffects.Add(new(++_effectSequence, effect, CampaignEffectReward.Points, amount, team, IdFor(token), sourceSeat, trickNumber));
        _events.Add($"Effect {_effectSequence}: {effect} {Signed(amount)} points for team {team + 1}.");
    }

    private void Money(int seat, int amount, CampaignScoringEffect effect, Card? token = null, int? sourceSeat = null, int? trickNumber = null)
    {
        _resources.Wallets[seat] = checked(_resources.Wallets[seat] + amount);
        var label = effect switch
        {
            CampaignScoringEffect.Millionaire => "Golden",
            CampaignScoringEffect.PeoplePleaser => "People Pleaser",
            CampaignScoringEffect.PennyPincher => "Penny Pincher",
            _ => effect.ToString()
        };
        _scoringEffects.Add(new(++_effectSequence, effect, CampaignEffectReward.Money, amount, seat,
            token is { } source ? IdFor(source) : null, sourceSeat, trickNumber));
        _events.Add($"Effect {_effectSequence}: {label} +${amount} for {_playerNames[seat]}.");
    }
    private void GenerateTarot(int seat)
    {
        var choices = Enum.GetValues<TarotId>();
        if (seat != 0)
        {
            var held = _resources.GeneratedTarot[seat];
            if (held.Length >= 2) { _events.Add($"{_playerNames[seat]}'s Tarot inventory is full."); return; }
            var generated = choices[_campaignRandom.NextInt(choices.Length)];
            var inventories = _resources.GeneratedTarot.Select(cards => cards.ToArray()).ToArray();
            inventories[seat] = [.. held, generated]; _resources = _resources with { GeneratedTarot = inventories };
            _events.Add($"{_playerNames[seat]} generated a Tarot (stored; bots do not use items)."); return;
        }
        var inventory = _resources.Inventory;
        if (!inventory.HasItemRoom) { _events.Add("Tarot creation skipped: inventory full."); return; }
        var card = choices[_campaignRandom.NextInt(choices.Length)];
        _resources = _resources with { Inventory = inventory with { Tarot = [.. inventory.Tarot, card] } };
        _events.Add($"Created {FeatureCatalog.Get(FeatureCategory.Tarot, card.ToString()).Name}.");
    }
    private enum OnPlayTrigger { Play, DoubleDownPlay, BouncyReplay, DeepDiggerReplay }

    private int LastLaughMultiplier(int trickNumber) => Has(JokerId.LastLaugh) && trickNumber == 6 ? 2 : 1;

    private void ResolveOnPlay(CardPlay play, int trickNumber, OnPlayTrigger trigger)
    {
        // Capture once per batch. An activation can change other cards before their next batch.
        var state = StateFor(play.Card);
        var original = trigger is OnPlayTrigger.Play or OnPlayTrigger.DoubleDownPlay;
        var activations = 1;
        if (original)
        {
            if (state.Enhancement == EnhancementId.Lucky && Has(JokerId.LuckyDuck)) activations++;
            if (state.Sigil == SigilId.Frog) activations++;
            if (trigger == OnPlayTrigger.DoubleDownPlay) activations++;
        }

        // A Bouncy replay is already repeated by its parent. Deep Digger gets its own Last Laugh pass.
        if (trigger != OnPlayTrigger.BouncyReplay) activations *= LastLaughMultiplier(trickNumber);
        for (var i = 0; i < activations; i++)
        {
            ApplyOnPlayEnhancement(play, state.Enhancement, trickNumber, original);
            ApplyOnPlaySigil(play, state.Sigil, trickNumber);
        }
    }

    private void ApplyOnPlayEnhancement(CardPlay play, EnhancementId? enhancement, int trickNumber, bool original)
    {
        var (seat, card) = play;
        switch (enhancement)
        {
            case EnhancementId.Lucky:
                if (_campaignRandom.NextInt(4) == 0) Point(seat, 4, CampaignScoringEffect.Lucky, card, seat, trickNumber);
                if (_campaignRandom.NextInt(3) == 0) Money(seat, 3, CampaignScoringEffect.Lucky, card, seat, trickNumber);
                break;
            case EnhancementId.Stone:
                Point(seat, 1, CampaignScoringEffect.Stone, card, seat, trickNumber);
                break;
            case EnhancementId.Bouncy when original:
                // Replays run in table order, before this activation's sigil. They cannot replay Bouncy.
                foreach (var prior in _currentTrick.Take(_currentTrick.Count - 1).ToArray())
                    ResolveOnPlay(prior, trickNumber, OnPlayTrigger.BouncyReplay);
                break;
        }
    }

    private void ApplyOnPlaySigil(CardPlay play, SigilId? sigil, int trickNumber)
    {
        var (seat, card) = play;
        switch (sigil)
        {
            case SigilId.Fish:
                Point(seat, 2, CampaignScoringEffect.Fish, card, seat, trickNumber);
                break;
            case SigilId.Duck:
                if (_campaignRandom.NextInt(3) == 0) Point(seat, 4, CampaignScoringEffect.Duck, card, seat, trickNumber);
                break;
            case SigilId.Spider:
                var others = _currentTrick.Where(other => other.Card != card).ToArray();
                if (others.Length > 0)
                    ChangeCard(IdFor(others[_campaignRandom.NextInt(others.Length)].Card),
                        state => state with { Enhancement = EnhancementId.Cursed });
                break;
        }
    }

    private void ResolvePlayedCard(CardPlay play)
    {
        var (seat, card) = play;
        _lastMatchCard = IdFor(card);
        if (!_firstMatchCard)
        {
            _firstMatchCard = true;
            if (Has(JokerId.CheeseTouch)) ChangeCard(IdFor(card), state => state with { Enhancement = EnhancementId.Cursed });
        }

        var doubleDown = seat == 0 && !_firstHumanRoundCard && Has(JokerId.DoubleDown);
        if (seat == 0) _firstHumanRoundCard = true;
        ResolveOnPlay(play, _completedTricks.Count + 1, doubleDown ? OnPlayTrigger.DoubleDownPlay : OnPlayTrigger.Play);
        if (seat == 0 && Has(JokerId.KingMidas) && StateFor(card).Face.Rank == Rank.King && _campaignRandom.NextInt(3) == 0)
            ChangeCard(IdFor(card), state => state with { Enhancement = EnhancementId.Golden });
    }

    // Suit-based effects count printed suits and Wild. Trick legality and strength use CampaignCardRules.
    private static bool MatchesEffectSuit(CardState card, Suit suit) =>
        card.Enhancement == EnhancementId.Wild || card.Face.Suit == suit;

    private void ResolveWonTrick(int winner, IReadOnlyList<CardPlay> plays)
    {
        var trickNumber = _completedTricks.Count;
        var lastLaugh = LastLaughMultiplier(trickNumber);
        var deepDigger = winner == 0 && Has(JokerId.DeepDigger);

        // Check each card at its turn. An earlier Spider replay can change later eligibility.
        if (deepDigger)
            foreach (var play in plays)
                if (MatchesEffectSuit(StateFor(play.Card), Suit.Spades))
                    ResolveOnPlay(play, trickNumber, OnPlayTrigger.DeepDiggerReplay);

        foreach (var play in plays)
        {
            var state = StateFor(play.Card);
            var activations = deepDigger && MatchesEffectSuit(state, Suit.Spades) ? lastLaugh * 2 : lastLaugh;
            for (var i = 0; i < activations; i++) ApplyTrickRewards(winner, play, state, trickNumber);

            // Eagle returns once per losing card, never once per activation.
            if (state.Sigil == SigilId.Eagle && play.Seat % 2 != winner % 2 && trickNumber != 6)
                _hands[play.Seat].Add(play.Card);
        }

        ScoreCampaignTrick(winner, plays, lastLaugh);
        ApplyWinnerFollowups(winner, plays, lastLaugh);
        _trickPoints.Clear();
    }

    private void ApplyTrickRewards(int winner, CardPlay play, CardState state, int trickNumber)
    {
        var team = winner % 2;
        var (seat, card) = play;
        if (state.Enhancement == EnhancementId.Golden || seat == 0 && Has(JokerId.Millionaire) && MatchesEffectSuit(state, Suit.Diamonds))
        {
            var effect = state.Enhancement == EnhancementId.Golden ? CampaignScoringEffect.Golden : CampaignScoringEffect.Millionaire;
            Money(winner, 1, effect, card, seat, trickNumber);
        }
        if (state.Enhancement == EnhancementId.Cursed)
            Point(winner, team == 0 && Has(JokerId.CureAll) ? 2 : -1, CampaignScoringEffect.Cursed, card, seat, trickNumber);
        if (state.Enhancement == EnhancementId.Silver)
            Money(team == 0 ? 1 : 0, 5, CampaignScoringEffect.Silver, card, seat, trickNumber);
        if (state.Sigil == SigilId.Vulture) GenerateTarot(winner);
    }

    private void ApplyWinnerFollowups(int winner, IReadOnlyList<CardPlay> plays, int lastLaugh)
    {
        if (winner != 0) return;
        if (Has(JokerId.PeoplePleaser))
        {
            var royalty = _hands[0].Count(card => StateFor(card).Face.Rank is Rank.Jack or Rank.Queen or Rank.King);
            Money(0, royalty * lastLaugh, CampaignScoringEffect.PeoplePleaser);
        }
        // The first card is the leftmost one in the player's manual or automatic arrangement.
        if (Has(JokerId.StickyFingers) && _stickyTriggered.Add(0) && _hands[0].Count > 0)
        {
            var copy = StateFor(plays.Single(play => play.Seat == 0).Card);
            var id = IdFor(_hands[0][0]);
            _overlays[id] = new(id, copy.Face, copy.Enhancement, copy.Sigil);
        }
    }
}
