using Avalonia;
using Avalonia.Automation;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;

namespace BidEuchre.Desktop;

internal static class BoosterOpening
{
    private const double Height = 340, PackY = 12, PackWidth = 192, PackHeight = 288, PortraitSize = 104;

    public static void EnableHoverLift(Button button, PixelMotion motion)
    {
        const double lift = 12;
        var translate = new TranslateTransform();
        button.Template = new FuncControlTemplate<Button>((_, _) =>
        {
            var face = new Border
            {
                Background = button.Background, BorderBrush = button.BorderBrush, BorderThickness = button.BorderThickness,
                CornerRadius = button.CornerRadius, Padding = button.Padding, Margin = new Thickness(0, lift, 0, 0),
                RenderTransform = translate, Child = new ContentPresenter { Content = button.Content }
            };
            var idleBorder = face.BorderBrush;
            void Highlight() => face.BorderBrush = button.IsPointerOver || button.IsFocused ? Brushes.White : idleBorder;
            button.PointerEntered += (_, _) => { translate.Y = -lift; Highlight(); };
            button.PointerExited += (_, _) => { translate.Y = 0; Highlight(); };
            button.GotFocus += (_, _) => Highlight();
            button.LostFocus += (_, _) => Highlight();
            if (motion.Lively)
                translate.Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(110), Easing = new CubicEaseOut() }];
            // The transparent hit area stays still as the visible face rises inside it.
            return new Grid { Background = Brushes.Transparent, Children = { face } };
        });
    }

    public static void ShowEffectOnHover(Control hitArea, Button card, TextBlock caption, string description)
    {
        AutomationProperties.SetHelpText(card, description);
        void Show() => caption.Text = description;
        void Clear()
        {
            if (caption.Text == description && !hitArea.IsPointerOver && !card.IsFocused) caption.Text = "";
        }
        hitArea.PointerEntered += (_, _) => Show();
        hitArea.PointerExited += (_, _) => Clear();
        card.GotFocus += (_, _) => Show();
        card.LostFocus += (_, _) => Clear();
    }

    private static Control Fit(Canvas stage) =>
        new Viewbox { ClipToBounds = false, MaxWidth = stage.Width, MaxHeight = Height, VerticalAlignment = VerticalAlignment.Center, Child = stage };

    public static Control Sealed(string name, string wrapperArt, string title, int count, PixelMotion motion, Action tear)
    {
        var width = 16 + 126 * count;
        var stage = new Canvas { Width = width, Height = Height };
        var button = MenuParts.Button("Click to open", false, motion, "Tear" + name, tear, true, 270);
        button.Padding = new Thickness(0);
        button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.RenderTransform = null;
        button.Content = new StackPanel
        {
            Spacing = 9, Children =
            {
                motion.Float(PixelArt.Machine(wrapperArt, PackWidth, PackHeight), count),
                new TextBlock { Text = "Click to open", FontFamily = PixelArt.Font, FontSize = 18, Foreground = MainWindow.Gold, TextAlignment = TextAlignment.Center }
            }
        };
        AutomationProperties.SetName(button, $"Open {title}");
        Canvas.SetLeft(button, (width - button.Width) / 2);
        Canvas.SetTop(button, PackY);
        stage.Children.Add(button);
        return Fit(stage);
    }

    // Measure the final controls behind the animation so every portrait can land on its own card.
    public static Control Reveal(string name, string wrapperArt, Image[] targets, Control choices, PixelMotion motion, Action ready)
    {
        var width = 16 + 126 * targets.Length;
        var packX = (width - PackWidth) / 2;
        var startX = (width - PortraitSize) / 2;
        var stage = new Canvas { Name = name + "Opening", Width = width, Height = Height, ClipToBounds = false };
        Image Wrapper(string suffix, string clip)
        {
            var piece = PixelArt.Machine(wrapperArt, PackWidth, PackHeight);
            piece.Name = name + suffix;
            piece.Clip = Geometry.Parse(clip);
            Canvas.SetLeft(piece, packX);
            Canvas.SetTop(piece, PackY);
            return piece;
        }

        const string seam = "0,56 24,50 46,58 68,48 92,56 116,46 142,54 166,44 192,50";
        var body = Wrapper("WrapperBody", $"M {seam} L 192,288 0,288 Z");
        var top = Wrapper("WrapperTop", $"M {seam} L 192,0 0,0 Z");
        var rotation = new RotateTransform();
        top.RenderTransform = rotation;
        var portraits = targets.Select(target => new Image
        {
            Name = target.Name!.Replace("Target", "Reveal"), Width = PortraitSize, Height = PortraitSize,
            Source = target.Source, Stretch = Stretch.Uniform, Opacity = 0,
            RenderTransform = new ScaleTransform(), RenderTransformOrigin = RelativePoint.Center
        }).ToArray();
        foreach (var portrait in portraits) stage.Children.Add(portrait);
        stage.Children.Add(body);
        stage.Children.Add(top);
        choices.Opacity = 0;
        choices.IsEnabled = false;
        foreach (var target in targets) target.Opacity = 0;
        var overlay = Fit(stage);
        overlay.IsHitTestVisible = false;
        var host = new Grid { Children = { choices, overlay } };
        IDisposable? frames = null;
        stage.AttachedToVisualTree += (_, _) =>
        {
            var started = AnimationClock.Now;
            frames = AnimationClock.Shared.Subscribe(now =>
            {
                var elapsed = (now - started) * 1000;
                var windup = Math.Clamp(elapsed / 360, 0, 1);
                var shake = 10 * Math.Sin(8 * Math.PI * windup) * Math.Sin(Math.PI * windup);
                var ms = Math.Max(0, elapsed - 360);
                Canvas.SetLeft(body, packX + shake);
                var tear = Math.Clamp(ms / 360, 0, 1);
                Canvas.SetTop(body, PackY + 76 * tear);
                Canvas.SetLeft(top, packX + shake + 156 * tear);
                Canvas.SetTop(top, PackY - 100 * tear);
                rotation.Angle = 26 * tear;
                top.Opacity = 1 - tear;
                var land = Math.Clamp((ms - 570 - 200 * targets.Length) / 440, 0, 1);
                land = land * land * (3 - 2 * land);
                var blend = Math.Clamp((land - .45) / .55, 0, 1);
                var portraitBlend = Math.Clamp((land - .84) / .16, 0, 1);
                body.Opacity = 1 - land;
                choices.Opacity = blend;
                for (var i = 0; i < portraits.Length; i++)
                {
                    var pop = Math.Clamp((ms - 240 - 200 * i) / 420, 0, 1);
                    var ease = 1 - Math.Pow(1 - pop, 3);
                    var portrait = portraits[i];
                    var x = startX + (20 + i * 126 - startX) * ease;
                    var y = PackY + 55 - 68 * ease - 18 * Math.Sin(Math.PI * pop);
                    var target = targets[i];
                    if (land > 0 && target.TranslatePoint(default, stage) is { } tl
                        && target.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), stage) is { } br)
                    {
                        x += ((tl.X + br.X - PortraitSize) / 2 - x) * land;
                        y += ((tl.Y + br.Y - PortraitSize) / 2 - y) * land;
                        var size = target.Source!.Size;
                        var ratio = Math.Min((br.X - tl.X) / size.Width, (br.Y - tl.Y) / size.Height)
                            / Math.Min(PortraitSize / size.Width, PortraitSize / size.Height);
                        var scale = (ScaleTransform)portrait.RenderTransform!;
                        scale.ScaleX = scale.ScaleY = 1 + (ratio - 1) * land;
                    }
                    target.Opacity = portraitBlend;
                    portrait.Opacity = Math.Min(1, pop * 6) * (1 - portraitBlend);
                    Canvas.SetLeft(portrait, x);
                    Canvas.SetTop(portrait, y);
                }
                if (!motion.Lively || land >= 1)
                {
                    frames?.Dispose();
                    foreach (var target in targets) target.Opacity = 1;
                    choices.Opacity = 1;
                    choices.IsEnabled = true;
                    host.Children.Remove(overlay);
                    ready();
                }
            });
        };
        stage.DetachedFromVisualTree += (_, _) => frames?.Dispose();
        return host;
    }
}
