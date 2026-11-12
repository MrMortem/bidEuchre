using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignRunTests
{
    private static CampaignRun AtMatch(int match, int lives = 3, int money = 0) =>
        new(lives, match, money, 0, CampaignPhase.InMatch);

    public static void Structure()
    {
        Assert.Equal(1, new CampaignRun(Match: 1).Ante);
        Assert.Equal(1, new CampaignRun(Match: 3).Ante);
        Assert.Equal(2, new CampaignRun(Match: 4).Ante);
        Assert.Equal(6, new CampaignRun(Match: 16).Ante);
        Assert.Equal(1, new CampaignRun(Match: 4).MatchInAnte);
        Assert.Equal(3, new CampaignRun(Match: 6).MatchInAnte);
        Assert.False(new CampaignRun(Match: 2).IsBoss);
        Assert.True(new CampaignRun(Match: 3).IsBoss);
        Assert.True(new CampaignRun(Match: 15).IsBoss);
        Assert.Equal(new MatchOptions(24) { NegativeThreshold = -24, HandLimit = 8 }, new CampaignRun(Match: 1).NextMatch());
        Assert.Equal(new MatchOptions(24) { NegativeThreshold = -24, HandLimit = 8 }, new CampaignRun(Match: 3).NextMatch());
        Assert.Equal(new MatchOptions(40) { NegativeThreshold = -40, HandLimit = 8 }, new CampaignRun(Match: 13).NextMatch());
        Assert.Equal(new MatchOptions(40) { NegativeThreshold = -40, HandLimit = 8 }, new CampaignRun(Match: 15).NextMatch());
        Assert.Equal(new MatchOptions(44) { NegativeThreshold = -44, HandLimit = 8 }, new CampaignRun(Match: 16).NextMatch());
    }

    public static void Lives()
    {
        var (win, _) = AtMatch(1).ApplyResult(true, 12);
        Assert.Equal(3, win.Lives);
        Assert.Equal(2, win.Match);
        Assert.Equal(1, win.MatchesWon);
        var (loss, lossOutcome) = AtMatch(1).ApplyResult(false, 5);
        Assert.Equal(2, loss.Lives);
        Assert.Equal(1, lossOutcome.LivesLost);
        Assert.Equal(0, loss.MatchesWon);
        var (boss, bossOutcome) = AtMatch(3).ApplyResult(false, 5);
        Assert.Equal(1, boss.Lives);
        Assert.Equal(2, bossOutcome.LivesLost);
        Assert.True(bossOutcome.Boss);
        Assert.False(boss.IsOver);
        var (dead, _) = AtMatch(3, lives: 1).ApplyResult(false, 5);
        Assert.Equal(-1, dead.Lives);
        Assert.True(dead.IsOver);
        Assert.Equal(3, dead.Match);
    }

    public static void Money()
    {
        Assert.Equal(13, AtMatch(1).ApplyResult(true, 24, 0).Outcome.Money);
        var (paid, o) = AtMatch(1, money: 12).ApplyResult(true, 24, 20);
        Assert.Equal(7, o.Payout);
        Assert.Equal(3, o.Interest);
        Assert.Equal(22, o.Money);
        Assert.Equal(22, paid.Money);
        var (_, lost) = AtMatch(1, money: 23).ApplyResult(false, 13);
        Assert.Equal(0, lost.Payout);
        Assert.Equal(4, lost.Interest);
        Assert.Equal(27, lost.Money);
        Assert.Equal(0, AtMatch(1, money: 3).ApplyResult(false, -9).Outcome.Payout);
        Assert.Equal(3, AtMatch(1, money: 3).ApplyResult(false, -9).Outcome.Money);
        Assert.Equal(5, AtMatch(1, money: 100).ApplyResult(true, 36).Outcome.Interest);
        var (_, f) = AtMatch(1, money: 10).Forfeit();
        Assert.True(f.Forfeited);
        Assert.False(f.Won);
        Assert.Equal(12, f.Money);
    }

    public static void PhasesAndVictory()
    {
        var run = new CampaignRun().StartMatch();
        Assert.Equal(CampaignPhase.InMatch, run.Phase);
        var (shop, _) = run.ApplyResult(true, 12);
        Assert.Equal(CampaignPhase.Shop, shop.Phase);
        Assert.Equal(CampaignPhase.Intro, shop.LeaveShop().Phase);
        Assert.Throws<InvalidOperationException>(() => shop.ApplyResult(true, 12));
        Assert.Throws<InvalidOperationException>(() => shop.Forfeit());
        Assert.Throws<InvalidOperationException>(() => shop.StartMatch());
        Assert.Throws<InvalidOperationException>(() => shop.AcknowledgeVictory());
        Assert.Throws<InvalidOperationException>(() => new CampaignRun().LeaveShop());

        var (victory, _) = AtMatch(15).ApplyResult(true, 12);
        Assert.Equal(CampaignPhase.Victory, victory.Phase);
        Assert.True(victory.VictoryShown);
        Assert.Equal(16, victory.Match);
        Assert.Equal(CampaignPhase.Shop, victory.AcknowledgeVictory().Phase);

        var (lostBoss, _) = AtMatch(15).ApplyResult(false, 0);
        Assert.Equal(CampaignPhase.Shop, lostBoss.Phase);
        Assert.False(lostBoss.VictoryShown);

        var later = new CampaignRun(3, 30, 0, 0, CampaignPhase.InMatch, VictoryShown: true);
        Assert.Equal(CampaignPhase.Shop, later.ApplyResult(true, 12).Run.Phase);
    }
}
