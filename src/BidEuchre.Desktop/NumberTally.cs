using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>A number that counts to its new value and floats the difference above itself, so a
/// score or balance shows how much it changed rather than only where it ended up.</summary>
internal sealed class NumberTally
{
    public const int CountMilliseconds = 520;
    public const int DeltaMilliseconds = 900;
    private static readonly IBrush Gain = Brush.Parse("#7CF2A0");
    private static readonly IBrush Loss = Brush.Parse("#FF617B");

    private readonly Func<int, string> _format;
    private readonly Func<int, string?> _delta;
    private readonly TextBlock _float;
    private readonly TranslateTransform _rise = new();
    private CancellationTokenSource? _count;
    private CancellationTokenSource? _drift;
    private int? _shown;

    public TextBlock Text { get; }
    /// <summary>The number with room for its floating difference; place this instead of <see cref="Text"/>.</summary>
    public Grid Host { get; }
    /// <summary>The value the tally is showing or counting towards.</summary>
    public int? Value { get; private set; }

    public NumberTally(TextBlock text, Func<int, string>? format = null, Func<int, string?>? delta = null, double deltaSize = 14)
    {
        Text = text;
        _format = format ?? (value => value.ToString());
        _delta = delta ?? (change => change > 0 ? $"+{change}" : $"−{-change}");
        _float = new TextBlock
        {
            FontFamily = PixelArt.Font, FontSize = deltaSize, FontWeight = FontWeight.Bold, Opacity = 0, IsHitTestVisible = false,
            RenderTransform = _rise
        };
        _float.Classes.Add("tally-delta");
        Canvas.SetLeft(_float, 6);
        Canvas.SetTop(_float, -deltaSize * .7);
        // A zero-size anchor at the number's right edge: the difference sits beside the number without taking space.
        var anchor = new Canvas
        {
            Width = 0, Height = 0, ClipToBounds = false, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { _float }
        };
        Host = new Grid { ClipToBounds = false, Children = { text, anchor } };
    }

    /// <summary>Shows <paramref name="value"/> at once, ending any count in progress.</summary>
    public void Set(int value)
    {
        Stop();
        Value = _shown = value;
        Text.Text = _format(value);
    }

    /// <summary>Counts from the number on screen to <paramref name="value"/> when <paramref name="animate"/>;
    /// otherwise behaves like <see cref="Set"/>. Asking for the value already targeted changes nothing.</summary>
    public void Change(int value, bool animate)
    {
        if (Value == value) return;
        var from = _shown ?? Value;
        if (!animate || from is not { } start)
        {
            Set(value);
            return;
        }
        Value = value;
        _count?.Cancel();
        var count = _count = new CancellationTokenSource();
        _ = Count(start, value, count);
        Float(value - start);
    }

    private async Task Count(int from, int to, CancellationTokenSource source)
    {
        try
        {
            await Tween.Run(CountMilliseconds, source.Token, t =>
            {
                var shown = (int)Math.Round(from + (to - from) * MotionCurves.EaseOut(t));
                if (shown == _shown) return;
                _shown = shown;
                Text.Text = _format(shown);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_count, source))
            {
                _count = null;
                _shown = to;
                Text.Text = _format(to);
            }
            source.Dispose();
        }
    }

    private async void Float(int change)
    {
        // A null difference label means the screen already explains the change.
        if (change == 0 || _delta(change) is not { } label) return;
        _drift?.Cancel();
        var source = _drift = new CancellationTokenSource();
        _float.Text = label;
        _float.Foreground = change > 0 ? Gain : Loss;
        try
        {
            await Tween.Run(DeltaMilliseconds, source.Token, t =>
            {
                // Beside the number: gains drift up, losses drift down.
                _rise.Y = (change > 0 ? -10 : 10) * MotionCurves.EaseOut(t);
                _float.Opacity = t < .15 ? t / .15 : 1 - MotionCurves.EaseIn((t - .15) / .85);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_drift, source))
            {
                _drift = null;
                _float.Opacity = 0;
            }
            source.Dispose();
        }
    }

    private void Stop()
    {
        _count?.Cancel();
        _count = null;
        _drift?.Cancel();
        _drift = null;
        _float.Opacity = 0;
    }
}
