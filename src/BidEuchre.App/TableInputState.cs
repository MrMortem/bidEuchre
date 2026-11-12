namespace BidEuchre.App;

/// <summary>Prevents a stale hand or asynchronous completion from accepting another move.</summary>
public sealed class TableInputState
{
    public long Revision { get; private set; }
    public bool Loading { get; private set; }
    public bool Pending { get; private set; }
    public bool Locked => Loading || Pending;

    public long BeginViewChange()
    {
        Revision++;
        Loading = true;
        Pending = false;
        return Revision;
    }
    public void CompleteView(long revision) { if (revision == Revision) Loading = false; }
    public bool TryBeginAction(out long revision)
    {
        revision = Revision;
        if (Locked) return false;
        Pending = true;
        return true;
    }
    public void CompleteAction(long revision) { if (revision == Revision) Pending = false; }
}
