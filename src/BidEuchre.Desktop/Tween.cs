using System.Diagnostics;
using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>Drives one movement from linear time 0 to 1, so a single curve shapes the whole of it.</summary>
internal static class Tween
{
    /// <summary>Calls <paramref name="step"/> every frame, always finishing on exactly 1 unless cancelled.</summary>
    public static Task Run(int milliseconds, CancellationToken token, Action<double> step)
    {
        // Continuations run later, never inside the Cancel() call that stopped the movement.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (token.IsCancellationRequested) { done.SetCanceled(token); return done.Task; }
        var started = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        CancellationTokenRegistration registration = default;
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, Stopwatch.GetElapsedTime(started).TotalMilliseconds / milliseconds);
            step(t);
            if (t < 1) return;
            timer.Stop();
            registration.Dispose();
            done.TrySetResult();
        };
        step(0);
        timer.Start();
        registration = token.Register(() =>
        {
            timer.Stop();
            done.TrySetCanceled(token);
        });
        return done.Task;
    }
}
