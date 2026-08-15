using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace BidEuchre.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose
        };

        AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .AfterSetup(builder =>
            {
                if (builder.Instance is { } application)
                {
                    application.RequestedThemeVariant = ThemeVariant.Dark;
                    application.Styles.Add(new FluentTheme());
                }
            })
            .SetupWithLifetime(lifetime);

        lifetime.MainWindow = new MainWindow();
        lifetime.Start(args);
    }
}
