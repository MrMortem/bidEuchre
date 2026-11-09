using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>A replaceable, non-interactive bid stamp that flies into the side rail.</summary>
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
        Canvas.SetLeft(badge, center.Value.X - 120);
        Canvas.SetTop(badge, center.Value.Y - height / 2);
        var shift = new TranslateTransform();
        badge.RenderTransform = shift;
        var dockVisible = destination.Value.Y - dock.Bounds.Height / 2 >= 0 &&
            destination.Value.Y + dock.Bounds.Height / 2 <= overlay.Bounds.Height;
        var x = (dockVisible ? destination.Value.X : -136) - center.Value.X;
        var y = dockVisible ? destination.Value.Y - center.Value.Y : 0;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(800), Easing = new CubicEaseInOut(), FillMode = FillMode.Forward,
            Children =
            {
                Frame(0, 0, 0), Frame(.5, 0, 0), Frame(1, x, y)
            }
        };
        using var source = new CancellationTokenSource();
        _source = source;
        _badge = badge;
        dock.Opacity = 0;
        overlay.Children.Add(badge);
        try { await animation.RunAsync(shift, source.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            overlay.Children.Remove(badge);
            if (ReferenceEquals(_source, source))
            {
                _source = null;
                _badge = null;
                dock.Opacity = 1;
            }
        }
    }

    private static KeyFrame Frame(double cue, double x, double y) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.XProperty, x), new Setter(TranslateTransform.YProperty, y) }
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
