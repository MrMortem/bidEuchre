namespace BidEuchre.Core;

// SplitMix64; full state is checkpointed. Rejection sampling avoids modulo bias.
public sealed class CampaignRandom(ulong state)
{
    public ulong State { get; private set; } = state;
    private ulong Next()
    {
        var z = State = unchecked(State + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        var bound = (ulong)exclusiveMax;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do value = Next(); while (value < threshold);
        return (int)(value % bound);
    }
    public void Shuffle<T>(IList<T> values)
    {
        for (var i = values.Count - 1; i > 0; i--)
        { var j = NextInt(i + 1); (values[i], values[j]) = (values[j], values[i]); }
    }
}
