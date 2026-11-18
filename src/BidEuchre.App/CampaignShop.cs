using BidEuchre.Core;
namespace BidEuchre.App;

public static class CampaignShop
{
    public const int GumballPrice = 3;
    public const int ShopTargetCount = 6;
    /// <summary>The Fortune Teller prints one blind ticket for this, or a spread of <see cref="TicketSpread"/> to choose one from.</summary>
    public const int TicketPrice = 3, TicketSpreadPrice = 6, TicketSpread = 3;
    public static int PrintPrice(int count) => count switch
    {
        1 => TicketPrice, TicketSpread => TicketSpreadPrice, _ => throw new GameRuleException($"The Fortune Teller prints 1 or {TicketSpread} tickets.")
    };
    public const int JokerSellValue = 2;
    private const string Full = "Your card slots are full. Discard a stored card first, or use the new purchase now.";
    private const string PrintFirst = "Tickets are printed at the Fortune Teller.";
    public static int RerollPrice(CampaignRun run) => checked(2 + (run.Shop?.RerollCount ?? 0));

    internal static void RequireCurrentShop(CampaignRun run, long? expectedRevision, bool allowUseSession = false, bool allowArcadeResult = false)
    {
        if (run.Phase != CampaignPhase.Shop || run.Shop is null) throw new GameRuleException("The shop is closed.");
        if (expectedRevision is { } revision && revision != run.Resources.Revision)
            throw new GameRuleException("The shop changed. Choose again.");
        if (!allowUseSession) RequireNoUseSession(run);
        if (!allowArcadeResult) CampaignArcade.RequireNoPendingResult(run);
    }

    private static void RequireNoUseSession(CampaignRun run)
    {
        if (run.Shop?.UseSession is not null) throw new GameRuleException("Finish using your purchased item before leaving these six cards.");
    }

    public static CampaignRun Open(CampaignRun run)
    {
        if (run.Phase is not (CampaignPhase.Shop or CampaignPhase.Victory)) throw new GameRuleException("Shop is only available between matches.");
        if (run.Shop?.UseSession is not null) return run;
        if (run.Shop?.MatchJustFinished == run.Match - 1)
        {
            var upgradedOffers = run.Shop.Offers;
            // Direct Sigil stock is replaced by the Arcade. A committed old purchase
            // returns above and retains its backing offer until it has been used.
            if (upgradedOffers.Any(o => o.Category == FeatureCategory.Sigil && !o.Sold && !o.IsArcadePrize))
                upgradedOffers = upgradedOffers.Where(o => o.Category != FeatureCategory.Sigil || o.Sold || o.IsArcadePrize).ToArray();
            // Completed collections in older saves omitted the machine for this visit entirely.
            // Add that missing pull once without replenishing or rerolling any existing stock.
            if (run.Resources.Inventory.Unlocks.Length == CampaignInventory.GumballCount
                && run.Shop.Offers.All(offer => offer.Category != FeatureCategory.Gumball))
            {
                var id = $"{run.Match - 1}:gumball";
                while (run.Shop.Offers.Any(offer => offer.Id == id)) id += ":new";
                upgradedOffers = [.. upgradedOffers, new(id, FeatureCategory.Gumball, "", GumballPrice)];
            }
            // Old visits did not save their eligible cards. Upgrade only missing opportunities,
            // preserving purchases and paid tickets; subsequent visits to this screen do no work.
            var upgradeRandom = new CampaignRandom(run.Resources.RandomState);
            if (upgradedOffers.Any(NeedsTargets)) upgradedOffers = upgradedOffers.Select(o => NeedsTargets(o) ? o with { TargetCards = DrawTargets(run, upgradeRandom) } : o).ToArray();
            return ReferenceEquals(upgradedOffers, run.Shop.Offers) && run.Shop.Arcade is not null ? run : run with
            {
                Shop = run.Shop with { Offers = upgradedOffers, Arcade = run.Shop.Arcade ?? new() },
                Resources = run.Resources with { RandomState = upgradeRandom.State, Revision = run.Resources.Revision + 1 }
            };
        }
        var random = new CampaignRandom(run.Resources.RandomState);
        // A gumball is a blind pull: which bid comes out is only drawn when it is paid for.
        var gumball = new ShopOffer($"{run.Match - 1}:2", FeatureCategory.Gumball, "", GumballPrice);
        var offers = Stock(run, random, run.Match - 1, 0, gumball);
        return run with { Shop = new(run.Match - 1, offers) { Arcade = new() }, Resources = run.Resources with { RandomState = random.State, Revision = run.Resources.Revision + 1, Wallets = new[] { run.Money, run.Resources.Wallets[1], run.Resources.Wallets[2], run.Resources.Wallets[3] } } };
    }

