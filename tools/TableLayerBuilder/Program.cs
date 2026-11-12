using System.Text.Json;
using SkiaSharp;

// Put all five layers on one canvas so both clients can apply the same cover transform.
var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/table-source");
var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/pixel");
using var sourceBase = SKBitmap.Decode(Path.Combine(sourceRoot, "table-flames.png"));
var width = sourceBase.Width;
var height = sourceBase.Height;
Directory.CreateDirectory(outputRoot);
using var background = NewCanvas();
using (var canvas = new SKCanvas(background))
{
    canvas.Clear(SKColor.Parse("#0B0720"));
    canvas.DrawBitmap(sourceBase, 0, 0);
}
Save(background, "table-flames.png");
using var preview = NewCanvas();
using var previewCanvas = new SKCanvas(preview);
previewCanvas.DrawBitmap(background, 0, 0);
// Original artwork coordinates, normalized if a future source uses another resolution.
(string Name, SKRect Rect)[] placements =
[
    ("heart", SKRect.Create(59, 38, 157, 155)),
    ("club", SKRect.Create(1439, 29, 190, 177)),
    ("spade", SKRect.Create(50, 698, 173, 182)),
    ("diamond", SKRect.Create(1458, 695, 155, 191))
];
var layers = new List<object>();
foreach (var (name, rect) in placements)
{
    using var source = SKBitmap.Decode(Path.Combine(sourceRoot, $"table-{name}.png"));
    if (source.GetPixel(0, 0).Alpha != 0 || source.GetPixel(source.Width - 1, source.Height - 1).Alpha != 0)
        throw new InvalidOperationException($"{name} source must have a transparent background.");
    var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
    for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
        if (source.GetPixel(x, y).Alpha >= 128)
        {
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
    if (right < left) throw new InvalidOperationException($"{name} source is empty.");
    var crop = new SKRect(left, top, right + 1, bottom + 1);
    var target = new SKRect(rect.Left * width / 1672, rect.Top * height / 941,
        rect.Right * width / 1672, rect.Bottom * height / 941);
    using var layer = NewCanvas();
    using (var canvas = new SKCanvas(layer))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, crop, target);
    }
    var file = $"table-{name}.png";
    Save(layer, file);
    previewCanvas.DrawBitmap(layer, 0, 0);
    layers.Add(new { suit = name, file, x = target.Left, y = target.Top, width = target.Width, height = target.Height });
}
Save(preview, "table-layers-preview.png");
File.WriteAllText(Path.Combine(outputRoot, "table-layers.json"), JsonSerializer.Serialize(new
{
    width, height, background = "table-flames.png", layers
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Created {width}x{height} flame base, four transparent suit layers and composite preview.");

SKBitmap NewCanvas() => new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
void Save(SKBitmap bitmap, string name)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var output = File.Create(Path.Combine(outputRoot, name));
    data.SaveTo(output);
}
