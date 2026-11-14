using System.Text.Json;
using SkiaSharp;

var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/curated-source/2026-11-13");
var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/pixel");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceRoot, "manifest.json")));
var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
using var preview = new SKBitmap(5 * 264 + 16, 4 * 226 + 16);
using var canvas = new SKCanvas(preview);
canvas.Clear(SKColor.Parse("#182133"));
using var typeface = SKTypeface.FromFile(Path.Combine(outputRoot, "Pixel.ttf"));
using var font = new SKFont(typeface, 12);
using var ink = new SKPaint { Color = SKColors.White, IsAntialias = false };
using var face = SKBitmap.Decode(Path.Combine(outputRoot, "cards/9H.png"));
var exported = new List<object>();
for (var index = 0; index < entries.Length; index++)
{
    var entry = entries[index];
    var id = entry.GetProperty("id").GetString()!;
    var sourceName = entry.GetProperty("source").GetString()!;
    var resource = entry.GetProperty("resource").GetString()!;
    using var source = SKBitmap.Decode(Path.Combine(sourceRoot, sourceName));
    var card = id.StartsWith("enhancement:");
    var width = card ? 124 : 128;
    var height = card ? 176 : 128;
    using var sprite = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var target = new SKCanvas(sprite))
    {
        target.Clear(SKColors.Transparent);
        target.DrawBitmap(source, new SKRect(0, 0, width, height));
    }
    var file = resource + ".png";
    using (var image = SKImage.FromBitmap(sprite))
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    using (var stream = File.Create(Path.Combine(outputRoot, file))) data.SaveTo(stream);
    var x = 16 + index % 5 * 264;
    var y = 16 + index / 5 * 226;
    canvas.DrawText(id.ToUpperInvariant(), x, y + 12, font, ink);
    canvas.DrawBitmap(sprite, x, y + 25);
    if (card)
    {
        using var white = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(x + 130, y + 25, 124, 176, white);
        canvas.DrawBitmap(sprite, x + 130, y + 25);
        // A rendering-only white halo preserves dark ink over the user's darker substrates.
        using var halo = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn) };
        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            canvas.DrawBitmap(face, x + 130 + dx, y + 25 + dy, halo);
        canvas.DrawBitmap(face, x + 130, y + 25);
    }
    var transparent = 0;
    for (var sy = 0; sy < height; sy++) for (var sx = 0; sx < width; sx++)
        if (sprite.GetPixel(sx, sy).Alpha == 0) transparent++;
    exported.Add(new { id, resource, file, width, height, source = sourceName, transparentPixels = transparent });
    Console.WriteLine($"{id}: {width}x{height}, {transparent} transparent pixels");
}
using (var image = SKImage.FromBitmap(preview))
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
using (var stream = File.Create(Path.Combine(outputRoot, "curated-art-preview.png"))) data.SaveTo(stream);
File.WriteAllText(Path.Combine(outputRoot, "curated-art.json"), JsonSerializer.Serialize(new { entries = exported }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
