using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

// Holding L shows the last completed trick over the table, so a trick collected while the player
// looked away can still be read. Trick piles show the latest trick their seat took on hover.
public sealed partial class MainWindow
{
    public const Key LastTrickKey = Key.L;
    private Canvas? _lastTrickLayer;
    private Control? _lastTrickPeek;
    private bool _lastTrickKeys;

    private void AttachLastTrick(Canvas layer)
    {
        HideLastTrick();
        _lastTrickLayer = layer;
        if (_lastTrickKeys) return;
        _lastTrickKeys = true;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            // Typing an L into a text box is never a peek.
            if (e.Key != LastTrickKey || e.KeyModifiers != KeyModifiers.None || FocusManager?.GetFocusedElement() is TextBox) return;
            if (ShowLastTrick()) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == LastTrickKey) HideLastTrick(); }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => HideLastTrick();
    }

    /// <summary>Shows the last completed trick centred on the table. False when there is none to show.</summary>
    private bool ShowLastTrick()
    {
        if (_lastTrickPeek is not null) return true;
        if (!TableShown || _lastTrickLayer is not { } layer || _actionView is not { } view || view.CompletedTricks.Count == 0) return false;
        var peek = LastTrickView(view, view.CompletedTricks[^1]);
        peek.Name = "LastTrickPeek";
        layer.Children.Add(peek);
        _lastTrickPeek = peek;
        peek.Measure(Size.Infinity);
        if (_tableHost.TranslatePoint(new Point(_tableHost.Bounds.Width / 2, _tableHost.Bounds.Height / 2), layer) is { } center)
        {
            Canvas.SetLeft(peek, center.X - peek.DesiredSize.Width / 2);
            Canvas.SetTop(peek, center.Y - peek.DesiredSize.Height / 2);
        }
        return true;
    }

    private void HideLastTrick()
    {
        if (_lastTrickPeek is null) return;
        _lastTrickLayer?.Children.Remove(_lastTrickPeek);
        _lastTrickPeek = null;
    }

    /// <summary>The four cards of <paramref name="trick"/> with who played each; the winner's is outlined.</summary>
    private static Border LastTrickView(GameView view, CompletedTrick trick, double cardWidth = 62)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var play in trick.Plays)
        {
            var face = CardFace(play.Card);
            face.Margin = default;
            var won = play.Seat == trick.Winner;
            if (won) { face.BorderBrush = Gold; face.BorderThickness = new Thickness(4); }
            row.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Viewbox { Width = cardWidth, Height = cardWidth * 176 / 124, Child = face },
                    new TextBlock
                    {
                        Text = view.Players[play.Seat].Name, FontSize = 10, MaxWidth = cardWidth + 8, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, Foreground = won ? Gold : Ink, FontWeight = won ? FontWeight.Bold : FontWeight.Normal
                    }
                }
            });
        }
        return new Border
        {
            Background = PanelRaised, BorderBrush = Gold, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10), IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"TRICK {trick.Number} · {view.Players[trick.Winner].Name.ToUpperInvariant()} WON",
                        FontFamily = PixelArt.Font, FontSize = 11, Foreground = Gold, HorizontalAlignment = HorizontalAlignment.Center
                    },
                    row
                }
            }
        };
    }
}
