using BidEuchre.Core;

namespace BidEuchre.App;

/// <summary>One-shot presentation cues derived exclusively from public table state.</summary>
public sealed record TableMotionCue(bool Deal, HashSet<string> AddedPlays, int? TurnSeat, int? WinnerSeat, HashSet<int> ScoreTeams)
{
    public bool HighBidChanged { get; init; }
    public static TableMotionCue None => new(false, [], null, null, []);
}

public sealed class TableMotionTracker
{
    private string? _context;
    private Frame? _previous;
    private sealed record Frame(int Hand, int? Turn, int Completed, HashSet<string> Plays, int? Winner, int[] Scores, string? Bid);

    public TableMotionCue Update(GameView? game, string context)
    {
        var last = game?.CompletedTricks.LastOrDefault();
        var frame = game is null ? null : new Frame(game.HandNumber, game.CurrentSeat, game.CompletedTricks.Count,
            (game.CurrentTrick.Count > 0 ? game.CurrentTrick : last?.Plays ?? []).Select(play => $"{play.Seat}:{play.Card.Code}").ToHashSet(),
            last?.Winner, game.Scores.ToArray(),
            game.HighBid is { } bid && game.Bidder is { } bidder ? $"{bidder}:{bid}" : null);
        var cue = TableMotionCue.None;
        if (_context == context && frame is not null)
        {
            var old = _previous;
            cue = cue with { Deal = old is null || frame.Hand != old.Hand };
            if (old is not null)
            {
                if (frame.Hand == old.Hand)
                {
                    cue = cue with { HighBidChanged = frame.Bid is not null && frame.Bid != old.Bid
                        && game!.Phase is GamePhase.Bidding or GamePhase.ChoosingContract };
                    cue.AddedPlays.UnionWith(frame.Plays.Except(old.Plays));
                    if (frame.Completed > old.Completed) cue = cue with { WinnerSeat = frame.Winner };
                }
                if (frame.Turn != old.Turn) cue = cue with { TurnSeat = frame.Turn };
                for (var team = 0; team < frame.Scores.Length; team++)
                    if (frame.Scores[team] != old.Scores[team]) cue.ScoreTeams.Add(team);
            }
        }
        _context = context;
        _previous = frame;
        return cue;
    }
}
