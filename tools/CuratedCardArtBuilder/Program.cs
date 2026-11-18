using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

if (args.FirstOrDefault() == "--jokers")
{
    JokerAssetSheet.Build(args.Skip(1).ToArray());
    return;
}
if (args.FirstOrDefault() == "--enhancements")
{
    EnhancementAssetSheet.Build(args.Skip(1).ToArray());
    return;
}

var sourceRoot = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "assets/curated-source/2026-11-18-shop-sheets");
var outputRoot = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "assets/pixel");
var manifestPath = Path.Combine(sourceRoot, "manifest.json");
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
if (entries.Length == 0) throw new InvalidDataException("The source manifest has no artwork entries.");
Directory.CreateDirectory(outputRoot);

const int columns = 5, cellWidth = 264, cellHeight = 240, margin = 16;
var rows = (entries.Length + columns - 1) / columns;
using var preview = new SKBitmap(columns * cellWidth + margin, rows * cellHeight + margin);
using var canvas = new SKCanvas(preview);
canvas.Clear(SKColor.Parse("#182133"));
using var typeface = SKTypeface.FromFile(Path.Combine(outputRoot, "Pixel.ttf")) ?? SKTypeface.Default;
using var font = new SKFont(typeface, 12);
using var caption = new SKFont(typeface, 10);
using var ink = new SKPaint { Color = SKColors.White, IsAntialias = false };
using var face = SKBitmap.Decode(Path.Combine(outputRoot, "cards/9H.png"));
var exported = new List<object>();
var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var index = 0; index < entries.Length; index++)
{
    var entry = entries[index];
    var sourceEntryId = Text(entry, "id") ?? throw new InvalidDataException("An artwork entry is missing its ID.");
    var category = Text(entry, "category") ?? sourceEntryId.Split(':')[0];
    // An explicit null is art with no gameplay item; only legacy manifests omit this field.
    var runtimeId = entry.TryGetProperty("runtimeId", out _) ? Text(entry, "runtimeId") : sourceEntryId;
    var displayId = runtimeId ?? sourceEntryId;
    var resource = Text(entry, "resource") ?? throw new InvalidDataException($"{sourceEntryId} has no resource mapping.");
    if (Path.GetFileName(resource) != resource || !resources.Add(resource))
        throw new InvalidDataException($"Invalid or duplicate resource: {resource}");
    // New manifests point at labeled originals; legacy manifests only supply source.
    var sourceName = Text(entry, "file") ?? Text(entry, "source")
        ?? throw new InvalidDataException($"{sourceEntryId} has no source file.");
    var sourcePath = Path.Combine(sourceRoot, sourceName);
    var sourceHash = Hash(sourcePath);
    if (Text(entry, "sha256") is { } expectedHash && !sourceHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Source checksum mismatch: {sourceName}");
    using var sheet = SKBitmap.Decode(sourcePath) ?? throw new InvalidDataException($"Cannot decode {sourceName}");
    using var source = new SKBitmap();
    var sourceBounds = new SKRectI(0, 0, sheet.Width, sheet.Height);
    if (entry.TryGetProperty("frame", out var frame))
    {
        var edges = frame.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        sourceBounds = new SKRectI(edges[0], edges[1], edges[2], edges[3]);
    }
    if (!sheet.ExtractSubset(source, sourceBounds)) throw new InvalidDataException($"Invalid frame for {sourceEntryId}");
    var (width, height) = category switch
    {
        "tarot" or "collector" or "pack" => (256, 384),
        "joker" => (280, 392),
        "machines" => FitDimensions(source, 768),
        "enhancement" => (124, 176),
        "soda" => FitDimensions(source, 256),
        "gumball" or "sigil" => (128, 128),
        "arena" or "arcade" => (source.Width, source.Height),
        _ => throw new InvalidDataException($"Unsupported artwork category: {category}")
    };
    var file = resource + ".png";
    var outputPath = Path.Combine(outputRoot, file);
    var runtimeSource = Text(entry, "runtimeFile");
    string? runtimeSourceHash = null;
    if (runtimeSource is not null)
    {
        // Approved final exports already contain their intended border and alpha.
        // Copy exact PNG bytes instead of resampling, remasking or framing them twice.
        if (category is not ("joker" or "enhancement") || Text(entry, "runtimeSha256") is not { } expectedRuntimeHash)
            throw new InvalidDataException($"Invalid final artwork mapping: {sourceEntryId}");
        var runtimePath = Path.Combine(sourceRoot, runtimeSource);
        runtimeSourceHash = Hash(runtimePath);
        if (!runtimeSourceHash.Equals(expectedRuntimeHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Final artwork checksum mismatch: {runtimeSource}");
        using var codec = SKCodec.Create(runtimePath) ?? throw new InvalidDataException($"Cannot decode {runtimeSource}");
        if (codec.Info.Width != width || codec.Info.Height != height)
            throw new InvalidDataException($"Unexpected final artwork dimensions: {runtimeSource}");
        File.Copy(runtimePath, outputPath, true);
    }
    else if (category is "arena" or "arcade")
    {
        // Scene artwork retains exact PNG bytes and native coordinates for animation.
        File.Copy(sourcePath, outputPath, true);
    }
    else
    {
        using var sprite = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var target = new SKCanvas(sprite))
        {
            target.Clear(SKColors.Transparent);
            var bounds = new SKRect(0, 0, width, height);
            // Enhancements match the existing card substrate; tickets/icons retain their aspect.
            DrawImage(target, source, category == "enhancement" ? bounds : Contain(source, bounds));
        }
        SavePng(sprite, outputPath);
    }

    // Inspect the written PNG, so metadata describes the delivered texture.
    using var output = SKBitmap.Decode(outputPath) ?? throw new InvalidDataException($"Cannot decode exported PNG: {file}");
    if (output.Width != width || output.Height != height)
        throw new InvalidDataException($"Unexpected output dimensions: {file}");
    var transparent = 0;
    var partialAlpha = 0;
    foreach (var pixel in output.Pixels)
    {
        if (pixel.Alpha == 0) transparent++;
        else if (pixel.Alpha < 255) partialAlpha++;
    }

    var x = margin + index % columns * cellWidth;
    var y = margin + index / columns * cellHeight;
    canvas.DrawText(displayId.ToUpperInvariant(), x, y + 12, font, ink);
    canvas.DrawText($"{Text(entry, "label") ?? Text(entry, "key") ?? category} / {width}x{height}", x, y + 28, caption, ink);
    if (category == "enhancement" && face is not null)
    {
        canvas.DrawBitmap(output, x, y + 42);
        using var white = new SKPaint { Color = SKColors.White };
        if (runtimeSource is null) canvas.DrawRect(x + 130, y + 42, 124, 176, white);
        canvas.DrawBitmap(output, x + 130, y + 42);
        // Rendering-only white halo keeps dark card ink legible on the substrate.
        using var halo = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn) };
        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            canvas.DrawBitmap(face, x + 130 + dx, y + 42 + dy, halo);
        canvas.DrawBitmap(face, x + 130, y + 42);
    }
    else
    {
        var bounds = new SKRect(x, y + 40, x + 254, y + 228);
        DrawImage(canvas, output, Contain(output, bounds, allowUpscale: false));
    }
    exported.Add(new
    {
        id = displayId, runtimeId, sourceEntryId, category, resource, file, width, height,
        usage = runtimeId is not null ? "catalog-item" : category is "arena" or "machines" or "arcade" or "pack" ? "scene-artwork" : "art-only",
        source = sourceName,
        originalSource = Text(entry, "source"),
        sourceName = Text(entry, "sourceName") ?? Text(entry, "source"),
        sourceWidth = source.Width,
        sourceHeight = source.Height,
        sourceSha256 = sourceHash,
        sourceUrl = Text(entry, "sourceUrl"),
        sourceDriveId = Text(entry, "sourceId"),
        sourceModifiedTime = Text(entry, "sourceModifiedTime"),
        sourceKind = Text(entry, "sourceKind"),
        mappingStatus = Text(entry, "mappingStatus"),
        transform = runtimeSource is not null ? category == "enhancement" ? "approved-transparent-enhancement" : "approved-uniform-joker-frame"
            : category is "arena" or "arcade" ? "original-bytes" : category == "enhancement" ? "card-substrate" : "aspect-fit",
        runtimeSource, runtimeSourceSha256 = runtimeSourceHash,
        outputSha256 = Hash(outputPath),
        transparentPixels = transparent,
        partialAlphaPixels = partialAlpha,
        hasTransparency = transparent + partialAlpha > 0
    });
    Console.WriteLine($"{displayId}: {width}x{height}, {transparent} transparent pixels, {partialAlpha} partial-alpha pixels");
}
SavePng(preview, Path.Combine(outputRoot, "curated-art-preview.png"));
File.WriteAllText(Path.Combine(outputRoot, "curated-art.json"), JsonSerializer.Serialize(new
{
    schemaVersion = 3,
    sourceManifest = Path.GetRelativePath(outputRoot, manifestPath).Replace(Path.DirectorySeparatorChar, '/'),
    sourceManifestSha256 = Hash(manifestPath),
    sourceProject = manifest.RootElement.TryGetProperty("project", out var project) ? project.Clone() : (JsonElement?)null,
    sourceCount = entries.Length,
    pathBase = "file paths are relative to this output directory; source paths are relative to the source manifest directory.",
    nonGameplayIds = "Arena and machine IDs identify scene artwork. Art-only entries are packaged without adding gameplay items.",
    preview = new { file = "curated-art-preview.png", width = preview.Width, height = preview.Height, columns, rows },
    entries = exported
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Exported {exported.Count} textures and a {columns}x{rows} preview to {outputRoot}");

static string? Text(JsonElement entry, string name) =>
    entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

static (int Width, int Height) FitDimensions(SKBitmap bitmap, int maximum)
{
    var scale = Math.Min(1, (double)maximum / Math.Max(bitmap.Width, bitmap.Height));
    return (Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
}

static SKRect Contain(SKBitmap bitmap, SKRect bounds, bool allowUpscale = true)
{
    var scale = Math.Min(bounds.Width / bitmap.Width, bounds.Height / bitmap.Height);
    if (!allowUpscale) scale = Math.Min(1, scale);
    var width = bitmap.Width * scale;
    var height = bitmap.Height * scale;
    return SKRect.Create(bounds.MidX - width / 2, bounds.MidY - height / 2, width, height);
}

static void DrawImage(SKCanvas canvas, SKBitmap bitmap, SKRect bounds)
{
    using var image = SKImage.FromBitmap(bitmap);
    canvas.DrawImage(image, bounds, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
}

static void SavePng(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);
}
