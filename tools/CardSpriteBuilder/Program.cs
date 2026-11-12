using System.Text.Json;
using SkiaSharp;

// Reproducible assembly: identity markings are code, only court illustrations are generated.
var root = Path.GetFullPath(args.FirstOrDefault() ?? "assets/pixel");
const int width = 124, height = 176;
string[] ranks = ["9", "T", "J", "Q", "K", "A"];
string[] suits = ["C", "D", "H", "S"];
var glyphs = new Dictionary<string, string[]>
{
    ["9"] = ["01110", "11011", "11011", "01111", "00011", "00110", "11100"],
    ["1"] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    ["0"] = ["01110", "11011", "11011", "11011", "11011", "11011", "01110"],
    ["J"] = ["00111", "00011", "00011", "00011", "11011", "11011", "01110"],
    ["Q"] = ["01110", "11011", "11011", "11011", "11011", "01110", "00011"],
    ["K"] = ["11011", "11011", "11110", "11100", "11110", "11011", "11011"],
    ["A"] = ["01110", "11011", "11011", "11111", "11011", "11011", "11011"],
    ["C"] = ["000111000", "001111100", "000111000", "011111110", "111111111", "111111111", "011111110", "000111000", "001111100"],
    ["D"] = ["000010000", "000111000", "001111100", "011111110", "111111111", "011111110", "001111100", "000111000", "000010000"],
    ["H"] = ["011000110", "111101111", "111111111", "111111111", "011111110", "001111100", "000111000", "000010000", "000000000"],
    ["S"] = ["000010000", "000111000", "001111100", "011111110", "111111111", "111111111", "011111110", "000111000", "001111100"]
};
using var courts = SKBitmap.Decode(Path.Combine(root, "courts-transparent.png"))
    ?? throw new InvalidOperationException("Missing transparent court source.");
if (courts.GetPixel(0, 0).Alpha != 0)
    throw new InvalidOperationException("Court source must have a truly transparent background.");
Directory.CreateDirectory(Path.Combine(root, "cards"));
using var atlas = new SKBitmap(width * 6, height * 4, SKColorType.Rgba8888, SKAlphaType.Premul);
using var atlasCanvas = new SKCanvas(atlas);
atlasCanvas.Clear(SKColors.Transparent);
var cards = new Dictionary<string, object>();
using var background = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
using (var paper = new SKCanvas(background)) paper.Clear(SKColors.White);
Save(background, Path.Combine(root, "card-background-white.png"));
using var preview = new SKBitmap((width + 16) * 6 + 16, (height + 20) * 4 + 20);
using var previewCanvas = new SKCanvas(preview);
previewCanvas.Clear(SKColor.Parse("#172C32"));
for (var row = 0; row < suits.Length; row++)
for (var column = 0; column < ranks.Length; column++)
{
    var suit = suits[row];
    var rank = ranks[column];
    var code = rank + suit;
    using var face = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(face))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(2);
        using var ink = new SKPaint { Color = SKColor.Parse(row is 1 or 2 ? "#CF1827" : "#111324"), IsAntialias = false };
        for (var end = 0; end < 2; end++)
        {
            canvas.Save();
            if (end == 1) { canvas.Translate(62, 88); canvas.RotateDegrees(180); }
            var label = rank == "T" ? "10" : rank;
            for (var i = 0; i < label.Length; i++) DrawGlyph(canvas, label[i].ToString(), 4 + i * 8.4f, 4, 1.4f, ink);
            DrawGlyph(canvas, suit, 4, 16, 1, ink);
            canvas.Restore();
        }
        if (column is >= 2 and <= 4)
        {
            var cell = courts.Width / 3;
            canvas.DrawBitmap(courts, new SKRect((column - 2) * cell, 0, (column - 1) * cell, courts.Height),
                new SKRect(16, 14, 46, 74));
            // Suit emblems distinguish the courts even in a heavily overlapped fan.
            DrawGlyph(canvas, suit, 25, 38, 1.3f, ink);
        }
        else if (rank == "A") DrawGlyph(canvas, suit, 20, 31, 2.5f, ink);
        else
        {
            // Eight outer pips, then one (nine) or two (ten) center pips.
            foreach (var y in new float[] { 18, 34, 54, 70 })
            foreach (var x in new float[] { 21, 41 }) Pip(canvas, suit, x, y, y > 44, ink);
            if (rank == "9") Pip(canvas, suit, 31, 44, false, ink);
            else { Pip(canvas, suit, 31, 26, false, ink); Pip(canvas, suit, 31, 62, true, ink); }
        }
    }
    Save(face, Path.Combine(root, "cards", code + ".png"));
    atlasCanvas.DrawBitmap(face, column * width, row * height);
    cards[code] = new { x = column * width, y = row * height, width, height };
    var px = 16 + column * (width + 16);
    var py = 20 + row * (height + 20);
    previewCanvas.DrawBitmap(background, px, py);
    previewCanvas.DrawBitmap(face, px, py);
}
Save(atlas, Path.Combine(root, "card-faces.png"));
Save(preview, Path.Combine(root, "cards-preview.png"));
File.WriteAllText(Path.Combine(root, "cards.json"), JsonSerializer.Serialize(new
{
    cardWidth = width, cardHeight = height, columns = 6, rows = 4, ranks, suits,
    atlas = "card-faces.png", background = "card-background-white.png", cards
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Created 24 transparent faces, 6x4 atlas, white background, manifest and preview.");

void DrawGlyph(SKCanvas canvas, string glyph, float x, float y, float scale, SKPaint paint)
{
    var pixels = glyphs[glyph];
    for (var gy = 0; gy < pixels.Length; gy++)
    for (var gx = 0; gx < pixels[gy].Length; gx++)
        if (pixels[gy][gx] == '1') canvas.DrawRect(x + gx * scale, y + gy * scale, scale, scale, paint);
}
void Pip(SKCanvas canvas, string suit, float x, float y, bool inverted, SKPaint paint)
{
    canvas.Save();
    canvas.Translate(x, y);
    if (inverted) canvas.RotateDegrees(180);
    DrawGlyph(canvas, suit, -4.5f, -4.5f, 1, paint);
    canvas.Restore();
}
static void Save(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);
}
