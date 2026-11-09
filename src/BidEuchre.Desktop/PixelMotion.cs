using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace BidEuchre.Desktop;

/// <summary>Short, cancellable effects on wrappers, independent of card hover transforms.</summary>
internal sealed class PixelMotion
{
    public bool Enabled { get; private set; } = true;
    private readonly HashSet<CancellationTokenSource> _active = [];

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) Cancel();
    }

    public void Cancel()
    {
        foreach (var source in _active.ToArray()) source.Cancel();
    }

    public Control Arrive(Control child, int seat, int delay = 0)
    {
        if (!Enabled) return child;
        var wrapper = new Border { Child = child };
        var (x, y) = seat switch { 1 => (-75d, 0d), 2 => (0d, -65d), 3 => (75d, 0d), _ => (0d, 65d) };
        var transform = new TranslateTransform();
        wrapper.RenderTransform = transform;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300), Delay = TimeSpan.FromMilliseconds(delay),
            Easing = new CubicEaseOut(),
            Children =
            {
                Frame(0, new Setter(TranslateTransform.XProperty, x), new Setter(TranslateTransform.YProperty, y)),
                Frame(.8, new Setter(TranslateTransform.XProperty, -x * .035), new Setter(TranslateTransform.YProperty, -y * .035)),
                Frame(1, new Setter(TranslateTransform.XProperty, 0d), new Setter(TranslateTransform.YProperty, 0d))
            }
        };
        OnAttach(wrapper, transform, animation);
        return wrapper;
    }

    public void Nudge(Control control)
    {
        if (!Enabled) return;
        var transform = new TranslateTransform();
        control.RenderTransform = transform;
        OnAttach(control, transform, new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut(),
            Children =
            {
                Frame(0, new Setter(TranslateTransform.YProperty, 0d)),
                Frame(.45, new Setter(TranslateTransform.YProperty, -4d)),
                Frame(1, new Setter(TranslateTransform.YProperty, 0d))
            }
        });
    }

    public void Pulse(Control control, double peak = 1.08)
    {
        if (!Enabled) return;
        control.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        var transform = new ScaleTransform(1, 1);
        control.RenderTransform = transform;
        OnAttach(control, transform, new Animation
        {
            Duration = TimeSpan.FromMilliseconds(380), Easing = new CubicEaseOut(),
            Children =
            {
                Frame(0, new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d)),
                Frame(.45, new Setter(ScaleTransform.ScaleXProperty, peak), new Setter(ScaleTransform.ScaleYProperty, peak)),
                Frame(1, new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d))
            }
        });
    }

    private void OnAttach(Control control, Animatable target, Animation animation)
    {
        // A reused score label is already attached; new cards start after attachment.
        if (control.IsAttachedToVisualTree()) _ = Run(control, target, animation);
        else control.AttachedToVisualTree += Start;
        void Start(object? sender, VisualTreeAttachmentEventArgs args)
        {
            control.AttachedToVisualTree -= Start;
            if (Enabled) _ = Run(control, target, animation);
        }
    }

    private async Task Run(Control control, Animatable target, Animation animation)
    {
        using var source = new CancellationTokenSource();
        _active.Add(source);
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => source.Cancel();
        control.DetachedFromVisualTree += Detached;
        try { await animation.RunAsync(target, source.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            control.DetachedFromVisualTree -= Detached;
            _active.Remove(source);
        }
    }

    private static KeyFrame Frame(double cue, params Setter[] setters)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };
        foreach (var setter in setters) frame.Setters.Add(setter);
        return frame;
    }
}
