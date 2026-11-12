using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

/// <summary>The look and behaviour shared by the menu, campaign and settings screens.</summary>
internal static class MenuParts
{
    private const string ContentClass = "menu-content";
    private const string ButtonClass = "menu-button";
    private static readonly Color GoldColor = Color.Parse("#FFDB62");
    private static readonly Color GlintColor = Color.Parse("#FFFBEA");
    private static readonly IBrush TitleShadow = Brush.Parse("#5B2A86");
    private static readonly IBrush PrimaryHover = Brush.Parse("#86D0FF");

    /// <summary>Places <paramref name="content"/> over the table art. Up and Down move between its
    /// menu buttons, the first of which starts focused. <paramref name="host"/> is the control that
    /// holds the content when it is not placed directly, such as a scroll viewer.</summary>
    public static Control Scene(PixelMotion motion, Control content, Control? host = null)
    {
        content.Classes.Add(ContentClass);
        content.KeyDown += (_, args) =>
        {
            var step = args.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
            if (step == 0) return;
            var buttons = Buttons(content);
            if (buttons.Count == 0) return;
            var current = buttons.FindIndex(button => button.IsFocused);
            // Another focused input, such as a drop-down, keeps its own arrow keys.
            if (current < 0 && TopLevel.GetTopLevel(content)?.FocusManager?.GetFocusedElement() is ComboBox) return;
            buttons[(Math.Max(current, 0) + step + buttons.Count) % buttons.Count].Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
        content.AttachedToVisualTree += (_, _) => FocusFirst(content);
        return new Grid
        {
            ClipToBounds = true,
            Children = { PixelArt.TableScene(motion), Vignette(), host ?? content }
        };
    }

    /// <summary>Focuses the first menu button once layout has caught up with a new set of buttons.</summary>
    public static void FocusFirst(Control content) =>
        Dispatcher.UIThread.Post(() => Buttons(content).FirstOrDefault()?.Focus());

    private static List<Button> Buttons(Control content) => content.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains(ButtonClass) && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
        .ToList();

    // Dark where the screen's content sits, clearing toward the edges so the flames and suits stay visible.
    private static Border Vignette() => new()
    {
        IsHitTestVisible = false,
        Background = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.Parse("#EB0B0720"), 0),
                new GradientStop(Color.Parse("#D00B0720"), .45),
                new GradientStop(Color.Parse("#590B0720"), 1)
            }
        }
    };

    /// <summary>A gold heading with a hard pixel shadow. A glint crosses it every few seconds; with
    /// motion off it is plain gold.</summary>
    public static Grid Title(PixelMotion motion, string text, double size)
    {
        var stops = new[]
        {
            new GradientStop(GoldColor, 0), new GradientStop(GoldColor, 0), new GradientStop(GlintColor, 0),
            new GradientStop(GoldColor, 0), new GradientStop(GoldColor, 1)
        };
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, .35, RelativeUnit.Relative)
        };
        brush.GradientStops.AddRange(stops);

        var shadow = Math.Round(size / 13);
        TextBlock Layer(IBrush foreground, double offset) => new()
        {
            Text = text, Foreground = foreground, FontSize = size, FontFamily = PixelArt.Font,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(offset, offset, 0, 0)
        };
        var title = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { Layer(TitleShadow, shadow), Layer(brush, 0) }
        };
        motion.Ambient(title, () =>
        {
            var center = motion.Enabled ? PixelMotion.Now % 5 / 1.3 * 1.4 - .2 : -1;
            stops[1].Offset = Math.Clamp(center - .12, 0, 1);
            stops[2].Offset = Math.Clamp(center, 0, 1);
            stops[3].Offset = Math.Clamp(center + .12, 0, 1);
            stops[2].Color = center is > 0 and < 1 ? GlintColor : GoldColor;
        });
        return title;
    }

    public static Border Rule(double width) => new()
    {
        Height = 3, Width = width, Background = Gold, HorizontalAlignment = HorizontalAlignment.Center
    };

    /// <summary>A menu button. Focus is its selection, moved by the pointer and the arrow keys alike.
    /// Unless <paramref name="stay"/> is set, pressing it fades the screen out before
    /// <paramref name="click"/> runs.</summary>
    public static Button Button(string text, bool primary, PixelMotion motion, string name, Action click,
        bool stay = false, double width = 300)
    {
        var foreground = primary ? Paper : Ink;
        var marker = new Polygon
        {
            Points = [new Point(0, 0), new Point(9, 7), new Point(0, 14)], Fill = primary ? Paper : Gold,
            Opacity = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }]
        };
        var slide = new TranslateTransform
        {
            Transitions =
            [
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(120),
                    Easing = new CubicEaseOut()
                }
            ]
        };
        var button = new Button
        {
            Name = name, Width = width, Padding = new Thickness(16, 14),
            Background = primary ? Green : MainWindow.Panel, Foreground = foreground,
            BorderBrush = primary ? Green : MainWindow.Line, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(2), FocusAdorner = null, RenderTransform = slide,
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                Children =
                {
                    marker,
                    new TextBlock
                    {
                        Text = text, FontSize = 20, FontFamily = PixelArt.Font,
                        FontWeight = primary ? FontWeight.Bold : FontWeight.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        button.Classes.Add(ButtonClass);
        AutomationProperties.SetName(button, text);
        // The theme's own hover and press colours would otherwise replace the palette.
        foreach (var state in new[] { "PointerOver", "Pressed" })
        {
            button.Resources[$"ButtonBackground{state}"] = primary ? PrimaryHover : PanelRaised;
            button.Resources[$"ButtonForeground{state}"] = foreground;
            button.Resources[$"ButtonBorderBrush{state}"] = Gold;
        }

        button.PointerEntered += (_, _) => button.Focus();
        button.GotFocus += (_, _) =>
        {
            button.BorderBrush = Gold;
            marker.Opacity = 1;
            slide.X = motion.Enabled ? 6 : 0;
        };
        button.LostFocus += (_, _) =>
        {
            button.BorderBrush = primary ? Green : MainWindow.Line;
            marker.Opacity = 0;
            slide.X = 0;
        };
        button.Click += (_, _) =>
        {
            var content = button.GetVisualAncestors().OfType<Control>()
                .FirstOrDefault(ancestor => ancestor.Classes.Contains(ContentClass));
            if (stay || content is null)
            {
                click();
                return;
            }
            // A screen that is already fading out has taken its one press.
            if (!content.IsHitTestVisible) return;
            if (button.Parent is Border wrapper) motion.Pulse(wrapper, .96);
            motion.Leave(content, click);
        };
        return button;
    }
}
