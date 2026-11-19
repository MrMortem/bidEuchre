using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

/// <summary>A passive, window-bounded explanation anchored to a campaign card.</summary>
internal sealed class CampaignCardExplanation : Border
{
    private const double Gap = 6, PanelWidth = 320;
    private static readonly ConditionalWeakTable<Control, CampaignCardExplanation> KeyboardPanels = new();
    private readonly Control _anchor;
    private OverlayLayer? _layer;

    private CampaignCardExplanation(Control anchor, string title, string description)
    {
        _anchor = anchor;
        Name = "CampaignCardExplanation";
        IsHitTestVisible = false;
        Padding = new Thickness(12, 9);
        Background = MainWindow.PanelRaised;
        BorderBrush = MainWindow.Line;
        BorderThickness = new Thickness(2);
        CornerRadius = new CornerRadius(6);
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, Foreground = MainWindow.Gold, FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = description, Foreground = MainWindow.Ink, FontSize = 16, TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    internal static T Attach<T>(T card, FeatureDefinition definition, string? description = null) where T : Control
        => Attach(card, definition.Name, description ?? definition.Description);

    internal static T Attach<T>(T card, string title, string description, Control? focusTarget = null) where T : Control
    {
        focusTarget ??= card;
        focusTarget.Focusable = true;
        AutomationProperties.SetHelpText(focusTarget, description);
        CampaignCardExplanation? panel = null;
        CampaignCardExplanation GetPanel() => panel ??= new(card, title, description);
        void Hide() => panel?.Hide();
        void Dismiss()
        {
            KeyboardPanels.Remove(focusTarget);
            Hide();
        }

        card.PointerEntered += (_, _) => GetPanel().Show();
        card.PointerExited += (_, _) =>
        {
            if (KeyboardPanels.TryGetValue(focusTarget, out _)) return;
            Hide();
            if (TopLevel.GetTopLevel(card)?.FocusManager?.GetFocusedElement() is Control focused &&
                KeyboardPanels.TryGetValue(focused, out var previous)) previous.Show();
        };
        focusTarget.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod == NavigationMethod.Pointer) return;
            if (focusTarget is not Button) focusTarget.BringIntoView();
            var explanation = KeyboardPanels.GetValue(focusTarget, _ => GetPanel());
            explanation.Show();
        };
        focusTarget.LostFocus += (_, _) =>
        {
            KeyboardPanels.Remove(focusTarget);
            if (!card.IsPointerOver) Hide();
        };
        focusTarget.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || panel?._layer is null) return;
            Dismiss();
            e.Handled = true;
        };
        if (focusTarget is Button button) button.Click += (_, _) => Dismiss();
        card.DetachedFromVisualTree += (_, _) => Dismiss();
        return card;
    }

    private void Show()
    {
        if (_layer is not null || OverlayLayer.GetOverlayLayer(_anchor) is not { } layer) return;
        foreach (var other in layer.Children.OfType<CampaignCardExplanation>().ToArray()) other.Hide();
        _layer = layer;
        layer.Children.Add(this);
        layer.LayoutUpdated += Reposition;
        Reposition(null, EventArgs.Empty);
    }

    private void Hide()
    {
        if (_layer is not { } layer) return;
        _layer = null;
        layer.LayoutUpdated -= Reposition;
        layer.Children.Remove(this);
    }

    private void Reposition(object? sender, EventArgs e)
    {
        if (_layer is not { } layer || layer.Bounds.Width <= Gap * 2 || layer.Bounds.Height <= Gap * 2 ||
            _anchor.TranslatePoint(default, layer) is not { } topLeft ||
            _anchor.TranslatePoint(new Point(_anchor.Bounds.Width, _anchor.Bounds.Height), layer) is not { } bottomRight)
            return;

        Width = Math.Min(PanelWidth, layer.Bounds.Width - Gap * 2);
        MaxHeight = layer.Bounds.Height - Gap * 2;
        Measure(new Size(Width, MaxHeight));
        var height = DesiredSize.Height;
        var x = (topLeft.X + bottomRight.X - Width) / 2;
        var y = bottomRight.Y + Gap;
        if (y + height > layer.Bounds.Height - Gap) y = topLeft.Y - Gap - height;
        Canvas.SetLeft(this, Math.Clamp(x, Gap, layer.Bounds.Width - Width - Gap));
        Canvas.SetTop(this, Math.Clamp(y, Gap, Math.Max(Gap, layer.Bounds.Height - height - Gap)));
    }
}
