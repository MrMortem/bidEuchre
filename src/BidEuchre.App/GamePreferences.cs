using System.Text.Json;

namespace BidEuchre.App;

public sealed record GamePreferences(
    bool MotionEnabled = true,
    bool Fullscreen = false,
    int WindowWidth = 1320,
    int WindowHeight = 860,
    double UiScale = 1.0,
    HandSort HandSort = HandSort.Suit)
{
    public static IReadOnlyList<(int Width, int Height)> WindowSizes { get; } =
        [(1280, 720), (1320, 860), (1600, 900), (1920, 1080)];

    public static IReadOnlyList<double> UiScales { get; } = [1.0, 1.25, 1.5];

    // Hand-edited or outdated files may hold values the window cannot honor.
    public GamePreferences Normalized()
    {
        var size = WindowSizes.Contains((WindowWidth, WindowHeight)) ? (WindowWidth, WindowHeight) : (1320, 860);
        return this with
        {
            WindowWidth = size.Item1,
            WindowHeight = size.Item2,
            UiScale = UiScales.Contains(UiScale) ? UiScale : 1.0,
            HandSort = Enum.IsDefined(HandSort) ? HandSort : HandSort.Suit
        };
    }

    public static GamePreferences Load(string path)
    {
        try { return (JsonSerializer.Deserialize<GamePreferences>(File.ReadAllText(path)) ?? new()).Normalized(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public bool Save(string path)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
