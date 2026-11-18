namespace BidEuchre.Core;

public enum FeatureCategory { Enhancement, Joker, Gumball, Sigil, Tarot }
public sealed record FeatureDefinition(string Id, FeatureCategory Category, string Key, string Name, string Description, int Price);
public static class FeatureCatalog
{
    public static IReadOnlyList<FeatureDefinition> All { get; } = Array.AsReadOnly(new FeatureDefinition[]
    {
        new("enhancement:lucky", FeatureCategory.Enhancement, "Lucky", "Lucky", "When played, independently roll 1 in 4 for +4 points and 1 in 3 for $3.", 0),
        new("enhancement:golden", FeatureCategory.Enhancement, "Golden", "Golden", "A won trick containing this card earns $1.", 0),
        new("enhancement:wild", FeatureCategory.Enhancement, "Wild", "Wild", "Counts as every suit.", 0),
        new("enhancement:cursed", FeatureCategory.Enhancement, "Cursed", "Cursed", "A trick containing this card earns -1 point.", 0),
        new("enhancement:stone", FeatureCategory.Enhancement, "Stone", "Stone", "When played, earn +1 point.", 0),
        new("enhancement:silver", FeatureCategory.Enhancement, "Silver", "Silver", "When a trick containing this card is won, the losing team earns $5.", 0),
        new("enhancement:glass", FeatureCategory.Enhancement, "Glass", "Glass", "Double all points earned and lost from card effects in this trick.", 0),
        new("enhancement:bouncy", FeatureCategory.Enhancement, "Bouncy", "Bouncy", "When played, replay the on-play effects of cards played before it in the trick.", 0),
        new("joker:threes-company", FeatureCategory.Joker, "ThreesCompany", "Three's Company", "Successful team bid of exactly 3: +2 points.", 4),
        new("joker:high-roller", FeatureCategory.Joker, "HighRoller", "High Roller", "Successful team bid of 5 or more: +3 points.", 4),
        new("joker:lover-boy", FeatureCategory.Joker, "LoverBoy", "Lover Boy", "When you call Hearts as trump, each Heart in a trick your team wins adds +1 point to that trick.", 4),
        new("joker:snake-eyes", FeatureCategory.Joker, "SnakeEyes", "Snake Eyes", "If your team wins a trick containing 2 or more Aces, +4 points.", 4),
        new("joker:twin-flame", FeatureCategory.Joker, "TwinFlame", "Twin Flame", "Team wins a trick in which human and partner played different colors: +1 point.", 4),
        new("joker:clubhouse", FeatureCategory.Joker, "Clubhouse", "Clubhouse", "Tricks your team wins that contain at least one Club are worth +1 point.", 4),
        new("joker:the-undertaker", FeatureCategory.Joker, "TheUndertaker", "The Undertaker", "+1 point per 9 in tricks the team wins.", 4),
        new("joker:roller-coaster", FeatureCategory.Joker, "RollerCoaster", "Roller Coaster", "+0 points each round when High/Low is trump. +1 when your team wins a High/Low trick. −1 when your team loses one.", 4),
        new("joker:deja-vu", FeatureCategory.Joker, "DejaVu", "Déjà Vu", "Starts at +0; increases by 1 for consecutive hands calling the same trump; resets when the team fails a bid; awards its value each hand.", 4),
        new("joker:last-laugh", FeatureCategory.Joker, "LastLaugh", "Last Laugh", "Double all points and effects from the final trick of each hand.", 4),
        new("joker:vowof-silence", FeatureCategory.Joker, "VowofSilence", "Vow of Silence", "Human cannot bid; double points earned from tricks the human wins.", 4),
        new("joker:cure-all", FeatureCategory.Joker, "CureAll", "Cure-All", "Cursed cards in tricks that your team wins give +2 points instead of −1.", 4),
        new("joker:trend-setter", FeatureCategory.Joker, "TrendSetter", "Trend Setter", "Human wins a trick containing four cards of the same suit: +2 points.", 4),
        new("joker:contrarian", FeatureCategory.Joker, "Contrarian", "Contrarian", "Human wins using a suit no other player played: double the trick's points.", 4),
        new("joker:consolation-prize", FeatureCategory.Joker, "ConsolationPrize", "Consolation Prize", "When the human's team did not call trump, +1 per trick the human wins.", 4),
        new("joker:royal-council", FeatureCategory.Joker, "RoyalCouncil", "Royal Council", "Tricks your team wins that contain an Ace, King, Queen, and Jack are worth ×2 points.", 4),
        new("joker:deep-digger", FeatureCategory.Joker, "DeepDigger", "Deep Digger", "Effects on Spades in tricks the human wins trigger twice.", 4),
        new("joker:lucky-duck", FeatureCategory.Joker, "LuckyDuck", "Lucky Duck", "Lucky cards trigger twice.", 4),
        new("joker:protective-bubble", FeatureCategory.Joker, "ProtectiveBubble", "Protective Bubble", "Remove the team's failed-contract point penalty; destroy this Joker after a failed bid.", 4),
        new("joker:penny-pincher", FeatureCategory.Joker, "PennyPincher", "Penny Pincher", "At match end, earn $1 per trick the team won beyond its bid.", 4),
        new("joker:bully", FeatureCategory.Joker, "Bully", "Bully", "At match end, earn $3 for winning by at least 6 points.", 4),
        new("joker:people-pleaser", FeatureCategory.Joker, "PeoplePleaser", "People Pleaser", "When the human wins a trick, earn $1 per face card remaining in hand.", 4),
        new("joker:millionaire", FeatureCategory.Joker, "Millionaire", "Millionaire", "Diamonds the human plays also count as Golden.", 4),
        new("joker:king-midas", FeatureCategory.Joker, "KingMidas", "King Midas", "Kings the human plays have a 1 in 3 chance to permanently become Golden.", 4),
        new("joker:sticky-fingers", FeatureCategory.Joker, "StickyFingers", "Sticky Fingers", "After the human's first trick win each hand, the leftmost card left in their hand copies the winning card for the rest of the match. Drag cards to choose which one.", 4),
        new("joker:medusa", FeatureCategory.Joker, "Medusa", "Medusa", "The last card played in each match permanently becomes Stone.", 4),
        new("joker:cheese-touch", FeatureCategory.Joker, "CheeseTouch", "Cheese Touch", "The first card played in each match permanently becomes Cursed.", 4),
        new("joker:one-up", FeatureCategory.Joker, "OneUp", "One Up", "At each hand's start, choose a hand card to swap with a random partner card.", 4),
        new("joker:cheater", FeatureCategory.Joker, "Cheater", "Cheater", "At each hand's start, choose a player and reveal one random card from their hand to the human.", 4),
        new("joker:alpha", FeatureCategory.Joker, "Alpha", "Alpha", "Human can outbid the partner by matching any eligible unlocked bid, including Safety Net, Partner's Best and Alone.", 4),
        new("joker:slick-willy", FeatureCategory.Joker, "SlickWilly", "Slick Willy", "Human never deals.", 4),
        new("joker:double-down", FeatureCategory.Joker, "DoubleDown", "Double Down", "Retrigger the first card you play each round.", 4),
        new("gumball:lowballer", FeatureCategory.Gumball, "Lowballer", "Safety Net", "Unlock bid 2 for every seat.", 3),
        new("gumball:chaos", FeatureCategory.Gumball, "Chaos", "Chaos", "Minimum bid 4; trump changes randomly each trick; double all points earned/lost that hand.", 3),
        new("gumball:poker", FeatureCategory.Gumball, "Poker", "Poker", "Win the auction with a bid of exactly 4. Reveal all hands, award 4 points to each team tied for the best five-card hand, then redeal.", 3),
        new("gumball:duelist", FeatureCategory.Gumball, "Duelist", "Duelist", "Bid 4–5; play alone against one chosen opponent; failed contract costs 2 extra points. Once per match, consumed when Duelist wins the auction; overcalled bids spend no use.", 3),
        new("gumball:telepath", FeatureCategory.Gumball, "Telepath", "Telepath", "Minimum bid 5; partner reveals one card before human chooses trump.", 3),
        new("gumball:partners-best", FeatureCategory.Gumball, "PartnersBest", "Partner's Best", "Existing card exchange and solo play; sweep six tricks for +12, otherwise -12.", 3),
        new("gumball:alone", FeatureCategory.Gumball, "Alone", "Alone", "Existing solo play; sweep six tricks for +24, otherwise -24.", 3),
        new("gumball:high-low", FeatureCategory.Gumball, "HighLow", "High/Low", "Unlock High or Low for every seat when winning a bid of 4 or more; special contracts keep their mode restrictions.", 3),
        new("sigil:bear", FeatureCategory.Sigil, "Bear", "Bear", "Count as one printed rank higher; Ace wraps to 9.", 3),
        new("sigil:eagle", FeatureCategory.Sigil, "Eagle", "Eagle", "Return to the player's hand if played in a losing trick.", 3),
        new("sigil:fish", FeatureCategory.Sigil, "Fish", "Fish", "When played, earn +2 points.", 3),
        new("sigil:spider", FeatureCategory.Sigil, "Spider", "Spider", "When played, Curse another random card already in the trick.", 3),
        new("sigil:frog", FeatureCategory.Sigil, "Frog", "Frog", "When played, retrigger this card's effects.", 3),
        new("sigil:duck", FeatureCategory.Sigil, "Duck", "Duck", "When played, 1 in 3 chance to earn +4 points.", 3),
        new("sigil:vulture", FeatureCategory.Sigil, "Vulture", "Vulture", "The trick taker creates a Tarot card.", 3),
        new("sigil:chameleon", FeatureCategory.Sigil, "Chameleon", "Chameleon", "When led, other players must avoid following its suit if possible.", 3),
        new("tarot:chameleon", FeatureCategory.Tarot, "Chameleon", "Festival", "Make one card Wild.", 3),
        new("tarot:grindstone", FeatureCategory.Tarot, "Grindstone", "Stone mason", "Make one card Stone.", 3),
        new("tarot:prospector", FeatureCategory.Tarot, "Prospector", "Prospector", "Make one card Golden.", 3),
        new("tarot:devilish-deal", FeatureCategory.Tarot, "DevilishDeal", "Devilish Deal", "Random enhancement on each of two selected cards; a third selected card becomes Cursed.", 3),
        new("tarot:gambler", FeatureCategory.Tarot, "Gambler", "Gambler", "Choose two cards to become Lucky.", 3),
        new("tarot:traitor", FeatureCategory.Tarot, "Traitor", "Traitor", "Make one card Silver.", 3),
        new("tarot:astronomer", FeatureCategory.Tarot, "Astronomer", "Astronomer", "Immediately grant a random locked Gumball bid; after all eight unlocks, grant a duplicate. Each extra copy earns +1 point on your successful matching bid, after hand multipliers.", 3),
        new("tarot:spring-loaded", FeatureCategory.Tarot, "SpringLoaded", "Ectoplasm", "Make one card Bouncy.", 3),
        new("tarot:tax-collector", FeatureCategory.Tarot, "TaxCollector", "Taxes", "Double money, capped at $20 gained.", 3),
    });
    public static FeatureDefinition Get(FeatureCategory category, string key) => All.Single(f => f.Category == category && f.Key == key);

    /// <summary>The title printed on a Sigil's collector card; the Sigil's name and effect are unchanged.</summary>
    public static string CollectorTitle(SigilId sigil) => sigil switch
    {
        SigilId.Bear => "Strength", SigilId.Fish => "Abundance", SigilId.Frog => "Echo", SigilId.Duck => "Lucky",
        SigilId.Chameleon => "Deception", SigilId.Eagle => "Resilient", SigilId.Spider => "Affliction", SigilId.Vulture => "Resourceful",
        _ => throw new ArgumentOutOfRangeException(nameof(sigil))
    };
}
