using BidEuchre.App;

internal static class GamePreferencesTests
{
    public static void PersistMotion()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"euchre-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "preferences.json");
        try
        {
            Check(GamePreferences.Load(path).MotionEnabled, "Missing preferences should use the default");
            Check(new GamePreferences(false).Save(path), "Preference should save into a new directory");
            Check(!GamePreferences.Load(path).MotionEnabled, "Motion off should survive restart");
            Check(new GamePreferences(true).Save(path), "Atomic overwrite should succeed");
            Check(GamePreferences.Load(path).MotionEnabled, "Overwritten preference should reload");
            File.WriteAllText(path, "not json");
            Check(GamePreferences.Load(path).MotionEnabled, "Corrupt settings must not prevent startup");
            Check(!new GamePreferences(false).Save(folder), "An invalid destination should fail gracefully");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    public static void PersistGraphics()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"euchre-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "preferences.json");
        try
        {
            var defaults = GamePreferences.Load(path);
            Check(!defaults.Fullscreen && defaults.WindowWidth == 1320 && defaults.WindowHeight == 860 && defaults.UiScale == 1.0,
                "Missing preferences should use the default graphics");
            var chosen = new GamePreferences(false, true, 1600, 900, 1.25);
            Check(chosen.Save(path), "Graphics preferences should save");
            Check(GamePreferences.Load(path) == chosen, "Graphics preferences should survive restart");

            File.WriteAllText(path, "{\"MotionEnabled\":false}");
            var legacy = GamePreferences.Load(path);
            Check(!legacy.MotionEnabled && legacy == new GamePreferences(false), "A motion-only file should keep motion and default the rest");

            File.WriteAllText(path, "{\"Fullscreen\":true,\"WindowWidth\":5,\"WindowHeight\":99999,\"UiScale\":40}");
            var repaired = GamePreferences.Load(path);
            Check(repaired.Fullscreen && repaired.WindowWidth == 1320 && repaired.WindowHeight == 860 && repaired.UiScale == 1.0,
                "Unsupported sizes and scales should fall back to defaults");
            Check(GamePreferences.WindowSizes.Contains((1320, 860)) && GamePreferences.UiScales.Contains(1.0),
                "Defaults must be offered as presets");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
