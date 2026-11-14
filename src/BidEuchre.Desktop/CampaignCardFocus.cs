using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>A separate outline preserves the card's selection and category colours while focused.</summary>
internal static class CampaignCardFocus
{
    public static void Apply(Button button, Control content)
    {
        var outline = new Border
        {
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(6), Child = content
        };
        button.FocusAdorner = null;
        button.Template = new FuncControlTemplate<Button>((_, _) => outline);
        button.GotFocus += (_, _) =>
        {
            outline.BorderBrush = Brushes.White;
            button.BringIntoView();
        };
        button.LostFocus += (_, _) => outline.BorderBrush = Brushes.Transparent;
    }
}
