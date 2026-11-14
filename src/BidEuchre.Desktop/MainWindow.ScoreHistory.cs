using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    private readonly ScrollViewer _handScoreSummary = new()
    {
        Name = "HandScoreSummary", IsVisible = false, MaxHeight = 210, MaxWidth = 500,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBlock _publicLogText = new()
    {
        TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Default, FontSize = 13, Foreground = Ink
    };
    private readonly Border _publicLogPanel = new()
    {
        IsVisible = false, Width = 650, Height = 430, MaxWidth = 700, MaxHeight = 460,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(20), Padding = new Thickness(14),
        Background = Brush.Parse("#FF100B28"), BorderBrush = Gold, BorderThickness = new Thickness(2)
    };

    private Button PublicLogButton()
    {
        var button = QuietButton("Public log"); button.Name = "TablePublicLog";
        button.FontSize = 11; button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top; button.Margin = new Thickness(0, 84, 20, 0);
        button.Click += (_, _) => _publicLogPanel.IsVisible = !_publicLogPanel.IsVisible;
        return button;
    }

    private Control PublicLogPanel()
    {
        _publicLogPanel.IsVisible = false;
        if (_publicLogPanel.Child is not null) return _publicLogPanel;
        var close = QuietButton("Close"); close.Name = "ClosePublicLog";
        close.Click += (_, _) => _publicLogPanel.IsVisible = false;
        _publicLogPanel.Child = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"), RowSpacing = 12,
            Children =
            {
                new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Children =
                {
                    new TextBlock { Text = "PUBLIC EVENT LOG", Foreground = Gold, VerticalAlignment = VerticalAlignment.Center },
                    Place(close, column: 1)
                } },
                Place(new ScrollViewer
                {
                    Name = "PublicEventLog", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _publicLogText
                }, row: 1)
            }
        };
        return _publicLogPanel;
    }

    private void UpdatePublicLog(GameView view) => _publicLogText.Text = string.Join("\n", ScoreHistoryControls.Events(view));

    private void ResetScoreHistory()
    {
        _handScoreSummary.IsVisible = false; _handScoreSummary.Content = null;
        _handScoreSummary.Width = double.NaN;
        _publicLogText.Text = string.Empty; _publicLogPanel.IsVisible = false;
        _centerPanel.MaxWidth = 340; _centerPanel.MaxHeight = 230;
    }

    private void RenderScoreSummary(GameView view)
    {
        UpdatePublicLog(view);
        var completed = view.Phase is GamePhase.HandComplete or GamePhase.GameComplete;
        _handScoreSummary.IsVisible = completed;
        _handScoreSummary.Content = completed ? ScoreHistoryControls.Content(view, view.Phase == GamePhase.GameComplete) : null;
        _handScoreSummary.Width = completed ? 500 : double.NaN;
        _centerPanel.MaxWidth = completed ? 560 : 340;
        _centerPanel.MaxHeight = completed ? 390 : 230;
    }
}
