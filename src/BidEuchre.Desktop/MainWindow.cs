using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Core;
using System.Text.Json;

namespace BidEuchre.Desktop;

public sealed class MainWindow : Window
{
    private static readonly IBrush Paper = Brush.Parse("#0B0720");
    private static readonly IBrush Panel = Brush.Parse("#100B28");
    private static readonly IBrush PanelRaised = Brush.Parse("#191039");
    private static readonly IBrush Ink = Brush.Parse("#FFF5DC");
    private static readonly IBrush Muted = Brush.Parse("#C0B6DD");
    private static readonly IBrush Green = Brush.Parse("#55BCFF");
    private static readonly IBrush GreenDark = Brush.Parse("#103480");
    private static readonly IBrush Felt = Brush.Parse("#100B28");
    private static readonly IBrush Amber = Brush.Parse("#FF617B");
    private static readonly IBrush Gold = Brush.Parse("#FFDB62");
    private static readonly IBrush Line = Brush.Parse("#8D54B1");
    private static readonly IBrush Red = Brush.Parse("#FF617B");
    private static readonly IBrush Walnut = Brush.Parse("#8D54B1");
    private static readonly IBrush CardPaper = Brush.Parse("#FFF9ED");
    private static readonly IBrush CardInk = Brush.Parse("#17201D");
    private static readonly IBrush CardRed = Brush.Parse("#B63D3D");

