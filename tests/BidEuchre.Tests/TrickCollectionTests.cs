using BidEuchre.App;

internal static class TrickCollectionTests
{
    public static void CollectOnce()
    {
        var state = new TrickCollectionState();
        Check(!state.Observe("table:0", 1, 0, false, true), "No trick to collect initially");
        Check(state.Observe("table:0", 1, 1, true, true), "New completed trick should collect");
        var first = state.Key;
        Check(!state.Observe("table:0", 1, 1, false, true) && !state.Hidden, "Polling must not restart or prematurely clear collection");
        state.Complete(first);
        Check(state.Hidden, "Center should clear after collection");
        Check(!state.Observe("table:0", 1, 1, false, true) && state.Hidden, "Collected trick must stay cleared on redraw");
        Check(state.Observe("table:0", 1, 2, true, true), "Next trick needs its own collection");
        state.Complete(first);
        Check(!state.Hidden, "A stale completion cannot clear a newer trick");
        Check(!state.Observe("table:1", 1, 2, false, true) && state.Hidden, "Switching viewers must not replay old tricks");
        Check(!state.Observe("table:1", 2, 0, false, true) && !state.Hidden, "New hand resets collection");
        Check(!state.Observe("table:1", 2, 1, true, false) && state.Hidden, "Motion off clears immediately");
        Check(!state.Observe("other:0", 3, 6, false, true) && state.Hidden, "Opening a completed hand must not show collected cards");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
