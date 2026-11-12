namespace BidEuchre.App;

public sealed record DealPacket(int Seat, int FirstCard, int Count, int DelayMilliseconds);

/// <summary>Two clockwise rounds of three cards, beginning left of the dealer.</summary>
public static class DealSequence
{
    public const int FlightMilliseconds = 340;
    public const int CardStaggerMilliseconds = 45;
    public const int Cards = 24;
    public static IReadOnlyList<DealPacket> Packets(int dealer)
    {
        if (dealer is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(dealer));
        return Enumerable.Range(0, 8).Select(packet => new DealPacket(
            (dealer + packet % 4 + 1) % 4, packet / 4 * 3, 3, 180 + packet * 250)).ToArray();
    }

    /// <summary>When one card of a packet leaves the deck.</summary>
    public static int CardDelay(DealPacket packet, int offset) =>
        packet.DelayMilliseconds + offset * CardStaggerMilliseconds;

    /// <summary>From the first frame until the last card has landed.</summary>
    public static int TotalMilliseconds(IReadOnlyList<DealPacket> packets) =>
        packets.Max(packet => CardDelay(packet, packet.Count - 1)) + FlightMilliseconds;

    /// <summary>How many of the deck's stacked backs remain once <paramref name="dealt"/> cards have left.</summary>
    public static int DeckLayers(int dealt, int layers) =>
        (int)Math.Ceiling(Math.Clamp(Cards - dealt, 0, Cards) * layers / (double)Cards);
}
