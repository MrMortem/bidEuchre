namespace BidEuchre.Core;

public sealed partial class GameEngine
{
    private bool CanEnhance(CardId id, EnhancementId enhancement)
    {
        var deck = _resources.Deck.Select(c => c.Id == id ? c with { Enhancement = enhancement } : c).ToArray();
        return deck.Count(c => c.Enhancement == EnhancementId.Stinky) <= 18 && CanDeal(deck);
    }
    /// <summary>Whether the human may play a stored Tarot or Sigil now: on any turn of theirs, in any phase.</summary>
    private bool CanUseItems(int seat) => seat == 0 && CampaignLegal(0).Count > 0;
    private void RequireItemTurn(int seat)
    { if (!CanUseItems(seat)) throw new GameRuleException("Use Tarots and Sigils on your own turn."); }
    private void ResolveSigil(int seat, CampaignCommand.UseSigil request)
    {
        RequireItemTurn(seat);
        var inventory = _resources.Inventory;
        if (request.Slot < 0 || request.Slot >= inventory.Sigils.Length) throw new GameRuleException("No Sigil in that slot.");
        if (!_hands[0].Contains(TokenFor(request.Target))) throw new GameRuleException("Choose a card from your hand.");
        var sigil = inventory.Sigils[request.Slot];
        ChangeCard(request.Target, c => c with { Sigil = sigil });
        _resources = _resources with { Inventory = inventory with { Sigils = inventory.Sigils.Where((_, i) => i != request.Slot).ToArray() } };
        _events.Add($"Used {FeatureCatalog.Get(FeatureCategory.Sigil, sigil.ToString()).Name}.");
    }
    private void ResolveTarot(int seat, CampaignCommand.UseTarot request)
    {
        RequireItemTurn(seat);
        var inventory = _resources.Inventory;
        if (request.Slot < 0 || request.Slot >= inventory.Tarot.Length) throw new GameRuleException("No Tarot in that slot.");
        var tarot = inventory.Tarot[request.Slot]; var targets = request.Targets?.ToArray() ?? throw new GameRuleException("Choose Tarot targets.");
        var required = Tarot.TargetCount(tarot);
        if (targets.Length != required || targets.Distinct().Count() != required || targets.Any(id => !_hands[0].Contains(TokenFor(id)))) throw new GameRuleException("Choose the required distinct cards from your hand.");
        if (tarot == TarotId.Astronomer && inventory.Unlocks.Length == CampaignInventory.GumballCount) throw new GameRuleException("All bidding options are unlocked.");
        if (tarot == TarotId.Beggar && !CanEnhance(targets[0], EnhancementId.Stinky)) throw new GameRuleException("At least six non-Stinky cards must remain.");
        // Validate everything before RNG, consumption, or permanent mutations.
        void Enhance(CardId id, EnhancementId e) => ChangeCard(id, c => c with { Enhancement = e });
        switch (tarot)
        {
            case var _ when Tarot.SuitOf(tarot) is { } suit: ChangeCard(targets[0], c => CampaignCards.ChangeSuit(c, suit)); break;
            case TarotId.TaxCollector: Money(0, Math.Min(20, _resources.Wallets[0]), "Tax Collector"); break;
            case TarotId.Astronomer:
                var options = Enum.GetValues<GumballId>().Except(inventory.Unlocks).ToArray();
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, options[_campaignRandom.NextInt(options.Length)]] }; break;
            case TarotId.Gambler:
                // Physical order, so how the player arranged their hand cannot steer the draw.
                var choices = _hands[0].Select(IdFor).OrderBy(id => id.Value).ToList(); _campaignRandom.Shuffle(choices);
                foreach (var id in choices.Take(2)) Enhance(id, EnhancementId.Lucky); break;
            case TarotId.DevilishDeal:
                // Preserve feasibility even when a random enhancement chooses Stinky.
                foreach (var id in targets.Take(2))
                { var pool = Enum.GetValues<EnhancementId>().Where(e => CanEnhance(id, e)).ToArray(); Enhance(id, pool[_campaignRandom.NextInt(pool.Length)]); }
                Enhance(targets[2], EnhancementId.Cursed); break;
            default:
                Enhance(targets[0], Tarot.EnhancementOf(tarot) ?? throw new GameRuleException("Unknown Tarot.")); break;
        }
        _resources = _resources with { Inventory = inventory with { Tarot = inventory.Tarot.Where((_, i) => i != request.Slot).ToArray() } };
        _events.Add($"Used {FeatureCatalog.Get(FeatureCategory.Tarot, tarot.ToString()).Name}.");
    }
}
