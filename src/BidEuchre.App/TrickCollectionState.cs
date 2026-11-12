namespace BidEuchre.App;

/// <summary>Remembers a collected public trick across redraws, without replaying history.</summary>
public sealed class TrickCollectionState
{
    private string? _key;
    public bool Hidden { get; private set; }

    public bool Observe(string context, int hand, int trick, bool justCompleted, bool motionEnabled)
    {
        var key = $"{context}/{hand}/{trick}";
        if (_key == key) return false;
        _key = key;
        Hidden = trick > 0 && (!justCompleted || !motionEnabled);
        return trick > 0 && justCompleted && motionEnabled;
    }

    public string? Key => _key;
    public void Complete(string? key)
    {
        if (key == _key) Hidden = true;
    }
}
