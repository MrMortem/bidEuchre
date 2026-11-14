using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

internal static class MainMenu
{
    public static Control Build(Action openCampaign, Action openTable, Action openCollection, Action openEngines, Action openRules,
        Action openSettings, PixelMotion motion)
    {
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16)
        };
        Control Entry(string text, bool primary, Action open, int index) =>
            motion.Enter(MenuParts.Button(text, primary, motion, $"Menu{text}", open), 460 + index * 70, 0, 16);

        var title = MenuParts.Title(motion, "BID EUCHRE", 64);
        title.Margin = new Thickness(0, -34, 0, 0);
        var rule = MenuParts.Rule(380);
        rule.Margin = new Thickness(0, 4, 0, 10);

        content.Children.Add(CardFan(motion));
        content.Children.Add(motion.Enter(title, 200, 0, -26, easing: new BackEaseOut(), duration: 380));
        content.Children.Add(motion.Enter(new StackPanel
        {
            Children =
            {
                rule,
                new TextBlock
                {
                    Text = "FOUR SEATS · TWO TEAMS", Foreground = Green, FontSize = 12, FontFamily = PixelArt.Font,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        }, 380, 0, 0));
        content.Children.Add(new StackPanel
        {
            Spacing = 10, Margin = new Thickness(0, 26, 0, 0),
            Children =
            {
                Entry("Play", true, openCampaign, 0),
                Entry("Table", false, openTable, 1),
                Entry("Collection", false, openCollection, 2),
                Entry("Engines", false, openEngines, 3),
                Entry("Rules", false, openRules, 4),
                Entry("Settings", false, openSettings, 5)
            }
        });
        content.Children.Add(motion.Enter(new TextBlock
        {
            Text = "UP / DOWN TO MOVE · ENTER TO SELECT", Foreground = Muted, FontSize = 10, Opacity = .8,
            FontFamily = PixelArt.Font, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 22, 0, 0)
        }, 900, 0, 0));

        return MenuParts.Scene(motion, content, new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
    }

    // The four jacks, the cards every bid is built around, spread from a single stack.
    private static Control CardFan(PixelMotion motion)
    {
        const double width = 62, height = 88;
        var fan = new Canvas
        {
            Width = 320, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false
        };
        var suits = new[] { Suit.Spades, Suit.Hearts, Suit.Clubs, Suit.Diamonds };
        for (var index = 0; index < suits.Length; index++)
        {
            var spread = index - 1.5;
            var x = spread * 46;
            var angle = spread * 11;
            var card = new Border
            {
                Width = width, Height = height, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Child = PixelArt.CardContent(new Card(suits[index], Rank.Jack)),
                RenderTransform = new RotateTransform(angle),
                RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
            };
            var placed = motion.Float(motion.Enter(card, index * 60, -x, 26, -angle), index);
            Canvas.SetLeft(placed, fan.Width / 2 - width / 2 + x);
            Canvas.SetTop(placed, 4 + spread * spread * 5);
            fan.Children.Add(placed);
        }
        return fan;
    }
}
