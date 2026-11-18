using System.Security.Cryptography;
using System.Text.Json;
using BidEuchre.Core;
using SkiaSharp;

// Export the existing substrates without repainting their interiors or changing game assets.
internal static class EnhancementAssetSheet
{
    private const int Width = 124, Height = 176, Padding = 16, Columns = 4;
    private const int CellWidth = Width + Padding * 2, CellHeight = Height + Padding * 2;

    public static void Build(string[] args)
    {
        var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/curated-source/2026-11-16-enhancement-sheet-inputs");
        var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/sheets/enhancements");
        var manifestPath = Path.Combine(sourceRoot, "curated-art.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var sources = manifest.RootElement.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("category").GetString() == "enhancement")
            .ToDictionary(entry => entry.GetProperty("runtimeId").GetString()!);
        var catalogue = FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Enhancement).ToArray();
        if (!catalogue.Select(feature => feature.Id).ToHashSet().SetEquals(sources.Keys))
            throw new InvalidDataException("Enhancement sources must exactly match the active catalogue.");

        Directory.CreateDirectory(Path.Combine(outputRoot, "sprites"));
        var rows = (catalogue.Length + Columns - 1) / Columns;
        using var atlas = Bitmap(Columns * CellWidth, rows * CellHeight);
        var atlasPixels = new SKColor[atlas.Width * atlas.Height];
        const int previewCellWidth = 288, previewCellHeight = 408, previewTop = 68;
        using var preview = Bitmap(Columns * previewCellWidth, rows * previewCellHeight + previewTop);
        using var canvas = new SKCanvas(preview);
        canvas.Clear(SKColor.Parse("#151C2A"));
        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var title = new SKFont(typeface, 25);
        using var label = new SKFont(typeface, 18);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var checker = new SKPaint();
        canvas.DrawText("EUCHATRO / CARD ENHANCEMENTS", 20, 30, title, ink);
        canvas.DrawText("8 original textures · transparent sheet and sprites · labels/checkerboard are preview only", 20, 55, label, ink);
        var frames = new Dictionary<string, object>();
        var audit = new List<object>();

        for (var index = 0; index < catalogue.Length; index++)
        {
            var feature = catalogue[index];
            var source = sources[feature.Id];
            var sourcePath = Path.Combine(sourceRoot, source.GetProperty("file").GetString()!);
            if (Hash(sourcePath) != source.GetProperty("outputSha256").GetString())
                throw new InvalidDataException($"Source checksum changed: {feature.Name}");
            using var codec = SKCodec.Create(sourcePath) ?? throw new InvalidDataException($"Cannot decode {sourcePath}");
            if (codec.Info.Width != Width || codec.Info.Height != Height)
                throw new InvalidDataException($"Unexpected dimensions: {feature.Name}");
            using var original = SKBitmap.Decode(codec, new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var pixels = original.Pixels;
            var inset = feature.Key == "Glass" ? 0 : feature.Key == "Stone" ? 7 : 8;
            var removed = MaskExterior(pixels, feature.Key, inset);
            using var sprite = Bitmap(Width, Height);
            sprite.Pixels = pixels;
            var file = "sprites/" + feature.Id.Replace(':', '-') + ".png";
            var spritePath = Path.Combine(outputRoot, file);
            if (feature.Key == "Glass") File.Copy(sourcePath, spritePath, true);
            else Save(sprite, spritePath);
            var column = index % Columns;
            var row = index / Columns;
            var x = column * CellWidth + Padding;
            var y = row * CellHeight + Padding;
            for (var py = 0; py < Height; py++)
                Array.Copy(pixels, py * Width, atlasPixels, (y + py) * atlas.Width + x, Width);
            frames.Add(feature.Id, new
            {
                index, name = feature.Name, key = feature.Key, row, column,
                frame = new { x, y, w = Width, h = Height }, rotated = false, trimmed = false,
                sourceSize = new { w = Width, h = Height }, spriteSourceSize = new { x = 0, y = 0, w = Width, h = Height },
                file, sha256 = Hash(spritePath), sourceFile = Relative(outputRoot, sourcePath), sourceSha256 = Hash(sourcePath),
                originalSourceSha256 = source.GetProperty("sourceSha256").GetString(),
                originalSourceName = source.GetProperty("sourceName").GetString()
            });
            audit.Add(new
            {
                id = feature.Id, name = feature.Name, removedExteriorPixels = removed,
                protectedInterior = new { x = inset, y = inset, w = Width - inset * 2, h = Height - inset * 2 },
                transparentPixels = pixels.Count(pixel => pixel.Alpha == 0),
                partialAlphaPixels = pixels.Count(pixel => pixel.Alpha is > 0 and < 255),
                retainedPixelsUnchanged = true,
                mask = feature.Key == "Glass" ? "none; exact original PNG including internal transparency"
                    : feature.Key == "Wild" ? "perimeter-connected light exterior; minimum RGB 180"
                    : $"perimeter-connected dark exterior; maximum RGB {DarkLimit(feature.Key)}"
            });

            var px = column * previewCellWidth + 20;
            var pyPreview = previewTop + row * previewCellHeight + 28;
            canvas.DrawText($"{index + 1:00}  {feature.Name}", px, pyPreview - 9, label, ink);
            for (var cy = 0; cy < Height * 2; cy += 16)
            for (var cx = 0; cx < Width * 2; cx += 16)
            {
                checker.Color = (cx / 16 + cy / 16) % 2 == 0 ? SKColor.Parse("#364354") : SKColor.Parse("#566479");
                canvas.DrawRect(px + cx, pyPreview + cy, Math.Min(16, Width * 2 - cx), Math.Min(16, Height * 2 - cy), checker);
            }
            using var image = SKImage.FromBitmap(sprite);
            canvas.DrawImage(image, SKRect.Create(px, pyPreview, Width * 2, Height * 2), new SKSamplingOptions(SKFilterMode.Nearest));
            Console.WriteLine($"{feature.Id}: {removed} exterior pixels removed; retained RGBA and protected interior unchanged.");
        }
        atlas.Pixels = atlasPixels;
        Save(atlas, Path.Combine(outputRoot, "enhancements.png"));
        Save(preview, Path.Combine(outputRoot, "enhancements-labeled-preview.png"));
        Json(Path.Combine(outputRoot, "enhancements.json"), new
        {
            frames,
            meta = new
            {
                image = "enhancements.png", format = "RGBA8888", size = new { w = atlas.Width, h = atlas.Height },
                count = catalogue.Length, columns = Columns, rows, cellWidth = CellWidth, cellHeight = CellHeight, padding = Padding,
                scale = "1", origin = "top-left", order = "active game catalogue; left to right, then top to bottom",
                sha256 = Hash(Path.Combine(outputRoot, "enhancements.png")), sourceManifest = Relative(outputRoot, manifestPath),
                sourceManifestSha256 = Hash(manifestPath), filter = "nearest", mipmaps = false, alpha = "straight (unpremultiplied)",
                note = "Original 124x176 game substrates. Only perimeter matte is removed from seven opaque cards. Glass retains its original internal transparency. No labels or checkerboard in the atlas; game assets are unchanged."
            }
        });
        Json(Path.Combine(outputRoot, "border-audit.json"), new
        {
            count = catalogue.Length,
            preservation = "Existing game assets and source files are unchanged. Every retained sprite RGBA pixel is preserved; masks cannot enter the protected interior. Glass is copied without any edits.",
            entries = audit
        });
        Console.WriteLine($"Exported {catalogue.Length} sprites and {atlas.Width}x{atlas.Height} transparent sheet to {outputRoot}");
    }

    private static int DarkLimit(string key) => key == "Golden" ? 50 : key == "Stone" ? 20 : 32;

    private static int MaskExterior(SKColor[] pixels, string key, int inset)
    {
        if (key == "Glass") return 0;
        var visited = new bool[pixels.Length];
        var queue = new Queue<int>();
        var removed = 0;
        for (var x = 0; x < Width; x++) { Visit(x); Visit((Height - 1) * Width + x); }
        for (var y = 0; y < Height; y++) { Visit(y * Width); Visit(y * Width + Width - 1); }
        while (queue.TryDequeue(out var p))
        {
            var x = p % Width;
            var y = p / Width;
            if (x >= inset && x < Width - inset && y >= inset && y < Height - inset)
                throw new InvalidDataException($"Exterior mask reached protected {key} artwork.");
            if (pixels[p].Alpha != 0) removed++;
            pixels[p] = SKColors.Transparent;
            if (x > 0) Visit(p - 1); if (x + 1 < Width) Visit(p + 1);
            if (y > 0) Visit(p - Width); if (y + 1 < Height) Visit(p + Width);
        }
        if (removed == 0 || removed > Width * Height / 5)
            throw new InvalidDataException($"Unexpected {key} exterior mask size: {removed}");
        return removed;

        void Visit(int p)
        {
            if (visited[p]) return;
            visited[p] = true;
            var color = pixels[p];
            var matches = key == "Wild" ? Math.Min(color.Red, Math.Min(color.Green, color.Blue)) >= 180
                : Math.Max(color.Red, Math.Max(color.Green, color.Blue)) <= DarkLimit(key);
            if (color.Alpha == 0 || matches) queue.Enqueue(p);
        }
    }

    private static SKBitmap Bitmap(int w, int h) => new(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private static void Json(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
