using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using BidEuchre.App;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

internal static class SettingsPage
{
    // Every change is reported immediately; the window applies and saves it.
    public static Control Build(GamePreferences current, Func<GamePreferences, bool> changed, Action back, PixelMotion effects, bool matchPaused = false)
    {
        var preferences = current;
        var saveError = new TextBlock
        {
            Name = "SettingsSaveError", IsVisible = false, Foreground = Avalonia.Media.Brush.Parse("#FF617B"), FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Settings changed for this session, but could not be saved. Your choices will be lost when you close the game. Try changing an option again after restoring access to the settings folder."
        };
        var sizes = GamePreferences.WindowSizes;
        var scales = GamePreferences.UiScales;

        var motion = OnOff("SettingsMotion", preferences.MotionEnabled);
        var hotseat = OnOff("SettingsHotseatPrivacy", preferences.AutomaticHotseatPrivacy);
        var fullscreen = OnOff("SettingsFullscreen", preferences.Fullscreen);
        var size = new ComboBox
        {
            Name = "SettingsWindowSize",
            ItemsSource = sizes.Select(option => $"{option.Width} × {option.Height}").ToArray(),
            SelectedIndex = Math.Max(0, sizes.ToList().IndexOf((preferences.WindowWidth, preferences.WindowHeight))),
            IsEnabled = !preferences.Fullscreen
        };
        var scale = new ComboBox
        {
            Name = "SettingsUiScale",
            ItemsSource = scales.Select(option => $"{option:P0}").ToArray(),
            SelectedIndex = Math.Max(0, scales.ToList().IndexOf(preferences.UiScale))
        };

        void Publish(GamePreferences next)
        {
            preferences = next;
            saveError.IsVisible = !changed(next);
        }

        hotseat.SelectionChanged += (_, _) => Publish(preferences with { AutomaticHotseatPrivacy = hotseat.SelectedIndex == 0 });
        motion.SelectionChanged += (_, _) => Publish(preferences with { MotionEnabled = motion.SelectedIndex == 0 });
        fullscreen.SelectionChanged += (_, _) =>
        {
            size.IsEnabled = fullscreen.SelectedIndex != 0;
            Publish(preferences with { Fullscreen = fullscreen.SelectedIndex == 0 });
        };
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedIndex < 0) return;
            var (width, height) = sizes[size.SelectedIndex];
            Publish(preferences with { WindowWidth = width, WindowHeight = height });
        };
        scale.SelectionChanged += (_, _) =>
        {
            if (scale.SelectedIndex >= 0) Publish(preferences with { UiScale = scales[scale.SelectedIndex] });
        };

        var menu = MenuParts.Button(matchPaused ? "Back to table" : "← Menu", false, effects, "SettingsBack", back, width: matchPaused ? 240 : 180);
        menu.HorizontalAlignment = HorizontalAlignment.Left;

        var rows = new StackPanel { Spacing = 14 };
        foreach (var (label, help, input) in new (string, string, ComboBox)[]
        {
            ("Automatic hotseat privacy", "With multiple human players, hide hands between turns until the next player presses Reveal.", hotseat),
            ("Motion", "Card flights, bid announcements and trick collection.", motion),
            ("Fullscreen", "Fill the whole display instead of a window.", fullscreen),
            ("Window size", "Used when fullscreen is off.", size),
            ("UI scale", "Enlarge everything. Scale is capped to fit the current window and restored when enlarged.", scale)
        })
        {
            StyleInput(input);
            input.HorizontalAlignment = HorizontalAlignment.Stretch;
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,220"), ColumnSpacing = 16 };
            row.Children.Add(new StackPanel
            {
                Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = label, Foreground = Ink, FontSize = 17, FontFamily = PixelArt.Font },
                    new TextBlock { Text = help, Foreground = Muted, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                }
            });
            row.Children.Add(Place(input, column: 1));
            rows.Children.Add(row);
        }

        var pause = new TextBlock
        {
            Name = "SettingsMatchPaused", IsVisible = matchPaused, Text = "Match paused",
            Foreground = Gold, FontSize = 17, FontFamily = PixelArt.Font
        };
        var title = MenuParts.Title(effects, "SETTINGS", 46);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        var content = new StackPanel
        {
            Spacing = 18, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                effects.Enter(menu, 0, -16, 0),
                pause,
                effects.Enter(title, 80, 0, -22, easing: new BackEaseOut(), duration: 360),
                effects.Enter(new TextBlock
                {
                    Text = "Your preferences apply immediately and are remembered.", Foreground = Muted, FontSize = 14
                }, 220, 0, 0),
                effects.Enter(CardPanel(new StackPanel { Spacing = 14, Children = { Eyebrow("PREFERENCES"), rows, saveError } }), 300, 0, 16)
            }
        };
        return MenuParts.Scene(effects, content, new ScrollViewer { Padding = new Thickness(30), Content = content });
    }

    private static ComboBox OnOff(string name, bool on) => new()
    {
        Name = name, ItemsSource = new[] { "On", "Off" }, SelectedIndex = on ? 0 : 1
    };
}
