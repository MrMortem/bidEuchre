namespace BidEuchre.Core;

// Explicit values preserve saved/protocol identities when a feature is retired.
public enum EnhancementId { Lucky = 0, Golden = 1, Wild = 2, Cursed = 3, Stone = 4, Silver = 6, Glass = 8, Bouncy = 9 }
public enum JokerId { ThreesCompany, HighRoller, LoverBoy, SnakeEyes, TwinFlame, Clubhouse, TheUndertaker, RollerCoaster, DejaVu, LastLaugh, VowofSilence, CureAll, TrendSetter, Contrarian, ConsolationPrize, RoyalCouncil, DeepDigger, LuckyDuck, ProtectiveBubble, PennyPincher, Bully, PeoplePleaser, Millionaire, KingMidas, StickyFingers, Medusa, CheeseTouch, OneUp, Cheater, Alpha, SlickWilly }
public enum GumballId { Lowballer, Chaos, Poker, Duelist, Telepath, PartnersBest, Alone, PanicButton, HighLow }
public enum SigilId { Bear = 1, Eagle = 2, Fish = 3, Spider = 4, Frog = 6, Duck = 8, Vulture = 9, Chameleon = 10 }
public enum TarotId { Chameleon = 4, Grindstone = 5, Prospector = 6, DevilishDeal = 7, Gambler = 8, Traitor = 11, Astronomer = 12, SpringLoaded = 14, TaxCollector = 15 }

public static class Tarot
{
    /// <summary>How many cards a Tarot must be aimed at.</summary>
    public static int TargetCount(TarotId tarot) => tarot == TarotId.DevilishDeal ? 3
        : tarot is TarotId.TaxCollector or TarotId.Astronomer or TarotId.Gambler ? 0 : 1;

    /// <summary>The enhancement a single-target enhancing Tarot gives its target, or null for other Tarots.</summary>
    public static EnhancementId? EnhancementOf(TarotId tarot) => tarot switch
    {
        TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden,
        TarotId.Traitor => EnhancementId.Silver,
        TarotId.SpringLoaded => EnhancementId.Bouncy, _ => null
    };

    /// <summary>What the target chosen at <paramref name="position"/> becomes, or null when the
    /// result is random or the Tarot does not change that card.</summary>
    public static CardState? Preview(TarotId tarot, CardState card, int position) =>
        tarot == TarotId.DevilishDeal ? position == 2 ? card with { Enhancement = EnhancementId.Cursed } : null
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
