using BidEuchre.Core;

internal static class CampaignItemPreviewTests
{
    private static GameEngine Fixture(CampaignInventory inventory)
    {
        var deck = GameRules.CreateDeck().ToArray();
        return GameEngine.CreateFixture(new FixtureStart(["You", "W", "P", "E"], Enumerable.Range(0, 4).Select(s => deck.Skip(s * 6).Take(6).ToArray()).ToArray(),
            3, [0, 0], Campaign: CampaignSnapshot.Create(17) with { Inventory = inventory }, Options: new MatchOptions(1000) { HandLimit = 8, NegativeThreshold = -1000 }));
    }
    private static CardState Card(GameEngine game, int id) => game.ExportCampaignSnapshot().Deck.Single(c => c.Id == new CardId(id));

    // Issue #28: the target preview must show exactly what using the Tarot does.
    public static void TarotPreviewMatchesEngine()
    {
        var previewed = 0;
        foreach (var tarot in Enum.GetValues<TarotId>())
        {
            var required = Tarot.TargetCount(tarot);
            if (required == 0) continue;
            var game = Fixture(new([], [tarot], []));
            var before = Enumerable.Range(0, required).Select(i => Card(game, i)).ToArray();
            game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, Enumerable.Range(0, required).Select(i => new CardId(i)).ToArray()));
            for (var position = 0; position < required; position++)
            {
                if (Tarot.Preview(tarot, before[position], position) is not { } expected) continue;
                Assert.Equal(expected, Card(game, position), $"{tarot} preview for target {position + 1}.");
                previewed++;
            }
        }
        var expectedCount = Enum.GetValues<TarotId>().Count(t => Tarot.SuitOf(t) is not null || Tarot.EnhancementOf(t) is not null) + 1;
        Assert.Equal(expectedCount, previewed, "Every suit and enhancement Tarot, and Devilish Deal's Cursed card, has a preview.");
    }

    public static void SigilsAreHandCardTargets()
    {
        foreach (var sigil in Enum.GetValues<SigilId>())
        {
            var game = Fixture(new([], [], []) { Sigils = [sigil] });
            game.ExecuteCampaignAction(0, new CampaignCommand.UseSigil(0, new(3)));
            Assert.Equal(sigil, Card(game, 3).Sigil!.Value);
        }
    }
}
