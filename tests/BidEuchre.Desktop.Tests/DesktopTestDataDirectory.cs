// All desktop tests, including older MainWindow fixtures, must keep persistence away from user data.
internal sealed class DesktopTestDataDirectory : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bideuchre-desktop-tests-" + Guid.NewGuid().ToString("N"));
    public DesktopTestDataDirectory()
    {
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", _directory);
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", _previous);
        Directory.Delete(_directory, recursive: true);
    }
}
