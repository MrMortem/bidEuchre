using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Server;

public sealed record CampaignCapability(bool Enabled, string? Reason);
public sealed record CampaignGrantFeedback(string Id, bool Duplicate, long Copies, int Bonus);
public sealed record CampaignFeedback(string Message, CampaignGrantFeedback[] Grants);

// Explicit public projections keep the private resource snapshot out of HTTP serialization.
public static class CampaignProjection
{
    public static CampaignCapability Can(bool enabled, string reason = "Unavailable in this phase.") => new(enabled, enabled ? null : reason);
    public static object Card(CardState card) => Card(new CampaignCardView(card.Id, card.Face, card.Enhancement, card.Sigil));
    public static object Card(CampaignCardView card) => new { id = card.Id.Value,
        face = new { card.Face.Code, card.Face.Suit, card.Face.Rank }, card.Enhancement, card.Sigil };
    public static object[] Catalog() => FeatureCatalog.All.Select(f => (object)new
    { f.Id, f.Category, f.Key, f.Name, f.Description, f.Price, targetCount = TargetCount(f.Category, f.Key),
      collectorTitle = f.Category == FeatureCategory.Sigil ? FeatureCatalog.CollectorTitle(Enum.Parse<SigilId>(f.Key)) : null }).ToArray();
    public static int TargetCount(FeatureCategory category, string key) => category switch
    { FeatureCategory.Tarot => Enum.TryParse<TarotId>(key, out var tarot) ? CampaignShop.TarotTargets(tarot) : 0, FeatureCategory.Sigil => 1, _ => 0 }; // An unprinted Fortune Teller has no ticket yet.
    public static object Inventory(CampaignRun run)
    {
        var inventory = run.Resources.Inventory;
        return new
        {
            jokers = inventory.Jokers.Select(j => new { j.Id, j.Value, sellValue = CampaignShop.JokerSellValue,
                sell = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && run.Shop?.UseSession is null && run.Shop?.Arcade?.Pending is null, "Jokers can be sold in the shop.") }).ToArray(),
            tarot = inventory.Tarot.Select((id, slot) => new { slot, id, targetCount = CampaignShop.TarotTargets(id) }).ToArray(),
            sigils = inventory.Sigils.Select((id, slot) => new { slot, id, targetCount = 1 }).ToArray(),
            gumballs = inventory.Unlocks.Select(id => new { id, copies = inventory.GumballCopies(id),
                bonus = inventory.GumballExtraCopies.GetValueOrDefault(id) }).ToArray(),
            inventory.ItemCount, itemSlots = CampaignInventory.ItemSlots, jokerSlots = 5
        };
    }
    public static object? Table(GameView? view, string? error)
    {
        if (view?.Campaign is not { } c) return null;
        object Play(CampaignTrickPlay p) => new { p.Seat, card = Card(p.Card), p.EffectiveSuit };
        return new
        {
            view.Phase, view.HandNumber, view.Dealer, view.CurrentSeat, view.Scores,
            players = c.Players.Select(p => new { p.Seat, view.Players[p.Seat].Name, view.Players[p.Seat].Team,
                p.CardCount, visibleCards = p.VisibleCards.Select(Card).ToArray(), p.IsSittingOut }).ToArray(),
            view.Auction, view.HighBid, view.Bidder, view.Contract, view.TricksByTeam, view.GameWinner,
            view.HandHistory, view.PublicEvents, view.CompletionReason, c.Revision, c.DealSerial,
            legalCommands = c.LegalActions.Select(CampaignWire.Command).ToArray(), trick = c.Trick.Select(Play).ToArray(),
            lastTrick = c.LastTrick.Select(Play).ToArray(), c.Events, c.PendingChoice, error, c.EffectSequence,
            pokerResult = c.PokerResult is not { } poker ? null : new
            {
                winners = poker.Winners.Select(w => new { w.Seat, w.Category, cardIds = w.CardIds.Select(id => id.Value).ToArray() }).ToArray(),
                poker.TeamPoints
            },
            scoringEffects = c.ScoringEffects.Select(effect => new
            {
                effect.Sequence, effect.Effect, effect.Reward, effect.Amount, effect.Recipient,
                sourceCard = effect.SourceCard?.Value, effect.SourceSeat, effect.TrickNumber
            }).ToArray()
        };
    }
    public static object? Shop(CampaignRun run)
    {
        if (run.Shop is not { } shop) return null;
        return new
        {
            useSession = shop.UseSession is not { } session ? null : new
            {
                session.OfferId, session.Category, session.FeatureId, session.PurchasedItemUsed,
                targetCount = TargetCount(session.Category, session.FeatureId),
                targetCards = CampaignShop.UseSessionCards(run).Select(Card).ToArray()
            },
            arcade = Arcade(run),
            offers = shop.Offers.Where(o => !o.IsArcadePrize).Select(o => new { o.Id, o.Category, o.FeatureId, o.Price, o.Sold,
                targetCount = TargetCount(o.Category, o.FeatureId),
                targetCards = CampaignShop.TargetCards(run, o.Id).Select(Card).ToArray(),
                tickets = (o.Printed ?? []).Select(id => new { id, targetCount = TargetCount(FeatureCategory.Tarot, id),
                    keep = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && shop.UseSession is null && shop.Arcade?.Pending is null && !o.Sold && run.Resources.Inventory.HasItemRoom, "The four shared Ticket and Sigil slots are full or the shop is closed."),
                    use = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && shop.UseSession is null && shop.Arcade?.Pending is null && !o.Sold) }).ToArray(),
                printOptions = o.Category == FeatureCategory.Tarot ? new[] { 1, CampaignShop.TicketSpread }.Select(count => new { count,
                    price = CampaignShop.PrintPrice(count), buy = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && shop.UseSession is null && shop.Arcade?.Pending is null && !o.Sold && o.Printed is null && run.Money >= CampaignShop.PrintPrice(count),
                        o.Printed is not null ? "Choose one of your printed tickets first." : o.Sold ? "Already printed this visit." : "Not enough money or shop closed.") }).ToArray() : [],
                buy = PurchaseCapability(run, o, false), buyUse = PurchaseCapability(run, o, true) }).ToArray(),
            rerollPrice = CampaignShop.RerollPrice(run), shop.RerollCount,
            reroll = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && shop.UseSession is null && shop.Arcade?.Pending is null && !shop.Offers.Any(o => o.Printed is not null) && run.Money >= CampaignShop.RerollPrice(run), "Resolve your purchase or Arcade result, or check your balance.")
        };
    }
    private static string FreeJokerText(int free) => $"fills {free} empty Joker slot{(free == 1 ? "" : "s")}";
    private static object Arcade(CampaignRun run)
    {
        var shop = run.Shop!;
        var arcade = shop.Arcade ?? new ArcadeState();
        var open = run.Phase == CampaignPhase.Shop && !run.IsOver && shop.UseSession is null;
        var ready = open && arcade.Pending is null && !shop.Offers.Any(o => o.Printed is not null);
        CampaignCapability Play(bool played) => Can(ready && !played && run.Money >= CampaignArcade.Price,
            played ? "Already played this shop visit." : !ready ? "Resolve your purchase or Arcade result first." : "Not enough money.");
        var result = arcade.Pending;
        return new
        {
            price = CampaignArcade.Price, plinkoLaneCount = CampaignArcade.PlinkoLaneCount, plinkoScores = CampaignArcade.PlinkoScores, arcade.PlinkoPlayed, arcade.RoulettePlayed,
            plinko = Play(arcade.PlinkoPlayed), roulette = Play(arcade.RoulettePlayed),
            pending = result is null ? null : new
            {
                result.PlayId, result.Game, result.Choice, result.Landing, result.Score, result.Path,
                result.PrizeCategory, result.FeatureId,
                targetCount = result.PrizeCategory is { } category && result.FeatureId is { } key ? TargetCount(category, key) : 0,
                targetCards = result.TargetCards.Select(id => Card(run.Resources.Deck.Single(c => c.Id == id))).ToArray(),
                pack = result.PackCards?.Select(key => key == CampaignArcade.LegendaryKey
                    ? new { key, title = "Legendary", name = FreeJokerText(CampaignArcade.JokerSlots - run.Resources.Inventory.Jokers.Length), legendary = true }
                    : new { key, title = FeatureCatalog.CollectorTitle(Enum.Parse<SigilId>(key)), name = FeatureCatalog.Get(FeatureCategory.Sigil, key).Name + " Sigil", legendary = false }).ToArray(),
                keep = Can(open && result.PrizeCategory is not null && !CampaignArcade.IsPack(result) && run.Resources.Inventory.HasItemRoom,
                    CampaignArcade.IsPack(result) ? "Open your Collector's Pack and choose a card." : "The four shared Ticket and Sigil slots are full. Use your prize now, or discard an item."),
                use = Can(open && result.PrizeCategory is not null && !CampaignArcade.IsPack(result)),
                pick = Can(open && CampaignArcade.IsPack(result)),
                dismiss = Can(open && result.PrizeCategory is null, "Keep or use your prize first.")
            }
        };
    }
    private static CampaignCapability PurchaseCapability(CampaignRun run, ShopOffer offer, bool use)
    {
        if (run.Phase != CampaignPhase.Shop || run.IsOver) return Can(false, "The shop is closed.");
        if (run.Shop?.UseSession is not null) return Can(false, "Finish using your purchase first.");
        if (run.Shop?.Arcade?.Pending is not null) return Can(false, "Resolve your Arcade result first.");
        if (offer.Sold) return Can(false, "Sold out.");
        if (offer.Category == FeatureCategory.Tarot) return Can(false, "Print tickets at the Fortune Teller first.");
        if (run.Money < offer.Price) return Can(false, "Not enough money.");
        if (use) return Can(offer.Category is FeatureCategory.Tarot or FeatureCategory.Sigil, "Only Tarot and Sigil cards can be used immediately.");
        var inventory = run.Resources.Inventory;
        if (offer.Category is FeatureCategory.Tarot or FeatureCategory.Sigil) return Can(inventory.HasItemRoom, "The four shared Tarot and Sigil slots are full.");
        if (offer.Category == FeatureCategory.Joker) return Can(inventory.Jokers.Length < 5 && !inventory.Jokers.Any(j => j.Id.ToString() == offer.FeatureId), "Five Joker slots; no duplicates.");
        return Can(true);
    }
    public static object State(CampaignRun? run, string version, bool dormant, bool corrupt, bool dirty,
        CampaignSavedResult? result, GameView? view, string? error, string? notice, CampaignFeedback? feedback)
    {
        var phase = corrupt ? "Recovery" : run is null ? "Empty" : result is not null ? "Result" : run.IsOver ? "RunOver" : run.Phase.ToString();
        var live = run?.Phase == CampaignPhase.InMatch && result is null && !dormant;
        var revealingPoker = view?.Phase == GamePhase.PokerReveal;
        var shop = phase == "Shop"; var items = phase is "Shop" or "Intro";
        var useSession = shop && run?.Shop?.UseSession is not null;
        var arcadePending = run?.Shop?.Arcade?.Pending is not null;
        var printed = run?.Shop?.Offers.Any(o => o.Printed is not null) == true;
        var capabilities = new Dictionary<string, CampaignCapability>
        {
            ["new"] = Can(true), ["delete"] = Can(run is not null || corrupt), ["continue"] = Can(run is not null && !corrupt),
            ["start-match"] = Can(phase == "Intro"), ["forfeit"] = Can(run?.Phase == CampaignPhase.InMatch),
            ["next-hand"] = Can(live && view?.Phase == GamePhase.HandComplete),
            ["commands"] = Can(live && view?.CurrentSeat == 0), ["arrange-hand"] = Can(live && !revealingPoker),
            ["items-use"] = Can(live && !revealingPoker && view?.CurrentSeat == 0 || useSession, "Use held items during a match, or held tickets in an open six-card opportunity."), ["items-discard"] = Can(items && !useSession), ["result-continue"] = Can(result is not null),
            ["victory-continue"] = Can(phase == "Victory"), ["shop-leave"] = Can(shop && !printed && !useSession && !arcadePending, "Finish your purchase, printed ticket or Arcade result first."),
            ["shop-reroll"] = Can(shop && !printed && !useSession && !arcadePending && run!.Money >= CampaignShop.RerollPrice(run), "Resolve your purchase or Arcade result, or check your balance."),
            ["shop-sell"] = Can(shop && !useSession && !arcadePending && run!.Resources.Inventory.Jokers.Length > 0),
            ["shop-use-purchased"] = Can(useSession && !run!.Shop!.UseSession!.PurchasedItemUsed, "The purchased item has already been used."),
            ["shop-finish-use"] = Can(useSession && run!.Shop!.UseSession!.PurchasedItemUsed, "Use your purchased item first.")
        };
        return new
        {
            hasRun = run is not null, version, phase, requiresContinue = dormant,
            run = run is null ? null : new { run.Lives, run.Ante, run.Match, run.MatchInAnte, boss = run.IsBoss, run.Money,
                run.Threshold, negativeThreshold = run.NextMatch().NegativeThreshold, handLimit = run.NextMatch().HandLimit,
                opponentBonus = run.OpponentSuccessfulBidBonus, run.MatchesWon, run.IsOver, run.VictoryShown },
            table = Table(view, error), shop = run is null ? null : Shop(run), deck = run?.Resources.Deck.Select(Card).ToArray() ?? [],
            inventory = run is null ? null : Inventory(run), pendingResult = result, capabilities,
            saveStatus = corrupt ? "corrupt" : dirty ? "failed" : "saved",
            notice = dirty ? "Could not save progress. Changes are retained in this session only." : notice,
            catalog = Catalog(), feedback
        };
    }
    public static CampaignFeedback? Feedback(CampaignInventory before, CampaignInventory after, string message)
    {
        var grants = Enum.GetValues<GumballId>().Where(id => after.GumballCopies(id) > before.GumballCopies(id))
            .Select(id => new CampaignGrantFeedback(id.ToString(), before.GumballCopies(id) > 0, after.GumballCopies(id),
                after.GumballExtraCopies.GetValueOrDefault(id))).ToArray();
        return new(message, grants);
    }
}
