using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Threading;
using System.Diagnostics;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

/// <summary>Short, cancellable effects on wrappers, independent of card hover transforms.</summary>
internal sealed class PixelMotion
{
    public bool Enabled { get; private set; } = true;
    private readonly HashSet<CancellationTokenSource> _active = [];

    public SuitBackdropMotion Backdrop { get; } = new();
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public void SetContract(Contract? contract)
    {
        Backdrop.SetContract(contract, Now);
        foreach (var update in _ambient.Values) update();
    }

    private readonly Dictionary<Control, Action> _ambient = [];
    private readonly DispatcherTimer _ambientTimer;

    public PixelMotion()
    {
        _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _ambientTimer.Tick += (_, _) =>
        {
            foreach (var update in _ambient.Values) update();
        };
    }

    // A dedicated wrapper keeps the fan, deal flight and selection lift independent.
    public Control Float(Control child, int phase, bool background = false, Func<Control, Point>? offset = null)
    {
        var rotate = new RotateTransform();
        var lift = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(rotate);
        transforms.Children.Add(lift);
        var wrapper = new Border
        {
            Child = child, RenderTransform = transforms,
            RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
        };
        var lastTime = Now;
        var phaseAngle = lastTime * 2 * Math.PI / (background ? 8.5 : 5.5) + phase * 1.17;
        void Update()
        {
            var seconds = Now;
            var exposure = background ? Backdrop.Exposure(phase, seconds, Enabled) : 0;
            phaseAngle += Math.Max(0, seconds - lastTime) * 2 * Math.PI /
                (background ? 8.5 - Math.Max(0, exposure) * 3 : 5.5);
            lastTime = seconds;
            var angle = phaseAngle;
            var shift = offset?.Invoke(wrapper) ?? default;
            rotate.Angle = Enabled ? Math.Sin(angle) * (background ? 1.8 : .85) : 0;
            lift.X = shift.X;
            lift.Y = shift.Y + (Enabled ? Math.Cos(angle) * (background ? 4 : 1.8) : 0);
        }
        // Layout can complete after attachment, including while the motion timer
        // is stopped. Static contract positions must also follow window resizing.
        if (background) wrapper.LayoutUpdated += (_, _) => Update();
        Ambient(wrapper, Update);
        return wrapper;
    }

    /// <summary>Runs <paramref name="update"/> every frame while the control is on screen, and once
    /// whenever motion is switched, so it can settle into a still pose.</summary>
    public void Ambient(Control control, Action update)
    {
        control.AttachedToVisualTree += (_, _) =>
        {
            _ambient[control] = update;
            update();
            if (Enabled) _ambientTimer.Start();
        };
        control.DetachedFromVisualTree += (_, _) =>
        {
            _ambient.Remove(control);
            if (_ambient.Count == 0) _ambientTimer.Stop();
        };
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) Cancel();
        foreach (var update in _ambient.Values) update();
        if (enabled && _ambient.Count > 0) _ambientTimer.Start();
        else _ambientTimer.Stop();
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
        OnAttach(wrapper, animation);
        return wrapper;
    }

    /// <summary>Fades a control in while it settles from an offset and tilt. The wrapper keeps this
    /// separate from any hover or float transform on the child.</summary>
    public Control Enter(Control child, int delay, double fromX, double fromY, double fromAngle = 0,
        Easing? easing = null, int duration = 320)
    {
        if (!Enabled) return child;
        var transforms = new TransformGroup();
        transforms.Children.Add(new RotateTransform());
        transforms.Children.Add(new TranslateTransform());
        // Hidden until its turn; the animation's delay would otherwise show it at rest first.
        var wrapper = new Border
        {
            Child = child, Opacity = 0, RenderTransform = transforms,
            RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
        };
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(duration), Delay = TimeSpan.FromMilliseconds(delay),
            Easing = easing ?? new CubicEaseOut(), FillMode = FillMode.Forward,
            Children =
            {
                Frame(0, new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, fromX),
                    new Setter(TranslateTransform.YProperty, fromY), new Setter(RotateTransform.AngleProperty, fromAngle)),
                Frame(1, new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d),
                    new Setter(TranslateTransform.YProperty, 0d), new Setter(RotateTransform.AngleProperty, 0d))
            }
        };
        // Finished, cancelled or never started, the control must end up visible.
        OnAttach(wrapper, animation, () => wrapper.Opacity = 1);
        return wrapper;
    }

    /// <summary>Fades a screen out, then hands over to <paramref name="then"/>.</summary>
    public async void Leave(Control control, Action then)
    {
        if (Enabled && control.IsAttachedToVisualTree())
        {
            control.IsHitTestVisible = false;
            await Run(control, new Animation
            {
                Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseIn(),
                Children = { Frame(0, new Setter(Visual.OpacityProperty, 1d)), Frame(1, new Setter(Visual.OpacityProperty, 0d)) }
            });
            control.Opacity = 0;
        }
        then();
        // The destination may decline to replace the screen; never leave it blank.
        if (control.IsAttachedToVisualTree())
        {
            control.Opacity = 1;
            control.IsHitTestVisible = true;
        }
    }

    public void Nudge(Control control)
    {
        if (!Enabled) return;
        var transform = new TranslateTransform();
        control.RenderTransform = transform;
        OnAttach(control, new Animation
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
        OnAttach(control, new Animation
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

    private void OnAttach(Control control, Animation animation, Action? done = null)
    {
        // A reused score label is already attached; new cards start after attachment.
        if (control.IsAttachedToVisualTree()) _ = Run(control, animation, done);
        else control.AttachedToVisualTree += Start;
        void Start(object? sender, VisualTreeAttachmentEventArgs args)
        {
            control.AttachedToVisualTree -= Start;
            if (Enabled) _ = Run(control, animation, done);
            else done?.Invoke();
        }
    }

    private async Task Run(Control control, Animation animation, Action? done = null)
    {
        using var source = new CancellationTokenSource();
        _active.Add(source);
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => source.Cancel();
        control.DetachedFromVisualTree += Detached;
        try { await animation.RunAsync(control, source.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            control.DetachedFromVisualTree -= Detached;
            _active.Remove(source);
            done?.Invoke();
        }
    }

    private static KeyFrame Frame(double cue, params Setter[] setters)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };
        foreach (var setter in setters) frame.Setters.Add(setter);
        return frame;
    }
}
