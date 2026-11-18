using BidEuchre.Core;

internal static class CampaignCardRulesTests
{
    public static void RankSuitAndPower()
    {
        var hearts = Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Hearts);
        var low = Contract.Create(BidLevel.Four, ContractMode.Low, null);
        int Power(string card, Contract contract, Suit? lead = null,
            EnhancementId? enhancement = null, SigilId? sigil = null, Suit? declaration = null) =>
            CampaignCardRules.TrickPower(Card.Parse(card), enhancement, sigil, contract, lead, declaration);

        Assert.Equal(Rank.Nine, CampaignCardRules.EffectiveRank(Rank.Ace, SigilId.Bear));
        Assert.Equal(Rank.Jack, CampaignCardRules.EffectiveRank(Rank.Ten, SigilId.Bear));
        Assert.Equal(Suit.Hearts, CampaignCardRules.EffectiveSuit(Card.Parse("TD"), null, SigilId.Bear, hearts));
        Assert.Equal(Suit.Diamonds, CampaignCardRules.EffectiveSuit(Card.Parse("JD"), null, SigilId.Bear, hearts));
        Assert.Equal(Suit.Diamonds, CampaignCardRules.EffectiveSuit(Card.Parse("JD"), EnhancementId.Wild, null, hearts, Suit.Diamonds));
        Assert.Equal(Suit.Hearts, CampaignCardRules.EffectiveSuit(Card.Parse("JD"), EnhancementId.Cursed, null, hearts, Suit.Diamonds));
        Assert.True(Power("JH", hearts) > Power("TD", hearts, sigil: SigilId.Bear));
        Assert.True(Power("TD", hearts, sigil: SigilId.Bear) > Power("AH", hearts));
        Assert.Equal(Power("JH", hearts), Power("JS", hearts, enhancement: EnhancementId.Wild, declaration: Suit.Hearts));
        Assert.Equal(int.MinValue, Power("JD", hearts, Suit.Clubs, EnhancementId.Wild, declaration: Suit.Diamonds));
        Assert.True(Power("AS", low, Suit.Spades, sigil: SigilId.Bear) > Power("TS", low, Suit.Spades));
        Assert.Equal(int.MinValue, Power("9H", low, Suit.Spades));
    }
}
