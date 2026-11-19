namespace BidEuchre.Core;

// Explicit values preserve saved/protocol identities when a feature is retired.
public enum EnhancementId { Lucky = 0, Golden = 1, Wild = 2, Cursed = 3, Stone = 4, Silver = 6, Glass = 8, Bouncy = 9 }
public enum JokerId { ThreesCompany, HighRoller, LoverBoy, SnakeEyes, TwinFlame, Clubhouse, TheUndertaker, RollerCoaster, DejaVu, LastLaugh, VowofSilence, CureAll, TrendSetter, Contrarian, ConsolationPrize, RoyalCouncil, DeepDigger, LuckyDuck, ProtectiveBubble, PennyPincher, Bully, PeoplePleaser, Millionaire, KingMidas, StickyFingers, Medusa, CheeseTouch, OneUp, Cheater, Alpha, SlickWilly, DoubleDown }
public enum SodaId
{
    OvertimeOrange, LimitlessLime, GalaxAde, CosmicCola, RerollRaspberry, CitrusSale,
    MidnightGrape, TwilightTangerine, PackAPunch, PeachPlus, DoubleBubble, RainbowStripes, SpectralSprite, GhostlyGuava
}

public static class SodaRules
{
    public static SodaId? Prerequisite(SodaId id) => id switch
    {
        SodaId.LimitlessLime => SodaId.OvertimeOrange,
        SodaId.CosmicCola => SodaId.GalaxAde,
        SodaId.CitrusSale => SodaId.RerollRaspberry,
        SodaId.TwilightTangerine => SodaId.MidnightGrape,
        SodaId.PeachPlus => SodaId.PackAPunch,
        SodaId.RainbowStripes => SodaId.DoubleBubble,
        SodaId.GhostlyGuava => SodaId.SpectralSprite,
        _ => null
    };
}
public enum GumballId { Lowballer = 0, Chaos = 1, Poker = 2, Duelist = 3, Telepath = 4, PartnersBest = 5, Alone = 6, HighLow = 8, OverAchiever = 9, RollerCoaster = 10 }
public enum SigilId { Bear = 1, Eagle = 2, Fish = 3, Spider = 4, Frog = 6, Duck = 8, Vulture = 9, Chameleon = 10 }
public enum TarotId { Chameleon = 4, Grindstone = 5, Prospector = 6, DevilishDeal = 7, Gambler = 8, Traitor = 11, Astronomer = 12, SpringLoaded = 14, TaxCollector = 15 }

public static class Tarot
{
    /// <summary>How many cards a Tarot must be aimed at.</summary>
    public static int TargetCount(TarotId tarot) => tarot switch
    {
        TarotId.DevilishDeal => 2,
        TarotId.Gambler or TarotId.TaxCollector or TarotId.Astronomer => 0,
        _ => 1
    };

    public static int MinimumEligibleCards(TarotId tarot) => tarot switch
    {
        TarotId.DevilishDeal => 3,
        TarotId.Gambler => 1,
        _ => TargetCount(tarot)
    };

    /// <summary>The enhancement a single-target enhancing Tarot gives its target, or null for other Tarots.</summary>
    public static EnhancementId? EnhancementOf(TarotId tarot) => tarot switch
    {
        TarotId.Chameleon => EnhancementId.Wild, TarotId.Grindstone => EnhancementId.Stone, TarotId.Prospector => EnhancementId.Golden,
        TarotId.Traitor => EnhancementId.Silver,
        TarotId.SpringLoaded => EnhancementId.Bouncy, _ => null
    };

    /// <summary>What a chosen target becomes when the result is known in advance.</summary>
    public static CardState? Preview(TarotId tarot, CardState card) =>
        EnhancementOf(tarot) is { } enhancement ? card with { Enhancement = enhancement } : null;

    public static void ApplyEnhancements(TarotId tarot, IReadOnlyList<CardId> targets, IReadOnlyList<CardId> eligible,
        CampaignRandom random, Action<CardId, EnhancementId> enhance)
    {
        if (eligible.Count < MinimumEligibleCards(tarot))
            throw new GameRuleException("There aren't enough eligible cards to use this Tarot.");

        // Draw by physical ID so rearranging the displayed cards cannot alter a seeded outcome.
        if (tarot == TarotId.Gambler)
        {
            var pool = eligible.OrderBy(id => id.Value).ToArray();
            enhance(pool[random.NextInt(pool.Length)], EnhancementId.Lucky);
        }
        else if (tarot == TarotId.DevilishDeal)
        {
            var pool = eligible.Except(targets).OrderBy(id => id.Value).ToArray();
            var enhancements = Enum.GetValues<EnhancementId>();
            foreach (var id in targets) enhance(id, enhancements[random.NextInt(enhancements.Length)]);
            enhance(pool[random.NextInt(pool.Length)], EnhancementId.Cursed);
        }
        else enhance(targets[0], EnhancementOf(tarot) ?? throw new GameRuleException("Unknown Tarot."));
    }
}
public sealed record JokerState(JokerId Id, int Value = 0)
{
    public int DisplayValue() => Id == JokerId.DejaVu ? 0 : Value;
}
public sealed record CampaignInventory(JokerState[] Jokers, TarotId[] Tarot, GumballId[] Unlocks)
{
    /// <summary>Tarots and Sigils are held together and share these slots.</summary>
    public const int ItemSlots = 4;
    public const int MaxItemCapacity = ItemSlots + 1;
    /// <summary>How many bidding options there are to unlock.</summary>
    public static readonly int GumballCount = Enum.GetValues<GumballId>().Length;
    public SodaId[] Sodas { get; init; } = [];
    public bool Has(SodaId id) => Sodas.Contains(id);
    public int JokerCapacity => Has(SodaId.CosmicCola) ? 6 : 5;
    public int ItemCapacity => ItemSlots + (Has(SodaId.GalaxAde) ? 1 : 0);
    public SigilId[] Sigils { get; init; } = [];
    /// <summary>Acquisitions beyond the first, separate from the unique bidding unlocks.</summary>
    public Dictionary<GumballId, int> GumballExtraCopies { get; init; } = [];
    public long GumballCopies(GumballId id) => (Unlocks.Contains(id) ? 1L : 0L) + GumballExtraCopies.GetValueOrDefault(id);
    public int ItemCount => Tarot.Length + Sigils.Length;
    public bool HasItemRoom => ItemCount < ItemCapacity;
    public static CampaignInventory Empty => new([], [], []);
    public CampaignInventory Copy() => new(Jokers.ToArray(), Tarot.ToArray(), Unlocks.ToArray()) { Sodas = Sodas.ToArray(), Sigils = Sigils.ToArray(), GumballExtraCopies = new(GumballExtraCopies) };
    public bool Has(JokerId id) => Jokers.Any(j => j.Id == id);
}
