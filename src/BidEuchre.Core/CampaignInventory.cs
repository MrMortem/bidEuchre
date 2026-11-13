namespace BidEuchre.Core;

public enum EnhancementId { Lucky, Golden, Wild, Cursed, Stone, Emerald, Silver, Stinky, Glass, Bouncy }
public enum JokerId { ThreesCompany, HighRoller, LoverBoy, SnakeEyes, TwinFlame, Clubhouse, TheUndertaker, RollerCoaster, DejaVu, LastLaugh, VowofSilence, CureAll, TrendSetter, Contrarian, ConsolationPrize, RoyalCouncil, DeepDigger, LuckyDuck, ProtectiveBubble, PennyPincher, Bully, PeoplePleaser, Millionaire, KingMidas, StickyFingers, Medusa, CheeseTouch, OneUp, Cheater, Alpha, SlickWilly }
public enum PlanetId { Lowballer, Chaos, Poker, Duelist, Telepath, PartnersBest, Alone, PanicButton }
public enum SigilId { Coin, Bear, Eagle, Fish, Spider, Snake, Frog, Owl, Duck, Vulture, Chameleon, Peacock }
public enum TarotId { LovePotion, Nightshade, CloverTonic, CrystalElixir, Chameleon, Grindstone, Prospector, DevilishDeal, Gambler, Merchant, Glassblower, Traitor, Astronomer, Beggar, SpringLoaded, TaxCollector }

public static class Tarot
{
    /// <summary>How many cards a Tarot must be aimed at.</summary>
    public static int TargetCount(TarotId tarot) => tarot == TarotId.DevilishDeal ? 3
        : tarot is TarotId.TaxCollector or TarotId.Astronomer or TarotId.Gambler ? 0 : 1;
}
public sealed record JokerState(JokerId Id, int Value = 0);
public sealed record CampaignInventory(JokerState[] Jokers, TarotId[] Tarot, PlanetId[] Unlocks)
{
    /// <summary>Tarots and Sigils are held together and share these slots.</summary>
    public const int ItemSlots = 4;
    public SigilId[] Sigils { get; init; } = [];
    public int ItemCount => Tarot.Length + Sigils.Length;
    public bool HasItemRoom => ItemCount < ItemSlots;
    public static CampaignInventory Empty => new([], [], []);
    public CampaignInventory Copy() => new(Jokers.ToArray(), Tarot.ToArray(), Unlocks.ToArray()) { Sigils = Sigils.ToArray() };
    public bool Has(JokerId id) => Jokers.Any(j => j.Id == id);
}
