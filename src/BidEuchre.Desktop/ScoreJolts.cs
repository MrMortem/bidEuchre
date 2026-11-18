using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

/// <summary>Jolts the trick card behind each campaign effect that scores points or money, one
/// effect after another. Starts are kept by card key so a jolt survives table redraws.</summary>
internal sealed class ScoreJolts
{
    public const int JoltMilliseconds = 360, StaggerMilliseconds = 240;
    private const double Duration = JoltMilliseconds / 1000d, Stagger = StaggerMilliseconds / 1000d;

    private readonly Dictionary<string, List<double>> _starts = [];
    private string? _session;
    private int _seen;
    private double _lastStart;

    private readonly ConditionalWeakTable<Control, object> _registered = new();
    private (long Deal, int Trick)? _trick;

    /// <summary>Queues new effects on their exact source cards after any cards in flight land.</summary>
    public int Observe(string session, GameView view, IReadOnlySet<string> added, bool enabled)
    {
        if (view.Campaign is not { } campaign) return 0;
        var trick = (campaign.DealSerial, view.CompletedTricks.Count + (campaign.Trick.Count > 0 ? 1 : 0));
        if (trick != _trick) { Cancel(); _trick = trick; }
        if (session != _session)
        {
            // Opening a table or resuming a save establishes a watermark.
            _session = session;
            _seen = campaign.EffectSequence;
            Cancel();
            return 0;
        }

        var previous = _seen;
        _seen = campaign.EffectSequence;
        if (!enabled) { Cancel(); return 0; }
        var now = PixelMotion.Now;
        if (_seen <= previous) return Remaining(now);

        var plays = campaign.Trick.Count > 0 ? campaign.Trick : campaign.LastTrick;
        var start = Math.Max(now + (added.Count > 0 ? MotionCurves.FlightMilliseconds / 1000d : 0), _lastStart + Stagger);
        foreach (var effect in campaign.ScoringEffects)
        {
            if (effect.Sequence <= previous || effect.TrickNumber != trick.Item2 || effect.SourceCard is not { } id || effect.SourceSeat is not { } seat ||
                !plays.Any(play => play.Seat == seat && play.Card.Id == id)) continue;
            var key = $"{seat}:id{id.Value}";
            if (!_starts.TryGetValue(key, out var starts)) _starts[key] = starts = [];
            starts.Add(start);
            _lastStart = start;
            start += Stagger;
        }
        return Remaining(now);
    }

    private int Remaining(double now) => _lastStart == 0 ? 0 :
        Math.Max(0, (int)Math.Ceiling((_lastStart + Duration - now) * 1000));

    /// <summary>Gives a freshly built trick card its pending or running jolts. Returns false when it has none.</summary>
    public bool Apply(string key, Control card, PixelMotion motion)
    {
        if (!_starts.TryGetValue(key, out var starts)) return false;
        var now = PixelMotion.Now;
        starts.RemoveAll(start => now - start > Duration);
        if (starts.Count == 0) { _starts.Remove(key); return false; }

        if (_registered.TryGetValue(card, out _)) return true;
        _registered.Add(card, new object());
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var lift = new TranslateTransform();
        card.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        card.RenderTransform = new TransformGroup { Children = { scale, rotate, lift } };
        motion.Ambient(card, () =>
        {
            double pop = 0, tilt = 0;
            if (motion.Enabled && _starts.TryGetValue(key, out var running))
            {
                var seconds = PixelMotion.Now;
                for (var index = 0; index < running.Count; index++)
                {
                    var t = (seconds - running[index]) / Duration;
                    if (t is < 0 or > 1) continue;
                    // A sharp punch outward, then a damped shake that alternates direction per trigger.
                    pop = Math.Max(pop, t < .16 ? MotionCurves.EaseOut(t / .16) : 1 - MotionCurves.EaseOut((t - .16) / .84));
                    tilt += Math.Sin(t * Math.PI * 4) * Math.Pow(1 - t, 2) * (index % 2 == 0 ? 1 : -1);
                }
            }
            // Reduced motion keeps a gentle swell to mark the card, without the shake or hop.
            scale.ScaleX = scale.ScaleY = 1 + (motion.Reduced ? .08 : .2) * pop;
            rotate.Angle = motion.Reduced ? 0 : 8 * tilt;
            lift.Y = motion.Reduced ? 0 : -7 * pop;
        });
        return true;
    }

    public void Cancel() { _starts.Clear(); _lastStart = 0; }
}
