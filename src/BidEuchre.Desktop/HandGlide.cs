using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>Lets cards that stay in a hand slide to their new places when the hand is redrawn,
/// instead of jumping. Each card gets its own wrapper, so fan, float and hover transforms survive.</summary>
internal sealed class HandGlide(Visual surface)
{
    // Ambient float moves a resting card by a few pixels; only a real change of slot glides.
    private const double Threshold = 6;
    private readonly Dictionary<string, Control> _cards = [];
    private readonly Dictionary<string, Point> _previous = [];
    private CancellationTokenSource? _frame;
    private string? _context;

    /// <summary>Remembers where the cards are now. A different <paramref name="context"/>
    /// (another hand, table or viewer) is a new layout, not a movement.</summary>
    public void BeginFrame(string context)
    {
        _previous.Clear();
        if (context == _context)
            foreach (var (key, card) in _cards)
                if (card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), surface) is { } center &&
                    card.Bounds.Width > 0) _previous[key] = center;
        _context = context;
        _cards.Clear();
        Stop();
    }

    public void Reset() { _context = null; _previous.Clear(); _cards.Clear(); Stop(); }

    private void Stop()
    {
        _frame?.Cancel();
        _frame?.Dispose();
        _frame = null;
    }

    public Control Card(string key, Control card, bool enabled)
    {
        var shift = new TranslateTransform();
        var wrapper = new Border { Child = card, RenderTransform = shift };
        _cards[key] = wrapper;
        if (!enabled || !_previous.TryGetValue(key, out var from)) return wrapper;
        var token = (_frame ??= new CancellationTokenSource()).Token;
        // Positions exist only after layout, which completes before the frame is drawn.
        void Placed(object? sender, EventArgs args)
        {
            if (wrapper.Bounds.Width <= 0 || surface.TranslatePoint(from, wrapper) is not { } start) return;
            wrapper.LayoutUpdated -= Placed;
            if (token.IsCancellationRequested) return;
            var x = start.X - wrapper.Bounds.Width / 2;
            var y = start.Y - wrapper.Bounds.Height / 2;
            if (Math.Abs(x) < Threshold && Math.Abs(y) < Threshold) return;
            _ = Glide(shift, x, y, token);
        }
        wrapper.LayoutUpdated += Placed;
        return wrapper;
    }

    private static async Task Glide(TranslateTransform shift, double x, double y, CancellationToken token)
    {
        try
        {
            await Tween.Run(MotionCurves.FanCloseMilliseconds, token, t =>
            {
                var left = 1 - MotionCurves.EaseOut(t);
                shift.X = x * left;
                shift.Y = y * left;
            });
        }
        catch (OperationCanceledException) { }
        // Stopped early (motion off, resize), the card belongs in its slot.
        finally { shift.X = shift.Y = 0; }
    }
}
