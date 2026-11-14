namespace BidEuchre.Core;

public enum EnhancementId { Lucky, Golden, Wild, Cursed, Stone, Emerald, Silver, Stinky, Glass, Bouncy }
public enum JokerId { ThreesCompany, HighRoller, LoverBoy, SnakeEyes, TwinFlame, Clubhouse, TheUndertaker, RollerCoaster, DejaVu, LastLaugh, VowofSilence, CureAll, TrendSetter, Contrarian, ConsolationPrize, RoyalCouncil, DeepDigger, LuckyDuck, ProtectiveBubble, PennyPincher, Bully, PeoplePleaser, Millionaire, KingMidas, StickyFingers, Medusa, CheeseTouch, OneUp, Cheater, Alpha, SlickWilly }
public enum GumballId { Lowballer, Chaos, Poker, Duelist, Telepath, PartnersBest, Alone, PanicButton, HighLow }
public enum SigilId { Coin, Bear, Eagle, Fish, Spider, Snake, Frog, Owl, Duck, Vulture, Chameleon, Peacock }
public enum TarotId { LovePotion, Nightshade, CloverTonic, CrystalElixir, Chameleon, Grindstone, Prospector, DevilishDeal, Gambler, Merchant, Glassblower, Traitor, Astronomer, Beggar, SpringLoaded, TaxCollector }

public static class Tarot
{
    /// <summary>How many cards a Tarot must be aimed at.</summary>
    public static int TargetCount(TarotId tarot) => tarot == TarotId.DevilishDeal ? 3
        : tarot is TarotId.TaxCollector or TarotId.Astronomer or TarotId.Gambler ? 0 : 1;

    /// <summary>The suit a suit-changing Tarot gives its target, or null for other Tarots.</summary>
    public static Suit? SuitOf(TarotId tarot) => tarot switch
    {
        TarotId.LovePotion => Suit.Hearts, TarotId.Nightshade => Suit.Spades,
        TarotId.CloverTonic => Suit.Clubs, TarotId.CrystalElixir => Suit.Diamonds, _ => null
    };

    /// <summary>The enhancement a single-target enhancing Tarot gives its target, or null for other Tarots.</summary>
    public static EnhancementId? EnhancementOf(TarotId tarot) => tarot switch
    {
        TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden,
        TarotId.Merchant => EnhancementId.Emerald, TarotId.Glassblower => EnhancementId.Glass, TarotId.Traitor => EnhancementId.Silver,
        TarotId.Beggar => EnhancementId.Stinky, TarotId.SpringLoaded => EnhancementId.Bouncy, _ => null
    };

    /// <summary>What the target chosen at <paramref name="position"/> becomes, or null when the
    /// result is random or the Tarot does not change that card.</summary>
    public static CardState? Preview(TarotId tarot, CardState card, int position) =>
        SuitOf(tarot) is { } suit ? CampaignCards.ChangeSuit(card, suit)
        : tarot == TarotId.DevilishDeal ? position == 2 ? card with { Enhancement = EnhancementId.Cursed } : null
        : EnhancementOf(tarot) is { } enhancement ? card with { Enhancement = enhancement } : null;
}
public sealed record JokerState(JokerId Id, int Value = 0);
public sealed record CampaignInventory(JokerState[] Jokers, TarotId[] Tarot, GumballId[] Unlocks)
{
    /// <summary>Tarots and Sigils are held together and share these slots.</summary>
    public const int ItemSlots = 4;
    /// <summary>How many bidding options there are to unlock.</summary>
    public static readonly int GumballCount = Enum.GetValues<GumballId>().Length;
    public SigilId[] Sigils { get; init; } = [];
    public int ItemCount => Tarot.Length + Sigils.Length;
    public bool HasItemRoom => ItemCount < ItemSlots;
    public static CampaignInventory Empty => new([], [], []);
    public CampaignInventory Copy() => new(Jokers.ToArray(), Tarot.ToArray(), Unlocks.ToArray()) { Sigils = Sigils.ToArray() };
    public bool Has(JokerId id) => Jokers.Any(j => j.Id == id);
}
