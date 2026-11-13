namespace BidEuchre.Core;

public enum EnhancementId { Lucky, Golden, Wild, Cursed, Stone, Emerald, Silver, Stinky, Glass, Bouncy }
public enum JokerId { ThreesCompany, HighRoller, LoverBoy, SnakeEyes, TwinFlame, Clubhouse, TheUndertaker, RollerCoaster, DejaVu, LastLaugh, VowofSilence, CureAll, TrendSetter, Contrarian, ConsolationPrize, RoyalCouncil, DeepDigger, LuckyDuck, ProtectiveBubble, PennyPincher, Bully, PeoplePleaser, Millionaire, KingMidas, StickyFingers, Medusa, CheeseTouch, OneUp, Cheater, Alpha, SlickWilly }
public enum PlanetId { Lowballer, Chaos, Poker, Duelist, Telepath, PartnersBest, Alone, PanicButton }
public enum SigilId { Coin, Bear, Eagle, Fish, Spider, Snake, Frog, Owl, Duck, Vulture, Chameleon, Peacock }
public enum TarotId { LovePotion, Nightshade, CloverTonic, CrystalElixir, Chameleon, Grindstone, Prospector, DevilishDeal, Gambler, Merchant, Glassblower, Traitor, Astronomer, Beggar, SpringLoaded, TaxCollector }

public sealed record JokerState(JokerId Id, int Value = 0);
public sealed record CampaignInventory(JokerState[] Jokers, TarotId[] Tarot, PlanetId[] Unlocks)
{
    public SigilId[] Sigils { get; init; } = [];
    public static CampaignInventory Empty => new([], [], []);
    public CampaignInventory Copy() => new(Jokers.ToArray(), Tarot.ToArray(), Unlocks.ToArray()) { Sigils = Sigils.ToArray() };
    public bool Has(JokerId id) => Jokers.Any(j => j.Id == id);
}
