using BidEuchre.App;

internal static class MotionCurveTests
{
    public static void CurvesStartAndFinishCleanly()
    {
        var curves = new (string Name, Func<double, double> Curve)[]
        {
            ("EaseOut", MotionCurves.EaseOut), ("EaseIn", MotionCurves.EaseIn),
            ("EaseInOut", MotionCurves.EaseInOut), ("Settle", MotionCurves.Settle)
        };
        foreach (var (name, curve) in curves)
        {
            Check(Math.Abs(curve(0)) < 1e-9 && Math.Abs(curve(1) - 1) < 1e-9, $"{name} must run from 0 to 1");
            Check(Math.Abs(curve(-3)) < 1e-9 && Math.Abs(curve(4) - 1) < 1e-9, $"{name} must clamp outside the flight");
        }
        // A stutter is a curve that stalls or reverses before the end of its travel.
        foreach (var (name, curve) in curves.Take(3))
            for (var step = 1; step <= 100; step++)
                Check(curve(step / 100d) > curve((step - 1) / 100d), $"{name} must keep moving the whole way");
        Check(MotionCurves.EaseOut(.5) > .5 && MotionCurves.EaseIn(.5) < .5, "Ease-out leads, ease-in trails");
        Check(Math.Abs(MotionCurves.EaseInOut(.5) - .5) < 1e-9, "Ease-in-out is symmetric");
    }

    public static void SettleOvershootsOnce()
    {
        var peak = 0d;
        var falling = false;
        var previous = 0d;
        for (var step = 1; step <= 200; step++)
        {
            var value = MotionCurves.Settle(step / 200d);
            peak = Math.Max(peak, value);
            if (value < previous) falling = true;
            else Check(!falling, "A settling card passes its place once and comes back, without bouncing");
            previous = value;
        }
        Check(peak > 1.01 && peak < 1.05, "The settle must be visible but small");
    }

    public static void ArcLiftsOnlyInFlight()
    {
        Check(Math.Abs(MotionCurves.Arc(0)) < 1e-9 && Math.Abs(MotionCurves.Arc(1)) < 1e-9, "A card starts and lands flat");
        Check(Math.Abs(MotionCurves.Arc(.5) - 1) < 1e-9, "The arc peaks mid-flight");
        Check(MotionCurves.Arc(-1) == 0 && MotionCurves.Arc(2) == 0, "No lift outside the flight");
    }

    public static void DealCardsLeaveOneAtATime()
    {
        var packets = DealSequence.Packets(0);
        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            for (var offset = 1; offset < packet.Count; offset++)
                Check(DealSequence.CardDelay(packet, offset) - DealSequence.CardDelay(packet, offset - 1) >= 40,
                    "Cards in a packet must be told apart");
            if (i + 1 < packets.Count)
                Check(DealSequence.CardDelay(packet, packet.Count - 1) < packets[i + 1].DelayMilliseconds,
                    "A packet must leave the deck before the next player's begins");
        }
        var last = packets[^1];
        Check(DealSequence.TotalMilliseconds(packets) >=
            DealSequence.CardDelay(last, last.Count - 1) + DealSequence.FlightMilliseconds,
            "The deal must not end before its last card lands");
        Check(DealSequence.DeckLayers(0, 4) == 4 && DealSequence.DeckLayers(24, 4) == 0, "The deck runs from full to empty");
        Check(DealSequence.DeckLayers(23, 4) == 1, "The last card still shows a deck");
        for (var dealt = 1; dealt <= 24; dealt++)
            Check(DealSequence.DeckLayers(dealt, 4) <= DealSequence.DeckLayers(dealt - 1, 4), "The deck only ever thins");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
