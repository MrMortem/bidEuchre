using BidEuchre.App;
using BidEuchre.Core;

internal static class TableMotionTests
{
    public static void EventsOnly()
    {
        var tracker = new TableMotionTracker();
        var game = Frame();
        var initial = tracker.Update(game, "table:0");
        Check(!initial.Deal && initial.AddedPlays.Count == 0 && initial.TurnSeat is null, "Opening a table must not replay past events");
        Check(tracker.Update(game, "table:0").AddedPlays.Count == 0, "Polling must not replay");
        var play = new CardPlay(0, Card.Parse("AH"));
        var played = game with { CurrentSeat = 1, CurrentTrick = new[] { play } };
        var cue = tracker.Update(played, "table:0");
        Check(cue.AddedPlays.SetEquals(new[] { "0:AH" }) && cue.TurnSeat == 1, "Only the new card and turn should animate");
        Check(tracker.Update(played, "table:0").AddedPlays.Count == 0, "Repeated plays must not replay");
        Check(tracker.Update(played, "table:1").AddedPlays.Count == 0, "Viewer switches must not replay the table");
        var finished = played with { CurrentTrick = Array.Empty<CardPlay>(), CompletedTricks = new[] { new CompletedTrick(1, 0, 0, new[] { play }) } };
        cue = tracker.Update(finished, "table:1");
        Check(cue.WinnerSeat == 0 && cue.AddedPlays.Count == 0, "Completing a trick must not reanimate its existing cards");
        Check(tracker.Update(finished, "table:1").WinnerSeat is null, "Winner accents happen once");
        cue = tracker.Update(finished with { HandNumber = 2, CompletedTricks = Array.Empty<CompletedTrick>(), Scores = new[] { 3, 0 } }, "table:1");
        Check(cue.Deal && cue.ScoreTeams.SetEquals(new[] { 0 }), "New hand and score change need their own cues");
    }

    public static void StartAndReconnect()
    {
        var tracker = new TableMotionTracker();
        tracker.Update(null, "table:0");
        Check(tracker.Update(Frame(), "table:0").Deal, "Starting a waiting table should deal");
        Check(!tracker.Update(Frame(), "other:0").Deal, "Opening another table should not deal retroactively");
    }

    public static void HighestBidOnly()
    {
        var tracker = new TableMotionTracker();
        var game = Frame() with { Phase = GamePhase.Bidding };
        tracker.Update(game, "a:0");
        game = game with { HighBid = BidLevel.Three, Bidder = 0 };
        Check(tracker.Update(game, "a:0").HighBidChanged, "First bid should be announced");
        Check(!tracker.Update(game, "a:0").HighBidChanged, "Polling should not repeat bids");
        game = game with { CurrentSeat = 2 };
        Check(!tracker.Update(game, "a:0").HighBidChanged, "A pass must not announce another bid");
        game = game with { HighBid = BidLevel.PartnersBest, Bidder = 2 };
        Check(tracker.Update(game, "a:0").HighBidChanged, "A raise should replace the announcement");
        Check(!tracker.Update(game, "a:1").HighBidChanged, "Viewer switches should not animate past bids");
        game = game with { HighBid = BidLevel.Alone, Bidder = 3, Phase = GamePhase.ChoosingContract };
        Check(tracker.Update(game, "a:1").HighBidChanged, "Auction-ending bids should animate");
        game = game with { HighBid = BidLevel.Six, Phase = GamePhase.Playing };
        Check(!tracker.Update(game, "a:1").HighBidChanged, "Do not replay bids during trick play");
    }

    private static GameView Frame() => new(GamePhase.Playing, 1, 0, 0, new[] { 0, 0 },
        Array.Empty<PlayerView>(), Array.Empty<AuctionAction>(), null, null, null,
        Array.Empty<CardPlay>(), Array.Empty<CompletedTrick>(), new[] { 0, 0 }, null,
        new LegalActionView(false, Array.Empty<BidLevel>(), Array.Empty<ContractMode>(), Array.Empty<Suit>(), Array.Empty<Card>()), Array.Empty<string>());
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
