using BidEuchre.Core;
namespace BidEuchre.App;

public static class CampaignShop
{
    public static CampaignRun Open(CampaignRun run)
    {
        if (run.Phase is not (CampaignPhase.Shop or CampaignPhase.Victory)) throw new GameRuleException("Shop is only available between matches.");
        if (run.Shop?.MatchJustFinished == run.Match - 1) return run;
        var random = new CampaignRandom(run.Resources.RandomState);
        var inventory = run.Resources.Inventory;
        var offers = new List<ShopOffer>();
        void Roll(FeatureCategory category, int number)
        {
            var options = FeatureCatalog.All.Where(f => f.Category == category)
                .Where(f => category != FeatureCategory.Joker || !inventory.Jokers.Any(j => j.Id.ToString() == f.Key))
                .Where(f => category != FeatureCategory.Planet || !inventory.Unlocks.Any(p => p.ToString() == f.Key)).ToList();
            for (var i = 0; i < number; i++)
            {
                if (options.Count == 0) continue;
                var choice = options[random.NextInt(options.Count)]; options.Remove(choice);
                offers.Add(new($"{run.Match - 1}:{offers.Count}", category, choice.Key, choice.Price));
            }
        }
        Roll(FeatureCategory.Joker, 2); Roll(FeatureCategory.Planet, 1); Roll(FeatureCategory.Tarot, 1); Roll(FeatureCategory.Sigil, 1);
        return run with { Shop = new(run.Match - 1, offers.ToArray()), Resources = run.Resources with { RandomState = random.State, Revision = run.Resources.Revision + 1, Wallets = new[] { run.Money, run.Resources.Wallets[1], run.Resources.Wallets[2], run.Resources.Wallets[3] } } };
    }
    public static CampaignRun Buy(CampaignRun run, string offerId, CardId? sigilTarget = null)
    {
        if (run.Phase != CampaignPhase.Shop || run.Shop is null) throw new GameRuleException("The shop is closed.");
        var offer = run.Shop.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        if (run.Money < offer.Price) throw new GameRuleException("Not enough money.");
        var resources = run.Resources.Copy(); var inventory = resources.Inventory;
        switch (offer.Category)
        {
            case FeatureCategory.Joker:
                var joker = Enum.Parse<JokerId>(offer.FeatureId);
                if (inventory.Jokers.Length >= 5 || inventory.Has(joker)) throw new GameRuleException("Five Joker slots; no duplicates.");
                inventory = inventory with { Jokers = [.. inventory.Jokers, new(joker)] }; break;
            case FeatureCategory.Planet:
                var planet = Enum.Parse<PlanetId>(offer.FeatureId);
                if (inventory.Unlocks.Contains(planet)) throw new GameRuleException("Already unlocked.");
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, planet] }; break;
            case FeatureCategory.Tarot:
                if (inventory.Tarot.Length >= 2) throw new GameRuleException("Tarot inventory full. Discard a card first.");
                inventory = inventory with { Tarot = [.. inventory.Tarot, Enum.Parse<TarotId>(offer.FeatureId)] }; break;
            case FeatureCategory.Sigil:
                if (sigilTarget is null || !resources.Deck.Any(c => c.Id == sigilTarget)) throw new GameRuleException("Choose a card from the shared deck.");
                resources = resources with { Deck = resources.Deck.Select(c => c.Id == sigilTarget ? c with { Sigil = Enum.Parse<SigilId>(offer.FeatureId) } : c).ToArray() }; break;
            default: throw new GameRuleException("This item cannot be purchased.");
        }
        resources.Wallets[0] = run.Money - offer.Price;
        return run with { Money = resources.Wallets[0], Resources = resources with { Inventory = inventory, Revision = resources.Revision + 1 }, Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true } : o).ToArray() } };
    }
    /// <summary>How many shared-deck cards a Tarot must be aimed at.</summary>
    public static int TarotTargets(TarotId tarot) => tarot == TarotId.DevilishDeal ? 3
        : tarot is TarotId.TaxCollector or TarotId.Astronomer or TarotId.Gambler ? 0 : 1;

    /// <summary>Buys a Tarot and plays it at once on cards of the shared deck, so it needs no inventory slot.
    /// The changes last for the rest of the run.</summary>
    public static CampaignRun BuyAndUse(CampaignRun run, string offerId, IReadOnlyList<CardId> targets)
    {
        if (run.Phase != CampaignPhase.Shop || run.Shop is null) throw new GameRuleException("The shop is closed.");
        var offer = run.Shop.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        if (offer.Category != FeatureCategory.Tarot) throw new GameRuleException("Only a Tarot can be used from the shop.");
        if (run.Money < offer.Price) throw new GameRuleException("Not enough money.");
        var tarot = Enum.Parse<TarotId>(offer.FeatureId);
        var resources = run.Resources.Copy(); var inventory = resources.Inventory; var deck = resources.Deck;
        var required = TarotTargets(tarot);
        if (targets.Count != required || targets.Distinct().Count() != required || targets.Any(id => deck.All(c => c.Id != id)))
            throw new GameRuleException(required == 1 ? "Choose a card from the shared deck." : $"Choose {required} different cards from the shared deck.");
        if (tarot == TarotId.Astronomer && inventory.Unlocks.Length == 8) throw new GameRuleException("All bidding options are unlocked.");
        // The human must always have six cards that can be dealt to them.
        bool CanEnhance(CardId id, EnhancementId enhancement) =>
            deck.Count(c => (c.Id == id ? enhancement : c.Enhancement) == EnhancementId.Stinky) <= 18;
        if (tarot == TarotId.Beggar && !CanEnhance(targets[0], EnhancementId.Stinky)) throw new GameRuleException("At least six non-Stinky cards must remain.");
        // Everything is checked before the random draws and the changes to the deck.
        var random = new CampaignRandom(resources.RandomState);
        var money = run.Money - offer.Price;
        void Change(CardId id, Func<CardState, CardState> update) => deck[Array.FindIndex(deck, c => c.Id == id)] = update(deck.First(c => c.Id == id));
        void Enhance(CardId id, EnhancementId enhancement) => Change(id, c => c with { Enhancement = enhancement });
        switch (tarot)
        {
            case TarotId.LovePotion: Change(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Hearts)); break;
            case TarotId.Nightshade: Change(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Spades)); break;
            case TarotId.CloverTonic: Change(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Clubs)); break;
            case TarotId.CrystalElixir: Change(targets[0], c => CampaignCards.ChangeSuit(c, Suit.Diamonds)); break;
            case TarotId.TaxCollector: money += Math.Min(20, money); break;
            case TarotId.Astronomer:
                var options = Enum.GetValues<PlanetId>().Except(inventory.Unlocks).ToArray();
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, options[random.NextInt(options.Length)]] }; break;
            case TarotId.Gambler:
                var choices = deck.Select(c => c.Id).ToList(); random.Shuffle(choices);
                foreach (var id in choices.Take(2)) Enhance(id, EnhancementId.Lucky); break;
            case TarotId.DevilishDeal:
                foreach (var id in targets.Take(2))
                { var pool = Enum.GetValues<EnhancementId>().Where(e => CanEnhance(id, e)).ToArray(); Enhance(id, pool[random.NextInt(pool.Length)]); }
                Enhance(targets[2], EnhancementId.Cursed); break;
            default:
                Enhance(targets[0], tarot switch
                {
                    TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden,
                    TarotId.Merchant => EnhancementId.Emerald, TarotId.Glassblower => EnhancementId.Glass, TarotId.Traitor => EnhancementId.Silver,
                    TarotId.Beggar => EnhancementId.Stinky, TarotId.SpringLoaded => EnhancementId.Bouncy,
                    _ => throw new GameRuleException("Unknown Tarot.")
                }); break;
        }
        resources.Wallets[0] = money;
        return run with
        {
            Money = money,
            Resources = resources with { Inventory = inventory, RandomState = random.State, Revision = resources.Revision + 1 },
            Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true } : o).ToArray() }
        };
    }
    public static CampaignRun SellJoker(CampaignRun run, JokerId id)
    {
        if (run.Phase != CampaignPhase.Shop || !run.Resources.Inventory.Has(id)) throw new GameRuleException("That Joker cannot be sold now.");
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money + 2;
        return run with { Money = run.Money + 2, Resources = resources with { Revision = resources.Revision + 1, Inventory = resources.Inventory with { Jokers = resources.Inventory.Jokers.Where(j => j.Id != id).ToArray() } } };
    }
    public static CampaignRun DiscardTarot(CampaignRun run, int slot, long? expectedRevision = null)
    {
        if (expectedRevision is { } revision && revision != run.Resources.Revision) throw new GameRuleException("The shop changed. Choose a Tarot again.");
        if (run.Phase != CampaignPhase.Shop || slot < 0 || slot >= run.Resources.Inventory.Tarot.Length) throw new GameRuleException("No Tarot in that slot.");
        return run with { Resources = run.Resources with { Revision = run.Resources.Revision + 1, Inventory = run.Resources.Inventory with { Tarot = run.Resources.Inventory.Tarot.Where((_, i) => i != slot).ToArray() } } };
    }
}
