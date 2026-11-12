namespace BidEuchre.App;

/// <summary>The table's shared timings and progress curves. Every curve takes linear time in
/// [0, 1] and is clamped, so one continuous curve drives a whole movement.</summary>
public static class MotionCurves
{
    public const int FlightMilliseconds = 420;
    /// <summary>How long a finished trick stays readable after its last card lands.</summary>
    public const int TrickHoldMilliseconds = 200;
    public const int GatherMilliseconds = 130;
    public const int CollectMilliseconds = 360;
    public const int FanCloseMilliseconds = 180;
    public const int LandingFadeMilliseconds = 90;

    public static double EaseOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    public static double EaseIn(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t;
    }

    public static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < .5 ? 4 * t * t * t : 1 - Math.Pow(2 - 2 * t, 3) / 2;
    }

    /// <summary>Travel that passes its destination by about 3% and comes back once.</summary>
    public static double Settle(double t)
    {
        const double back = .9;
        var u = Math.Clamp(t, 0, 1) - 1;
        return 1 + (back + 1) * u * u * u + back * u * u;
    }

    /// <summary>Lift above the straight path: flat at both ends, 1 at mid-flight.</summary>
    public static double Arc(double t) => t is <= 0 or >= 1 ? 0 : Math.Sin(t * Math.PI);
}
