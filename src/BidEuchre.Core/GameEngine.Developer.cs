namespace BidEuchre.Core;

// Developer-console hooks. They bypass the rules on purpose, so only the developer console calls them.
public sealed partial class GameEngine
{
    /// <summary>Replaces the live campaign resources (deck, inventory, wallets) mid-match.</summary>
    public void DeveloperReplaceResources(Func<CampaignSnapshot, CampaignSnapshot> update)
    {
        if (!IsCampaign) throw new GameRuleException("Only campaign tables have campaign resources.");
        var next = update(ExportCampaignSnapshot());
        if (!next.IsValid()) throw new GameRuleException("Those campaign resources are not valid.");
        _resources = next.Copy();
        CommitCampaign();
    }

    /// <summary>Sets both team scores. The match only ends when a hand finishes, unless <see cref="DeveloperEndMatch"/> is used.</summary>
    public void DeveloperSetScores(int team0, int team1)
    {
        _scores[0] = team0; _scores[1] = team1;
        _events.Add($"[dev] Scores set to {team0}–{team1}.");
    }

    /// <summary>Ends the match at once with the given winner, as if the last hand had just been scored.</summary>
    public void DeveloperEndMatch(int winner)
    {
        if (winner is not (0 or 1)) throw new GameRuleException("The winner must be team 0 or 1.");
        if (Phase is GamePhase.NotStarted or GamePhase.GameComplete) throw new GameRuleException("There is no match in progress.");
        if (_scores[winner] <= _scores[1 - winner]) _scores[winner] = _scores[1 - winner] + 1;
        CurrentSeat = null;
        if (IsCampaign) _pendingActor = null;
        GameWinner = winner;
        Phase = GamePhase.GameComplete;
        _events.Add($"[dev] Team {winner + 1} won the game.");
        if (IsCampaign) { CampaignMatchEnd(); CommitCampaign(); }
    }
}
