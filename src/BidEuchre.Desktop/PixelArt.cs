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

    private static IBrush? _table;
    public static IBrush Table => _table ??= CreateTable();

    // Each transparent suit shares the base canvas, so all five layers stay aligned
    // through the same cover transform. A different base can be used independently.
    public static IBrush CreateTable(string background = "table-flames", bool includeSuits = true)
    {
        var image = Load(background);
        var bounds = new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new ImageDrawing { ImageSource = image, Rect = bounds });
        if (includeSuits)
            foreach (var suit in new[] { "heart", "club", "spade", "diamond" })
                drawing.Children.Add(new ImageDrawing { ImageSource = Load($"table-{suit}"), Rect = bounds });
        return new DrawingBrush(drawing) { Stretch = Stretch.UniformToFill };
    }
    // A shared logical canvas preserves the cover crop while each suit rocks about
    // its own center. Only the small transparent crop moves; the flames stay still.
    public static Control TableScene(PixelMotion motion)
    {
        var scene = new TableScenePanel { IsHitTestVisible = false, ClipToBounds = true };
        scene.Children.Add(new Image { Source = Load("table-flames"), Stretch = Stretch.Fill });
        for (var index = 0; index < TableScenePanel.Suits.Length; index++)
        {
            var (name, x, y, width, height) = TableScenePanel.Suits[index];
            var crop = new CroppedBitmap(Load($"table-{name}"), new PixelRect(x, y, width, height));
            var suitIndex = index;
            scene.Children.Add(motion.Float(new Image { Source = crop, Stretch = Stretch.Fill }, index,
                background: true, offset: control =>
                {
                    var exposure = motion.Backdrop.Exposure(suitIndex, PixelMotion.Now, motion.Enabled);
                    var bounds = control.Bounds;
                    var right = suitIndex is 1 or 3;
                    var bottom = suitIndex >= 2;
                    var inset = Math.Min(24, Math.Min(scene.Bounds.Width, scene.Bounds.Height) * .04);
                    var activeX = right ? Math.Min(bounds.X - 32, scene.Bounds.Width - bounds.Width - inset) : Math.Max(bounds.X + 32, inset);
                    var activeY = bottom ? Math.Min(bounds.Y - 20, scene.Bounds.Height - bounds.Height - inset) : Math.Max(bounds.Y + 20, inset);
                    var hiddenX = right ? scene.Bounds.Width + bounds.Width + inset : -bounds.Width * 2 - inset;
                    return exposure >= 0
                        ? new Point((activeX - bounds.X) * exposure, (activeY - bounds.Y) * exposure)
                        : new Point((hiddenX - bounds.X) * -exposure, 0);
                }));
        }
        return scene;
    }

    private sealed class TableScenePanel : Panel
    {
        public static readonly (string Name, int X, int Y, int Width, int Height)[] Suits =
        [
            ("heart", 59, 38, 157, 155), ("club", 1439, 29, 190, 177),
            ("spade", 50, 698, 173, 182), ("diamond", 1458, 695, 155, 191)
        ];

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(Size.Infinity);
            // Decorative art must not determine the size of the menu or table.
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var scale = Math.Max(finalSize.Width / 1672, finalSize.Height / 941);
            var left = (finalSize.Width - 1672 * scale) / 2;
            var top = (finalSize.Height - 941 * scale) / 2;
            Children[0].Arrange(new Rect(left, top, 1672 * scale, 941 * scale));
            for (var index = 0; index < Suits.Length; index++)
            {
                var (_, x, y, width, height) = Suits[index];
                Children[index + 1].Arrange(new Rect(left + x * scale, top + y * scale,
                    width * scale, height * scale));
            }
            return finalSize;
        }
    }

    public static Image Back(double width = 42, double height = 62) => Sprite(Load("back"), width, height);

    public static Image CampaignIcon(FeatureDefinition definition, double size = 64)
    {
        var image = Sprite(Load($"campaign/{definition.Id.Replace(':', '/')}"), size, size);
        image.HorizontalAlignment = HorizontalAlignment.Center;
        image.VerticalAlignment = VerticalAlignment.Center;
        image.IsHitTestVisible = false;
        return image;
    }

    public static Border Portrait(int seat) => new()
    {
        Width = 50, Height = 54, HorizontalAlignment = HorizontalAlignment.Center,
        BorderBrush = Brush.Parse("#FFDB62"), BorderThickness = new Thickness(3),
        Child = Sprite(Tile("portraits", seat is 2 or 3 ? 1 : 0, seat is 1 or 3 ? 1 : 0, 2, 2), 44, 48)
    };

    // Face identity and paper are independent layers, including cards used in flights.
    // Pass another resource name here to reskin a card without changing its face.
    public static Control CardContent(Card card, string background = "card-background-white")
    {
        var column = card.Rank switch
        {
            Rank.Nine => 0, Rank.Ten => 1, Rank.Jack => 2,
            Rank.Queen => 3, Rank.King => 4, Rank.Ace => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(card))
        };
        var row = card.Suit switch
        {
            Suit.Clubs => 0, Suit.Diamonds => 1, Suit.Hearts => 2, Suit.Spades => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(card))
        };
        var grid = new Grid();
        foreach (var source in new[] { (IImage)Load(background), Tile("card-faces", column, row, 6, 4) })
        {
            var layer = new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapInterpolationMode(layer, BitmapInterpolationMode.None);
            grid.Children.Add(layer);
        }
        AutomationProperties.SetName(grid, $"{Card.DisplayRank(card.Rank)} of {card.Suit}");
        return grid;
    }
}
