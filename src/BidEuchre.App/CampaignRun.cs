using System.Text.Json.Serialization;
using BidEuchre.Core;

namespace BidEuchre.App;

public enum CampaignPhase
{
    Intro,
    InMatch,
    Shop,
    Victory
}

public sealed record MatchOutcome(
    bool Won,
    bool Boss,
    int LivesLost,
    int Payout,
    int Interest,
    int Money,
    bool Forfeited);

// One campaign run. Every rule of the loop lives here so the screens only display it.
public sealed record CampaignRun(
    int Lives = CampaignRun.StartingLives,
    int Match = 1,
    int Money = 0,
    int MatchesWon = 0,
    CampaignPhase Phase = CampaignPhase.Intro,
    bool VictoryShown = false)
{
    public const int StartingLives = 3;
    public const int TargetScore = 12;
    public const int MatchesPerAnte = 3;
    public const int VictoryMatch = 15;
    private const int InterestStep = 5;
    private const int InterestCap = 5;

    [JsonIgnore] public int Ante => (Match - 1) / MatchesPerAnte + 1;
    [JsonIgnore] public int MatchInAnte => (Match - 1) % MatchesPerAnte + 1;
    [JsonIgnore] public bool IsBoss => MatchInAnte == MatchesPerAnte;
    [JsonIgnore] public bool IsOver => Lives <= 0;
    [JsonIgnore] public int PlayerStartingScore => -(Ante - 1);

    // Placeholder boss rule until bosses get their own rule system.
    [JsonIgnore] public int OpponentStartingScore => IsBoss ? Ante + 1 : 0;

    public MatchOptions NextMatch() => new(TargetScore, PlayerStartingScore, OpponentStartingScore);

    public CampaignRun StartMatch() => From(CampaignPhase.Intro) with { Phase = CampaignPhase.InMatch };

    public (CampaignRun Run, MatchOutcome Outcome) ApplyResult(bool won, int playerScore) =>
        Finish(won, playerScore, forfeited: false);

    public (CampaignRun Run, MatchOutcome Outcome) Forfeit() => Finish(won: false, playerScore: 0, forfeited: true);

    public CampaignRun AcknowledgeVictory() => From(CampaignPhase.Victory) with { Phase = CampaignPhase.Shop };

    public CampaignRun LeaveShop() => From(CampaignPhase.Shop) with { Phase = CampaignPhase.Intro };

    private (CampaignRun Run, MatchOutcome Outcome) Finish(bool won, int playerScore, bool forfeited)
    {
        From(CampaignPhase.InMatch);
        var livesLost = won ? 0 : IsBoss ? 2 : 1;
        var payout = won ? Math.Max(0, playerScore - TargetScore) : 0;
        var interest = Math.Min(InterestCap, (Money + payout) / InterestStep);
        var money = Money + payout + interest;
        var lives = Lives - livesLost;
        var victory = won && Match == VictoryMatch && !VictoryShown;
        var run = this with
        {
            Lives = lives,
            Money = money,
            MatchesWon = MatchesWon + (won ? 1 : 0),
            // A finished run keeps the match it ended on.
            Match = lives <= 0 ? Match : Match + 1,
            Phase = victory ? CampaignPhase.Victory : CampaignPhase.Shop,
            VictoryShown = VictoryShown || victory
        };
        return (run, new MatchOutcome(won, IsBoss, livesLost, payout, interest, money, forfeited));
    }

    private CampaignRun From(CampaignPhase expected) => Phase == expected
        ? this
        : throw new InvalidOperationException($"The campaign is in {Phase}, not {expected}.");
}
