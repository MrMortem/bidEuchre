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
        Assert.Equal(78, FeatureCatalog.All.Count);
        Assert.Equal(78, FeatureCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.Equal(10, Enum.GetValues<EnhancementId>().Length);
        Assert.Equal(31, Enum.GetValues<JokerId>().Length);
        Assert.Equal(9, Enum.GetValues<GumballId>().Length);
        Assert.Equal(12, Enum.GetValues<SigilId>().Length);
        Assert.Equal(16, Enum.GetValues<TarotId>().Length);
    }
}
