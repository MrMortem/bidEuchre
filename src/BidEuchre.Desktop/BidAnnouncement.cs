using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>A replaceable, non-interactive bid stamp that flies from the board into the top strip.</summary>
internal sealed class BidAnnouncement(Canvas overlay, Control table, ContentControl dock)
{
    private CancellationTokenSource? _source;
    private Control? _badge;
    private string? _key;
    private long _generation;

    public void Update(string value, string player, bool animate)
    {
        var key = $"{value}\n{player}";
        if (_key == key) return;
        Cancel();
        _key = key;
        dock.Content = Badge(value, player, compact: true);
        dock.IsVisible = true;
        if (!animate) return;
        var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _generation && dock.IsVisible) _ = Fly(value, player);
        }, DispatcherPriority.Loaded);
    }

    public void Cancel()
    {
        _generation++;
        var source = _source;
        _source = null;
        source?.Cancel();
        if (_badge is not null) overlay.Children.Remove(_badge);
        _badge = null;
        dock.Opacity = 1;
    }

    public void Clear()
    {
        Cancel();
        _key = null;
        dock.Content = null;
        dock.IsVisible = false;
    }

    private async Task Fly(string value, string player)
    {
        var center = table.TranslatePoint(new Point(table.Bounds.Width / 2, table.Bounds.Height * .48), overlay);
        var destination = dock.TranslatePoint(new Point(dock.Bounds.Width / 2, dock.Bounds.Height / 2), overlay);
        if (center is null || destination is null || overlay.Bounds.Width <= 0) return;
        var badge = Badge(value, player, compact: false);
        badge.Width = 240;
        badge.Measure(new Size(240, double.PositiveInfinity));
        var height = badge.DesiredSize.Height;
        // The stamp fades up over the bid buttons, holds, then shrinks into its slot in the top
        // strip, arriving at the docked badge's own size so the hand-over does not jump.
        var shift = new TranslateTransform();
        var size = new ScaleTransform(.9, .9);
        badge.RenderTransform = size;
        badge.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        var flyer = new Border { Child = badge, RenderTransform = shift, Opacity = 0, IsHitTestVisible = false };
        Canvas.SetLeft(flyer, center.Value.X - 120);
        Canvas.SetTop(flyer, center.Value.Y - height / 2);
        var docked = dock.TranslatePoint(new Point(dock.Bounds.Width, 0), overlay) is { } right
            ? Math.Abs(right.X - (destination.Value.X - dock.Bounds.Width / 2)) : dock.Bounds.Width;
        var shrink = docked > 0 ? Math.Clamp(docked / 240, .2, 1) : .5;
        var x = destination.Value.X - center.Value.X;
        var y = destination.Value.Y - center.Value.Y;
        var duration = TimeSpan.FromMilliseconds(950);
        var travel = new Animation
        {
            Duration = duration, Easing = new CubicEaseInOut(), FillMode = FillMode.Forward,
            Children =
            {
                Move(0, 0, 0, 0), Move(.16, 0, 0, 1), Move(.5, 0, 0, 1), Move(1, x, y, 1)
            }
        };
        var resize = new Animation
        {
            Duration = duration, Easing = new CubicEaseInOut(), FillMode = FillMode.Forward,
            Children = { Size(0, .9), Size(.16, 1), Size(.5, 1), Size(1, shrink) }
        };
        using var source = new CancellationTokenSource();
        _source = source;
        _badge = flyer;
        dock.Opacity = 0;
        overlay.Children.Add(flyer);
        try { await Task.WhenAll(travel.RunAsync(flyer, source.Token), resize.RunAsync(badge, source.Token)); }
        catch (OperationCanceledException) { }
        finally
        {
            overlay.Children.Remove(flyer);
            if (ReferenceEquals(_source, source))
            {
                _source = null;
                _badge = null;
                dock.Opacity = 1;
            }
        }
    }

    private static KeyFrame Move(double cue, double x, double y, double opacity) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(TranslateTransform.XProperty, x), new Setter(TranslateTransform.YProperty, y),
            new Setter(Visual.OpacityProperty, opacity)
        }
    };

    private static KeyFrame Size(double cue, double scale) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) }
    };

    private static Border Badge(string value, string player, bool compact) => new()
    {
        IsHitTestVisible = false, Padding = new Thickness(compact ? 8 : 14),
        BorderThickness = new Thickness(3), BorderBrush = Brush.Parse("#FFDB62"),
        Background = Brush.Parse("#123278"),
        Child = new StackPanel
        {
            Spacing = compact ? 3 : 6,
            Children =
            {
                new TextBlock { Text = "HIGHEST BID", FontFamily = PixelArt.Font, FontSize = 10,
                    Foreground = Brush.Parse("#FFDB62"), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = value, FontFamily = PixelArt.Font,
                    FontSize = value.Length > 4 ? (compact ? 15 : 22) : (compact ? 24 : 36),
                    Foreground = Brush.Parse("#FFF5DC"), TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = player, FontFamily = PixelArt.Font, FontSize = 10,
                    Foreground = Brush.Parse("#FFF5DC"), TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        }
    };
}