    private readonly EngineCatalog _catalog = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly ContentControl _playHost = new();
    private readonly TextBlock _status = new() { Foreground = Red, FontSize = 12 };
    private readonly TextBox _tableName = new() { Text = "Friday Table", MaxLength = 40 };
    private readonly ComboBox _pace = new();
    private readonly TextBox[] _seatNames = new TextBox[4];
    private readonly ComboBox[] _seatKinds = new ComboBox[4];
    private readonly ComboBox[] _seatEngines = new ComboBox[4];
    private readonly TextBox _engineExecutable = new();
    private readonly TextBox _engineArguments = new();
    private readonly StackPanel _engineList = new() { Spacing = 8 };
    private readonly TextBlock _teamZeroScore = ScoreText();
    private readonly TextBlock _teamOneScore = ScoreText();
    private readonly TextBlock _phaseText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Amber, FontWeight = FontWeight.Bold, FontSize = 12 };
    private readonly TextBlock _contractText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ink, FontSize = 19, FontFamily = PixelArt.Font };
    private readonly TextBlock _dealerText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 };
    private readonly ComboBox _viewerSeat = new() { MinWidth = 180 };
    private readonly Button _startButton = PrimaryButton("Start session");
    private readonly ContentControl _tableHost = new();
    private readonly TextBlock _actionTitle = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ink, FontSize = 18, FontFamily = PixelArt.Font };
    private readonly TextBlock _actionHelp = new() { FontFamily = FontFamily.Default, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _actionButtons = new() { ItemWidth = double.NaN, ItemHeight = double.NaN };
    private readonly StackPanel _eventList = new() { Spacing = 1 };
    private readonly TextBlock _sessionError = new() { Foreground = Red, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs;

    private GameSession? _session;
    private int? _viewer;
    private bool _refreshing;
    private bool _tableTransitioning;
    private long _sessionGeneration;
    private string? _renderKey;
    private readonly TableMotionTracker _motionTracker = new();
    private readonly PixelMotion _motion = new();
    private BidAnnouncement? _bidAnnouncement;
    private readonly TableInputState _input = new();
    private readonly string _preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BidEuchre", "preferences.json");
    private GamePreferences _preferences = new();
    private TableMotionCue _motionCue = TableMotionCue.None;

    public MainWindow()
    {
        Title = "Bid Euchre";
        Width = 1320;
        Height = 860;
        MinWidth = 980;
        MinHeight = 680;
        Background = Paper;
        FontFamily = PixelArt.Font;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeChanged += (_, _) => _bidAnnouncement?.Cancel();
        _preferences = GamePreferences.Load(_preferencesPath);
        _motion.SetEnabled(_preferences.MotionEnabled);

        StyleInput(_tableName);
        StyleInput(_pace);
        StyleInput(_engineExecutable);
        StyleInput(_engineArguments);
        StyleInput(_viewerSeat);
        _pace.ItemsSource = PaceOption.All;
        _pace.SelectedIndex = 1;
        _viewerSeat.SelectionChanged += async (_, _) =>
        {
            if (_viewerSeat.SelectedItem is ViewerOption option)
            {
                _input.BeginViewChange();
                _motion.Cancel();
                _bidAnnouncement?.Cancel();
                SyncNativeInput();
                _viewer = option.Seat;
                _renderKey = null;
                await RefreshGameAsync();
            }
        };
        _startButton.Click += async (_, _) => await StartSessionAsync();

        _tabs = new TabControl
        {
            Background = Paper,
            Foreground = Ink,
            ItemsSource = new[]
            {
                new TabItem { Header = "Play", Content = _playHost },
                new TabItem { Header = "Engines", Content = BuildEnginePage() },
                new TabItem { Header = "Rules", Content = BuildRulesPage() }
            }
        };

        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedIndex != 0) _bidAnnouncement?.Cancel(); };

        Content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Children =
            {
                _tabs,
                Place(_status, row: 1)
            }
        };
        _status.Margin = new Thickness(20, 5, 20, 10);

        ShowSetup();
        RefreshEngineCatalog();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _refreshTimer.Tick += async (_, _) => await RefreshGameAsync();
        _refreshTimer.Start();

        Closed += async (_, _) =>
        {
            _refreshTimer.Stop();
            _motion.Cancel();
            _bidAnnouncement?.Cancel();
            var session = DetachSession();
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch
                {
                    // The window is already closing; session disposal is best effort.
                }
            }
        };
    }

    private Control BuildSetupPage()
    {
        var content = new StackPanel { Spacing = 18, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(PageHeading("New session", "Set the table, choose a comfortable pace, and assign all four seats."));

        var options = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("2*,*"), ColumnSpacing = 12 };
        options.Children.Add(Field("Table name", _tableName));
        options.Children.Add(Place(Field("Bot pace", _pace), column: 1));
        content.Children.Add(CardPanel(options));

        var seats = new StackPanel { Spacing = 10 };
        var defaults = new[]
        {
            ("You", 0),
            ("TableBot West", 1),
            ("TableBot North", 1),
            ("TableBot East", 1)
        };
        for (var seat = 0; seat < 4; seat++)
        {
            _seatNames[seat] = new TextBox { Text = defaults[seat].Item1, MaxLength = 30 };
            _seatKinds[seat] = new ComboBox { ItemsSource = new[] { "Human", "Bot" }, SelectedIndex = defaults[seat].Item2 };
            _seatEngines[seat] = new ComboBox { IsEnabled = defaults[seat].Item2 == 1 };
            StyleInput(_seatNames[seat]);
            StyleInput(_seatKinds[seat]);
            StyleInput(_seatEngines[seat]);
            var capturedSeat = seat;
            _seatKinds[seat].SelectionChanged += (_, _) =>
                _seatEngines[capturedSeat].IsEnabled = _seatKinds[capturedSeat].SelectedIndex == 1;

            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("46,2*,*,2*"), ColumnSpacing = 10 };
            row.Children.Add(new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = seat % 2 == 0 ? Green : Amber,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (seat + 1).ToString(),
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            row.Children.Add(Place(Field("Player name", _seatNames[seat]), column: 1));
            row.Children.Add(Place(Field("Control", _seatKinds[seat]), column: 2));
            row.Children.Add(Place(Field("Engine", _seatEngines[seat]), column: 3));
            seats.Children.Add(CardPanel(row, 14));
        }
        content.Children.Add(seats);

        var create = PrimaryButton("Create table");
        create.HorizontalAlignment = HorizontalAlignment.Right;
        create.Click += async (_, _) => await CreateTableAsync(create);
        content.Children.Add(new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = "Partners are seats 1 + 3 and seats 2 + 4.",
                    Foreground = Muted,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Place(create, column: 1)
            }
        });

        return new ScrollViewer
        {
            Padding = new Thickness(30),
            Content = content
        };
    }

    private Control BuildGamePage()
    {
        var back = QuietButton("← New table");
        back.Click += async (_, _) => await LeaveTableAsync(back);

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 12 };
        header.Children.Add(back);
        header.Children.Add(Place(new StackPanel
        {
            Spacing = 1,
            Children =
            {
                Eyebrow("LIVE SESSION"),
                new TextBlock { Name = "NativeGameName", Text = _session?.Name ?? "Table", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 22, FontFamily = PixelArt.Font, Foreground = Ink }
            }
        }, column: 1));
        header.Children.Add(Place(Field("View hand", _viewerSeat), column: 2));
        var motionButton = QuietButton($"Motion: {(_motion.Enabled ? "On" : "Off")}");
        motionButton.FontSize = 10;
        motionButton.Click += async (_, _) =>
        {
            _motion.SetEnabled(!_motion.Enabled);
            if (!_motion.Enabled) _bidAnnouncement?.Cancel();
            _preferences = _preferences with { MotionEnabled = _motion.Enabled };
            if (!_preferences.Save(_preferencesPath)) ShowError("Motion changed for this session, but the preference could not be saved.");
            motionButton.Content = $"Motion: {(_motion.Enabled ? "On" : "Off")}";
            _renderKey = null;
            await RefreshGameAsync();
        };
        header.Children.Add(Place(motionButton, column: 3));
        header.Children.Add(Place(_startButton, column: 4));

        _bidAnnouncement?.Clear();
        var bidDock = new ContentControl { IsVisible = false };
        var bidOverlay = new Canvas { IsHitTestVisible = false };
        _bidAnnouncement = new BidAnnouncement(bidOverlay, _tableHost, bidDock);
        var handInfo = CardPanel(new StackPanel { Spacing = 8, Children = { _phaseText, _contractText, _dealerText } }, 12);
        var guide = CardPanel(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                GuideLine("HIGH", "Highest wins"), GuideLine("LOW", "Lowest wins"),
                GuideLine("TRUMP", "Follow suit. Trump wins."),
                GuideLine("PARTNER’S BEST", "3 players · exchange 1 card"),
                GuideLine("ALONE", "3 players · no partner")
            }
        }, 12);
        var score = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Eyebrow("TEAMS · TO 40"),
                ScorePanel("TEAM 1 · 1 + 3", _teamZeroScore, Green),
                ScorePanel("TEAM 2 · 2 + 4", _teamOneScore, Amber)
            }
        };

        var actionPanel = CardPanel(new StackPanel
        {
            Spacing = 9,
            Children =
            {
                Eyebrow("ACTION CENTER"),
                _actionTitle,
                _actionHelp,
                _actionButtons,
                _sessionError
            }
        });
        actionPanel.Padding = new Thickness(14);
        var logScroll = new ScrollViewer
        {
            MaxHeight = 150,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _eventList
        };
        var logPanel = CardPanel(new StackPanel
        {
            Spacing = 10,
            Children = { Eyebrow("TABLE LOG · EVERY CARD"), logScroll }
        });
        var rail = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                score, bidDock, actionPanel, handInfo,
                new Expander { Header = "CONTRACT GUIDE", Foreground = Ink, FontSize = 11, Content = guide },
                new Expander { Header = "TABLE LOG", Foreground = Ink, FontSize = 11, Content = logPanel }
            }
        };
        var gameGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("260,*"), ColumnSpacing = 14 };
        var railScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = rail
        };
        railScroll.ScrollChanged += (_, args) =>
        {
            if (args.OffsetDelta.X != 0 || args.OffsetDelta.Y != 0) _bidAnnouncement?.Cancel();
        };
        gameGrid.Children.Add(railScroll);
        gameGrid.Children.Add(Place(_tableHost, column: 1));
        // Keep a finite viewport: the board scales to the window instead of forcing
        // the primary action controls below a full-height, scrollable table.
        var page = new Grid
        {
            Margin = new Thickness(16), RowDefinitions = RowDefinitions.Parse("Auto,*"), RowSpacing = 12,
            Children = { header, Place(gameGrid, row: 1) }
        };
        return new Grid { Children = { page, bidOverlay } };
    }

    private Control BuildEnginePage()
    {
        var browse = QuietButton("Browse…");
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a BEUCI engine executable",
                AllowMultiple = false
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _engineExecutable.Text = path;
            }
        };
        var pathRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
        pathRow.Children.Add(_engineExecutable);
        pathRow.Children.Add(Place(browse, column: 1));

        var load = PrimaryButton("Handshake & load");
        load.Click += async (_, _) => await LoadEngineAsync(load);
        var form = CardPanel(new StackPanel
        {
            Spacing = 13,
            Children =
            {
                Eyebrow("EXTERNAL PROCESS"),
                new TextBlock { Text = "Load a BEUCI engine", FontSize = 25, FontFamily = PixelArt.Font },
                Field("Executable", pathRow),
                Field("Arguments", _engineArguments),
                new TextBlock
                {
                    Text = "For a .NET bot, choose dotnet as the executable and enter the full bot DLL path as the argument.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Muted,
                    FontSize = 12
                },
                load
            }
        });

        var catalog = CardPanel(new StackPanel
        {
            Spacing = 13,
            Children =
            {
                Eyebrow("ENGINE CATALOG"),
                new TextBlock { Text = "Available bots", FontSize = 25, FontFamily = PixelArt.Font },
                _engineList
            }
        });

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*"), ColumnSpacing = 14, MaxWidth = 1050, HorizontalAlignment = HorizontalAlignment.Center };
        grid.Children.Add(catalog);
        grid.Children.Add(Place(form, column: 1));
        return new ScrollViewer { Padding = new Thickness(30), Content = grid };
    }

    private static Control BuildRulesPage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "bid-euchre-final-rules.md");
        var rules = File.Exists(path) ? File.ReadAllText(path) : "Rules file was not found beside the executable.";
        return new ScrollViewer
        {
            Padding = new Thickness(30),
            Content = new Border
            {
                MaxWidth = 960,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(10),
                Background = Panel,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = rules,
                    Foreground = Ink,
                    FontFamily = FontFamily.Default,
                    FontSize = 14,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private async Task CreateTableAsync(Button button)
    {
        if (_tableTransitioning)
        {
            return;
        }

        _tableTransitioning = true;
        button.IsEnabled = false;
        ClearStatus();
        try
        {
            var previousSession = DetachSession();
            if (previousSession is not null)
            {
                await previousSession.DisposeAsync();
            }

            var engines = _catalog.List();
            var seats = Enumerable.Range(0, 4).Select(seat =>
            {
                var isBot = _seatKinds[seat].SelectedIndex == 1;
                var engineIndex = Math.Max(0, _seatEngines[seat].SelectedIndex);
                return new SeatConfiguration(
                    seat,
                    string.IsNullOrWhiteSpace(_seatNames[seat].Text) ? $"Player {seat + 1}" : _seatNames[seat].Text!.Trim(),
                    isBot ? PlayerKind.Bot : PlayerKind.Human,
                    isBot ? engines[engineIndex].Id : null);
            }).ToArray();
            var pace = _pace.SelectedItem as PaceOption ?? PaceOption.All[1];
            var session = new GameSession(
                Guid.NewGuid().ToString("N")[..10],
                string.IsNullOrWhiteSpace(_tableName.Text) ? "Bid Euchre Table" : _tableName.Text!.Trim(),
                seats,
                _catalog,
                seed: null,
                botActionDelay: TimeSpan.FromMilliseconds(pace.Milliseconds));
            _session = session;
            _sessionGeneration++;

            var viewers = seats.Where(seat => seat.Kind is PlayerKind.Human)
                .Select(seat => new ViewerOption(seat.Seat, $"Seat {seat.Seat + 1}: {seat.Name}"))
                .ToList();
            if (viewers.Count == 0)
            {
                viewers.Add(new ViewerOption(null, "Spectator"));
            }
            _viewerSeat.ItemsSource = viewers;
            _viewerSeat.SelectedIndex = 0;
            _viewer = viewers[0].Seat;
            _playHost.Content = BuildGamePage();
            _renderKey = null;
            await RefreshGameAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _tableTransitioning = false;
            button.IsEnabled = true;
        }
    }

    private async Task LeaveTableAsync(Button button)
    {
        if (_tableTransitioning)
        {
            return;
        }

        _tableTransitioning = true;
        button.IsEnabled = false;
        var session = DetachSession();
        string? shutdownError = null;
        try
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            shutdownError = $"The old table could not shut down cleanly: {exception.Message}";
        }
        finally
        {
            ShowSetup();
            if (shutdownError is not null)
            {
                ShowError(shutdownError);
            }
            _tableTransitioning = false;
        }
    }

    private async Task StartSessionAsync()
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null)
        {
            return;
        }
        ClearStatus();
        _startButton.IsEnabled = false;
        _startButton.Content = "Starting engines…";
        try
        {
            await session.StartAsync();
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                await RefreshGameAsync();
            }
        }
        catch (Exception exception)
        {
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                ShowError(exception.Message);
                _startButton.IsEnabled = true;
                _startButton.Content = "Start session";
            }
        }
    }

    private async Task RefreshGameAsync()
    {
        var session = _session;
        var generation = _sessionGeneration;
        var viewer = _viewer;
        var revision = _input.Revision;
        if (_refreshing || session is null)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var view = await session.GetViewAsync(viewer);
            if (generation != _sessionGeneration ||
                viewer != _viewer || revision != _input.Revision ||
                !ReferenceEquals(session, _session))
            {
                return;
            }
            if (view is null)
            {
                if (_renderKey == "waiting")
                {
                    return;
                }
                _renderKey = "waiting";
                _motionTracker.Update(null, $"{session.Id}:{viewer}");
                _bidAnnouncement?.Clear();
                _contractText.IsVisible = true;
                RenderWaitingTable();
                _input.CompleteView(revision);
                SyncNativeInput();
                return;
            }

            var renderKey = JsonSerializer.Serialize(view) + '\n' + (session.LastError ?? string.Empty);
            if (renderKey == _renderKey)
            {
                return;
            }
            _renderKey = renderKey;
            _motionCue = _motionTracker.Update(view, $"{session.Id}:{viewer}");

            _startButton.IsVisible = false;
            _teamZeroScore.Text = view.Scores[0].ToString();
            _teamOneScore.Text = view.Scores[1].ToString();
            _phaseText.Text = $"{SplitWords(view.Phase.ToString()).ToUpperInvariant()} · HAND {view.HandNumber}";
            _contractText.Text = ContractName(view);
            _dealerText.Text = $"Seat {view.Dealer + 1} deals · {view.TricksByTeam[0]}–{view.TricksByTeam[1]} tricks";
            _sessionError.Text = session.LastError ?? string.Empty;
            _tableHost.Content = BuildTable(view);
            foreach (var team in _motionCue.ScoreTeams) _motion.Pulse(team == 0 ? _teamZeroScore : _teamOneScore, 1.2);
            RenderActions(view);
            RenderEvents(view.Events);
            UpdateBidAnnouncement(view);
            _input.CompleteView(revision);
            SyncNativeInput();
        }
        catch (ObjectDisposedException)
        {
            // The user returned to setup while a timer refresh was finishing.
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateBidAnnouncement(GameView view)
    {
        var auction = view.Phase is GamePhase.Bidding or GamePhase.ChoosingContract;
        if (auction && view.HighBid is { } bid && view.Bidder is { } bidder)
        {
            _contractText.IsVisible = false;
            var player = view.Players.FirstOrDefault(player => player.Seat == bidder)?.Name ?? $"Seat {bidder + 1}";
            _bidAnnouncement?.Update(BidName(bid), player, _motionCue.HighBidChanged && _motion.Enabled);
        }
        else
        {
            _contractText.IsVisible = true;
            _bidAnnouncement?.Clear();
        }
    }

    private void SyncNativeInput()
    {
        _tableHost.IsVisible = !_input.Loading;
        _tableHost.IsEnabled = !_input.Locked;
        _actionButtons.IsEnabled = !_input.Locked;
        _viewerSeat.IsEnabled = !_input.Locked;
    }

    private Control BuildTable(GameView view)
    {
        return PixelTable(view.Players.Select(player => PlayerPanel(player, view)).ToArray(), TrickPanel(view));
    }

    // A fixed logical table scales as one unit, so the four seats never push
    // the trick or action controls out of place at the minimum window size.
    private static Control PixelTable(Control[] players, Control center)
    {
        var canvas = new Canvas { Width = 900, Height = 600 };
        for (var seat = 0; seat < 4; seat++)
        {
            var panel = players[seat];
            panel.Width = seat is 1 or 3 ? 190 : 410;
            Canvas.SetLeft(panel, seat switch { 1 => 0, 3 => 710, _ => 245 });
            Canvas.SetTop(panel, seat switch { 2 => 4, 0 => 438, _ => 175 });
            canvas.Children.Add(panel);
        }
        center.Width = 220;
        Canvas.SetLeft(center, 340);
        Canvas.SetTop(center, 175);
        canvas.Children.Add(center);
        return new Border
        {
            Background = PixelArt.Table, BorderBrush = Line, BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = canvas }
        };
    }

    private Control PlayerPanel(PlayerView player, GameView view)
    {
        var current = view.CurrentSeat == player.Seat;
        var tag = new Border
        {
            CornerRadius = new CornerRadius(2),
            Background = current ? Gold : player.Team == 0 ? GreenDark : Brush.Parse("#740D2D"),
            Padding = new Thickness(10, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { PixelArt.Portrait(player.Seat), new TextBlock
            {
                Text = $"{(current ? "▶ " : string.Empty)}{player.Name}{(view.Dealer == player.Seat ? "  D" : string.Empty)}{(player.IsSittingOut ? "  · sitting out" : string.Empty)}",
                Foreground = current ? Paper : Ink,
                MaxWidth = 130, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10,
                FontWeight = FontWeight.Bold
            } }
            }
        };
        if (_motionCue.WinnerSeat == player.Seat) _motion.Pulse(tag);
        else if (_motionCue.TurnSeat == player.Seat) _motion.Nudge(tag);
        var cards = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = player.Seat is 1 or 3 ? 140 : 410
        };
        if (player.Cards is null)
        {
            for (var index = 0; index < player.CardCount; index++)
            {
                var back = CardBack();
                cards.Children.Add(_motionCue.Deal ? _motion.Arrive(back, 0, index * 32) : back);
            }
        }
        else
        {
            var index = 0;
            foreach (var card in player.Cards)
            {
                var legal = current && view.LegalActions.Cards.Contains(card);
                var button = CardButton(card, legal, view);
                cards.Children.Add(_motionCue.Deal ? _motion.Arrive(button, 0, index * 32) : button);
                index++;
            }
        }

        return new StackPanel
        {
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 5,
            Opacity = player.IsSittingOut ? 0.5 : 1,
            Children = { tag, cards }
        };
    }

    private Control TrickPanel(GameView view)
    {
        IReadOnlyList<CardPlay> plays = view.CurrentTrick;
        CompletedTrick? completed = null;
        if (plays.Count == 0 && view.CompletedTricks.Count > 0)
        {
            completed = view.CompletedTricks[^1];
            plays = completed.Plays;
        }

        var cards = new Grid { Width = 188, Height = 234 };
        foreach (var play in plays)
        {
            var card = CardFace(play.Card);
            card.HorizontalAlignment = play.Seat switch { 1 => HorizontalAlignment.Left, 3 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            card.VerticalAlignment = play.Seat switch { 2 => VerticalAlignment.Top, 0 => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
            if (completed?.Winner == play.Seat)
            {
                card.BorderBrush = Gold;
                card.BorderThickness = new Thickness(3);
                if (_motionCue.WinnerSeat is not null) _motion.Pulse(card, 1.065);
            }
            if (_motionCue.AddedPlays.Contains($"{play.Seat}:{play.Card.Code}"))
            {
                // Keep the seat alignment on the wrapper, not its child.
                var animated = _motion.Arrive(card, play.Seat);
                animated.HorizontalAlignment = card.HorizontalAlignment;
                animated.VerticalAlignment = card.VerticalAlignment;
                cards.Children.Add(animated);
            }
            else cards.Children.Add(card);
        }

        var label = new TextBlock
        {
            Text = completed is null
                ? (plays.Count == 0 ? "Cards played here" : "Current trick")
                : $"{view.Players[completed.Winner].Name} won trick {completed.Number}",
            Foreground = completed is null ? Brush.Parse("#AFC4B8") : Gold,
            FontSize = 11,
            FontWeight = completed is null ? FontWeight.Normal : FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        return new Border
        {
            MinWidth = 190,
            MinHeight = 170,
            Padding = new Thickness(10),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children = { cards, label }
            }
        };
    }

    private void RenderActions(GameView view)
    {
        _actionButtons.Children.Clear();
        if (view.Phase is GamePhase.HandComplete)
        {
            _actionTitle.Text = "Hand complete";
            _actionHelp.Text = "The final trick stays on the table. Review every card in the log, then deal again.";
            var next = ActionButton("Deal next hand");
            next.Click += async (_, _) => await StartNextHandAsync(next);
            _actionButtons.Children.Add(next);
            return;
        }
        if (view.Phase is GamePhase.GameComplete)
        {
            _actionTitle.Text = $"Team {view.GameWinner + 1} wins";
            _actionHelp.Text = "The race to 40 is complete.";
            return;
        }
        if (view.CurrentSeat != _viewer)
        {
            var current = view.CurrentSeat is null ? null : view.Players[view.CurrentSeat.Value];
            var currentSeat = view.CurrentSeat is null ? null : _session?.Seats[view.CurrentSeat.Value];
            if (current is not null && currentSeat?.Kind is PlayerKind.Human)
            {
                _actionTitle.Text = $"Waiting for {current.Name}";
                _actionHelp.Text = $"Choose Seat {current.Seat + 1} in View hand to continue this hot-seat game.";
                _actionButtons.Children.Add(ActionStatus("Switch View hand to continue", false));
            }
            else
            {
                _actionTitle.Text = current is null ? SplitWords(view.Phase.ToString()) : $"{current.Name} is thinking";
                _actionHelp.Text = "Bot turns are deliberately paced so every action remains visible.";
                _actionButtons.Children.Add(ActionStatus("Bot turn in progress", true));
            }
            return;
        }

        switch (view.Phase)
        {
            case GamePhase.Bidding:
                _actionTitle.Text = "Make your bid";
                _actionHelp.Text = "Only announce the level. Choose High, Low, or trump after winning.";
                foreach (var bid in view.LegalActions.Bids)
                {
                    var capturedBid = bid;
                    AddAction(BidName(bid), () => SendActionAsync(new GameActionRequest(_viewer!.Value, "bid", capturedBid, null, null, null)));
                }
                if (view.LegalActions.CanPass)
                {
                    AddAction("Pass", () => SendActionAsync(new GameActionRequest(_viewer!.Value, "pass", null, null, null, null)));
                }
                break;
            case GamePhase.ChoosingContract:
                _actionTitle.Text = "Choose the contract";
                _actionHelp.Text = "Reveal the mode now that the auction is over.";
                if (view.LegalActions.ContractModes.Contains(ContractMode.High))
                {
                    AddAction("High", () => SendActionAsync(new GameActionRequest(_viewer!.Value, "contract", null, ContractMode.High, null, null)));
                }
                if (view.LegalActions.ContractModes.Contains(ContractMode.Low))
                {
                    AddAction("Low", () => SendActionAsync(new GameActionRequest(_viewer!.Value, "contract", null, ContractMode.Low, null, null)));
                }
                if (view.LegalActions.ContractModes.Contains(ContractMode.Trump))
                {
                    foreach (var suit in view.LegalActions.TrumpSuits)
                    {
                        var capturedSuit = suit;
                        AddAction($"{SuitSymbol(suit)} {suit}", () => SendActionAsync(new GameActionRequest(_viewer!.Value, "contract", null, ContractMode.Trump, capturedSuit, null)));
                    }
                }
                break;
            case GamePhase.ExchangingBidderCard:
                _actionTitle.Text = "Give one card";
                _actionHelp.Text = "Choose a highlighted card for your partner. The exchange is private.";
                _actionButtons.Children.Add(ActionStatus("Select a gold-outlined card", false));
                break;
            case GamePhase.ExchangingPartnerCard:
                _actionTitle.Text = "Return one card";
                _actionHelp.Text = "Choose a highlighted card to return to the bidder.";
                _actionButtons.Children.Add(ActionStatus("Select a gold-outlined card", false));
                break;
            case GamePhase.Playing:
                _actionTitle.Text = "Play a card";
                _actionHelp.Text = "Legal cards are outlined in gold. The table pauses after every bot card.";
                _actionButtons.Children.Add(ActionStatus("Select a gold-outlined card", false));
                break;
        }
    }

    private void RenderEvents(IReadOnlyList<string> events)
    {
        _eventList.Children.Clear();
        foreach (var item in events.Reverse().Take(40))
        {
            _eventList.Children.Add(new Border
            {
                BorderBrush = Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 7),
                Child = new TextBlock { Text = item, Foreground = Muted, FontFamily = FontFamily.Default, FontSize = 12, TextWrapping = TextWrapping.Wrap }
            });
        }
    }

    private async Task SendActionAsync(GameActionRequest request)
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null || !_input.TryBeginAction(out var revision)) return;
        SyncNativeInput();
        ClearStatus();
        try
        {
            await session.ExecuteAsync(request);
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                await RefreshGameAsync();
            }
        }
        catch (Exception exception)
        {
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                ShowError(exception.Message);
                await RefreshGameAsync();
            }
        }
        finally { _input.CompleteAction(revision); SyncNativeInput(); }
    }

    private async Task StartNextHandAsync(Button button)
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null || !_input.TryBeginAction(out var revision)) return;
        SyncNativeInput();
        button.IsEnabled = false;
        ClearStatus();
        try
        {
            await session.StartNextHandAsync();
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                _renderKey = null;
                await RefreshGameAsync();
            }
        }
        catch (Exception exception)
        {
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                ShowError(exception.Message);
                await RefreshGameAsync();
                button.IsEnabled = true;
            }
        }
        finally { _input.CompleteAction(revision); SyncNativeInput(); }
    }

    private async Task LoadEngineAsync(Button button)
    {
        if (string.IsNullOrWhiteSpace(_engineExecutable.Text))
        {
            ShowError("Choose an engine executable first.");
            return;
        }
        button.IsEnabled = false;
        button.Content = "Handshaking…";
        try
        {
            var engine = await _catalog.LoadAsync(_engineExecutable.Text.Trim(), _engineArguments.Text?.Trim());
            _engineExecutable.Text = string.Empty;
            _engineArguments.Text = string.Empty;
            RefreshEngineCatalog();
            _status.Foreground = Green;
            _status.Text = $"Loaded {engine.Name} by {engine.Author}.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = "Handshake & load";
        }
    }

    private void RefreshEngineCatalog()
    {
        var engines = _catalog.List();
        _engineList.Children.Clear();
        foreach (var engine in engines)
        {
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = engine.Name, FontWeight = FontWeight.Bold, Foreground = Ink },
                    new TextBlock { Text = $"by {engine.Author} · {(engine.IsBuiltIn ? "built in" : "BEUCI process")}", Foreground = Muted, FontSize = 11 }
                }
            });
            if (!engine.IsBuiltIn)
            {
                var remove = QuietButton("Remove");
                remove.Foreground = Red;
                remove.Click += (_, _) =>
                {
                    _catalog.Remove(engine.Id);
                    RefreshEngineCatalog();
                };
                row.Children.Add(Place(remove, column: 1));
            }
            _engineList.Children.Add(new Border
            {
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(12),
                Child = row
            });
        }

        foreach (var selector in _seatEngines.Where(selector => selector is not null))
        {
            var selected = selector.SelectedIndex;
            selector.ItemsSource = engines.Select(engine => engine.Name).ToArray();
            selector.SelectedIndex = Math.Clamp(selected, 0, engines.Count - 1);
        }
    }

    private Button CardButton(Card card, bool legal, GameView view)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        var button = new Button
        {
            Width = 62,
            Height = 88,
            Margin = new Thickness(2),
            Padding = new Thickness(3),
            Background = CardPaper,
            Foreground = red ? CardRed : CardInk,
            BorderBrush = legal ? Gold : Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(legal ? 3 : 1),
            CornerRadius = new CornerRadius(2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = legal,
            Focusable = legal,
            Opacity = 1,
            Content = PixelArt.CardContent(card)
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"{RankText(card.Rank)} of {card.Suit}");
        if (legal)
        {
            button.Cursor = new Cursor(StandardCursorType.Hand);
            var lift = new TranslateTransform();
            if (_motion.Enabled) lift.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.YProperty,
                    Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() }
            };
            button.RenderTransform = lift;
            button.PointerEntered += (_, _) => lift.Y = -6;
            button.PointerExited += (_, _) => { if (!button.IsFocused) lift.Y = 0; };
            button.GotFocus += (_, _) => lift.Y = -6;
            button.LostFocus += (_, _) => lift.Y = 0;
            button.Click += async (_, _) =>
            {
                var type = view.Phase is GamePhase.Playing ? "play" : "exchange";
                await SendActionAsync(new GameActionRequest(_viewer!.Value, type, null, null, null, card.Code));
            };
        }
        return button;
    }

    private static Border CardFace(Card card)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        return new Border
        {
            Width = 62,
            Height = 88,
            Margin = new Thickness(2),
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(2),
            BorderBrush = Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(1),
            Background = CardPaper,
            Child = PixelArt.CardContent(card)
        };
    }

    private static Border CardBack() => new()
    {
        Margin = new Thickness(1), BorderBrush = CardPaper, BorderThickness = new Thickness(2),
        Child = PixelArt.Back()
    };

    private void ShowSetup()
    {
        _renderKey = null;
        _playHost.Content = BuildSetupPage();
        _startButton.IsVisible = true;
        _startButton.IsEnabled = true;
        _startButton.Content = "Start session";
        ClearStatus();
        RefreshEngineCatalog();
    }

    private GameSession? DetachSession()
    {
        _bidAnnouncement?.Clear();
        _input.BeginViewChange();
        _motion.Cancel();
        var session = _session;
        if (session is not null)
        {
            _session = null;
            _sessionGeneration++;
            _renderKey = null;
        }

        return session;
    }

    private void RenderWaitingTable()
    {
        _startButton.IsVisible = true;
        _startButton.IsEnabled = true;
        _startButton.Content = "Start session";
        _teamZeroScore.Text = "0";
        _teamOneScore.Text = "0";
        _phaseText.Text = "WAITING TO START";
        _contractText.Text = "No contract";
        _dealerText.Text = "Dealer not chosen";
        _actionTitle.Text = "Table created";
        _actionHelp.Text = "Start the session when every seat is ready.";
        _actionButtons.Children.Clear();
        _eventList.Children.Clear();
        if (_session is not null)
        {
            var players = _session.Seats.Select(seat => new PlayerView(seat.Seat, seat.Name, seat.Seat % 2, 6, null, false)).ToArray();
            _tableHost.Content = BuildWaitingTable(players);
        }
    }

    private Control BuildWaitingTable(IReadOnlyList<PlayerView> players)
    {
        var panels = new Control[4];
        for (var seat = 0; seat < 4; seat++)
        {
            panels[seat] = new StackPanel
            {
                Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    PixelArt.Portrait(seat),
                    new TextBlock { Text = players[seat].Name, Foreground = Ink, FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center },
                    new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center,
                        MaxWidth = seat is 1 or 3 ? 140 : 350,
                        Children = { CardBack(), CardBack(), CardBack(), CardBack(), CardBack(), CardBack() } }
                }
            };
        }
        return PixelTable(panels, new TextBlock
        {
            Text = "Press Start session", Foreground = Gold, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 95, 0, 0)
        });
    }

    private void AddAction(string label, Func<Task> action)
    {
        var button = ActionButton(label);
        button.Click += async (_, _) => await action();
        _actionButtons.Children.Add(button);
    }

    private static Border ActionStatus(string text, bool busy) => new()
    {
        Margin = new Thickness(0, 2, 0, 5),
        Padding = new Thickness(11, 8),
        CornerRadius = new CornerRadius(2),
        Background = GreenDark,
        BorderBrush = Brush.Parse("#315E4D"),
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = $"{(busy ? "●" : "→")}  {text}",
            Foreground = busy ? Green : Ink,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        }
    };

    private static void StyleInput(TextBox input)
    {
        input.Background = Brush.Parse("#0D1513");
        input.Foreground = Ink;
        input.BorderBrush = Line;
        input.BorderThickness = new Thickness(1);
        input.CornerRadius = new CornerRadius(2);
        input.Padding = new Thickness(10, 7);
        input.MinHeight = 38;
    }

    private static void StyleInput(ComboBox input)
    {
        input.Background = Brush.Parse("#0D1513");
        input.Foreground = Ink;
        input.BorderBrush = Line;
        input.BorderThickness = new Thickness(1);
        input.CornerRadius = new CornerRadius(2);
        input.Padding = new Thickness(10, 7);
        input.MinHeight = 38;
    }

    private static Border Field(string label, Control control) => new()
    {
        Child = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = label.ToUpperInvariant(), Foreground = Muted, FontSize = 10, FontWeight = FontWeight.Bold },
                control
            }
        }
    };

    private static Border CardPanel(Control child, double padding = 20) => new()
    {
        Background = PanelRaised,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Padding = new Thickness(padding),
        Child = child
    };

    private static Control PageHeading(string title, string subtitle) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            Eyebrow("BID EUCHRE"),
            new TextBlock { Text = title, Foreground = Ink, FontSize = 43, FontFamily = PixelArt.Font },
            new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 14 }
        }
    };

    private static Control GuideLine(string title, string detail) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = title, Foreground = Gold, FontSize = 11 },
            new TextBlock { Text = detail, Foreground = Ink, FontSize = 11, TextWrapping = TextWrapping.Wrap }
        }
    };

    private static Border ScorePanel(string label, TextBlock score, IBrush accent) => CardPanel(new Grid
    {
        ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        Children =
        {
            new TextBlock { Text = label, Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
            Place(score, column: 1)
        }
    }, 13);

    private static TextBlock ScoreText() => new()
    {
        Text = "0",
        Foreground = Ink,
        FontSize = 32,
        FontFamily = PixelArt.Font
    };

    private static TextBlock Eyebrow(string text) => new()
    {
        Text = text,
        Foreground = Green,
        FontSize = 10,
        FontWeight = FontWeight.Bold
    };

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Background = Green,
        Foreground = Paper,
        Padding = new Thickness(16, 9),
        CornerRadius = new CornerRadius(2),
        BorderThickness = new Thickness(0),
        FontWeight = FontWeight.Bold
    };

    private static Button QuietButton(string text) => new()
    {
        Content = text,
        Background = Panel,
        Foreground = Ink,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(13, 8),
        CornerRadius = new CornerRadius(2)
    };

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 0, 7, 7),
        Padding = new Thickness(12, 8),
        Background = Brush.Parse("#143480"),
        Foreground = Brush.Parse("#FFF5DC"),
        BorderBrush = Brush.Parse("#65BFFF"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        FontWeight = FontWeight.Bold,
        FontSize = 12
    };

    private static T Place<T>(T control, int row = 0, int column = 0) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private void ShowError(string message)
    {
        _status.Foreground = Red;
        _status.Text = message;
    }

    private void ClearStatus() => _status.Text = string.Empty;

    private static string ContractName(GameView view)
    {
        if (view.Contract is null)
        {
            return view.HighBid is null ? "Auction open" : $"{BidName(view.HighBid.Value)} high bid";
        }
        var prefix = BidName(view.Contract.Bid);
        return view.Contract.Mode is ContractMode.Trump
            ? $"{prefix} · {SuitSymbol(view.Contract.Trump!.Value)} {view.Contract.Trump}"
            : $"{prefix} · {view.Contract.Mode}";
    }

    private static string BidName(BidLevel bid) => bid switch
    {
        BidLevel.PartnersBest => "Partners Best",
        BidLevel.Alone => "Alone",
        _ => ((int)bid).ToString()
    };

    private static string RankText(Rank rank) => Card.DisplayRank(rank);

    private static string SuitSymbol(Suit suit) => suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts => "♥",
        Suit.Spades => "♠",
        _ => "?"
    };

    private static string SplitWords(string value)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            if (result.Length > 0 && char.IsUpper(character))
            {
                result.Append(' ');
            }
            result.Append(character);
        }
        return result.ToString();
    }

    private sealed record PaceOption(string Name, int Milliseconds)
    {
        public static PaceOption[] All { get; } =
        [
            new("Relaxed · 1.5 seconds", 1500),
            new("Normal · 1 second", 1000),
            new("Quick · 0.6 seconds", 600)
        ];
        public override string ToString() => Name;
    }

    private sealed record ViewerOption(int? Seat, string Name)
    {
        public override string ToString() => Name;
    }
}
