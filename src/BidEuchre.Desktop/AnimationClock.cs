using System.Diagnostics;
using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>One monotonic frame clock for desktop animations. It stops when nothing is subscribed.</summary>
internal sealed class AnimationClock
{
    public static AnimationClock Shared { get; } = new();
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<Subscription> _subscriptions = [];
    private bool _ticking;

    private AnimationClock() => _timer.Tick += (_, _) => Tick();

    public IDisposable Subscribe(Action<double> update)
    {
        var subscription = new Subscription(this, update);
        _subscriptions.Add(subscription);
        if (!_timer.IsEnabled) _timer.Start();
        return subscription;
    }

    private void Tick()
    {
        var now = Now;
        var count = _subscriptions.Count;
        _ticking = true;
        try
        {
            // A callback may add or remove an animation. New subscriptions start on the next tick.
            for (var i = 0; i < count; i++)
                if (_subscriptions[i] is { Active: true } subscription) subscription.Update(now);
        }
        finally
        {
            _ticking = false;
            _subscriptions.RemoveAll(static subscription => !subscription.Active);
            if (_subscriptions.Count == 0) _timer.Stop();
        }
    }

    private sealed class Subscription(AnimationClock clock, Action<double> update) : IDisposable
    {
        public bool Active { get; private set; } = true;
        public void Update(double now) => update(now);

        public void Dispose()
        {
            if (!Active) return;
            Active = false;
            if (clock._ticking) return;
            clock._subscriptions.Remove(this);
            if (clock._subscriptions.Count == 0) clock._timer.Stop();
        }
    }
}
