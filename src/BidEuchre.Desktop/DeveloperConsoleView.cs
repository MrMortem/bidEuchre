using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>A drop-down developer console, toggled with the ` (backtick) key.</summary>
internal sealed class DeveloperConsoleView : Border
{
    private static readonly FontFamily Mono = new("Menlo, Consolas, DejaVu Sans Mono, Courier New, monospace");
    // Spray-paint colours for the graffiti greeting, one per line, top to bottom.
    private static readonly IBrush[] Spray =
        new[] { "#FF617B", "#FF7F6A", "#FFA25C", "#FFDB62", "#7DE0A0", "#55BCFF" }.Select(c => (IBrush)Brush.Parse(c)).ToArray();
    private static readonly IBrush Outline = Brush.Parse("#2A0E3D");

    private readonly DeveloperConsole _console;
    private readonly Func<Task> _prepare;
    private readonly StackPanel _lines = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private int _historyIndex;
    private int _bannerLine;
    private bool _busy;

    public DeveloperConsoleView(DeveloperConsole console, Func<Task> prepare)
    {
        _console = console;
        _prepare = prepare;
        IsVisible = false;
        VerticalAlignment = VerticalAlignment.Top;
        Background = Brush.Parse("#F2070414");
        BorderBrush = MainWindow.Gold;
        BorderThickness = new Thickness(0, 0, 0, 2);
        Padding = new Thickness(14, 10, 14, 8);
        ZIndex = 1000;

        _scroll = new ScrollViewer { Content = _lines, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _input = new TextBox
        {
            Name = "DeveloperConsoleInput",
            FontFamily = Mono, FontSize = 13, Foreground = MainWindow.Ink, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(4, 2), Watermark = "type a command…  (help)",
            CaretBrush = MainWindow.Gold
        };
        _input.AddHandler(KeyDownEvent, OnInputKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var prompt = new TextBlock { Text = "TJS›", FontFamily = Mono, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = MainWindow.Gold, VerticalAlignment = VerticalAlignment.Center };
        var inputRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"), Margin = new Thickness(0, 6, 0, 0) };
        inputRow.Children.Add(prompt);
        Grid.SetColumn(_input, 1);
        inputRow.Children.Add(_input);

        var layout = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };
        layout.Children.Add(_scroll);
        Grid.SetRow(inputRow, 1);
        layout.Children.Add(inputRow);
        Child = layout;

        Write(DeveloperConsole.Greeting());
    }

    public bool IsOpen => IsVisible;

    public void Toggle()
    {
        IsVisible = !IsVisible;
        if (IsVisible)
        {
            _historyIndex = _console.History.Count;
            Dispatcher.UIThread.Post(() => { _input.Focus(); _scroll.ScrollToEnd(); }, DispatcherPriority.Input);
        }
    }

    /// <summary>Keeps the console at about half the window's height.</summary>
    public void Fit(double windowHeight) => Height = Math.Max(240, windowHeight * 0.48);

    private async void OnInputKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await Submit();
                break;
            case Key.Up:
                e.Handled = true;
                Recall(-1);
                break;
            case Key.Down:
                e.Handled = true;
                Recall(+1);
                break;
            case Key.Tab:
                e.Handled = true;
                Complete();
                break;
            case Key.Escape or Key.OemTilde:
                e.Handled = true;
                Toggle();
                break;
            case Key.L when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                e.Handled = true;
                Clear();
                break;
        }
    }

    private async Task Submit()
    {
        if (_busy) return;
        var text = _input.Text ?? "";
        _input.Text = "";
        if (text.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase) || text.Trim().Equals("cls", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }
        _busy = true;
        try
        {
            await _prepare();
            Write(await _console.ExecuteAsync(text));
            await _prepare();
        }
        catch (Exception error)
        {
            Write([new ConsoleLine($"Unexpected error: {error.Message}", ConsoleTone.Error)]);
        }
        finally
        {
            _busy = false;
            _historyIndex = _console.History.Count;
        }
    }

    private void Recall(int step)
    {
        var history = _console.History;
        if (history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + step, 0, history.Count);
        _input.Text = _historyIndex < history.Count ? history[_historyIndex] : "";
        _input.CaretIndex = _input.Text.Length;
    }

    private void Complete()
    {
        var text = _input.Text ?? "";
        if (text.Contains(' ')) return;
        var matches = _console.CommandNames.Where(n => n.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1)
        {
            _input.Text = matches[0] + " ";
            _input.CaretIndex = _input.Text.Length;
        }
        else if (matches.Length > 1)
        {
            Write([new ConsoleLine(string.Join("  ", matches), ConsoleTone.Muted)]);
        }
    }

    private void Clear()
    {
        _lines.Children.Clear();
        _bannerLine = 0;
    }

    private void Write(IEnumerable<ConsoleLine> lines)
    {
        foreach (var line in lines) _lines.Children.Add(Render(line));
        // Very long sessions keep only the most recent output.
        while (_lines.Children.Count > 600) _lines.Children.RemoveAt(0);
        Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private Control Render(ConsoleLine line)
    {
        if (line.Tone == ConsoleTone.Banner) return RenderBanner(line.Text);
        _bannerLine = 0;
        return new SelectableTextBlock
        {
            Text = line.Text, FontFamily = Mono, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            FontWeight = line.Tone == ConsoleTone.Accent ? FontWeight.Bold : FontWeight.Normal,
            Foreground = line.Tone switch
            {
                ConsoleTone.Muted => MainWindow.Muted,
                ConsoleTone.Ok => Brush.Parse("#7DE0A0"),
                ConsoleTone.Error => Brush.Parse("#FF617B"),
                ConsoleTone.Accent => MainWindow.Gold,
                _ => MainWindow.Ink
            }
        };
    }

    // Graffiti letters: a dark offset "outline" behind a spray-coloured fill.
    private Control RenderBanner(string text)
    {
        var colour = Spray[Math.Min(_bannerLine++, Spray.Length - 1)];
        TextBlock Layer(IBrush brush, double x, double y) => new()
        {
            Text = text, FontFamily = Mono, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = brush,
            RenderTransform = new TranslateTransform(x, y)
        };
        return new Panel { Children = { Layer(Outline, 2, 2), Layer(colour, 0, 0) } };
    }
}
