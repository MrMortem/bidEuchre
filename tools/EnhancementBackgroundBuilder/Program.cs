using System.Text.Json;
using SkiaSharp;

var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/enhancement-source");
var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/pixel");
string[] names = ["Lucky", "Golden", "Wild", "Cursed", "Stone", "Emerald", "Silver", "Stinky", "Glass", "Bouncy"];
const int width = 124, height = 176, tileWidth = 412, tileHeight = 224;
using var preview = new SKBitmap(tileWidth * 5 + 16, tileHeight * 2 + 16);
using var canvas = new SKCanvas(preview);
canvas.Clear(SKColor.Parse("#172032"));
using var typeface = SKTypeface.FromFile(Path.Combine(outputRoot, "Pixel.ttf"));
using var font = new SKFont(typeface, 13);
using var labelInk = new SKPaint { Color = SKColor.Parse("#FFF5DC"), IsAntialias = false };
using var redFace = SKBitmap.Decode(Path.Combine(outputRoot, "cards/9H.png"));
using var blackFace = SKBitmap.Decode(Path.Combine(outputRoot, "cards/KS.png"));
var entries = new List<object>();
for (var i = 0; i < names.Length; i++)
{
    var name = names[i];
    var slug = name.ToLowerInvariant();
    var sourcePath = Path.Combine(sourceRoot, slug + ".png");
    using var source = SKBitmap.Decode(sourcePath) ?? throw new InvalidOperationException($"Missing {name} source.");
    using var sprite = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var target = new SKCanvas(sprite))
    {
        target.Clear(SKColors.White);
        target.DrawBitmap(source, new SKRect(0, 0, width, height));
    }
    // Verify the actual exported layer, not just the source image metadata.
    double light = 0;
    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
    {
        var pixel = sprite.GetPixel(x, y);
        if (pixel.Alpha != 255) throw new InvalidOperationException($"{name} substrate must be opaque.");
        if (x >= 25 && x < 99 && y >= 36 && y < 140)
            light += .2126 * pixel.Red + .7152 * pixel.Green + .0722 * pixel.Blue;
    }
    var centerBrightness = light / (74 * 104);
    if (centerBrightness < 185) throw new InvalidOperationException($"{name} center is too dark for card ink.");
    var file = $"card-background-{slug}.png";
    Save(sprite, Path.Combine(outputRoot, file));
    entries.Add(new { id = "enhancement:" + slug, key = name, file, resource = "card-background-" + slug,
        width, height, centerBrightness = Math.Round(centerBrightness, 1) });
    var px = 16 + i % 5 * tileWidth;
    var py = 16 + i / 5 * tileHeight;
    canvas.DrawText(name.ToUpperInvariant(), px, py + 14, font, labelInk);
    for (var sample = 0; sample < 3; sample++)
    {
        var x = px + sample * 132;
        canvas.DrawBitmap(sprite, x, py + 26);
        if (sample == 1) canvas.DrawBitmap(redFace, x, py + 26);
        if (sample == 2) canvas.DrawBitmap(blackFace, x, py + 26);
    }
}
Save(preview, Path.Combine(outputRoot, "enhancement-backgrounds-preview.png"));
File.WriteAllText(Path.Combine(outputRoot, "enhancement-backgrounds.json"), JsonSerializer.Serialize(new
{
    kind = "card-face-background", width, height, defaultBackground = "card-background-white.png",
    entries
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Exported 10 opaque 124x176 face backgrounds; verified alpha and light centers. Preview includes each substrate, red pips, and black court faces.");

static void Save(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);
}
