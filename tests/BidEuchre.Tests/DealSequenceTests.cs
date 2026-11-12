using BidEuchre.App;
internal static class DealSequenceTests
{
    public static void ThreeAtATime()
    {
        for (var dealer = 0; dealer < 4; dealer++)
        {
            var packets = DealSequence.Packets(dealer);
            if (packets.Count != 8) throw new Exception("Deal needs two rounds of four packets");
            for (var i = 0; i < packets.Count; i++)
            {
                var p = packets[i];
                if (p.Seat != (dealer + i % 4 + 1) % 4 || p.FirstCard != i / 4 * 3 || p.Count != 3)
                    throw new Exception("Each player gets three cards, clockwise from dealer's left, twice");
                if (i > 0 && p.DelayMilliseconds <= packets[i-1].DelayMilliseconds)
                    throw new Exception("Packets must be sent in sequence");
            }
            if (packets.GroupBy(p => p.Seat).Any(g => g.Sum(p => p.Count) != 6))
                throw new Exception("Every hand must receive six cards");
        }
    }
}
