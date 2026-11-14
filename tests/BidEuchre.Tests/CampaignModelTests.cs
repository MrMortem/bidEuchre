using BidEuchre.Core;

internal static class CampaignModelTests
{
    public static void IdentityAndSlots()
    {
        var deck = CampaignCards.CreateDeck();
        Assert.Equal(24, deck.Select(c => c.Id).Distinct().Count());
        var a = deck[0] with { Enhancement = EnhancementId.Glass, Sigil = SigilId.Fish };
        var changed = CampaignCards.ChangeSuit(a, Suit.Hearts);
        Assert.Equal(EnhancementId.Glass, changed.Enhancement!.Value);
        Assert.Equal(SigilId.Fish, changed.Sigil!.Value);
        Assert.Equal(a.Id, changed.Id);
        var b = deck[1] with { Face = changed.Face };
        Assert.True(b.Id != changed.Id);
        var context = new CampaignContext(CampaignSnapshot.Create(42));
        var snapshot = context.Snapshot();
        snapshot.Wallets[0] = 99;
        Assert.Equal(0, context.Snapshot().Wallets[0]);
    }

    public static void RandomAndCatalogue()
    {
        var random = new CampaignRandom(42);
        for (var i = 0; i < 7; i++) random.NextInt(24);
        var resumed = new CampaignRandom(random.State);
        for (var i = 0; i < 20; i++) Assert.Equal(random.NextInt(17), resumed.NextInt(17));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        foreach (var retired in new[] { "Emerald", "Stinky" })
            Assert.False(Enum.GetNames<EnhancementId>().Contains(retired), $"{retired} must not remain an enhancement.");
        foreach (var retired in new[] { "Merchant", "Beggar" })
            Assert.False(Enum.GetNames<TarotId>().Contains(retired), $"{retired} must not remain a Tarot.");
        foreach (var retired in new[] { "enhancement:emerald", "enhancement:stinky", "tarot:merchant", "tarot:beggar" })
            Assert.False(FeatureCatalog.All.Any(feature => feature.Id == retired), $"{retired} must not remain in the catalogue.");
        Assert.Equal(74, FeatureCatalog.All.Count);
        Assert.Equal(74, FeatureCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.Equal(8, Enum.GetValues<EnhancementId>().Length);
        Assert.Equal(31, Enum.GetValues<JokerId>().Length);
        Assert.Equal(9, Enum.GetValues<GumballId>().Length);
        Assert.Equal(12, Enum.GetValues<SigilId>().Length);
        Assert.Equal(14, Enum.GetValues<TarotId>().Length);
        Assert.True(Enum.GetValues<EnhancementId>().Select(value => (int)value).SequenceEqual(new[] { 0, 1, 2, 3, 4, 6, 8, 9 }), "Active enhancement IDs must keep their protocol values.");
        Assert.True(Enum.GetValues<TarotId>().Select(value => (int)value).SequenceEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 14, 15 }), "Active Tarot IDs must keep their protocol values.");
    }
}
