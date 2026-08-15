using Avalonia;
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
    private static readonly IBrush Paper = Brush.Parse("#0A0F0E");
    private static readonly IBrush Panel = Brush.Parse("#111816");
    private static readonly IBrush PanelRaised = Brush.Parse("#17211E");
    private static readonly IBrush Ink = Brush.Parse("#F1F7F4");
    private static readonly IBrush Muted = Brush.Parse("#A8B9B2");
    private static readonly IBrush Green = Brush.Parse("#62D0A2");
    private static readonly IBrush GreenDark = Brush.Parse("#0C2A20");
    private static readonly IBrush Felt = Brush.Parse("#0F3A2D");
    private static readonly IBrush Amber = Brush.Parse("#E7B563");
    private static readonly IBrush Gold = Brush.Parse("#F1C76F");
    private static readonly IBrush Line = Brush.Parse("#2B3D37");
    private static readonly IBrush Red = Brush.Parse("#FF837A");
    private static readonly IBrush Walnut = Brush.Parse("#456154");
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
    private readonly TextBlock _phaseText = new() { Foreground = Amber, FontWeight = FontWeight.Bold, FontSize = 12 };
    private readonly TextBlock _contractText = new() { Foreground = Ink, FontSize = 19, FontFamily = new FontFamily("Georgia") };
    private readonly TextBlock _dealerText = new() { Foreground = Muted, FontSize = 12 };
    private readonly ComboBox _viewerSeat = new() { MinWidth = 180 };
    private readonly Button _startButton = PrimaryButton("Start session");
    private readonly ContentControl _tableHost = new();
    private readonly TextBlock _actionTitle = new() { Foreground = Ink, FontSize = 21, FontFamily = new FontFamily("Georgia") };
    private readonly TextBlock _actionHelp = new() { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
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

    public MainWindow()
    {
        Title = "Bid Euchre";
        Width = 1320;
        Height = 860;
        MinWidth = 980;
        MinHeight = 680;
        Background = Paper;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

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

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        header.Children.Add(back);
        header.Children.Add(Place(new StackPanel
        {
            Spacing = 1,
            Children =
            {
                Eyebrow("LIVE SESSION"),
                new TextBlock { Name = "NativeGameName", Text = _session?.Name ?? "Table", FontSize = 26, FontFamily = new FontFamily("Georgia"), Foreground = Ink }
            }
        }, column: 1));
        header.Children.Add(Place(Field("View hand", _viewerSeat), column: 2));
        header.Children.Add(Place(_startButton, column: 3));

        var score = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,1.4*,*"),
            ColumnSpacing = 12,
            Children =
            {
                ScorePanel("TEAM 1 · SEATS 1 + 3", _teamZeroScore, Green),
                Place(CardPanel(new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 2,
                    Children = { _phaseText, _contractText, _dealerText }
                }, 13), column: 1),
                Place(ScorePanel("TEAM 2 · SEATS 2 + 4", _teamOneScore, Amber), column: 2)
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
        actionPanel.MinHeight = 220;
        var logScroll = new ScrollViewer
        {
            MaxHeight = 380,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _eventList
        };
        var logPanel = CardPanel(new StackPanel
        {
            Spacing = 10,
            Children = { Eyebrow("TABLE LOG · EVERY CARD"), logScroll }
        });
        var rightRail = new StackPanel { Spacing = 12, Children = { actionPanel, logPanel } };

        var gameGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,310"),
            ColumnSpacing = 14,
            MinWidth = 884
        };
        gameGrid.Children.Add(_tableHost);
        gameGrid.Children.Add(Place(rightRail, column: 1));

        var page = new Grid
        {
            Margin = new Thickness(18),
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*"),
            RowSpacing = 12,
            Children =
            {
                header,
                Place(score, row: 1),
                Place(gameGrid, row: 2)
            }
        };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = page
        };
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
                new TextBlock { Text = "Load a BEUCI engine", FontSize = 25, FontFamily = new FontFamily("Georgia") },
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
                new TextBlock { Text = "Available bots", FontSize = 25, FontFamily = new FontFamily("Georgia") },
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
        if (_refreshing || session is null)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var view = await session.GetViewAsync(viewer);
            if (generation != _sessionGeneration ||
                viewer != _viewer ||
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
                RenderWaitingTable();
                return;
            }

            var renderKey = JsonSerializer.Serialize(view) + '\n' + (session.LastError ?? string.Empty);
            if (renderKey == _renderKey)
            {
                return;
            }
            _renderKey = renderKey;

            _startButton.IsVisible = false;
            _teamZeroScore.Text = view.Scores[0].ToString();
            _teamOneScore.Text = view.Scores[1].ToString();
            _phaseText.Text = $"{SplitWords(view.Phase.ToString()).ToUpperInvariant()} · HAND {view.HandNumber}";
            _contractText.Text = ContractName(view);
            _dealerText.Text = $"Seat {view.Dealer + 1} deals · {view.TricksByTeam[0]}–{view.TricksByTeam[1]} tricks";
            _sessionError.Text = session.LastError ?? string.Empty;
            _tableHost.Content = BuildTable(view);
            RenderActions(view);
            RenderEvents(view.Events);
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

    private Control BuildTable(GameView view)
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            MinHeight = 510,
            Background = Felt
        };
        grid.Children.Add(Place(PlayerPanel(view.Players[2], view), row: 0, column: 1));
        grid.Children.Add(Place(PlayerPanel(view.Players[1], view), row: 1, column: 0));
        grid.Children.Add(Place(PlayerPanel(view.Players[3], view), row: 1, column: 2));
        grid.Children.Add(Place(PlayerPanel(view.Players[0], view), row: 2, column: 1));
        grid.Children.Add(Place(TrickPanel(view), row: 1, column: 1));

        return new Border
        {
            CornerRadius = new CornerRadius(76),
            Background = Brush.Parse("#0B1311"),
            Padding = new Thickness(8),
            Child = new Border
            {
                CornerRadius = new CornerRadius(68),
                BorderBrush = Walnut,
                BorderThickness = new Thickness(2),
                Background = Felt,
                Padding = new Thickness(18),
                Child = grid
            }
        };
    }

    private Control PlayerPanel(PlayerView player, GameView view)
    {
        var current = view.CurrentSeat == player.Seat;
        var tag = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = current ? Gold : GreenDark,
            Padding = new Thickness(10, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{(current ? "▶ " : string.Empty)}{player.Name}{(view.Dealer == player.Seat ? "  D" : string.Empty)}{(player.IsSittingOut ? "  · sitting out" : string.Empty)}",
                Foreground = current ? Paper : Ink,
                FontSize = 11,
                FontWeight = FontWeight.Bold
            }
        };
        var cards = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = player.Seat is 1 or 3 ? 120 : 430
        };
        if (player.Cards is null)
        {
            for (var index = 0; index < player.CardCount; index++)
            {
                cards.Children.Add(CardBack());
            }
        }
        else
        {
            foreach (var card in player.Cards)
            {
                var legal = current && view.LegalActions.Cards.Contains(card);
                cards.Children.Add(CardButton(card, legal, view));
            }
        }

        return new StackPanel
        {
            Margin = new Thickness(8),
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

        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var play in plays)
        {
            cards.Children.Add(new StackPanel
            {
                Margin = new Thickness(3),
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = $"Seat {play.Seat + 1}", Foreground = Brush.Parse("#C8D6C5"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center },
                    CardFace(play.Card)
                }
            });
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
            MinWidth = 280,
            MinHeight = 170,
            Padding = new Thickness(10),
            Background = Brush.Parse("#0B2A21"),
            BorderBrush = Brush.Parse("#34745C"),
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
                Child = new TextBlock { Text = item, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap }
            });
        }
    }

    private async Task SendActionAsync(GameActionRequest request)
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null)
        {
            return;
        }
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
            }
        }
    }

    private async Task StartNextHandAsync(Button button)
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null)
        {
            return;
        }

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
                button.IsEnabled = true;
            }
        }
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
                CornerRadius = new CornerRadius(7),
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
            Width = 52,
            Height = 72,
            Margin = new Thickness(2),
            Padding = new Thickness(3),
            Background = CardPaper,
            Foreground = red ? CardRed : CardInk,
            BorderBrush = legal ? Gold : Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(legal ? 3 : 1),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsHitTestVisible = legal,
            Focusable = legal,
            Opacity = 1,
            Content = CardLabel(card, red ? CardRed : CardInk)
        };
        if (legal)
        {
            button.Cursor = new Cursor(StandardCursorType.Hand);
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
            Width = 52,
            Height = 72,
            Margin = new Thickness(2),
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(1),
            Background = CardPaper,
            Child = CardLabel(card, red ? CardRed : CardInk)
        };
    }

    private static TextBlock CardLabel(Card card, IBrush? foreground = null) => new()
    {
        Text = $"{RankText(card.Rank)}\n{SuitSymbol(card.Suit)}",
        Foreground = foreground,
        TextAlignment = TextAlignment.Center,
        FontFamily = new FontFamily("Georgia"),
        FontSize = 19,
        FontWeight = FontWeight.Bold,
        LineHeight = 24,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border CardBack() => new()
    {
        Width = 34,
        Height = 50,
        Margin = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        BorderBrush = Brush.Parse("#6BE0AF"),
        BorderThickness = new Thickness(2),
        Background = Brush.Parse("#173F33")
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
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            MinHeight = 510,
            Background = Felt
        };
        for (var seat = 0; seat < 4; seat++)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(8),
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = players[seat].Name, Foreground = Brushes.White, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    new WrapPanel { Children = { CardBack(), CardBack(), CardBack(), CardBack(), CardBack(), CardBack() } }
                }
            };
            var (row, column) = seat switch { 0 => (2, 1), 1 => (1, 0), 2 => (0, 1), _ => (1, 2) };
            grid.Children.Add(Place(panel, row, column));
        }
        grid.Children.Add(Place(new TextBlock
        {
            Text = "Press Start session",
            Foreground = Brush.Parse("#C8D6C5"),
            FontSize = 18,
            FontFamily = new FontFamily("Georgia"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, 1, 1));
        return new Border
        {
            CornerRadius = new CornerRadius(76),
            Background = Brush.Parse("#0B1311"),
            Padding = new Thickness(8),
            Child = new Border
            {
                CornerRadius = new CornerRadius(68),
                BorderBrush = Walnut,
                BorderThickness = new Thickness(2),
                Background = Felt,
                Padding = new Thickness(18),
                Child = grid
            }
        };
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
        CornerRadius = new CornerRadius(7),
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
        input.CornerRadius = new CornerRadius(7);
        input.Padding = new Thickness(10, 7);
        input.MinHeight = 38;
    }

    private static void StyleInput(ComboBox input)
    {
        input.Background = Brush.Parse("#0D1513");
        input.Foreground = Ink;
        input.BorderBrush = Line;
        input.BorderThickness = new Thickness(1);
        input.CornerRadius = new CornerRadius(7);
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
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = child
    };

    private static Control PageHeading(string title, string subtitle) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            Eyebrow("BID EUCHRE"),
            new TextBlock { Text = title, Foreground = Ink, FontSize = 43, FontFamily = new FontFamily("Georgia") },
            new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 14 }
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
        FontFamily = new FontFamily("Georgia")
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
        CornerRadius = new CornerRadius(8),
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
        CornerRadius = new CornerRadius(8)
    };

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 0, 7, 7),
        Padding = new Thickness(12, 8),
        Background = Brush.Parse("#153128"),
        Foreground = Brush.Parse("#C7F5E1"),
        BorderBrush = Brush.Parse("#38715C"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
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
