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
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
