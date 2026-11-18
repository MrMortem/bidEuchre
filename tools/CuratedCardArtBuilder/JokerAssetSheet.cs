using System.Security.Cryptography;
using System.Text.Json;
using BidEuchre.Core;
using SkiaSharp;

// Deterministic export with the approved exterior mask and a shared rounded frame.
// Original PNGs stay intact. Interior scenes and lettering are never resampled.
internal static class JokerAssetSheet
{
    private const int Width = 280, Height = 392, Padding = 20, Columns = 8;
    private const int CellWidth = Width + Padding * 2, CellHeight = Height + Padding * 2;
    private static readonly SKColor Paper = SKColor.Parse("#F4E7CD");
    private static readonly SKColor Outline = SKColor.Parse("#1B1713");

    public static void Build(string[] args)
    {
        // Rebuild from preserved pre-frame inputs, never from already imported game sprites.
        var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/curated-source/2026-11-16-joker-frame-inputs");
        var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/sheets/jokers");
        var manifestPath = Path.Combine(sourceRoot, "curated-art.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var sources = manifest.RootElement.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("category").GetString() == "joker"
                && entry.GetProperty("runtimeId").ValueKind == JsonValueKind.String)
            .ToDictionary(entry => entry.GetProperty("runtimeId").GetString()!);
        var jokers = FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Joker).ToArray();
        if (!jokers.Select(joker => joker.Id).ToHashSet().SetEquals(sources.Keys))
            throw new InvalidDataException("Joker sources must exactly match the active catalogue.");
        Directory.CreateDirectory(Path.Combine(outputRoot, "sprites"));
        var rows = (jokers.Length + Columns - 1) / Columns;
        using var atlas = NewBitmap(Columns * CellWidth, rows * CellHeight);
        var atlasPixels = new SKColor[atlas.Width * atlas.Height];
        using var preview = new SKBitmap(Columns * CellWidth, rows * (CellHeight + 36) + 64);
        using var canvas = new SKCanvas(preview);
        canvas.Clear(SKColor.Parse("#131B28"));
        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 18);
        using var heading = new SKFont(typeface, 25);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var checker = new SKPaint();
        canvas.DrawText($"EUCHATRO / JOKERS / {jokers.Length} CARDS", Padding, 32, heading, ink);
        canvas.DrawText("Names follow the game catalogue. Checkerboard is preview only; PNG sheet and sprites have real alpha.", Padding, 54, font, ink);
        var frames = new Dictionary<string, object>();
        var audit = new List<object>();
        var cleanedCount = 0;
        var framePixels = SharedFrame();
        using (var frameBitmap = NewBitmap(Width, Height))
        {
            frameBitmap.Pixels = framePixels;
            Save(frameBitmap, Path.Combine(outputRoot, "joker-frame.png"));
        }
        for (var index = 0; index < jokers.Length; index++)
        {
            var joker = jokers[index];
            var source = sources[joker.Id];
            var file = source.GetProperty("file").GetString()!;
            var sourcePath = Path.Combine(sourceRoot, file);
            if (Hash(sourcePath) != source.GetProperty("outputSha256").GetString())
                throw new InvalidDataException($"Source checksum changed: {joker.Name}");
            using var codec = SKCodec.Create(sourcePath) ?? throw new InvalidDataException($"Cannot decode {file}");
            if (codec.Info.Width != Width || codec.Info.Height != Height)
                throw new InvalidDataException($"Unexpected dimensions: {file}");
            using var original = SKBitmap.Decode(codec, new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var pixels = original.Pixels;
            // Opaque source images (including aspect-fitted Bully) have an opaque centre at the top edge.
            var hasMatte = pixels[Width / 2].Alpha == 255;
            var removed = MaskExterior(pixels, hasMatte);
            if (removed > 0) cleanedCount++;
            var cleaned = NormalizeFrame(pixels, framePixels);
            using var sprite = NewBitmap(Width, Height);
            sprite.Pixels = cleaned;
            var spriteName = "sprites/" + joker.Id.Replace(':', '-') + ".png";
            var spritePath = Path.Combine(outputRoot, spriteName);
            Save(sprite, spritePath);
            var column = index % Columns;
            var row = index / Columns;
            var x = column * CellWidth + Padding;
            var y = row * CellHeight + Padding;
            for (var py = 0; py < Height; py++)
                Array.Copy(cleaned, py * Width, atlasPixels, (y + py) * atlas.Width + x, Width);
            frames.Add(joker.Id, new
            {
                index, name = joker.Name, key = joker.Key, row, column,
                frame = new { x, y, w = Width, h = Height },
                rotated = false, trimmed = false,
                sourceSize = new { w = Width, h = Height },
                spriteSourceSize = new { x = 0, y = 0, w = Width, h = Height },
                file = spriteName,
                sha256 = Hash(spritePath),
                sourceFile = Path.GetRelativePath(outputRoot, sourcePath).Replace(Path.DirectorySeparatorChar, '/'),
                sourceSha256 = Hash(sourcePath)
            });
            audit.Add(new
            {
                id = joker.Id, name = joker.Name, sourceExterior = hasMatte ? "opaque-matte" : "transparent",
                removedExteriorPixels = removed, preservedInteriorScene = true,
                changedFramePixels = cleaned.Where((pixel, p) => pixel != pixels[p]).Count(),
                transparentPixels = cleaned.Count(pixel => pixel.Alpha == 0),
                partialAlphaPixels = cleaned.Count(pixel => pixel.Alpha is > 0 and < 255),
                bounds = Bounds(cleaned)
            });
            var previewX = column * CellWidth + Padding;
            var previewY = 64 + row * (CellHeight + 36) + 32;
            canvas.DrawText($"{index + 1:00}  {joker.Name}", previewX, previewY - 9, font, ink);
            for (var cy = 0; cy < Height; cy += 14)
            for (var cx = 0; cx < Width; cx += 14)
            {
                checker.Color = ((cx / 14 + cy / 14) % 2 == 0) ? SKColor.Parse("#364354") : SKColor.Parse("#566479");
                canvas.DrawRect(previewX + cx, previewY + cy, Math.Min(14, Width - cx), Math.Min(14, Height - cy), checker);
            }
            canvas.DrawBitmap(sprite, previewX, previewY);
            Console.WriteLine($"{joker.Id}: {removed} exterior pixels removed; shared rounded frame applied; interior preserved.");
        }
        atlas.Pixels = atlasPixels;
        Save(atlas, Path.Combine(outputRoot, "jokers.png"));
        Save(preview, Path.Combine(outputRoot, "jokers-labeled-preview.png"));
        WriteJson(Path.Combine(outputRoot, "jokers.json"), new
        {
            frames,
            meta = new
            {
                image = "jokers.png", format = "RGBA8888", size = new { w = atlas.Width, h = atlas.Height },
                scale = "1", columns = Columns, rows, cellWidth = CellWidth, cellHeight = CellHeight,
                padding = Padding, origin = "top-left", order = "active game catalogue; left to right, then top to bottom",
                count = jokers.Length, sha256 = Hash(Path.Combine(outputRoot, "jokers.png")),
                sourceManifest = Path.GetRelativePath(outputRoot, manifestPath).Replace(Path.DirectorySeparatorChar, '/'),
                sourceManifestSha256 = Hash(manifestPath),
                filter = "nearest", mipmaps = false, alpha = "straight (unpremultiplied)",
                border = new
                {
                    template = "joker-frame.png", sha256 = Hash(Path.Combine(outputRoot, "joker-frame.png")),
                    outer = new { x = 4, y = 4, w = 272, h = 384, radius = 12 },
                    outlineWidth = 2, totalRimWidth = 8, outline = "#1B1713", paper = "#F4E7CD",
                    aperture = new { x = 12, y = 12, w = 256, h = 368, radius = 4 },
                    antialiasSamplesPerPixel = 16
                },
                note = "All cards share the same rounded outline, cream rim and silhouette. Interior scenes and Joker lettering are not resampled. Bully keeps its narrower interior within the common frame. No labels or checkerboard in the atlas."
            }
        });
        WriteJson(Path.Combine(outputRoot, "border-audit.json"), new
        {
            count = jokers.Length, cleanedCount,
            mask = "Perimeter-connected near-white exterior (RGB minimum 224) for opaque mattes; perimeter alpha <= 8 for existing transparent exteriors; detached exterior components removed. Retained RGBA pixels unchanged.",
            border = "Shared radius-12 rounded outline, 2px dark stroke, 8px total cream rim. Old perimeter strokes removed by a four-pixel silhouette erosion; connected paper harmonized without changing scene pixels or dark lettering.",
            preservation = "Original source files, interior scenes, lettering and existing near-opaque interior alpha are retained. Only the exterior and printed border paper are changed; no resampling.",
            entries = audit
        });
        Console.WriteLine($"Exported {jokers.Length} sprites, {atlas.Width}x{atlas.Height} transparent atlas, labels, coordinates and border audit to {outputRoot}");
    }

    private static bool InAperture(double x, double y) => InRoundRect(x, y, 12, 12, 268, 380, 4);

    private static bool InRoundRect(double x, double y, double left, double top, double right, double bottom, double radius)
    {
        if (x < left || y < top || x >= right || y >= bottom) return false;
        var cx = Math.Clamp(x, left + radius, right - radius);
        var cy = Math.Clamp(y, top + radius, bottom - radius);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
    }

    private static SKColor[] SharedFrame()
    {
        var pixels = new SKColor[Width * Height];
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        {
            if (InAperture(x + .5, y + .5)) continue;
            var covered = 0; var paper = 0;
            for (var sy = 0; sy < 4; sy++) for (var sx = 0; sx < 4; sx++)
            {
                var px = x + (sx + .5) / 4; var py = y + (sy + .5) / 4;
                if (InRoundRect(px, py, 4, 4, 276, 388, 12)) covered++;
                if (InRoundRect(px, py, 6, 6, 274, 386, 10)) paper++;
            }
            if (covered == 0) continue;
            byte Mix(byte dark, byte light) => (byte)Math.Round((dark * (covered - paper) + light * paper) / (double)covered);
            pixels[y * Width + x] = new SKColor(Mix(Outline.Red, Paper.Red), Mix(Outline.Green, Paper.Green),
                Mix(Outline.Blue, Paper.Blue), (byte)Math.Round(255.0 * covered / 16));
        }
        return pixels;
    }

    private static SKColor[] NormalizeFrame(SKColor[] source, SKColor[] frame)
    {
        // Remove the old outer stroke along its own contour, including curved fragments.
        // An eight-neighbour distance transform avoids fixed corner crops touching a scene.
        var distance = Enumerable.Repeat(int.MaxValue, source.Length).ToArray();
        var queue = new Queue<int>();
        for (var p = 0; p < source.Length; p++)
            if (source[p].Alpha == 0) { distance[p] = 0; queue.Enqueue(p); }
        while (queue.TryDequeue(out var p))
        {
            if (distance[p] >= 4) continue;
            var x = p % Width; var y = p / Width;
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx; var ny = y + dy;
                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;
                var n = ny * Width + nx;
                if (distance[n] <= distance[p] + 1) continue;
                distance[n] = distance[p] + 1; queue.Enqueue(n);
            }
        }
        var result = source.ToArray();
        for (var p = 0; p < result.Length; p++)
            if (distance[p] <= 4) result[p] = Paper;

        // Standardize only connected printed paper. The inner dark scene line and
        // bounded label/paper zones prevent light details inside the artwork changing.
        var reference = source[12 * Width + Width / 2];
        var seen = new bool[result.Length];
        for (var x = 12; x < 268; x++) { Visit(x, 12); Visit(x, 379); }
        for (var y = 12; y < 380; y++) { Visit(12, y); Visit(267, y); }
        while (queue.TryDequeue(out var p))
        {
            result[p] = new SKColor(Paper.Red, Paper.Green, Paper.Blue, result[p].Alpha);
            var x = p % Width; var y = p / Width;
            Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
        }
        void Visit(int x, int y)
        {
            if (x < 12 || x >= 268 || y < 12 || y >= 380) return;
            if (!(x < 20 || x >= 260 || y < 20 || y >= 372 || x < 42 && y < 158 || x >= 238 && y >= 236)) return;
            var p = y * Width + x;
            if (seen[p]) return;
            seen[p] = true;
            var color = result[p];
            if (distance[p] <= 4 || Math.Max(Math.Abs(color.Red - reference.Red),
                Math.Max(Math.Abs(color.Green - reference.Green), Math.Abs(color.Blue - reference.Blue))) <= 24)
                queue.Enqueue(p);
        }
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        {
            var p = y * Width + x;
            if (!InAperture(x + .5, y + .5)) result[p] = frame[p];
            if (x >= 42 && x < 238 && y >= 20 && y < 372 && result[p] != source[p])
                throw new InvalidDataException("Shared frame changed the protected interior scene.");
        }
        return result;
    }

    private static int MaskExterior(SKColor[] pixels, bool hasMatte)
    {
        var original = pixels.ToArray();
        {
            // The dark outer frame encloses the matching light-coloured printed border.
            // Reach the exterior from the perimeter; never globally key out white/cream.
            var visited = new bool[pixels.Length];
            var queue = new Queue<int>();
            for (var x = 0; x < Width; x++) { Visit(x); Visit((Height - 1) * Width + x); }
            for (var y = 0; y < Height; y++) { Visit(y * Width); Visit(y * Width + Width - 1); }
            while (queue.TryDequeue(out var p))
            {
                pixels[p] = SKColors.Transparent;
                var x = p % Width; var y = p / Width;
                if (x > 0) Visit(p - 1); if (x + 1 < Width) Visit(p + 1);
                if (y > 0) Visit(p - Width); if (y + 1 < Height) Visit(p + Width);
            }
            void Visit(int p)
            {
                if (visited[p]) return;
                visited[p] = true;
                var color = pixels[p];
                if (hasMatte
                    ? color.Alpha == 0 || Math.Min(color.Red, Math.Min(color.Green, color.Blue)) >= 224
                    : color.Alpha <= 8) queue.Enqueue(p);
            }
        }
        // A full framed card is one component. Disconnected exterior export specks are not artwork.
        var seen = new bool[pixels.Length];
        var largest = new List<int>();
        for (var start = 0; start < pixels.Length; start++)
        {
            if (seen[start] || pixels[start].Alpha == 0) continue;
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start); seen[start] = true;
            while (queue.TryDequeue(out var p))
            {
                component.Add(p);
                var x = p % Width; var y = p / Width;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    var n = ny * Width + nx;
                    if (seen[n] || pixels[n].Alpha == 0) continue;
                    seen[n] = true; queue.Enqueue(n);
                }
            }
            if (component.Count > largest.Count) largest = component;
        }
        if (largest.Count < Width * Height * .8) throw new InvalidDataException("Mask breached the card frame.");
        var keep = new bool[pixels.Length];
        foreach (var p in largest) keep[p] = true;
        var removed = 0;
        for (var p = 0; p < pixels.Length; p++)
        {
            if (!keep[p]) pixels[p] = SKColors.Transparent;
            if (pixels[p].Alpha == 0 && original[p].Alpha != 0)
            {
                var x = p % Width; var y = p / Width;
                if (x >= 20 && x < Width - 20 && y >= 20 && y < Height - 20)
                    throw new InvalidDataException("Exterior mask reached protected interior artwork.");
                removed++;
            }
            else if (pixels[p].Alpha != 0 && pixels[p] != original[p])
                throw new InvalidDataException("Exterior mask changed a retained pixel.");
        }
        return removed;
    }

    private static object Bounds(SKColor[] pixels)
    {
        var left = Width; var top = Height; var right = -1; var bottom = -1;
        for (var p = 0; p < pixels.Length; p++)
            if (pixels[p].Alpha != 0)
            {
                var x = p % Width; var y = p / Width;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        return new { x = left, y = top, width = right - left + 1, height = bottom - top + 1 };
    }

    private static SKBitmap NewBitmap(int width, int height) => new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
