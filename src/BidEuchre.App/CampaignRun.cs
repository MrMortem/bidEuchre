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
    bool Forfeited)
{
    public int BaseReward { get; init; }
    public int MarginBonus { get; init; }
    public int JokerIncome { get; init; }
}

// One campaign run. Every rule of the loop lives here so the screens only display it.
public sealed record CampaignRun(
    int Lives = CampaignRun.StartingLives,
    int Match = 1,
    int Money = 0,
    int MatchesWon = 0,
    CampaignPhase Phase = CampaignPhase.Intro,
    bool VictoryShown = false)
{
    public CampaignSnapshot Resources { get; init; } = CampaignSnapshot.Create((ulong)Random.Shared.NextInt64());
    public ShopState? Shop { get; init; }
    public long LastAppliedRevision { get; init; }
    public const int StartingLives = 3;
    public const int TargetScore = 24;
    [JsonIgnore] public int Threshold => 24 + 4 * (Ante - 1);
    public const int MatchesPerAnte = 3;
    public const int VictoryMatch = 15;
    private const int InterestStep = 5;
    private const int InterestCap = 5;

    [JsonIgnore] public int Ante => (Match - 1) / MatchesPerAnte + 1;
    [JsonIgnore] public int MatchInAnte => (Match - 1) % MatchesPerAnte + 1;
    [JsonIgnore] public bool IsBoss => MatchInAnte == MatchesPerAnte;
    [JsonIgnore] public bool IsOver => Lives <= 0;
    [JsonIgnore] public int PlayerStartingScore => 0;

    // Placeholder boss rule until bosses get their own rule system.
    [JsonIgnore] public int OpponentStartingScore => 0;

    public MatchOptions NextMatch() => new(Threshold) { NegativeThreshold = -Threshold, HandLimit = 8 };

    public CampaignRun StartMatch() => From(CampaignPhase.Intro) with { Phase = CampaignPhase.InMatch };

    public (CampaignRun Run, MatchOutcome Outcome) ApplyResult(bool won, int playerScore, int opponentScore = 0, int matchEndIncome = 0) =>
        Finish(won, playerScore, opponentScore, matchEndIncome, forfeited: false);

    public (CampaignRun Run, MatchOutcome Outcome) Forfeit() => Finish(false, 0, 0, 0, forfeited: true);

    public CampaignRun AcknowledgeVictory() => From(CampaignPhase.Victory) with { Phase = CampaignPhase.Shop };

    public CampaignRun LeaveShop() => From(CampaignPhase.Shop) with { Phase = CampaignPhase.Intro };

    private (CampaignRun Run, MatchOutcome Outcome) Finish(bool won, int playerScore, int opponentScore, int matchEndIncome, bool forfeited)
    {
        From(CampaignPhase.InMatch);
        var livesLost = won ? 0 : IsBoss ? 2 : 1;
        var baseReward = won ? MatchInAnte switch { 1 => 5, 2 => 6, _ => 8 } : 0;
        var margin = playerScore - opponentScore;
        var marginBonus = won ? margin >= 10 ? 6 : margin >= 5 ? 4 : margin >= 1 ? 2 : 0 : 0;
        var payout = baseReward + marginBonus;
        var interest = Math.Min(InterestCap, (Money + payout + matchEndIncome) / InterestStep);
        var money = Money + payout + matchEndIncome + interest;
        var lives = Lives - livesLost;
        var victory = won && Match == VictoryMatch && !VictoryShown;
        var run = this with
        {
            Lives = lives,
            Money = money,
            Resources = Resources with { Wallets = new[] { money, Resources.Wallets[1], Resources.Wallets[2], Resources.Wallets[3] } },
            MatchesWon = MatchesWon + (won ? 1 : 0),
            // A finished run keeps the match it ended on.
            Match = lives <= 0 ? Match : Match + 1,
            Phase = victory ? CampaignPhase.Victory : CampaignPhase.Shop,
            VictoryShown = VictoryShown || victory
        };
        return (run, new MatchOutcome(won, IsBoss, livesLost, payout, interest, money, forfeited) { BaseReward = baseReward, MarginBonus = marginBonus, JokerIncome = matchEndIncome });
    }

    private CampaignRun From(CampaignPhase expected) => Phase == expected
        ? this
        : throw new InvalidOperationException($"The campaign is in {Phase}, not {expected}.");
}
