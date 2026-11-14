using BidEuchre.Core;
namespace BidEuchre.App;

public static class CampaignShop
{
    public const int GumballPrice = 3;
    private const string Full = "Your card slots are full. Use or discard a card first.";
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
                .Where(f => category != FeatureCategory.Gumball || !inventory.Unlocks.Any(p => p.ToString() == f.Key)).ToList();
            for (var i = 0; i < number; i++)
            {
                if (options.Count == 0) continue;
                var choice = options[random.NextInt(options.Count)]; options.Remove(choice);
                offers.Add(new($"{run.Match - 1}:{offers.Count}", category, choice.Key, choice.Price));
            }
        }
        Roll(FeatureCategory.Joker, 2);
        // A gumball is a blind pull: which bid comes out is only drawn when it is paid for.
        if (inventory.Unlocks.Length < CampaignInventory.GumballCount) offers.Add(new($"{run.Match - 1}:{offers.Count}", FeatureCategory.Gumball, "", GumballPrice));
        Roll(FeatureCategory.Tarot, 1); Roll(FeatureCategory.Sigil, 1);
        return run with { Shop = new(run.Match - 1, offers.ToArray()), Resources = run.Resources with { RandomState = random.State, Revision = run.Resources.Revision + 1, Wallets = new[] { run.Money, run.Resources.Wallets[1], run.Resources.Wallets[2], run.Resources.Wallets[3] } } };
    }
    public static CampaignRun Buy(CampaignRun run, string offerId, CardId? sigilTarget = null)
    {
        if (run.Phase != CampaignPhase.Shop || run.Shop is null) throw new GameRuleException("The shop is closed.");
        var offer = run.Shop.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        if (run.Money < offer.Price) throw new GameRuleException("Not enough money.");
        var resources = run.Resources.Copy(); var inventory = resources.Inventory;
        string? pulled = null;
        switch (offer.Category)
        {
            case FeatureCategory.Joker:
                var joker = Enum.Parse<JokerId>(offer.FeatureId);
                if (inventory.Jokers.Length >= 5 || inventory.Has(joker)) throw new GameRuleException("Five Joker slots; no duplicates.");
                inventory = inventory with { Jokers = [.. inventory.Jokers, new(joker)] }; break;
            case FeatureCategory.Gumball:
                // Whatever the offer says, the bid is drawn now from the ones still locked.
                var locked = Enum.GetValues<GumballId>().Except(inventory.Unlocks).ToArray();
                if (locked.Length == 0) throw new GameRuleException("All bidding options are unlocked.");
                var random = new CampaignRandom(resources.RandomState);
                var gumball = locked[random.NextInt(locked.Length)];
                resources = resources with { RandomState = random.State };
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, gumball] }; pulled = gumball.ToString(); break;
            case FeatureCategory.Tarot:
                if (!inventory.HasItemRoom) throw new GameRuleException(Full);
                inventory = inventory with { Tarot = [.. inventory.Tarot, Enum.Parse<TarotId>(offer.FeatureId)] }; break;
            case FeatureCategory.Sigil:
                var sigil = Enum.Parse<SigilId>(offer.FeatureId);
                if (sigilTarget is { } target) resources = ApplySigil(resources, sigil, target);
                else if (!inventory.HasItemRoom) throw new GameRuleException(Full);
                else inventory = inventory with { Sigils = [.. inventory.Sigils, sigil] };
                break;
            default: throw new GameRuleException("This item cannot be purchased.");
        }
        resources.Wallets[0] = run.Money - offer.Price;
        return run with { Money = resources.Wallets[0], Resources = resources with { Inventory = inventory, Revision = resources.Revision + 1 }, Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true, FeatureId = pulled ?? o.FeatureId } : o).ToArray() } };
    }
    /// <summary>How many shared-deck cards a Tarot must be aimed at.</summary>
    public static int TarotTargets(TarotId tarot) => Tarot.TargetCount(tarot);

    /// <summary>Buys a Tarot or Sigil and plays it at once on cards of the shared deck, so it needs no inventory slot.
    /// The changes last for the rest of the run.</summary>
    public static CampaignRun BuyAndUse(CampaignRun run, string offerId, IReadOnlyList<CardId> targets)
    {
        if (run.Phase != CampaignPhase.Shop || run.Shop is null) throw new GameRuleException("The shop is closed.");
        var offer = run.Shop.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        if (offer.Category == FeatureCategory.Sigil)
        {
            if (targets.Count != 1) throw new GameRuleException("Choose a card from the shared deck.");
            return Buy(run, offerId, targets[0]);
        }
        if (offer.Category != FeatureCategory.Tarot) throw new GameRuleException("Only a Tarot or Sigil can be used from the shop.");
        if (run.Money < offer.Price) throw new GameRuleException("Not enough money.");
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money - offer.Price;
        resources = ApplyTarot(resources, Enum.Parse<TarotId>(offer.FeatureId), targets);
        return run with
        {
            Money = resources.Wallets[0],
            Resources = resources with { Revision = resources.Revision + 1 },
            Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true } : o).ToArray() }
        };
    }
    // Plays a Tarot on the shared deck. Takes a copy it may change, with the human's wallet already up to date.
    private static CampaignSnapshot ApplyTarot(CampaignSnapshot resources, TarotId tarot, IReadOnlyList<CardId> targets)
    {
        var inventory = resources.Inventory; var deck = resources.Deck;
        var required = TarotTargets(tarot);
        if (targets.Count != required || targets.Distinct().Count() != required || targets.Any(id => deck.All(c => c.Id != id)))
            throw new GameRuleException(required == 1 ? "Choose a card from the shared deck." : $"Choose {required} different cards from the shared deck.");
        if (tarot == TarotId.Astronomer && inventory.Unlocks.Length == CampaignInventory.GumballCount) throw new GameRuleException("All bidding options are unlocked.");
        // Everything is checked before the random draws and the changes to the deck.
        var random = new CampaignRandom(resources.RandomState);
        var money = resources.Wallets[0];
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
                var options = Enum.GetValues<GumballId>().Except(inventory.Unlocks).ToArray();
                inventory = inventory with { Unlocks = [.. inventory.Unlocks, options[random.NextInt(options.Length)]] }; break;
            case TarotId.Gambler:
                var choices = deck.Select(c => c.Id).ToList(); random.Shuffle(choices);
                foreach (var id in choices.Take(2)) Enhance(id, EnhancementId.Lucky); break;
            case TarotId.DevilishDeal:
                var pool = Enum.GetValues<EnhancementId>();
                foreach (var id in targets.Take(2))
                    Enhance(id, pool[random.NextInt(pool.Length)]);
                Enhance(targets[2], EnhancementId.Cursed); break;
            default:
                Enhance(targets[0], tarot switch
                {
                    TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden,
                    TarotId.Glassblower => EnhancementId.Glass, TarotId.Traitor => EnhancementId.Silver,
                    TarotId.SpringLoaded => EnhancementId.Bouncy,
                    _ => throw new GameRuleException("Unknown Tarot.")
                }); break;
        }
        resources.Wallets[0] = money;
        return resources with { Inventory = inventory, RandomState = random.State };
    }
    public static CampaignRun SellJoker(CampaignRun run, JokerId id)
    {
        if (run.Phase != CampaignPhase.Shop || !run.Resources.Inventory.Has(id)) throw new GameRuleException("That Joker cannot be sold now.");
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money + 2;
        return run with { Money = run.Money + 2, Resources = resources with { Revision = resources.Revision + 1, Inventory = resources.Inventory with { Jokers = resources.Inventory.Jokers.Where(j => j.Id != id).ToArray() } } };
    }
    private static CampaignSnapshot ApplySigil(CampaignSnapshot resources, SigilId sigil, CardId target)
    {
        if (!resources.Deck.Any(c => c.Id == target)) throw new GameRuleException("Choose a card from the shared deck.");
        return resources with { Deck = resources.Deck.Select(c => c.Id == target ? c with { Sigil = sigil } : c).ToArray() };
    }
    /// <summary>Stored cards can be played or thrown away in the shop and on the screen before a match.</summary>
    private static CampaignSnapshot Held(CampaignRun run, int slot, int count, string kind, long? expectedRevision)
    {
        if (expectedRevision is { } revision && revision != run.Resources.Revision) throw new GameRuleException($"Your cards changed. Choose a {kind} again.");
        if (run.Phase is not (CampaignPhase.Shop or CampaignPhase.Intro) || slot < 0 || slot >= count) throw new GameRuleException($"No {kind} in that slot.");
        return run.Resources.Copy();
    }
    private static T[] Without<T>(T[] cards, int slot) => cards.Where((_, i) => i != slot).ToArray();
    private static CampaignRun Updated(CampaignRun run, CampaignSnapshot resources, CampaignInventory inventory) =>
        run with { Money = resources.Wallets[0], Resources = resources with { Revision = resources.Revision + 1, Inventory = inventory } };
    public static CampaignRun UseSigil(CampaignRun run, int slot, CardId target, long? expectedRevision = null)
    {
        var sigils = run.Resources.Inventory.Sigils;
        var resources = ApplySigil(Held(run, slot, sigils.Length, "Sigil", expectedRevision), sigils[slot], target);
        return Updated(run, resources, resources.Inventory with { Sigils = Without(sigils, slot) });
    }
    /// <summary>Plays a stored Tarot on cards of the shared deck.</summary>
    public static CampaignRun UseTarot(CampaignRun run, int slot, IReadOnlyList<CardId> targets, long? expectedRevision = null)
    {
        var tarot = run.Resources.Inventory.Tarot;
        var resources = Held(run, slot, tarot.Length, "Tarot", expectedRevision);
        // Outside the shop the wallet is the run's money.
        resources.Wallets[0] = run.Money;
        resources = ApplyTarot(resources, tarot[slot], targets);
        return Updated(run, resources, resources.Inventory with { Tarot = Without(tarot, slot) });
    }
    public static CampaignRun DiscardTarot(CampaignRun run, int slot, long? expectedRevision = null)
    {
        var tarot = run.Resources.Inventory.Tarot;
        var resources = Held(run, slot, tarot.Length, "Tarot", expectedRevision);
        return Updated(run, resources, resources.Inventory with { Tarot = Without(tarot, slot) });
    }
    public static CampaignRun DiscardSigil(CampaignRun run, int slot, long? expectedRevision = null)
    {
        var sigils = run.Resources.Inventory.Sigils;
        var resources = Held(run, slot, sigils.Length, "Sigil", expectedRevision);
        return Updated(run, resources, resources.Inventory with { Sigils = Without(sigils, slot) });
    }
}
