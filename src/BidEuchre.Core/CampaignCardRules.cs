namespace BidEuchre.Core;

/// <summary>Campaign card values shared by trick resolution and seat-private bot decisions.</summary>
public static class CampaignCardRules
{
    public static Rank EffectiveRank(Rank rank, SigilId? sigil) =>
        sigil != SigilId.Bear ? rank : rank == Rank.Ace ? Rank.Nine : rank + 1;

    /// <summary>A declared Wild suit takes precedence over the left-bower suit rule.</summary>
    public static Suit EffectiveSuit(Card face, EnhancementId? enhancement, SigilId? sigil,
        Contract? contract, Suit? declaredWildSuit = null) =>
        ResolveSuit(face with { Rank = EffectiveRank(face.Rank, sigil) }, enhancement, contract, declaredWildSuit);

    /// <summary>Compares cards within a trick. Omit the led suit to compare possible leads.</summary>
    public static int TrickPower(Card face, EnhancementId? enhancement, SigilId? sigil,
        Contract contract, Suit? ledSuit, Suit? declaredWildSuit = null)
    {
        var effective = face with { Rank = EffectiveRank(face.Rank, sigil) };
        var suit = ResolveSuit(effective, enhancement, contract, declaredWildSuit);
        if (contract.Mode == ContractMode.Trump && suit == contract.Trump)
        {
            if (effective.Rank != Rank.Jack) return 200 + (int)effective.Rank;
            return enhancement == EnhancementId.Wild || face.Suit == contract.Trump ? 400 : 399;
        }

        if (ledSuit is { } lead && suit != lead) return int.MinValue;
        return contract.Mode == ContractMode.Low ? 100 - (int)effective.Rank : 100 + (int)effective.Rank;
    }

    private static Suit ResolveSuit(Card effective, EnhancementId? enhancement, Contract? contract, Suit? declaredWildSuit)
    {
        // Spider can replace Wild after the card has already declared a suit.
        if (enhancement == EnhancementId.Wild && declaredWildSuit is { } declared) return declared;
        return contract is null ? effective.Suit : GameRules.EffectiveSuit(effective, contract);
    }
}
