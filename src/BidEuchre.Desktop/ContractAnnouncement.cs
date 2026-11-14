using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace BidEuchre.Desktop;

/// <summary>A readable contract stamp. Motion changes its entrance, never the reading time.</summary>
internal sealed class ContractAnnouncement(Canvas overlay, Control table)
{
    public async Task ShowAsync(string value, string caller, bool animate, CancellationToken cancellationToken)
    {
        // The table frame must have its final bounds before centering the stamp.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        cancellationToken.ThrowIfCancellationRequested();
        var badge = new Border
        {
            Name = "ContractAnnouncement", IsHitTestVisible = false, Width = 290,
            Padding = new Thickness(16), BorderThickness = new Thickness(3),
            BorderBrush = Brush.Parse("#FFDB62"), Background = Brush.Parse("#123278"),
            Child = new StackPanel
            {
                Spacing = 10, Children =
                {
                    new TextBlock { Text = "CONTRACT CALLED", FontFamily = PixelArt.Font, FontSize = 12,
                        Foreground = Brush.Parse("#FFDB62"), TextAlignment = TextAlignment.Center },
                    new TextBlock { Name = "ContractAnnouncementValue", Text = value, FontFamily = PixelArt.Font, FontSize = 28,
                        Foreground = Brush.Parse("#FFF5DC"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    new TextBlock { Name = "ContractAnnouncementCaller", Text = caller, FontFamily = PixelArt.Font, FontSize = 14,
                        Foreground = Brush.Parse("#FFF5DC"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }
                }
            }
        };
        void Position(object? sender, EventArgs args)
        {
            badge.Width = Math.Min(290, Math.Max(180, overlay.Bounds.Width - 24));
            badge.Measure(new Size(badge.Width, double.PositiveInfinity));
            var center = table.TranslatePoint(new Point(table.Bounds.Width / 2, table.Bounds.Height * .46), overlay);
            if (center is not { } at) return;
            Canvas.SetLeft(badge, Math.Clamp(at.X - badge.Width / 2, 0, Math.Max(0, overlay.Bounds.Width - badge.Width)));
            Canvas.SetTop(badge, Math.Clamp(at.Y - badge.DesiredSize.Height / 2, 0, Math.Max(0, overlay.Bounds.Height - badge.DesiredSize.Height)));
        }
        overlay.Children.Add(badge);
        overlay.LayoutUpdated += Position;
        Position(null, EventArgs.Empty);
        try
        {
            if (animate)
            {
                var entrance = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut(), FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
                    }
                };
                await entrance.RunAsync(badge, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
        }
        finally
        {
            overlay.LayoutUpdated -= Position;
            overlay.Children.Remove(badge);
        }
    }
}
