using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>Drives a movement from linear time 0 to 1 on the shared animation clock.</summary>
internal static class Tween
{
    public static Task Delay(int milliseconds, CancellationToken token = default) => Run(milliseconds, token, static _ => { });

    public static async Task Run(int milliseconds, CancellationToken token, Action<double> step)
    {
        token.ThrowIfCancellationRequested();
        if (milliseconds <= 0) { step(1); return; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = AnimationClock.Now;
        step(0);
        var subscription = AnimationClock.Shared.Subscribe(now =>
        {
            if (done.Task.IsCompleted || token.IsCancellationRequested) return;
            var progress = Math.Min(1, (now - started) * 1000 / milliseconds);
            try { step(progress); }
            catch (Exception error) { done.TrySetException(error); return; }
            if (progress >= 1) done.TrySetResult();
        });
        using var registration = token.Register(() => done.TrySetCanceled(token));
        // The clock and its subscriptions belong to the UI thread, including cancellation.
        try { await done.Task; }
        finally
        {
            if (Dispatcher.UIThread.CheckAccess()) subscription.Dispose();
            else await Dispatcher.UIThread.InvokeAsync(subscription.Dispose);
        }
    }
}
