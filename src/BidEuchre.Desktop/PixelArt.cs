using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

internal static class PixelArt
{
    public static readonly FontFamily Font = new("avares://BidEuchre/Pixel#Silkscreen");
    private static readonly Dictionary<string, Bitmap> Images = new();
    private static readonly Dictionary<string, IImage> Crops = new();

    private static Bitmap Load(string name)
    {
        if (!Images.TryGetValue(name, out var image))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://BidEuchre/Pixel/{name}.png"));
            Images[name] = image = new Bitmap(stream);
        }
        return image;
    }

    private static IImage Tile(string name, int column, int row, int columns, int rows)
    {
        var key = $"{name}:{column}:{row}";
        if (!Crops.TryGetValue(key, out var crop))
        {
            var image = Load(name);
            var width = image.PixelSize.Width / columns;
            var height = image.PixelSize.Height / rows;
            Crops[key] = crop = new CroppedBitmap(image, new PixelRect(column * width, row * height, width, height));
        }
        return crop;
    }

    private static Image Sprite(IImage source, double width, double height)
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return image;
    }

    public static IBrush Table => new ImageBrush(Load("table")) { Stretch = Stretch.UniformToFill };
    public static Image Back(double width = 42, double height = 62) => Sprite(Load("back"), width, height);
    public static Border Portrait(int seat) => new()
    {
        Width = 50, Height = 54, HorizontalAlignment = HorizontalAlignment.Center,
        BorderBrush = Brush.Parse("#FFDB62"), BorderThickness = new Thickness(3),
        Child = Sprite(Tile("portraits", seat is 2 or 3 ? 1 : 0, seat is 1 or 3 ? 1 : 0, 2, 2), 44, 48)
    };

    public static Control CardContent(Card card)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        var ink = Brush.Parse(red ? "#CF1827" : "#111324");
        var suit = card.Suit switch { Suit.Hearts => "♥", Suit.Diamonds => "♦", Suit.Clubs => "♣", _ => "♠" };
        var rank = Card.DisplayRank(card.Rank);
        var grid = new Grid { Width = 50, Height = 76 };
        if (rank is "J" or "Q" or "K")
        {
            var court = Sprite(Tile("courts", rank == "J" ? 0 : rank == "Q" ? 1 : 2, 0, 3, 1), 37, 57);
            court.HorizontalAlignment = HorizontalAlignment.Right;
            court.VerticalAlignment = VerticalAlignment.Bottom;
            grid.Children.Add(court);
        }
        else
        {
            grid.Children.Add(new TextBlock { Text = suit, Foreground = ink, FontSize = 35,
                FontFamily = new FontFamily("Georgia"), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 16, 0, 0) });
        }
        grid.Children.Add(new TextBlock { Text = $"{rank}\n{suit}", Foreground = ink,
            FontFamily = Font, FontSize = 13, LineHeight = 15, Margin = new Thickness(0), FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        AutomationProperties.SetName(grid, $"{rank} of {card.Suit}");
        return grid;
    }
}
