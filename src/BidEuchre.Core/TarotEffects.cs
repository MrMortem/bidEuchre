namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    private bool CanEnhance(CardId id, EnhancementId enhancement)
    {
        var deck = _resources.Deck.Select(c => c.Id == id ? c with { Enhancement = enhancement } : c).ToArray();
        return deck.Count(c => c.Enhancement == EnhancementId.Stinky) <= 18 && CanDeal(deck);
    }
    private void ResolveTarot(int seat, CampaignCommand.UseTarot request)
    {
        if (seat != 0 || CurrentSeat != 0 || Phase != GamePhase.Bidding || _pendingChoice is not null) throw new GameRuleException("Use Tarot before your bidding action.");
        var inventory = _resources.Inventory;
        if (request.Slot < 0 || request.Slot >= inventory.Tarot.Length) throw new GameRuleException("No Tarot in that slot.");
        var tarot = inventory.Tarot[request.Slot]; var targets = request.Targets?.ToArray() ?? throw new GameRuleException("Choose Tarot targets.");
        var required = tarot == TarotId.DevilishDeal ? 3 : tarot is TarotId.TaxCollector or TarotId.Astronomer or TarotId.Gambler ? 0 : 1;
        if (targets.Length != required || targets.Distinct().Count() != required || targets.Any(id => !_hands[0].Contains(TokenFor(id)))) throw new GameRuleException("Choose the required distinct cards from your hand.");
        if (tarot == TarotId.Astronomer && inventory.Unlocks.Length == 8) throw new GameRuleException("All bidding options are unlocked.");
        if (tarot == TarotId.Beggar && !CanEnhance(targets[0], EnhancementId.Stinky)) throw new GameRuleException("At least six non-Stinky cards must remain.");
        // Validate everything before RNG, consumption, or permanent mutations.
        void Enhance(CardId id, EnhancementId e) => ChangeCard(id, c => c with { Enhancement = e });
        switch (tarot)
        {
            case TarotId.LovePotion: ChangeCard(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Hearts)); break;
            case TarotId.Nightshade: ChangeCard(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Spades)); break;
            case TarotId.CloverTonic: ChangeCard(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Clubs)); break;
            case TarotId.CrystalElixir: ChangeCard(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Diamonds)); break;
            case TarotId.TaxCollector: Money(0, Math.Min(20, _resources.Wallets[0]), "Tax Collector"); break;
            case TarotId.Astronomer:
                var options = Enum.GetValues<PlanetId>().Except(inventory.Unlocks).ToArray();
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, options[_campaignRandom.NextInt(options.Length)]] }; break;
            case TarotId.Gambler:
                var choices = _hands[0].Select(IdFor).ToList(); _campaignRandom.Shuffle(choices);
                foreach (var id in choices.Take(2)) Enhance(id, EnhancementId.Lucky); break;
            case TarotId.DevilishDeal:
                // Preserve feasibility even when a random enhancement chooses Stinky.
                foreach (var id in targets.Take(2))
                { var pool = Enum.GetValues<EnhancementId>().Where(e => CanEnhance(id, e)).ToArray(); Enhance(id, pool[_campaignRandom.NextInt(pool.Length)]); }
                Enhance(targets[2], EnhancementId.Cursed); break;
            default:
                var enhancement = tarot switch
                { TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden, TarotId.Merchant => EnhancementId.Emerald, TarotId.Glassblower => EnhancementId.Glass, TarotId.Traitor => EnhancementId.Silver, TarotId.Beggar => EnhancementId.Stinky, TarotId.SpringLoaded => EnhancementId.Bouncy, _ => throw new GameRuleException("Unknown Tarot.") };
                Enhance(targets[0], enhancement); break;
        }
        _resources = _resources with { Inventory = inventory with { Tarot = inventory.Tarot.Where((_, i) => i != request.Slot).ToArray() } };
        _events.Add($"Used {FeatureCatalog.Get(FeatureCategory.Tarot, tarot.ToString()).Name}.");
    }
}