    private static bool NeedsTargets(ShopOffer offer) => !offer.Sold && offer.TargetCards is null
        && (offer.Category == FeatureCategory.Sigil || offer.Category == FeatureCategory.Tarot && offer.Printed is not null);

    internal static CardId[] DrawTargets(CampaignRun run, CampaignRandom random)
    {
        var cards = run.Resources.Deck.Select(c => c.Id).OrderBy(id => id.Value).ToList();
        random.Shuffle(cards);
        return cards.Take(ShopTargetCount).ToArray();
    }

    /// <summary>Returns this offer's saved six-card opportunity without drawing or changing the run.</summary>
    public static CardState[] TargetCards(CampaignRun run, string offerId)
    {
        var offer = run.Shop?.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold || offer.Category is not (FeatureCategory.Sigil or FeatureCategory.Tarot)
            || offer.Category == FeatureCategory.Tarot && offer.Printed is null) return [];
        return CardsForOffer(run, offer);
    }

    private static CardState[] CardsForOffer(CampaignRun run, ShopOffer offer)
    {
        var ids = offer.TargetCards;
        if (ids is null || ids.Length != ShopTargetCount || ids.Distinct().Count() != ShopTargetCount
            || ids.Any(id => run.Resources.Deck.All(c => c.Id != id))) return [];
        return ids.Select(id => run.Resources.Deck.Single(c => c.Id == id)).ToArray();
    }

    /// <summary>The current card states for a committed purchase's unchanged six physical cards.</summary>
    public static CardState[] UseSessionCards(CampaignRun run)
    {
        if (run.Shop?.UseSession is not { } session || run.Phase != CampaignPhase.Shop) return [];
        var offer = run.Shop.Offers.SingleOrDefault(o => o.Id == session.OfferId);
        return offer is { Sold: true, Printed: null } && offer.Category == session.Category && offer.FeatureId == session.FeatureId
            && session.Category is FeatureCategory.Sigil or FeatureCategory.Tarot ? CardsForOffer(run, offer) : [];
    }

    /// <summary>Charges and reserves a Sigil before opening its six-card menu. No inventory slot is needed.</summary>
    public static CampaignRun BeginSigilUse(CampaignRun run, string offerId, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = run.Shop!.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold || offer.Category != FeatureCategory.Sigil) throw new GameRuleException("Choose an available Sigil.");
        RequireActiveOffer(offer);
        RequireTargets(run, offer, []);
        if (run.Money < offer.Price) throw new GameRuleException("Not enough money.");
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money - offer.Price;
        return run with
        {
            Money = resources.Wallets[0],
            Resources = resources with { Revision = resources.Revision + 1 },
            Shop = run.Shop with
            {
                Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true } : o).ToArray(),
                UseSession = new(offerId, offer.Category, offer.FeatureId, false)
            }
        };
    }

    /// <summary>Commits one already-paid printed ticket to its six cards, losing the other tickets without charging again.</summary>
    public static CampaignRun BeginTicketUse(CampaignRun run, string offerId, TarotId ticket, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = Chosen(run, offerId, ticket);
        RequireTargets(run, offer, []);
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money;
        var committed = Took(run, offer, ticket, resources);
        return committed with { Shop = committed.Shop! with { UseSession = new(offerId, FeatureCategory.Tarot, ticket.ToString(), false) } };
    }

    private static ShopUseSession RequireUseSession(CampaignRun run, long? expectedRevision)
    {
        RequireCurrentShop(run, expectedRevision, allowUseSession: true);
        var session = run.Shop!.UseSession ?? throw new GameRuleException("Open a purchased item's six cards first.");
        if (UseSessionCards(run).Length != ShopTargetCount) throw new GameRuleException("This purchase has no valid six-card opportunity.");
        return session;
    }

    private static void RequireSessionTargets(CampaignRun run, IReadOnlyList<CardId> targets)
    {
        var eligible = UseSessionCards(run).Select(c => c.Id).ToArray();
        if (eligible.Length != ShopTargetCount || targets.Any(id => !eligible.Contains(id)))
            throw new GameRuleException("Choose from the six cards offered for this purchase.");
    }

    /// <summary>Applies the committed purchase once, leaving the menu open for held Tarots.</summary>
    public static CampaignRun UsePurchased(CampaignRun run, IReadOnlyList<CardId> targets, long? expectedRevision = null)
    {
        var session = RequireUseSession(run, expectedRevision);
        if (session.PurchasedItemUsed) throw new GameRuleException("The purchased item has already been used.");
        RequireSessionTargets(run, targets);
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money;
        if (session.Category == FeatureCategory.Sigil)
        {
            if (targets.Count != 1) throw new GameRuleException("Choose one of the six offered cards.");
            resources = ApplySigil(resources, Enum.Parse<SigilId>(session.FeatureId), targets[0]);
        }
        else resources = ApplyTarot(resources, Enum.Parse<TarotId>(session.FeatureId), targets);
        return run with
        {
            Money = resources.Wallets[0], Resources = resources with { Revision = resources.Revision + 1 },
            Shop = run.Shop! with { UseSession = session with { PurchasedItemUsed = true } }
        };
    }

    /// <summary>Uses a held Tarot before or after the purchased item, on the same six cards.</summary>
    public static CampaignRun UseHeldTarot(CampaignRun run, int slot, IReadOnlyList<CardId> targets, long? expectedRevision = null)
    {
        RequireUseSession(run, expectedRevision);
        var held = run.Resources.Inventory.Tarot;
        if (slot < 0 || slot >= held.Length) throw new GameRuleException("No Tarot in that slot.");
        RequireSessionTargets(run, targets);
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money;
        resources = ApplyTarot(resources, held[slot], targets);
        return Updated(run, resources, resources.Inventory with { Tarot = Without(resources.Inventory.Tarot, slot) });
    }

    /// <summary>Closes the opportunity only once the committed purchase has been applied.</summary>
    public static CampaignRun FinishUse(CampaignRun run, long? expectedRevision = null)
    {
        var session = RequireUseSession(run, expectedRevision);
        if (!session.PurchasedItemUsed) throw new GameRuleException("Use your purchased item before finishing.");
        return run with { Shop = run.Shop! with { UseSession = null }, Resources = run.Resources with { Revision = run.Resources.Revision + 1 } };
    }

    private static void RequireTargets(CampaignRun run, ShopOffer offer, IReadOnlyList<CardId> targets)
    {
        var eligible = TargetCards(run, offer.Id).Select(c => c.Id).ToArray();
        if (eligible.Length != ShopTargetCount || targets.Any(id => !eligible.Contains(id)))
            throw new GameRuleException("Choose from the six cards offered for this purchase.");
    }

    /// <summary>Replenishes regular stock, including purchased slots, while retaining this visit's gumball pull.</summary>
    public static CampaignRun Reroll(CampaignRun run, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var shop = run.Shop!;
        RequireNoPrintedTickets(run);
        if (shop.RerollCount < 0 || shop.RerollCount >= int.MaxValue - 2) throw new GameRuleException("This shop cannot be rerolled again.");
        var price = RerollPrice(run);
        if (run.Money < price) throw new GameRuleException("Not enough money.");
        // Validate before drawing or copying; payment, RNG, stock and revision form one transaction.
        var random = new CampaignRandom(run.Resources.RandomState);
        var count = shop.RerollCount + 1;
        var offers = Stock(run, random, shop.MatchJustFinished, count, shop.Offers.SingleOrDefault(o => o.Category == FeatureCategory.Gumball));
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money - price;
        return run with
        {
            Money = resources.Wallets[0],
            Shop = shop with { Offers = offers, RerollCount = count },
            Resources = resources with { RandomState = random.State, Revision = resources.Revision + 1 }
        };
    }

    private static ShopOffer[] Stock(CampaignRun run, CampaignRandom random, int visit, int generation, ShopOffer? gumball)
    {
        var inventory = run.Resources.Inventory;
        var offers = new List<ShopOffer>();
        void Roll(FeatureCategory category, int number)
        {
            var options = FeatureCatalog.All.Where(f => f.Category == category)
                .Where(f => category != FeatureCategory.Joker || !inventory.Jokers.Any(j => j.Id.ToString() == f.Key)).ToList();
            for (var i = 0; i < number && options.Count > 0; i++)
            {
                var choice = options[random.NextInt(options.Count)]; options.Remove(choice);
                var id = generation == 0 ? $"{visit}:{offers.Count}" : $"{visit}:r{generation}:{offers.Count}";
                offers.Add(new(id, category, choice.Key, choice.Price)
                { TargetCards = category == FeatureCategory.Sigil ? DrawTargets(run, random) : null });
            }
        }
        Roll(FeatureCategory.Joker, 2);
        if (gumball is not null) offers.Add(gumball);
        // The Fortune Teller's tickets are blind: which ones come out is drawn when they are printed.
        offers.Add(new(generation == 0 ? $"{visit}:{offers.Count}" : $"{visit}:r{generation}:{offers.Count}", FeatureCategory.Tarot, "", TicketPrice));
        return offers.ToArray();
    }
    public static CampaignRun Buy(CampaignRun run, string offerId, CardId? sigilTarget = null, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = run.Shop!.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        RequireActiveOffer(offer);
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
                // Draw at purchase time: locked bids first, then repeat acquisitions.
                var grant = GumballGrants.Pull(inventory, resources.RandomState);
                resources = resources with { RandomState = grant.RandomState };
                inventory = grant.Inventory; pulled = grant.Id.ToString(); break;
            case FeatureCategory.Tarot: throw new GameRuleException(PrintFirst);
            case FeatureCategory.Sigil:
                var sigil = Enum.Parse<SigilId>(offer.FeatureId);
                if (sigilTarget is { } target)
                {
                    RequireTargets(run, offer, [target]);
                    resources = ApplySigil(resources, sigil, target);
                }
                else if (!inventory.HasItemRoom) throw new GameRuleException(Full);
                else inventory = inventory with { Sigils = [.. inventory.Sigils, sigil] };
                break;
            default: throw new GameRuleException("This item cannot be purchased.");
        }
        resources.Wallets[0] = run.Money - offer.Price;
        return run with { Money = resources.Wallets[0], Resources = resources with { Inventory = inventory, Revision = resources.Revision + 1 }, Shop = run.Shop with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Sold = true, FeatureId = pulled ?? o.FeatureId } : o).ToArray() } };
    }
    private static void RequireActiveOffer(ShopOffer offer)
    {
        if (offer.Category != FeatureCategory.Sigil) return;
        if (!FeatureCatalog.All.Any(f => f.Category == offer.Category && f.Key == offer.FeatureId))
            throw new GameRuleException("Unknown shop item.");
    }
    /// <summary>How many eligible cards a Tarot must be aimed at.</summary>
    public static int TarotTargets(TarotId tarot) => Tarot.TargetCount(tarot);

    /// <summary>Buys a Sigil and plays it at once on one of its six offered cards, so it needs no inventory slot.
    /// The changes last for the rest of the run.</summary>
    public static CampaignRun BuyAndUse(CampaignRun run, string offerId, IReadOnlyList<CardId> targets, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = run.Shop!.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Sold) throw new GameRuleException("That item is sold out.");
        if (offer.Category == FeatureCategory.Sigil)
        {
            if (targets.Count != 1) throw new GameRuleException("Choose one of the six offered cards.");
            return Buy(run, offerId, targets[0], expectedRevision);
        }
        throw new GameRuleException(offer.Category == FeatureCategory.Tarot ? PrintFirst : "Only a Sigil can be bought and used at once.");
    }

    /// <summary>Pays the Fortune Teller to print <paramref name="count"/> blind tickets (1 or <see cref="TicketSpread"/>, all different).
    /// They wait on the offer until one is kept or used; the others are lost. One print a visit.</summary>
    public static CampaignRun PrintTickets(CampaignRun run, string offerId, int count, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = Teller(run, offerId);
        if (offer.Printed is not null) throw new GameRuleException("Choose one of your printed tickets first.");
        var price = PrintPrice(count);
        if (run.Money < price) throw new GameRuleException("Not enough money.");
        var random = new CampaignRandom(run.Resources.RandomState);
        var pool = Enum.GetValues<TarotId>().ToList();
        var printed = new string[count];
        for (var i = 0; i < count; i++) { var pick = pool[random.NextInt(pool.Count)]; pool.Remove(pick); printed[i] = pick.ToString(); }
        var targets = DrawTargets(run, random);
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money - price;
        return run with
        {
            Money = resources.Wallets[0],
            Resources = resources with { RandomState = random.State, Revision = resources.Revision + 1 },
            Shop = run.Shop! with { Offers = run.Shop.Offers.Select(o => o.Id == offerId ? o with { Printed = printed, TargetCards = targets } : o).ToArray() }
        };
    }

    /// <summary>Keeps one printed ticket in a free card slot.</summary>
    public static CampaignRun KeepTicket(CampaignRun run, string offerId, TarotId ticket, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = Chosen(run, offerId, ticket);
        if (!run.Resources.Inventory.HasItemRoom) throw new GameRuleException(Full);
        var resources = run.Resources.Copy();
        return Took(run, offer, ticket, resources with { Inventory = resources.Inventory with { Tarot = [.. resources.Inventory.Tarot, ticket] } });
    }

    /// <summary>Plays one printed ticket on its saved six-card opportunity, so it needs no slot.</summary>
    public static CampaignRun UseTicket(CampaignRun run, string offerId, TarotId ticket, IReadOnlyList<CardId> targets, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        var offer = Chosen(run, offerId, ticket);
        RequireTargets(run, offer, targets);
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money;
        return Took(run, offer, ticket, ApplyTarot(resources, ticket, targets));
    }

    /// <summary>Printed tickets are paid for, so the shop cannot be left or restocked until one is taken.</summary>
    public static void RequireNoPrintedTickets(CampaignRun run)
    {
        RequireNoUseSession(run);
        CampaignArcade.RequireNoPendingResult(run);
        if (run.Shop?.Offers.Any(o => o.Printed is not null) is true) throw new GameRuleException("Choose one of your printed tickets first.");
    }

    private static ShopOffer Teller(CampaignRun run, string offerId)
    {
        var offer = run.Shop!.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Category != FeatureCategory.Tarot) throw new GameRuleException("The Fortune Teller is not here.");
        if (offer.Sold) throw new GameRuleException("The Fortune Teller has already printed this visit.");
        return offer;
    }

    private static ShopOffer Chosen(CampaignRun run, string offerId, TarotId ticket)
    {
        var offer = Teller(run, offerId);
        if (offer.Printed is null || !offer.Printed.Contains(ticket.ToString())) throw new GameRuleException("Choose one of the printed tickets.");
        return offer;
    }

    private static CampaignRun Took(CampaignRun run, ShopOffer offer, TarotId ticket, CampaignSnapshot resources) => run with
    {
        Money = resources.Wallets[0],
        Resources = resources with { Revision = resources.Revision + 1 },
        Shop = run.Shop! with { Offers = run.Shop.Offers.Select(o => o.Id == offer.Id ? o with { Sold = true, FeatureId = ticket.ToString(), Printed = null } : o).ToArray() }
    };
    // Plays a purchased Tarot on its offered cards. Takes a copy it may change, with the human's wallet already up to date.
    private static CampaignSnapshot ApplyTarot(CampaignSnapshot resources, TarotId tarot, IReadOnlyList<CardId> targets)
    {
        var inventory = resources.Inventory; var deck = resources.Deck;
        var required = TarotTargets(tarot);
        if (targets.Count != required || targets.Distinct().Count() != required || targets.Any(id => deck.All(c => c.Id != id)))
            throw new GameRuleException(required == 1 ? "Choose one of the six offered cards." : $"Choose {required} different cards from the six offered cards.");
        // Everything is checked before the random draws and the changes to the deck.
        var random = new CampaignRandom(resources.RandomState);
        var money = resources.Wallets[0];
        void Change(CardId id, Func<CardState, CardState> update) => deck[Array.FindIndex(deck, c => c.Id == id)] = update(deck.First(c => c.Id == id));
        void Enhance(CardId id, EnhancementId enhancement) => Change(id, c => c with { Enhancement = enhancement });
        switch (tarot)
        {
            case TarotId.TaxCollector: money += Math.Min(20, money); break;
            case TarotId.Astronomer:
                var grant = GumballGrants.Pull(inventory, random.State);
                inventory = grant.Inventory; random = new(grant.RandomState); break;
            case TarotId.Gambler:
                foreach (var id in targets) Enhance(id, EnhancementId.Lucky); break;
            case TarotId.DevilishDeal:
                var pool = Enum.GetValues<EnhancementId>();
                foreach (var id in targets.Take(2))
                    Enhance(id, pool[random.NextInt(pool.Length)]);
                Enhance(targets[2], EnhancementId.Cursed); break;
            default:
                Enhance(targets[0], Tarot.EnhancementOf(tarot) ?? throw new GameRuleException("Unknown Tarot.")); break;
        }
        resources.Wallets[0] = money;
        return resources with { Inventory = inventory, RandomState = random.State };
    }
    public static CampaignRun SellJoker(CampaignRun run, JokerId id, long? expectedRevision = null)
    {
        RequireCurrentShop(run, expectedRevision);
        if (!run.Resources.Inventory.Has(id)) throw new GameRuleException("That Joker cannot be sold now.");
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money + JokerSellValue;
        return run with { Money = run.Money + JokerSellValue, Resources = resources with { Revision = resources.Revision + 1, Inventory = resources.Inventory with { Jokers = resources.Inventory.Jokers.Where(j => j.Id != id).ToArray() } } };
    }
    private static CampaignSnapshot ApplySigil(CampaignSnapshot resources, SigilId sigil, CardId target)
    {
        if (!resources.Deck.Any(c => c.Id == target)) throw new GameRuleException("Choose a card from the shared deck.");
        return resources with { Deck = resources.Deck.Select(c => c.Id == target ? c with { Sigil = sigil } : c).ToArray() };
    }
    /// <summary>Stored cards can be discarded in the shop and on the screen before a match.</summary>
    private static CampaignSnapshot Held(CampaignRun run, int slot, int count, string kind, long? expectedRevision)
    {
        RequireNoUseSession(run);
        if (expectedRevision is { } revision && revision != run.Resources.Revision) throw new GameRuleException($"Your cards changed. Choose a {kind} again.");
        if (run.Phase is not (CampaignPhase.Shop or CampaignPhase.Intro) || slot < 0 || slot >= count) throw new GameRuleException($"No {kind} in that slot.");
        return run.Resources.Copy();
    }
    private static T[] Without<T>(T[] cards, int slot) => cards.Where((_, i) => i != slot).ToArray();
    private static CampaignRun Updated(CampaignRun run, CampaignSnapshot resources, CampaignInventory inventory) =>
        run with { Money = resources.Wallets[0], Resources = resources with { Revision = resources.Revision + 1, Inventory = inventory } };
    public static CampaignRun UseSigil(CampaignRun run, int slot, CardId target, long? expectedRevision = null)
        => throw new GameRuleException("Use stored Sigils on cards in your hand during a match.");
    /// <summary>Stored Tarots may also be played while a committed shop purchase's six-card menu is open.</summary>
    public static CampaignRun UseTarot(CampaignRun run, int slot, IReadOnlyList<CardId> targets, long? expectedRevision = null)
        => run.Shop?.UseSession is not null ? UseHeldTarot(run, slot, targets, expectedRevision)
            : throw new GameRuleException("Use stored Tarots during a match or while a purchased item's six cards are open.");
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
