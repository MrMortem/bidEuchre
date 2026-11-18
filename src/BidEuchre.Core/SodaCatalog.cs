namespace BidEuchre.Core;

public sealed record SodaDefinition(string Name, string Description, string Art);

public static class SodaCatalog
{
    public static SodaDefinition Get(SodaId id) => id switch
    {
        SodaId.OvertimeOrange => new("Overtime Orange", "Adds one hand to every match.", "overtime-orange"),
        SodaId.LimitlessLime => new("Limitless Lime", "Adds a second extra hand to every match.", "limitless-lime"),
        SodaId.GalaxAde => new("Galax-Ade", "Adds one shared Ticket and Sigil slot.", "galax-ade"),
        SodaId.CosmicCola => new("Cosmic Cola", "Adds one Joker slot.", "cosmic-cola"),
        SodaId.RerollRaspberry => new("Reroll Raspberry", "The first reroll in each shop is free.", "reroll-raspberry"),
        SodaId.CitrusSale => new("Citrus Sale", "Shop purchases cost $1 less.", "citrus-sale"),
        SodaId.MidnightGrape => new("Midnight Grape", "The Arcade appears in half of shops.", "midnight-grape"),
        SodaId.TwilightTangerine => new("Twilight Tangerine", "The Arcade appears in every shop.", "twilight-tangerine"),
        SodaId.PackAPunch => new("Pack-a-Punch", "Adds a second Joker Booster to each shop.", "pack-a-punch"),
        SodaId.PeachPlus => new("Peach Plus", "Packs offer one more card to choose from.", "peach-plus"),
        SodaId.DoubleBubble => new("Double Bubble", "Your first Gumball purchase each shop grants a second Gumball free.", "double-bubble"),
        SodaId.RainbowStripes => new("Rainbow Stripes", "Buy unlimited Gumballs. Each paid pull raises the price by $1 for this visit.", "rainbow-stripes"),
        SodaId.SpectralSprite => new("Spectral Sprite", "The Fortune Teller can print Spectral cards in place of Tickets.", "spectral-sprite"),
        SodaId.GhostlyGuava => new("Ghostly Guava", "Spectral cards can appear for sale in the shop.", "ghostly-guava"),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
