using BidEuchre.App;

internal static class TableInputTests
{
    public static void StaleWorkCannotUnlockNewView()
    {
        var input = new TableInputState();
        var firstView = input.BeginViewChange();
        Check(!input.TryBeginAction(out _), "A loading hand must not accept a card");
        input.CompleteView(firstView);
        Check(input.TryBeginAction(out var firstAction), "Loaded hand should accept a move");
        Check(!input.TryBeginAction(out _), "Double clicks must not submit two moves");
        var nextView = input.BeginViewChange();
        input.CompleteAction(firstAction);
        input.CompleteView(firstView);
        Check(input.Loading && input.Locked, "Old work must not reveal or unlock another hand");
        input.CompleteView(nextView);
        Check(input.TryBeginAction(out var nextAction), "Newly loaded hand should accept its own move");
        input.CompleteAction(firstAction);
        Check(input.Locked, "An old completion must not unlock a new move");
        input.CompleteAction(nextAction);
        Check(!input.Locked, "Current action completion releases input");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
