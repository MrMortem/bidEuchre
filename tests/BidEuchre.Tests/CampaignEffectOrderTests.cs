using System.Text.Json;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignEffectOrderTests
{
    public static void MixedRetriggersAcrossSave()
    {
        var inventory = new CampaignInventory(
            [new(JokerId.DoubleDown), new(JokerId.LuckyDuck), new(JokerId.DeepDigger)], [], []);
        var resources = CampaignSnapshot.Create(17) with { Inventory = inventory };
        resources.Deck[0] = resources.Deck[0] with { Face = Card.Parse("AS"), Enhancement = EnhancementId.Lucky, Sigil = SigilId.Frog };
        resources.Deck[6] = resources.Deck[6] with { Face = Card.Parse("9S"), Enhancement = EnhancementId.Bouncy, Sigil = SigilId.Spider };
        resources.Deck[12] = resources.Deck[12] with { Face = Card.Parse("TS"), Enhancement = EnhancementId.Stone, Sigil = SigilId.Fish };
        resources.Deck[18] = resources.Deck[18] with { Face = Card.Parse("QS"), Enhancement = EnhancementId.Cursed, Sigil = SigilId.Vulture };
        var run = new CampaignRun(Phase: CampaignPhase.InMatch);
        var game = GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
            GameRules.CreateDeck().Chunk(6).ToArray(), 3, [0, 0], Campaign: resources, Options: run.NextMatch()));
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Three));
        for (var seat = 1; seat < 4; seat++) game.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades));

        CampaignEffectEvent[] expected =
        [
            new(1, CampaignScoringEffect.Lucky, CampaignEffectReward.Points, 4, 0, new(0), 0, 1),
            new(2, CampaignScoringEffect.Lucky, CampaignEffectReward.Money, 3, 0, new(0), 0, 1),
            new(3, CampaignScoringEffect.Lucky, CampaignEffectReward.Money, 3, 0, new(0), 0, 1),
            new(4, CampaignScoringEffect.Lucky, CampaignEffectReward.Points, 4, 0, new(0), 0, 1),
            new(5, CampaignScoringEffect.Stone, CampaignEffectReward.Points, 1, 0, new(12), 2, 1),
            new(6, CampaignScoringEffect.Fish, CampaignEffectReward.Points, 2, 0, new(12), 2, 1),
            new(7, CampaignScoringEffect.Stone, CampaignEffectReward.Points, 1, 0, new(12), 2, 1),
            new(8, CampaignScoringEffect.Fish, CampaignEffectReward.Points, 2, 0, new(12), 2, 1),
            new(9, CampaignScoringEffect.Cursed, CampaignEffectReward.Points, -1, 0, new(0), 0, 1),
            new(10, CampaignScoringEffect.Cursed, CampaignEffectReward.Points, -1, 0, new(0), 0, 1),
            new(11, CampaignScoringEffect.Cursed, CampaignEffectReward.Points, -1, 0, new(18), 3, 1),
            new(12, CampaignScoringEffect.Cursed, CampaignEffectReward.Points, -1, 0, new(18), 3, 1)
        ];
        ulong[] expectedRandom = [0xf1bbcdcbfa53e0b9, 0xcc623af8783354f8, 0xcc623af8783354f8, 0xa708a824f612c937];
        int[] expectedCounts = [4, 4, 6, 12];
        void Play(GameEngine engine, int seat)
        {
            engine.ExecuteCampaignAction(seat, new CampaignCommand.Play(new(seat * 6)));
            var snapshot = engine.ExportCampaignMatchSnapshot();
            Assert.Equal(expectedRandom[seat], snapshot.Resources.RandomState, $"RNG changed after seat {seat} played.");
            Assert.True(expected.Take(expectedCounts[seat]).SequenceEqual(snapshot.ScoringEffects),
                $"Scoring effect order changed after seat {seat} played.");
            if (seat == 1) Assert.Equal(EnhancementId.Cursed, snapshot.Resources.Deck[0].Enhancement!.Value);
        }

        Play(game, 0);
        Play(game, 1);
        var path = Path.Combine(Path.GetTempPath(), $"campaign-effects-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run.WithCheckpoint(new(game.ExportCampaignMatchSnapshot(), null)), path));
            var resumed = GameEngine.RestoreCampaignMatch(CampaignStore.Load(path)!.LiveMatch!);
            for (var seat = 2; seat < 4; seat++) { Play(game, seat); Play(resumed, seat); }
            Assert.True(game.ExportCampaignSnapshot().Inventory.Tarot.SequenceEqual([TarotId.TaxCollector, TarotId.Astronomer]),
                "Deep Digger's Spider draw must precede both Vulture rewards.");
            Assert.Equal(JsonSerializer.Serialize(game.ExportCampaignMatchSnapshot()), JsonSerializer.Serialize(resumed.ExportCampaignMatchSnapshot()));
        }
        finally { File.Delete(path); }
    }
}
