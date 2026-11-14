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
    { f.Id, f.Category, f.Key, f.Name, f.Description, f.Price, targetCount = TargetCount(f.Category, f.Key) }).ToArray();
    public static int TargetCount(FeatureCategory category, string key) => category switch
    { FeatureCategory.Tarot => CampaignShop.TarotTargets(Enum.Parse<TarotId>(key)), FeatureCategory.Sigil => 1, _ => 0 };
    public static object Inventory(CampaignRun run)
    {
        var inventory = run.Resources.Inventory;
        return new
        {
            jokers = inventory.Jokers.Select(j => new { j.Id, j.Value, sellValue = CampaignShop.JokerSellValue,
                sell = Can(run.Phase == CampaignPhase.Shop && !run.IsOver, "Jokers can be sold in the shop.") }).ToArray(),
            tarot = inventory.Tarot.Select((id, slot) => new { slot, id, targetCount = CampaignShop.TarotTargets(id) }).ToArray(),
            sigils = inventory.Sigils.Select((id, slot) => new { slot, id, targetCount = 1 }).ToArray(),
            gumballs = inventory.Unlocks.Select(id => new { id, copies = inventory.GumballCopies(id),
                bonus = id == GumballId.PanicButton ? 0 : inventory.GumballExtraCopies.GetValueOrDefault(id) }).ToArray(),
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
            lastTrick = c.LastTrick.Select(Play).ToArray(), c.Events, c.PendingChoice, error
        };
    }
    public static object? Shop(CampaignRun run)
    {
        if (run.Shop is not { } shop) return null;
        return new
        {
            offers = shop.Offers.Select(o => new { o.Id, o.Category, o.FeatureId, o.Price, o.Sold,
                targetCount = TargetCount(o.Category, o.FeatureId),
                buy = PurchaseCapability(run, o, false), buyUse = PurchaseCapability(run, o, true) }).ToArray(),
            rerollPrice = CampaignShop.RerollPrice(run), shop.RerollCount,
            reroll = Can(run.Phase == CampaignPhase.Shop && !run.IsOver && run.Money >= CampaignShop.RerollPrice(run), "Not enough money or shop closed.")
        };
    }
    private static CampaignCapability PurchaseCapability(CampaignRun run, ShopOffer offer, bool use)
    {
        if (run.Phase != CampaignPhase.Shop || run.IsOver) return Can(false, "The shop is closed.");
        if (offer.Sold) return Can(false, "Sold out.");
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
        var shop = phase == "Shop"; var items = phase is "Shop" or "Intro";
        var capabilities = new Dictionary<string, CampaignCapability>
        {
            ["new"] = Can(true), ["delete"] = Can(run is not null || corrupt), ["continue"] = Can(run is not null && !corrupt),
            ["start-match"] = Can(phase == "Intro"), ["forfeit"] = Can(run?.Phase == CampaignPhase.InMatch),
            ["next-hand"] = Can(live && view?.Phase == GamePhase.HandComplete),
            ["commands"] = Can(live && view?.CurrentSeat == 0), ["arrange-hand"] = Can(live),
            ["items-use"] = Can(items), ["items-discard"] = Can(items), ["result-continue"] = Can(result is not null),
            ["victory-continue"] = Can(phase == "Victory"), ["shop-leave"] = Can(shop),
            ["shop-reroll"] = Can(shop && run!.Money >= CampaignShop.RerollPrice(run), "Not enough money or shop closed."),
            ["shop-sell"] = Can(shop && run!.Resources.Inventory.Jokers.Length > 0)
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
                id == GumballId.PanicButton ? 0 : after.GumballExtraCopies.GetValueOrDefault(id))).ToArray();
        return new(message, grants);
    }
}
