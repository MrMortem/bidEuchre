using Avalonia.Controls;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class ScoreJoltTests
{
    private static readonly Card Face = Card.Parse("AH");

    public static void Run()
    {
        var stone = new CampaignCardView(new(0), Face, EnhancementId.Stone, null);
        var fish = new CampaignCardView(new(1), Face, null, SigilId.Fish);
        var plain = new CampaignCardView(new(2), Face, EnhancementId.Stone, null);
        var otherStone = new CampaignCardView(new(3), Face, EnhancementId.Stone, null);
        var players = Enumerable.Range(0, 4).Select(seat => new CampaignPlayerView(seat, 0, [], false)).ToArray();
        CampaignTrickPlay[] trick = [new(0, stone, Suit.Hearts), new(1, fish, Suit.Hearts), new(2, plain, Suit.Hearts), new(3, otherStone, Suit.Hearts)];
        var campaign = new CampaignGameView(0, 0, 0, players, [], trick, [], CampaignInventory.Empty, 0, 1, 20, null);
        var basis = new GameEngine(["A", "B", "C", "D"], 1).CreateView(0) with { Campaign = campaign };
        GameView With(params CampaignEffectEvent[] effects) => basis with
        {
            PublicEvents = ["Effect 200: Stone +1 points for team 1."],
            Campaign = campaign with { ScoringEffects = effects, EffectSequence = effects.LastOrDefault()?.Sequence ?? 0 }
        };
        var old = new CampaignEffectEvent(1, CampaignScoringEffect.Stone, CampaignEffectReward.Points, 1, 0, new(0), 0, 1);
        var fishEffect = new CampaignEffectEvent(2, CampaignScoringEffect.Fish, CampaignEffectReward.Points, 2, 1, new(1), 1, 1);
        var stoneEffect = new CampaignEffectEvent(3, CampaignScoringEffect.Stone, CampaignEffectReward.Points, 1, 0, new(2), 2, 1);
        string[] keys = ["0:id0", "1:id1", "2:id2", "3:id3"];
        var motion = new PixelMotion();
        bool Jolts(ScoreJolts jolts, string key) => jolts.Apply(key, new Border(), motion);

        var jolts = new ScoreJolts();
        // Effects already on the table when it opens are never replayed.
        if (jolts.Observe("s", With(old), new HashSet<string>(), true) != 0 || keys.Any(key => Jolts(jolts, key)))
            throw new Exception("Opening a table must not jolt cards for effects that already happened.");
        var hold = jolts.Observe("s", With(old, fishEffect, stoneEffect), new HashSet<string> { "3:id3" }, true);
        if (hold <= 0) throw new Exception("New scoring effects must hold the trick until their jolts settle.");
        if (!Jolts(jolts, "1:id1") || !Jolts(jolts, "2:id2") || Jolts(jolts, "0:id0") || Jolts(jolts, "3:id3"))
            throw new Exception("Each new effect must jolt the card that caused it, even if a teammate has the same effect.");
        Console.WriteLine("PASS  Scoring effects jolt only the trick cards that triggered them");

        var off = new ScoreJolts();
        off.Observe("s", With(), new HashSet<string>(), false);
        if (off.Observe("s", With(old), new HashSet<string>(), false) != 0 || Jolts(off, "0:id0"))
            throw new Exception("Motion off must not schedule jolts.");
        Console.WriteLine("PASS  Scoring jolts stay still with Motion off");
    }
}
