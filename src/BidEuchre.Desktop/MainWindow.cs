using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
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
    internal static readonly IBrush Paper = Brush.Parse("#0B0720");
    internal static readonly IBrush Panel = Brush.Parse("#100B28");
    internal static readonly IBrush PanelRaised = Brush.Parse("#191039");
    internal static readonly IBrush Ink = Brush.Parse("#FFF5DC");
    internal static readonly IBrush Muted = Brush.Parse("#C0B6DD");
    internal static readonly IBrush Green = Brush.Parse("#55BCFF");
    private static readonly IBrush GreenDark = Brush.Parse("#103480");
    private static readonly IBrush Felt = Brush.Parse("#100B28");
    private static readonly IBrush Amber = Brush.Parse("#FF617B");
    internal static readonly IBrush Gold = Brush.Parse("#FFDB62");
    internal static readonly IBrush Line = Brush.Parse("#8D54B1");
    private static readonly IBrush Red = Brush.Parse("#FF617B");
    private static readonly IBrush Walnut = Brush.Parse("#8D54B1");
    private static readonly IBrush CardPaper = Brushes.White;
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
    private readonly TextBlock _contractText = new() { Foreground = Ink, FontSize = 17, FontFamily = PixelArt.Font };
    private readonly TextBlock _dealerText = new() { Foreground = Muted, FontSize = 10 };
    private readonly ComboBox _viewerSeat = new() { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _startButton = PrimaryButton("Start session");
    private readonly ContentControl _tableHost = new();
    // The one-line turn status in the top strip; the centre prompt repeats it above its buttons.
    private readonly TextBlock _actionTitle = new() { Foreground = Muted, FontSize = 11, FontFamily = PixelArt.Font, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _centerTitle = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Ink, FontSize = 18, FontFamily = PixelArt.Font };
    private readonly TextBlock _actionHelp = new() { FontFamily = FontFamily.Default, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly WrapPanel _actionButtons = new() { ItemWidth = double.NaN, ItemHeight = double.NaN, HorizontalAlignment = HorizontalAlignment.Center };
    // Bids and other choices sit over the middle of the board while they are open.
    private readonly Border _centerPanel;
    private readonly StackPanel _jokerRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _tarotRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private string? _inventoryKey;
    // Errors lie over the top edge of the board, so showing one never resizes it.
    private readonly TextBlock _sessionError = new()
    {
        Foreground = Red, Background = Paper, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false,
        Padding = new Thickness(10, 6), Margin = new Thickness(12), IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top
    };
    private const double StripHeight = 104;
    // Holds the table, or the Engines or Rules page opened from the main menu.
    private readonly ContentControl _pageHost = new();
    private readonly Control _enginePage;
    private readonly Control _rulesPage;
    private readonly ContentControl _screenHost = new();
    private readonly LayoutTransformControl _scaleHost = new();
    private readonly Grid _tableScreen;

    private GameSession? _session;
    private int? _viewer;
    private bool _refreshing;
    private bool _tableTransitioning;
    private long _sessionGeneration;
    private string? _renderKey;
    // Hand order. Campaign hands keep their order in the engine (Sticky Fingers reads it); free play
    // keeps it here. A dragged hand is left alone until the next deal or a Sort button press.
    private string? _autoArranged;
    private string? _draggedDeal;
    private string? _freePlayOrderHand;
    private readonly Dictionary<int, List<string>> _freePlayOrder = [];
    private readonly TableMotionTracker _motionTracker = new();
    private readonly PixelMotion _motion = new();
    private BidAnnouncement? _bidAnnouncement;
    private CardFlights? _cardFlights;
    private DeckDeal? _deckDeal;
    private HandGlide? _handGlide;
    private readonly TrickCollectionState _trickCollection = new();
    private readonly TableInputState _input = new();
    private TextBlock? _campaignStrip;
    private static string DataDirectory => Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _preferencesPath = Path.Combine(DataDirectory, "BidEuchre", "preferences.json");
    private GamePreferences _preferences = new();
    private readonly string _campaignPath = Path.Combine(DataDirectory, "BidEuchre", "campaign.json");
    private readonly CampaignFlow _campaign;
    private readonly Button _menuButton = QuietButton(MenuLabel);
    // The live session when it is a campaign match; free-play tables leave this null.
    private GameSession? _campaignSession;
    private TextBlock _scoreEyebrow = Eyebrow("TEAMS · TO 40");
    private const string MenuLabel = "← Menu";
    private const string ForfeitLabel = "Forfeit match?";
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
        SizeChanged += (_, _) => { _bidAnnouncement?.Cancel(); _cardFlights?.Cancel(); _deckDeal?.Cancel(); _handGlide?.Reset(); };
        _preferences = GamePreferences.Load(_preferencesPath);
        ApplyGraphics();
        _centerPanel = new Border
        {
            IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 470, Padding = new Thickness(16, 14, 16, 8), CornerRadius = new CornerRadius(2),
            Background = Brush.Parse("#EE100B28"), BorderBrush = Gold, BorderThickness = new Thickness(2),
            Child = new StackPanel { Spacing = 9, Children = { _centerTitle, _actionHelp, _actionButtons } }
        };
        _campaign = new CampaignFlow(_campaignPath, screen => _screenHost.Content = screen,
            StartCampaignMatchAsync, ShowMenu, _motion);

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
                _cardFlights?.Cancel(); _deckDeal?.Cancel();
                SyncNativeInput();
                _viewer = option.Seat;
                _renderKey = null;
                await RefreshGameAsync();
            }
        };
        _startButton.Click += async (_, _) => await StartSessionAsync();

        _enginePage = BuildEnginePage();
        _rulesPage = BuildRulesPage();
        _pageHost.Content = _playHost;

        var menuButton = _menuButton;
        menuButton.Name = "TableBackToMenu";
        menuButton.FontSize = 11;
        menuButton.HorizontalAlignment = HorizontalAlignment.Right;
        menuButton.VerticalAlignment = VerticalAlignment.Top;
        menuButton.Margin = new Thickness(0, 6, 20, 0);
        menuButton.Click += async (_, _) => await ReturnToMenuAsync(menuButton);
        _tableScreen = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Children =
            {
                _pageHost,
                menuButton,
                Place(_status, row: 1)
            }
        };
        _status.Margin = new Thickness(20, 5, 20, 10);
        _status.IsVisible = false;
        _scaleHost.Child = _screenHost;
        Content = _scaleHost;
        ShowMenu();

        ShowSetup();
        RefreshEngineCatalog();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _refreshTimer.Tick += async (_, _) => await RefreshGameAsync();
        _refreshTimer.Start();

        Closed += async (_, _) =>
        {
            _refreshTimer.Stop();
            _deckDeal?.Cancel();
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
        // These inputs outlive each page; release them from the previous page before reuse.
        foreach (var input in new Control[] { _tableName, _pace }) (input.Parent as Panel)?.Children.Remove(input);
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
        // These controls survive sessions; detached pages still own their logical children.
        // Release each immediate parent before constructing the next table page.
        foreach (var control in new Control[]{_startButton,_viewerSeat,_contractText,_dealerText,
            _scoreEyebrow,_teamZeroScore,_teamOneScore,_actionTitle,_sessionError,_centerPanel,_jokerRow,_tarotRow,_tableHost})
        {
            switch (control.Parent)
            {
                case Panel panel: panel.Children.Remove(control); break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentControl content: content.Content = null; break;
            }
        }
        // A campaign match has fixed seats and can only be left through the menu's forfeit prompt.
        var campaign = InCampaignMatch;
        _campaignStrip = campaign
            ? new TextBlock { Name = "CampaignStrip", Text = _campaign.Strip, Foreground = Gold, FontSize = 11, FontWeight = FontWeight.Bold }
            : null;

        _bidAnnouncement?.Clear();
        var bidDock = new ContentControl { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var bidOverlay = new Canvas { IsHitTestVisible = false };
        _bidAnnouncement = new BidAnnouncement(bidOverlay, _tableHost, bidDock);
        _cardFlights?.Cancel(); _deckDeal?.Cancel();
        _cardFlights = new CardFlights(bidOverlay);
        _deckDeal = new DeckDeal(bidOverlay, _tableHost, SyncNativeInput);
        _handGlide = new HandGlide(bidOverlay);
        _inventoryKey = null;
        RenderInventory(null);

        static Control Slots(string label, Control row) => new StackPanel
        {
            Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Eyebrow(label), row }
        };
        // Fixed sizes throughout the strip: a bid arriving or a status changing must not move the board.
        var status = new StackPanel
        {
            Spacing = 4, Width = 250, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _campaignStrip ?? new TextBlock { Name = "NativeGameName", Text = _session?.Name ?? "Table", Foreground = Green,
                    FontSize = 10, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
                _contractText, _actionTitle
            }
        };
        var score = new StackPanel
        {
            Name = "TableScore", Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { (_scoreEyebrow = Eyebrow("TEAMS · TO 40")), _dealerText } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { ScorePanel("TEAM 1", _teamZeroScore, Green), ScorePanel("TEAM 2", _teamOneScore, Amber) }
                }
            }
        };
        var inventory = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Children =
            {
                Slots("JOKERS", _jokerRow), Slots("CARDS", _tarotRow), status,
                new Border { Width = 128, Height = StripHeight, Child = bidDock }
            }
        };
        // Only a hot-seat table has more than one hand to look at.
        if (!campaign && _viewerSeat.ItemCount > 1) inventory.Children.Add(_viewerSeat);
        // The right margin keeps the strip clear of the menu button in the window's corner.
        // A narrow window shrinks the left of the strip rather than pushing the score off it.
        var strip = new Grid
        {
            Name = "TableStrip", ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 14,
            Height = StripHeight, Margin = new Thickness(4, 0, 170, 0),
            Children =
            {
                new Viewbox
                {
                    Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left, Child = inventory
                },
                Place(score, column: 1)
            }
        };

        // Keep a finite viewport: the board scales to the window, and takes all of it below the strip.
        var page = new Grid
        {
            Margin = new Thickness(8, 6, 8, 0),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 6,
            Children =
            {
                strip,
                Place(new Grid { Children = { _tableHost, _centerPanel, _sessionError } }, row: 1)
            }
        };
        return new Grid { Children = { page, bidOverlay } };
    }

    // Illustrated campaign cards retain their names and rules on hover.
    private void RenderInventory(CampaignGameView? campaign)
    {
        var usable = campaign is not null && CampaignTableControls.ItemsUsable(campaign);
        // A usable card sends the view's revision, so it is rebuilt whenever that moves.
        var key = campaign is null ? "" : JsonSerializer.Serialize(campaign.Inventory) + (usable ? $":{campaign.Revision}" : "");
        if (key == _inventoryKey) return;
        _inventoryKey = key;
        CampaignTableControls.Fill(_jokerRow, CampaignTableControls.JokerSlots,
            campaign?.Inventory.Jokers.Select((joker, slot) => _motion.Float(CampaignTableControls.Joker(joker), 40 + slot)) ?? []);
        CampaignTableControls.Fill(_tarotRow, CampaignTableControls.ItemSlots,
            campaign is null ? [] : CampaignTableControls.Items(campaign.Inventory).Select((item, index) => _motion.Float(
                CampaignTableControls.Item(campaign, item.Category, item.Slot, usable, command => SendCampaignActionAsync(campaign, command)), 50 + index)));
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
        return new ScrollViewer { Padding = new Thickness(30, 54, 30, 30), Content = grid };
    }

    private static Control BuildRulesPage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "bid-euchre-final-rules.md");
        var rules = File.Exists(path) ? File.ReadAllText(path) : "Rules file was not found beside the executable.";
        return new ScrollViewer
        {
            Padding = new Thickness(30, 54, 30, 30),
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
            await OpenSessionAsync(seats,
                string.IsNullOrWhiteSpace(_tableName.Text) ? "Bid Euchre Table" : _tableName.Text!.Trim(), options: null);
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

    private bool InCampaignMatch => _session is not null && ReferenceEquals(_session, _campaignSession);

    private async Task<GameSession> OpenSessionAsync(IReadOnlyList<SeatConfiguration> seats, string name, MatchOptions? options)
    {
        var pace = _pace.SelectedItem as PaceOption ?? PaceOption.All[1];
        var session = new GameSession(
            Guid.NewGuid().ToString("N")[..10],
            name,
            seats,
            _catalog,
            seed: null,
            botActionDelay: TimeSpan.FromMilliseconds(pace.Milliseconds),
            matchOptions: options,
            campaign: options is null ? null : new CampaignContext(_campaign.Run!.Resources),
            checkpoint: options is null ? null : _campaign.Checkpoint);
        _session = session;
        _campaignSession = options is null ? null : session;
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
        return session;
    }

    // Opens and starts one campaign match. Throws when the table cannot start, so the run is not charged for it.
    private async Task StartCampaignMatchAsync(MatchOptions options)
    {
        if (_tableTransitioning) throw new InvalidOperationException("The table is busy.");
        _tableTransitioning = true;
        try
        {
            ClearStatus();
            var previous = DetachSession();
            if (previous is not null) await previous.DisposeAsync();

            SeatConfiguration Bot(int seat, string name) => new(seat, name, PlayerKind.Bot, EngineCatalog.BuiltInEngineId);
            var session = await OpenSessionAsync(
                [new SeatConfiguration(0, "You", PlayerKind.Human, null), Bot(1, "West"), Bot(2, "Partner"), Bot(3, "East")],
                "Campaign", options);
            _menuButton.Content = MenuLabel;
            ShowTable();
            await StartSessionAsync();
            if (!session.Started)
            {
                if (ReferenceEquals(DetachSession(), session)) await session.DisposeAsync();
                ShowSetup();
                throw new InvalidOperationException("The campaign match could not start.");
            }
        }
        finally
        {
            _tableTransitioning = false;
        }
    }

    // Records the result at once, then swaps to the result screen after the last trick has been seen.
    private void CompleteCampaignMatch(GameSession session, GameView view)
    {
        _campaign.MatchCompleted(view.GameWinner == 0, view.Scores);
        DispatcherTimer.RunOnce(async () =>
        {
            if (!ReferenceEquals(session, _session)) return;
            DetachSession();
            try { await session.DisposeAsync(); }
            catch (Exception exception) { ShowError(exception.Message); }
            ShowSetup();
            _campaign.ShowPendingResult();
        }, TimeSpan.FromMilliseconds(2200));
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
            await session.StartAsync(waitForDeal: _motion.Enabled);
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
        // Redrawing would drop a card the player is holding; the drop refreshes the table itself.
        if (_refreshing || session is null || HandDrag.Active)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var view = await session.GetViewAsync(viewer);
            if (view?.Campaign is { } arranging && await AutoArrangeCampaignHandAsync(session, view, arranging))
                view = await session.GetViewAsync(viewer);
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
                _cardFlights?.Cancel(); _deckDeal?.Cancel();
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
            var lastTrick = view.CompletedTricks.LastOrDefault();
            var motionEnabled = _motion.Enabled && TableShown;
            var collectTrick = _trickCollection.Observe($"{session.Id}:{viewer}", view.HandNumber,
                lastTrick?.Number ?? 0, _motionCue.WinnerSeat is not null && view.CurrentTrick.Count == 0, motionEnabled);

            _startButton.IsVisible = false;
            if (view.Campaign is not null && _campaignStrip is not null) _campaignStrip.Text = _campaign.Strip;
            _teamZeroScore.Text = view.Scores[0].ToString();
            _teamOneScore.Text = view.Scores[1].ToString();
            _scoreEyebrow.Text = view.Campaign is null ? $"TEAMS · TO {view.TargetScore}" : $"TEAMS · ±{view.TargetScore} · 8 HANDS";
            _contractText.Text = ContractName(view);
            _dealerText.Text = $"HAND {view.HandNumber} · TRICKS {view.TricksByTeam[0]}–{view.TricksByTeam[1]}";
            _sessionError.Text = session.LastError ?? string.Empty;
            _sessionError.IsVisible = _sessionError.Text.Length > 0;
            RenderInventory(view.Campaign);
            if (!motionEnabled || view.Phase != GamePhase.Bidding) _deckDeal?.Cancel();
            _deckDeal?.BeginFrame();
            _cardFlights?.BeginFrame();
            _handGlide?.BeginFrame($"{session.Id}:{viewer}:{view.HandNumber}");
            _tableHost.Content = BuildTable(view);
            if (motionEnabled && _motionCue.Deal && view.Phase == GamePhase.Bidding)
                _deckDeal?.Start(view.Dealer, () => _ = FinishDealAsync(session, view.HandNumber));
            else if (session.WaitingForDeal && _deckDeal?.Running != true)
                _ = FinishDealAsync(session, view.HandNumber);
            var collectionKey = _trickCollection.Key;
            _cardFlights?.EndFrame(motionEnabled, collectTrick ? lastTrick?.Winner : null,
                collectTrick ? () =>
                {
                    _trickCollection.Complete(collectionKey);
                    _renderKey = null;
                    Dispatcher.UIThread.Post(async () => await RefreshGameAsync());
                }
            : null);
            foreach (var team in _motionCue.ScoreTeams) _motion.Pulse(team == 0 ? _teamZeroScore : _teamOneScore, 1.2);
            RenderActions(view);
            UpdateBidAnnouncement(view);
            _input.CompleteView(revision);
            SyncNativeInput();
            if (view.Phase is GamePhase.GameComplete && _campaign.InMatch && ReferenceEquals(session, _campaignSession))
            {
                CompleteCampaignMatch(session, view);
            }
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

    private async Task FinishDealAsync(GameSession session, int handNumber)
    {
        try
        {
            await session.CompleteDealAsync(handNumber);
            if (!ReferenceEquals(session, _session)) return;
            _renderKey = null;
            await RefreshGameAsync();
            SyncNativeInput();
        }
        catch (ObjectDisposedException) { }
    }

    private void SyncNativeInput()
    {
        _tableHost.IsVisible = !_input.Loading;
        _tableHost.IsEnabled = !_input.Locked && _cardFlights?.Collecting != true && _deckDeal?.Running != true && _session?.WaitingForDeal != true;
        _actionButtons.IsEnabled = !_input.Locked && _cardFlights?.Collecting != true && _deckDeal?.Running != true && _session?.WaitingForDeal != true;
        _tarotRow.IsEnabled = _actionButtons.IsEnabled;
        _viewerSeat.IsEnabled = !_input.Locked;
    }

    private Control BuildTable(GameView view)
    {
        _motion.SetContract(view.Contract);
        return PixelTable(view.Players.Select(player => PlayerPanel(player, view)).ToArray(), TrickPanel(view), SortButtons());
    }

    // Reorders the face-up hand only; the choice is remembered like any other preference.
    private Control SortButtons()
    {
        Button Choice(string label, HandSort sort)
        {
            var chosen = _preferences.HandSort == sort;
            var button = QuietButton(label);
            button.Name = $"Sort{sort}";
            button.FontSize = 11;
            button.FontWeight = FontWeight.Bold;
            button.Padding = new Thickness(12, 6);
            button.Focusable = false;
            if (chosen) { button.Background = Gold; button.Foreground = Paper; button.BorderBrush = Gold; }
            ToolTip.SetTip(button, $"Sort your hand by {label.ToLowerInvariant()}. Drag cards to arrange them yourself.");
            // Pressing a sort, even the current one, also undoes any dragging in this hand.
            button.Click += async (_, _) =>
            {
                if (!chosen)
                {
                    _preferences = _preferences with { HandSort = sort };
                    if (!_preferences.Save(_preferencesPath)) ShowError("Hand sorting changed for this session, but could not be saved.");
                }
                _autoArranged = _draggedDeal = null;
                _freePlayOrder.Clear();
                _renderKey = null;
                await RefreshGameAsync();
            };
            return button;
        }
        return new StackPanel
        {
            Name = "HandSortButtons", Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { Eyebrow("SORT"), Choice("Suit", HandSort.Suit), Choice("Rank", HandSort.Rank) }
        };
    }

    // A fixed logical table scales as one unit, so the four seats never push
    // the trick or action controls out of place at the minimum window size.
    private Control PixelTable(Control[] players, Control center, Control? handControls = null)
    {
        var canvas = new Canvas { Width = 1000, Height = 700 };
        for (var seat = 0; seat < 4; seat++)
        {
            var panel = players[seat];
            panel.Width = seat is 1 or 3 ? 230 : 500;
            Canvas.SetLeft(panel, seat switch { 1 => 0, 3 => 770, _ => 250 });
            Canvas.SetTop(panel, seat switch { 2 => 0, 0 => 492, _ => 180 });
            canvas.Children.Add(panel);
        }
        center.Width = 352;
        Canvas.SetLeft(center, 324);
        Canvas.SetTop(center, 202);
        canvas.Children.Add(center);
        if (handControls is not null)
        {
            // Beside the bottom hand, in the corner no seat's fan reaches.
            Canvas.SetLeft(handControls, 762);
            Canvas.SetTop(handControls, 656);
            canvas.Children.Add(handControls);
        }
        return new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = new Grid
            {
                ClipToBounds = true,
                Children = { PixelArt.TableScene(_motion),
                new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = canvas } }
            }
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
                Orientation = Orientation.Horizontal,
                Spacing = 8,
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
        _cardFlights?.PlayerSeat(player.Seat, tag);
        _deckDeal?.Player(player.Seat, tag);
        if (_motionCue.WinnerSeat == player.Seat) _motion.Pulse(tag);
        else if (_motionCue.TurnSeat == player.Seat) _motion.Nudge(tag);
        var cards = new List<Control>();
        var glide = _motion.Enabled && TableShown && _deckDeal?.Running != true;
        Control Slot(string key, Control card) => _handGlide?.Card($"{player.Seat}:{key}", card, glide) ?? card;
        Action<int, int>? reorder = null;
        if (view.Campaign is { } campaign)
        {
            // The viewer's own hand is shown in the engine's order, which is the order they arranged.
            var own = campaign.ViewerSeat == player.Seat;
            var shown = campaign.Players[player.Seat].VisibleCards;
            var visible = own ? shown : HandOrder.Sort(shown, card => card.Face, _preferences.HandSort, view.Contract);
            if (own && _session is { } session)
                reorder = (from, to) => _ = ArrangeCampaignHandAsync(session, campaign, player.Seat, Move(visible.Select(card => card.Id).ToList(), from, to));
            for (var i = 0; i < player.CardCount; i++)
            {
                if (i < visible.Count)
                {
                    var card = visible[i];
                    var choices = campaign.LegalActions.Where(c => c is CampaignCommand.Play p && p.Id == card.Id || c is CampaignCommand.Exchange e && e.Id == card.Id).ToArray();
                    var button = CardButton(card.Face, choices.Length == 1, view, choices.Length == 1 ? () => SendCampaignActionAsync(campaign, choices[0]) : null, card.Enhancement);
                    ToolTip.SetTip(button, $"#{card.Id.Value + 1} · {card.Enhancement?.ToString() ?? "No enhancement"} · {card.Sigil?.ToString() ?? "No Sigil"}");
                    var key = $"id{card.Id.Value}";
                    _cardFlights?.HandCard($"{player.Seat}:{key}", button, HandFan.Angle(player.Seat, i, player.CardCount));
                    _deckDeal?.Card(player.Seat, i, button, HandFan.Angle(player.Seat, i, player.CardCount)); cards.Add(Slot(key, button));
                }
                else { var back = FanCardBack(); _deckDeal?.Card(player.Seat, i, back, HandFan.Angle(player.Seat, i, player.CardCount)); cards.Add(Slot($"#{i}", back)); }
            }
        }
        else if (player.Cards is null)
        {
            for (var index = 0; index < player.CardCount; index++)
            {
                var back = FanCardBack();
                if (index == player.CardCount / 2)
                    _cardFlights?.HandCard($"{player.Seat}:back", back,
                        HandFan.Angle(player.Seat, index, player.CardCount));
                _deckDeal?.Card(player.Seat, index, back, HandFan.Angle(player.Seat, index, player.CardCount));
                cards.Add(Slot($"#{index}", back));
            }
        }
        else
        {
            var index = 0;
            var ordered = FreePlayHand(player, view);
            reorder = (from, to) =>
            {
                _freePlayOrder[player.Seat] = Move(ordered.Select(card => card.Code).ToList(), from, to);
                _renderKey = null;
                _ = RefreshGameAsync();
            };
            foreach (var card in ordered)
            {
                var legal = current && view.LegalActions.Cards.Contains(card);
                var button = CardButton(card, legal, view);
                _cardFlights?.HandCard($"{player.Seat}:{card.Code}", button,
                    HandFan.Angle(player.Seat, index, player.Cards.Count));
                _deckDeal?.Card(player.Seat, index, button, HandFan.Angle(player.Seat, index, player.Cards.Count));
                cards.Add(Slot(card.Code, button));
                index++;
            }
        }

        var panel = HandFan.Arrange(player.Seat, tag, cards, _motion, reorder);
        panel.Opacity = player.IsSittingOut ? 0.5 : 1;
        return panel;
    }

    private Control TrickPanel(GameView view)
    {
        IReadOnlyList<CardPlay> plays = view.CurrentTrick;
        CompletedTrick? completed = null;
        if (plays.Count == 0 && view.CompletedTricks.Count > 0)
        {
            completed = view.CompletedTricks[^1];
            plays = _trickCollection.Hidden ? [] : completed.Plays;
        }

        var cards = new Grid
        {
            Width = 330, Height = 250, RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(HandFan.CardScale, HandFan.CardScale)
        };
        foreach (var play in plays)
        {
            // Match the public physical card by seat, not its printed face: copies may have different enhancements.
            var physical = view.Campaign is { } cv ? (cv.Trick.Count > 0 ? cv.Trick : cv.LastTrick).FirstOrDefault(p => p.Seat == play.Seat) : null;
            var enhancement = physical?.Card.Enhancement;
            var card = CardFace(play.Card, enhancement);
            card.ZIndex = play.Seat switch { 2 => 0, 1 => 1, 3 => 2, _ => 3 };
            card.HorizontalAlignment = play.Seat switch { 1 => HorizontalAlignment.Left, 3 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            card.VerticalAlignment = play.Seat switch { 2 => VerticalAlignment.Top, 0 => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
            if (completed?.Winner == play.Seat)
            {
                card.BorderBrush = Gold;
                card.BorderThickness = new Thickness(3);
                if (_motionCue.WinnerSeat is not null) _motion.Pulse(card, 1.065);
            }
            var key = physical is null ? $"{play.Seat}:{play.Card.Code}" : $"{play.Seat}:id{physical.Card.Id.Value}";
            _cardFlights?.PlayedCard(key, card, _motionCue.AddedPlays.Contains(key), () => CardFace(play.Card, enhancement));
            cards.Children.Add(card);
        }

        var label = new TextBlock
        {
            Text = completed is null
                ? (plays.Count == 0 ? "Cards played here" : "Current trick")
                : _trickCollection.Hidden ? "Trick collected"
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
        _actionHelp.Text = string.Empty;
        RenderChoices(view);
        ShowCenter(view.Phase is GamePhase.GameComplete);
    }

    // The centre prompt is only up while it has something to press, or a result to announce.
    private void ShowCenter(bool always = false)
    {
        _centerTitle.Text = _actionTitle.Text;
        _actionHelp.IsVisible = !string.IsNullOrEmpty(_actionHelp.Text);
        _centerPanel.IsVisible = always || _actionButtons.Children.Count > 0;
    }

    private void RenderChoices(GameView view)
    {
        if (_session?.WaitingForDeal == true)
        {
            _actionTitle.Text = "Dealing cards";
            return;
        }
        if (view.Phase is GamePhase.HandComplete)
        {
            _actionTitle.Text = "Hand complete";
            var next = ActionButton("Deal next hand");
            next.Click += async (_, _) => await StartNextHandAsync(next);
            _actionButtons.Children.Add(next);
            return;
        }
        if (view.Phase is GamePhase.GameComplete)
        {
            _actionTitle.Text = $"Team {view.GameWinner + 1} wins";
            _actionHelp.Text = view.Campaign is null ? $"The race to {view.TargetScore} is complete." : string.Empty;
            return;
        }
        if (view.Campaign is { } campaign)
        {
            _actionTitle.Text = CampaignTableControls.Title(campaign);
            foreach (var command in CampaignTableControls.CenterCommands(campaign))
            {
                var captured = command;
                AddAction(CampaignTableControls.Describe(command, campaign), () => SendCampaignActionAsync(campaign, captured));
            }
            return;
        }
        if (view.CurrentSeat != _viewer)
        {
            var current = view.CurrentSeat is null ? null : view.Players[view.CurrentSeat.Value];
            var currentSeat = view.CurrentSeat is null ? null : _session?.Seats[view.CurrentSeat.Value];
            _actionTitle.Text = current is null ? SplitWords(view.Phase.ToString())
                : currentSeat?.Kind is PlayerKind.Human ? $"Waiting for {current.Name} · switch View hand to seat {current.Seat + 1}"
                : $"{current.Name} is thinking";
            return;
        }

        switch (view.Phase)
        {
            case GamePhase.Bidding:
                _actionTitle.Text = "Make your bid";
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
                _actionTitle.Text = "Give your partner a gold-outlined card";
                break;
            case GamePhase.ExchangingPartnerCard:
                _actionTitle.Text = "Return a gold-outlined card to the bidder";
                break;
            case GamePhase.Playing:
                _actionTitle.Text = "Play a gold-outlined card";
                break;
        }
    }

    private static List<T> Move<T>(List<T> items, int from, int to)
    {
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
        return items;
    }

    // Free play: the preferred sort, unless the player dragged this hand. Cards that arrive later
    // (the exchange) go on the right end of a dragged hand.
    private IReadOnlyList<Card> FreePlayHand(PlayerView player, GameView view)
    {
        var hand = $"{_session?.Id}:{view.HandNumber}";
        if (_freePlayOrderHand != hand) { _freePlayOrder.Clear(); _freePlayOrderHand = hand; }
        var sorted = HandOrder.Sort(player.Cards!, card => card, _preferences.HandSort, view.Contract);
        if (!_freePlayOrder.TryGetValue(player.Seat, out var order)) return sorted;
        var rank = order.Select((code, index) => (code, index)).ToDictionary(x => x.code, x => x.index);
        return sorted.OrderBy(card => rank.GetValueOrDefault(card.Code, int.MaxValue)).ToArray();
    }

    // Campaign: each deal is laid out by the preferred sort, and again once the contract is known
    // (trump first), unless the player has dragged cards in this deal.
    private async Task<bool> AutoArrangeCampaignHandAsync(GameSession session, GameView view, CampaignGameView campaign)
    {
        if (campaign.ViewerSeat is not { } seat) return false;
        var hand = campaign.Players[seat].VisibleCards;
        var deal = $"{session.Id}:{campaign.DealSerial}";
        var stage = $"{deal}:{view.Contract?.Mode}:{view.Contract?.Trump}:{_preferences.HandSort}";
        if (hand.Count < 2 || _draggedDeal == deal || _autoArranged == stage) return false;
        _autoArranged = stage;
        var sorted = HandOrder.Sort(hand, card => card.Face, _preferences.HandSort, view.Contract).Select(card => card.Id).ToArray();
        if (sorted.SequenceEqual(hand.Select(card => card.Id))) return false;
        try { await session.ArrangeCampaignHandAsync(seat, sorted); return true; }
        catch (GameRuleException) { return false; }
    }

    private async Task ArrangeCampaignHandAsync(GameSession session, CampaignGameView campaign, int seat, IReadOnlyList<CardId> order)
    {
        _draggedDeal = $"{session.Id}:{campaign.DealSerial}";
        try { await session.ArrangeCampaignHandAsync(seat, order); }
        catch (Exception error) { ShowError(error.Message); }
        _renderKey = null;
        await RefreshGameAsync();
    }

    private async Task SendCampaignActionAsync(CampaignGameView view, CampaignCommand command)
    {
        var session = _session; if (session is null || !_input.TryBeginAction(out var revision)) return;
        SyncNativeInput();
        try
        {
            await session.ExecuteCampaignAsync(new CampaignActionRequest(_viewer!.Value, view.Revision, view.DealSerial, command));
            _renderKey = null;
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { _input.CompleteAction(revision); SyncNativeInput(); }
        await RefreshGameAsync();
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
            await session.StartNextHandAsync(waitForDeal: _motion.Enabled);
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
            _status.IsVisible = true;
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

    private Button CardButton(Card card, bool legal, GameView view, Func<Task>? campaignAction = null, EnhancementId? enhancement = null)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        var button = new Button
        {
            Width = 124,
            Height = 176,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Background = CardPaper,
            Foreground = red ? CardRed : CardInk,
            BorderBrush = legal ? Gold : Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(legal ? 3 : 1),
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            // Unplayable cards still take the pointer so their tooltip can open; they just have no click.
            Focusable = legal,
            Opacity = 1,
            Content = PixelArt.CardContent(card, enhancement)
        };
        // Card art has its own paper/ink palette. Fluent's dark button hover and
        // pressed template must not replace the paper behind transparent artwork.
        var surface = new Border
        {
            Background = CardPaper,
            BorderBrush = button.BorderBrush,
            BorderThickness = button.BorderThickness,
            CornerRadius = button.CornerRadius,
            ClipToBounds = true,
            Padding = button.Padding,
            Child = new ContentPresenter
            {
                Content = button.Content,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            }
        };
        button.Template = new FuncControlTemplate<Button>((_, _) => surface);
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
            void UpdateHighlight()
            {
                var highlighted = button.IsPointerOver || button.IsKeyboardFocusWithin;
                lift.Y = highlighted ? -8 : 0;
                // Highlight the wrapper; keep the selected background sprite intact.
                surface.BorderBrush = highlighted ? Gold : button.BorderBrush;
            }
            button.PointerEntered += (_, _) => UpdateHighlight();
            button.PointerExited += (_, _) => UpdateHighlight();
            button.GotFocus += (_, _) => UpdateHighlight();
            button.LostFocus += (_, _) => UpdateHighlight();
            button.Click += async (_, _) =>
            {
                if (campaignAction is not null) { await campaignAction(); return; }
                var type = view.Phase is GamePhase.Playing ? "play" : "exchange";
                await SendActionAsync(new GameActionRequest(_viewer!.Value, type, null, null, null, card.Code));
            };
        }
        return button;
    }

    private static Border CardFace(Card card, EnhancementId? enhancement = null)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        return new Border
        {
            Width = 124,
            Height = 176,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            BorderBrush = Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(1),
            Background = CardPaper,
            Child = PixelArt.CardContent(card, enhancement)
        };
    }

    private static Border CardBack() => new()
    {
        Margin = new Thickness(1),
        Background = CardPaper,
        BorderBrush = CardPaper,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        ClipToBounds = true,
        Child = PixelArt.Back()
    };

    private static Border FanCardBack() => new()
    {
        Width = 124,
        Height = 176,
        Margin = new Thickness(2),
        Background = CardPaper,
        BorderBrush = CardPaper,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(10),
        ClipToBounds = true,
        Child = PixelArt.Back(120, 172)
    };

    private void ShowMenu()
    {
        _motion.SetContract(null);
        _menuButton.Content = MenuLabel;
        _screenHost.Content = MainMenu.Build(_campaign.Open, ShowTable, ShowEngines, ShowRules, ShowSettings, _motion);
    }

    private bool TableShown => ReferenceEquals(_pageHost.Content, _playHost);

    private void ShowTable() => ShowPage(_playHost);

    private void ShowEngines()
    {
        RefreshEngineCatalog();
        ShowPage(_enginePage);
    }

    private void ShowRules() => ShowPage(_rulesPage);

    private void ShowPage(Control page)
    {
        ClearStatus();
        _pageHost.Content = page;
        _screenHost.Content = _tableScreen;
    }

    private void ShowSettings() => _screenHost.Content = SettingsPage.Build(_preferences, ChangePreferences, ShowMenu, _motion);

    private void ChangePreferences(GamePreferences preferences)
    {
        _preferences = preferences;
        ApplyGraphics();
        if (!_preferences.Save(_preferencesPath)) ShowError("Settings changed for this session, but could not be saved.");
    }

    private void ApplyGraphics()
    {
        _motion.SetEnabled(_preferences.MotionEnabled);
        _scaleHost.LayoutTransform = new ScaleTransform(_preferences.UiScale, _preferences.UiScale);
        WindowState = _preferences.Fullscreen ? WindowState.FullScreen : WindowState.Normal;
        if (!_preferences.Fullscreen)
        {
            Width = _preferences.WindowWidth;
            Height = _preferences.WindowHeight;
        }
    }

    // Leaving for the menu ends any live table, exactly like starting a new one.
    private async Task ReturnToMenuAsync(Button button)
    {
        if (_tableTransitioning) return;
        // Leaving a live campaign match costs it, so the first press only asks.
        if (InCampaignMatch && _campaign.InMatch && !Equals(button.Content, ForfeitLabel))
        {
            button.Content = ForfeitLabel;
            return;
        }
        if (_session is not null)
        {
            await LeaveTableAsync(button);
            button.IsEnabled = true;
        }
        ShowMenu();
    }

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
        _cardFlights?.Cancel(); _deckDeal?.Cancel();
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
        _contractText.Text = "No contract";
        _dealerText.Text = string.Empty;
        _sessionError.IsVisible = false;
        _actionTitle.Text = "Table created";
        _actionHelp.Text = "Start the session when every seat is ready.";
        _actionButtons.Children.Clear();
        (_startButton.Parent as Panel)?.Children.Remove(_startButton);
        _actionButtons.Children.Add(_startButton);
        RenderInventory(null);
        ShowCenter();
        if (_session is not null)
        {
            var players = _session.Seats.Select(seat => new PlayerView(seat.Seat, seat.Name, seat.Seat % 2, 6, null, false)).ToArray();
            _tableHost.Content = BuildWaitingTable(players);
        }
    }

    private Control BuildWaitingTable(IReadOnlyList<PlayerView> players)
    {
        _motion.SetContract(null);
        var panels = new Control[4];
        for (var seat = 0; seat < 4; seat++)
        {
            var portrait = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    PixelArt.Portrait(seat),
                    new TextBlock { Text = players[seat].Name, Foreground = Ink, FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = seat is 1 or 3 ? 76 : 260,
                        TextWrapping = TextWrapping.Wrap }
                }
            };
            panels[seat] = HandFan.Arrange(seat, portrait,
                Enumerable.Range(0, 6).Select(_ => (Control)FanCardBack()).ToArray(), _motion);
        }
        return PixelTable(panels, new Panel());
    }

    private void AddAction(string label, Func<Task> action)
    {
        var button = ActionButton(label);
        button.Click += async (_, _) => await action();
        _actionButtons.Children.Add(button);
    }

    internal static void StyleInput(TextBox input)
    {
        input.Background = Brush.Parse("#0D1513");
        input.Foreground = Ink;
        input.BorderBrush = Line;
        input.BorderThickness = new Thickness(1);
        input.CornerRadius = new CornerRadius(2);
        input.Padding = new Thickness(10, 7);
        input.MinHeight = 38;
    }

    internal static void StyleInput(ComboBox input)
    {
        input.Background = Brush.Parse("#0D1513");
        input.Foreground = Ink;
        input.BorderBrush = Line;
        input.BorderThickness = new Thickness(1);
        input.CornerRadius = new CornerRadius(2);
        input.Padding = new Thickness(10, 7);
        input.MinHeight = 38;
    }

    internal static Border Field(string label, Control control) => new()
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

    internal static Border CardPanel(Control child, double padding = 20) => new()
    {
        Background = PanelRaised,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Padding = new Thickness(padding),
        Child = child
    };

    internal static Control PageHeading(string title, string subtitle) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            Eyebrow("BID EUCHRE"),
            new TextBlock { Text = title, Foreground = Ink, FontSize = 43, FontFamily = PixelArt.Font },
            new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 14 }
        }
    };

    private static Border ScorePanel(string label, TextBlock score, IBrush accent) => CardPanel(new Grid
    {
        ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto"), ColumnSpacing = 10, MinWidth = 112,
        Children =
        {
            new TextBlock { Text = label, Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
            Place(score, column: 1)
        }
    }, 9);

    private static TextBlock ScoreText() => new()
    {
        Text = "0",
        Foreground = Ink,
        FontSize = 30,
        FontFamily = PixelArt.Font
    };

    internal static TextBlock Eyebrow(string text) => new()
    {
        Text = text,
        Foreground = Green,
        FontSize = 10,
        FontWeight = FontWeight.Bold
    };

    internal static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Background = Green,
        Foreground = Paper,
        Padding = new Thickness(16, 9),
        CornerRadius = new CornerRadius(2),
        BorderThickness = new Thickness(0),
        FontWeight = FontWeight.Bold
    };

    internal static Button QuietButton(string text) => new()
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
        Margin = new Thickness(4, 0, 4, 8),
        Padding = new Thickness(16, 10),
        Background = Brush.Parse("#143480"),
        Foreground = Brush.Parse("#FFF5DC"),
        BorderBrush = Brush.Parse("#65BFFF"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        FontWeight = FontWeight.Bold,
        FontSize = 14
    };

    internal static T Place<T>(T control, int row = 0, int column = 0) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private void ShowError(string message)
    {
        _status.Foreground = Red;
        _status.Text = message;
        _status.IsVisible = true;
    }

    private void ClearStatus()
    {
        _status.Text = string.Empty;
        _status.IsVisible = false;
    }

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
