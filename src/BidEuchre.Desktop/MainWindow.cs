using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Core;
using System.Text.Json;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow : Window
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

    private readonly EngineCatalog _catalog;
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
    private NumberTally? _teamZeroTally, _teamOneTally;
    private NumberTally ScoreTally(int team) => team == 0
        ? _teamZeroTally ??= new NumberTally(_teamZeroScore)
        : _teamOneTally ??= new NumberTally(_teamOneScore);
    /// <summary>Shows both team scores, counting the ones this view changed when motion is on.</summary>
    private void ShowScores(GameView view, bool animate)
    {
        for (var team = 0; team < 2; team++)
            ScoreTally(team).Change(view.Scores[team], animate && _motionCue.ScoreTeams.Contains(team));
    }
    private readonly TextBlock _contractText = new() { Foreground = Ink, FontSize = 17, FontFamily = PixelArt.Font,
        TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _dealerText = new() { Foreground = Muted, FontSize = 10 };
    private readonly ComboBox _viewerSeat = new() { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _startButton = PrimaryButton("Start session");
    private readonly ContentControl _tableHost = new();
    // The one-line turn status in the top strip; the centre prompt repeats it above its buttons.
    private readonly TextBlock _actionTitle = new() { Foreground = Muted, FontSize = 11, FontFamily = PixelArt.Font, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _centerTitle = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Ink, FontSize = 15, FontFamily = PixelArt.Font };
    private readonly TextBlock _actionHelp = new() { FontFamily = FontFamily.Default, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly WrapPanel _actionButtons = new() { ItemWidth = double.NaN, ItemHeight = double.NaN, HorizontalAlignment = HorizontalAlignment.Center };
    // Choices float in the middle of the board, kept small enough to clear every hand.
    private readonly Border _centerPanel;
    // Bidding is staged: a number, then (if that number has any) a gumball. Trump follows as its own phase.
    private BidLevel? _bidLevel;
    private string? _bidStepKey;
    private GameView? _actionView;
    private readonly StackPanel _jokerRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _sodaRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _tarotRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private string? _inventoryKey;
    // A stored Tarot or Sigil whose targets are being chosen by pressing cards in hand.
    private sealed record ItemTargeting(long DealSerial, FeatureCategory Category, int Slot, FeatureDefinition Definition, int Required, List<CardId> Chosen);
    private ItemTargeting? _targeting;
    // Errors lie over the top edge of the board, so showing one never resizes it.
    private readonly TextBlock _sessionError = new()
    {
        Foreground = Red, Background = Paper, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false,
        Padding = new Thickness(10, 6), Margin = new Thickness(12), IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top
    };
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
    private GameView? _renderedView;
    private string? _renderedError;
    private bool _renderedWaiting;
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
    private readonly ScoreJolts _scoreJolts = new();
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
        _catalog = new EngineCatalog(Path.Combine(DataDirectory, "BidEuchre", "engines.json"), Path.Combine(AppContext.BaseDirectory, "engines"));
        _lastTableSetup = LastTableSetup.Load(_lastTableSetupPath);
        Title = "Bid Euchre";
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://BidEuchre/Icon/window-icon.png")));
        Width = 1320;
        Height = 860;
        MinWidth = 980;
        MinHeight = 680;
        Background = Paper;
        FontFamily = PixelArt.Font;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeChanged += (_, _) => { ApplyUiScale(); _bidAnnouncement?.Cancel(); _cardFlights?.Cancel(); _deckDeal?.Cancel(); _handGlide?.Reset(); };
        _preferences = GamePreferences.Load(_preferencesPath);
        ApplyGraphics();
        _centerPanel = new Border
        {
            Name = "TableChoices", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 340, MaxHeight = 230, Padding = new Thickness(10, 10, 10, 2), CornerRadius = new CornerRadius(2),
            Background = Brush.Parse("#EE100B28"), BorderBrush = Gold, BorderThickness = new Thickness(2),
            Child = new ScrollViewer
            {
                Name = "TableActionScroll", HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new StackPanel { Spacing = 7, Children = { _centerTitle, _actionHelp, _handScoreSummary, _actionButtons } }
            }
        };
        _campaign = new CampaignFlow(_campaignPath, screen => _screenHost.Content = screen,
            StartCampaignMatchAsync, ShowMenu, _motion);

        StyleInput(_tableName);
        StyleInput(_pace);
        StyleInput(_engineExecutable);
        StyleInput(_engineArguments);
        StyleInput(_viewerSeat);
        _pace.ItemsSource = PaceOption.All;
        _tableName.Text = _lastTableSetup.TableName;
        _pace.SelectedItem = PaceOption.All.First(option => option.Milliseconds == _lastTableSetup.BotDelayMilliseconds);
        _viewerSeat.SelectionChanged += async (_, _) =>
        {
            if (_settingViewer) return;
            if (AutomaticHotseat) { SetViewerSelection(_viewer); return; }
            if (_viewerSeat.SelectedItem is ViewerOption option)
            {
                _input.BeginViewChange();
                _motion.Cancel();
                _bidAnnouncement?.Cancel();
                _cardFlights?.Cancel(); _deckDeal?.Cancel();
                SyncNativeInput();
                _viewer = option.Seat;
                _renderedView = null;
                await RefreshGameAsync();
            }
        };
        _startButton.Click += async (_, _) => await StartSessionAsync();

        _enginePage = BuildEnginePage();
        _rulesPage = RulesPage.Build();
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
                BuildTableNavigation(menuButton),
                Place(_status, row: 1)
            }
        };
        _status.Margin = new Thickness(20, 5, 20, 10);
        _status.IsVisible = false;
        _scaleHost.Child = _screenHost;
        Content = WithDeveloperConsole(_scaleHost);
        ShowMenu();

        ShowSetup();
        RefreshEngineCatalog();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _refreshTimer.Tick += async (_, _) => await RefreshGameAsync();
        _refreshTimer.Start();
        Deactivated += (_, _) => _pokerReveal = null;

        Closed += async (_, _) =>
        {
            _campaign.Close();
            SaveTableSetup();
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
        var defaults = _lastTableSetup.Seats;
        for (var seat = 0; seat < 4; seat++)
        {
            _seatNames[seat] = new TextBox { Text = defaults[seat].Name, MaxLength = 30 };
            _seatKinds[seat] = new ComboBox { ItemsSource = new[] { "Human", "Bot" }, SelectedIndex = defaults[seat].Kind == PlayerKind.Bot ? 1 : 0 };
            _seatEngines[seat] = new ComboBox { IsEnabled = defaults[seat].Kind == PlayerKind.Bot };
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
            _scoreEyebrow,ScoreTally(0).Host,ScoreTally(1).Host,_actionTitle,_sessionError,_centerPanel,_jokerRow,_tarotRow,_sodaRow,_tableHost,_publicLogPanel})
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
            ? new TextBlock { Name = "CampaignStrip", Text = _campaign.Strip, Foreground = Gold, FontSize = 12, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap }
            : null;

        CancelContractAnnouncement();
        _bidAnnouncement?.Clear();
        var bidDock = new ContentControl { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var bidOverlay = new Canvas { IsHitTestVisible = false };
        _bidAnnouncement = new BidAnnouncement(bidOverlay, _tableHost, bidDock);
        _contractAnnouncement = new ContractAnnouncement(bidOverlay, _tableHost);
        _cardFlights?.Cancel(); _deckDeal?.Cancel();
        _cardFlights = new CardFlights(bidOverlay);
        AttachLastTrick(bidOverlay);
        _deckDeal = new DeckDeal(bidOverlay, _tableHost, SyncNativeInput);
        _handGlide = new HandGlide(bidOverlay);
        _inventoryKey = null;
        RenderInventory(null);

        var status = new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
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
                    Children = { ScorePanel("TEAM 1", ScoreTally(0).Host, Green), ScorePanel("TEAM 2", ScoreTally(1).Host, Amber) }
                }
            }
        };
        var summary = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"), ColumnSpacing = 12,
            Margin = new Thickness(4, 0, 170, 0),
            Children = { status, Place(new Border { Width = 128, Child = bidDock }, column: 1), Place(score, column: 2) }
        };
        if (!campaign && _viewerSeat.ItemCount > 1) status.Children.Add(_viewerSeat);
        if (campaign)
            status.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = _sodaRow
            });
        // Keep a finite viewport: the board scales to the window, and takes all of it below the strip.
        var page = new Grid
        {
            Margin = new Thickness(8, 6, 8, 0),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 6,
            Children =
            {
                summary,
                Place(new Grid
                {
                    Children = { _tableHost, _centerPanel, _sessionError }
                }, row: 1)
            }
        };
        var board = new Grid { Children = { page, bidOverlay, PublicLogPanel() } };
        Control surface = campaign ? new CampaignInventoryScreen(board, _jokerRow, _tarotRow) : board;
        return new Grid { Children = { surface, PublicLogButton() } };
    }

    // Illustrated campaign cards retain their names and rules on hover.
    private void RenderInventory(CampaignGameView? campaign)
    {
        var usable = campaign is not null && CampaignTableControls.ItemsUsable(campaign);
        if (!usable || _targeting is { } targeting && !TargetingValid(campaign!, targeting)) _targeting = null;
        // A usable card sends the view's revision, so it is rebuilt whenever that moves.
        var key = campaign is null ? "" : JsonSerializer.Serialize(campaign.Inventory) + (usable ? $":{campaign.Revision}" : "")
            + $":{_targeting?.Category}{_targeting?.Slot}";
        if (key == _inventoryKey) return;
        _inventoryKey = key;
        _sodaRow.Children.Clear();
        foreach (var soda in campaign?.Inventory.Sodas ?? []) _sodaRow.Children.Add(SodaArtwork.Can(soda, 38));
        CampaignTableControls.Fill(_jokerRow, campaign?.Inventory.JokerCapacity ?? CampaignTableControls.JokerSlots, _motion, 0,
            campaign?.Inventory.Jokers.Select(CampaignTableControls.Joker) ?? []);
        CampaignTableControls.Fill(_tarotRow, campaign?.Inventory.ItemCapacity ?? CampaignTableControls.ItemSlots, _motion, 7,
            campaign is null ? [] : CampaignTableControls.Items(campaign.Inventory).Select(item =>
                CampaignTableControls.Item(campaign, item.Category, item.Slot, usable,
                    _targeting is { } active && active.Category == item.Category && active.Slot == item.Slot,
                    () => ToggleTargeting(campaign, item.Category, item.Slot))));
    }

    private static bool TargetingValid(CampaignGameView campaign, ItemTargeting targeting)
    {
        var held = targeting.Category == FeatureCategory.Sigil ? campaign.Inventory.Sigils.Length : campaign.Inventory.Tarot.Length;
        if (targeting.DealSerial != campaign.DealSerial || targeting.Slot >= held ||
            CampaignTableControls.Definition(campaign.Inventory, targeting.Category, targeting.Slot).Id != targeting.Definition.Id) return false;
        // Cards that left the hand (played, swapped) can no longer be targets.
        targeting.Chosen.RemoveAll(id => campaign.Players[0].VisibleCards.All(card => card.Id != id));
        return true;
    }

    // Pressing a stored item starts choosing its targets; pressing it again (or Cancel) stops.
    private void ToggleTargeting(CampaignGameView campaign, FeatureCategory category, int slot)
    {
        var definition = CampaignTableControls.Definition(campaign.Inventory, category, slot);
        _targeting = _targeting is { } active && active.Category == category && active.Slot == slot ? null
            : new(campaign.DealSerial, category, slot, definition,
                category == FeatureCategory.Sigil ? 1 : Tarot.TargetCount(campaign.Inventory.Tarot[slot]), []);
        _inventoryKey = null; _renderedView = null;
        _ = RefreshGameAsync();
    }

    private void ToggleTarget(CardId id)
    {
        if (_targeting is not { } targeting) return;
        if (!targeting.Chosen.Remove(id))
        {
            // With one target, pressing another card moves the choice.
            if (targeting.Required == 1) targeting.Chosen.Clear();
            if (targeting.Chosen.Count < targeting.Required) targeting.Chosen.Add(id);
        }
        _renderedView = null;
        _ = RefreshGameAsync();
    }

    // What a chosen card will look like once the item is used, when that is certain.
    private static CampaignCardView Preview(ItemTargeting targeting, CampaignCardView card, int position)
    {
        var state = new CardState(card.Id, card.Face, card.Enhancement, card.Sigil);
        var after = targeting.Category == FeatureCategory.Sigil
            ? state with { Sigil = Enum.Parse<SigilId>(targeting.Definition.Key) }
            : Tarot.Preview(Enum.Parse<TarotId>(targeting.Definition.Key), state, position);
        return after is null ? card : new(after.Id, after.Face, after.Enhancement, after.Sigil);
    }

    private void RenderTargeting(CampaignGameView campaign, ItemTargeting targeting)
    {
        var name = targeting.Definition.Name;
        _actionTitle.Text = $"Use {name}";
        var count = targeting.Chosen.Count;
        var step = targeting.Required switch
        {
            0 => "It needs no target.",
            1 => count == 0 ? "Press a card in your hand." : "Press another card to change it.",
            var n => $"Press {n} cards in your hand · {count} of {n} chosen."
        };
        if (targeting.Definition.Key == nameof(TarotId.DevilishDeal)) step += " The third card you press becomes Cursed.";
        _actionHelp.Text = $"{targeting.Definition.Description}\n{step}";
        var use = ActionButton($"Use {name}");
        use.Name = "UseItem";
        use.IsEnabled = count == targeting.Required;
        use.Click += async (_, _) =>
        {
            CampaignCommand command = targeting.Category == FeatureCategory.Sigil
                ? new CampaignCommand.UseSigil(targeting.Slot, targeting.Chosen[0])
                : new CampaignCommand.UseTarot(targeting.Slot, targeting.Chosen.ToArray());
            _targeting = null; _inventoryKey = null;
            await SendCampaignActionAsync(campaign, command);
        };
        _actionButtons.Children.Add(use);
        var cancel = ActionButton("Cancel");
        cancel.Name = "CancelItem";
        cancel.Click += (_, _) => ToggleTargeting(campaign, targeting.Category, targeting.Slot);
        _actionButtons.Children.Add(cancel);
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

        var load = _engineLoadButton = PrimaryButton("Handshake & load");
        var cancelRepair = _cancelEngineRepair = QuietButton("Cancel repair");
        cancelRepair.Name = "CancelEngineRepair";
        cancelRepair.IsVisible = false;
        cancelRepair.Click += (_, _) =>
        {
            _repairEngineId = null;
            _engineExecutable.Text = "";
            _engineArguments.Text = "";
            load.Content = "Handshake & load";
            cancelRepair.IsVisible = false;
            ClearStatus();
        };
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
                load, cancelRepair
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

            var seats = ReadTableSeats();
            SaveTableSetup();
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
        return await OpenSessionAtPaceAsync(seats, name, options,
            (_pace.SelectedItem as PaceOption ?? PaceOption.All[1]).Milliseconds);
    }

    private async Task<GameSession> OpenSessionAtPaceAsync(IReadOnlyList<SeatConfiguration> seats, string name, MatchOptions? options, int paceMilliseconds)
    {
        ResetHotseat();
        _freePlayRematch = options is null ? new(name, paceMilliseconds, seats.ToArray()) : null;
        var session = new GameSession(
            Guid.NewGuid().ToString("N")[..10],
            name,
            seats,
            _catalog,
            seed: null,
            botActionDelay: TimeSpan.FromMilliseconds(paceMilliseconds),
            matchOptions: options,
            campaign: options is null ? null : new CampaignContext(_campaign.Run!.Resources),
            liveCheckpoint: options is null ? null : _campaign.Checkpoint,
            waitForContractPresentation: true,
            waitForTrickPresentation: true);
        _session = session;
        _campaignSession = options is null ? null : session;
        _sessionGeneration++;
        _renderedWaiting = false;

        var viewers = seats.Where(seat => seat.Kind is PlayerKind.Human)
            .Select(seat => new ViewerOption(seat.Seat, $"Seat {seat.Seat + 1}: {seat.Name}"))
            .ToList();
        if (viewers.Count == 0)
        {
            viewers.Add(new ViewerOption(null, "Spectator"));
        }
        _viewer = viewers[0].Seat;
        _settingViewer = true;
        try { _viewerSeat.ItemsSource = viewers; _viewerSeat.SelectedIndex = 0; }
        finally { _settingViewer = false; }
        _playHost.Content = BuildGamePage();
        _renderedView = null;
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
            if (_campaign.Run?.LiveMatch is { } live)
            {
                var arrangement = _campaign.Run.Arrangement;
                var deal = $"{session.Id}:{live.DealSerial}";
                _draggedDeal = arrangement?.ManuallyArranged is true ? deal : null;
                _autoArranged = arrangement?.AutoStage is { } stage ? $"{deal}:{stage}" : null;
                try
                {
                    await session.RestoreCampaignAsync(new(live, arrangement));
                    await RefreshGameAsync();
                }
                catch
                {
                    if (ReferenceEquals(DetachSession(), session)) await session.DisposeAsync();
                    throw;
                }
            }
            else await StartSessionAsync();
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
    private async Task CompleteCampaignMatchAsync(GameSession session, GameView view)
    {
        _campaign.MatchCompleted(view.GameWinner == 0, view.Scores, view);
        var presentation = _cardFlights?.CollectionCompletion ?? Task.CompletedTask;
        if (presentation.IsCompleted) await Tween.Delay(MotionCurves.TrickHoldMilliseconds);
        else await presentation;
        if (!ReferenceEquals(session, _session)) return;
        DetachSession();
        try { await session.DisposeAsync(); }
        catch (Exception exception) { ShowError(exception.Message); }
        ShowSetup();
        _campaign.ShowPendingResult();
    }

    private async Task FinishTrickPresentationAsync(GameSession session, long deal, int trick)
    {
        try { await session.CompleteTrickPresentationAsync(deal, trick); }
        catch (ObjectDisposedException) { return; }
        if (!ReferenceEquals(session, _session)) return;
        _renderedView = null;
        SyncNativeInput();
        Dispatcher.UIThread.Post(async () => await RefreshGameAsync());
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
        if (_refreshing || session is null || HandDrag.Active || _liveSettingsOpen)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var (view, error) = await session.ReadTableViewAsync(viewer);
            if (HandDrag.Active || _liveSettingsOpen || revision != _input.Revision) return;
            if (view?.Campaign is { } arranging && await AutoArrangeCampaignHandAsync(session, view, arranging))
                (view, error) = await session.ReadTableViewAsync(viewer);
            if (generation != _sessionGeneration ||
                viewer != _viewer || revision != _input.Revision ||
                !ReferenceEquals(session, _session) || _liveSettingsOpen || HandDrag.Active)
            {
                return;
            }
            if (view is null)
            {
                if (_renderedWaiting)
                {
                    return;
                }
                _renderedView = null;
                _renderedWaiting = true;
                _motionTracker.Update(null, $"{session.Id}:{viewer}");
                CancelContractAnnouncement();
                _pokerReveal = null;
                _bidAnnouncement?.Clear();
                _cardFlights?.Cancel(); _deckDeal?.Cancel();
                _contractText.IsVisible = true;
                RenderWaitingTable();
                _input.CompleteView(revision);
                SyncNativeInput();
                return;
            }

            if (RenderHotseatHandoff(session, view)) { RenderScoreSummary(view); return; }

            _renderedWaiting = false;
            if (ReferenceEquals(view, _renderedView) && error == _renderedError)
            {
                UpdateContractAnnouncement(session, view);
                UpdatePokerReveal(session, view);
                return;
            }
            _renderedView = view;
            _renderedError = error;
            UpdatePublicLog(view);
            _motionCue = _motionTracker.Update(view, $"{session.Id}:{viewer}");
            var lastTrick = view.CompletedTricks.LastOrDefault();
            var motionEnabled = _motion.Enabled && TableShown;
            var dealSerial = view.DealSerial ?? view.HandNumber;
            var trickNumber = lastTrick?.Number ?? 0;
            var pendingTrick = session.WaitingForTrickPresentation && view.CurrentTrick.Count == 0 && trickNumber > 0;
            var collectTrick = _trickCollection.Observe($"{session.Id}:{viewer}", view.HandNumber,
                trickNumber, (pendingTrick || _motionCue.WinnerSeat is not null) && view.CurrentTrick.Count == 0, motionEnabled);

            _startButton.IsVisible = false;
            if (view.Campaign is not null && _campaignStrip is not null) _campaignStrip.Text = _campaign.Strip;
            ShowScores(view, motionEnabled);
            _scoreEyebrow.Text = view.Campaign is null ? $"TEAMS · TO {view.TargetScore}" : $"TEAMS · ±{view.TargetScore}" + (view.Campaign.HandLimit is { } limit ? $" · {limit} HANDS" : "");
            _contractText.Text = ContractName(view);
            _dealerText.Text = view.Phase == GamePhase.PokerReveal
                ? $"HAND {view.HandNumber} · POKER"
                : $"HAND {view.HandNumber} · TRICKS {view.TricksByTeam[0]}–{view.TricksByTeam[1]}";
            _sessionError.Text = error ?? (InCampaignMatch ? _campaign.ResumeNotice : null) ?? string.Empty;
            _sessionError.IsVisible = _sessionError.Text.Length > 0;
            RenderInventory(view.Campaign);
            if (!motionEnabled || view.Phase != GamePhase.Bidding) _deckDeal?.Cancel();
            _deckDeal?.BeginFrame();
            _cardFlights?.BeginFrame();
            _handGlide?.BeginFrame($"{session.Id}:{viewer}:{view.HandNumber}");
            var joltHold = _scoreJolts.Observe($"{session.Id}:{viewer}", view, _motionCue.AddedPlays, motionEnabled);
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
                    _ = FinishTrickPresentationAsync(session, dealSerial, trickNumber);
                }
            : null, joltHold);
            if (pendingTrick && _cardFlights?.Collecting != true)
                _ = FinishTrickPresentationAsync(session, dealSerial, trickNumber);
            foreach (var team in _motionCue.ScoreTeams) _motion.Pulse(team == 0 ? _teamZeroScore : _teamOneScore, 1.2);
            RenderActions(view);
            UpdateBidAnnouncement(view);
            UpdateContractAnnouncement(session, view);
            if (_motionCue.TrumpChanged && motionEnabled) _motion.Pulse(_contractText, 1.1);
            _input.CompleteView(revision);
            SyncNativeInput();
            UpdatePokerReveal(session, view);
            if (view.Phase is GamePhase.GameComplete && _campaign.InMatch && ReferenceEquals(session, _campaignSession))
            {
                _ = CompleteCampaignMatchAsync(session, view);
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
            _renderedView = null;
            await RefreshGameAsync();
            SyncNativeInput();
        }
        catch (ObjectDisposedException) { }
    }

    private void SyncNativeInput()
    {
        _tableHost.IsVisible = !_input.Loading;
        _tableHost.IsEnabled = _hotseatPending is null && !_input.Locked && _cardFlights?.Collecting != true && _deckDeal?.Running != true && _session?.WaitingForDeal != true && _session?.WaitingForContractPresentation != true && _session?.WaitingForTrickPresentation != true;
        _actionButtons.IsEnabled = !_input.Locked && _cardFlights?.Collecting != true && _deckDeal?.Running != true && _session?.WaitingForDeal != true && _session?.WaitingForContractPresentation != true && _session?.WaitingForTrickPresentation != true;
        _tarotRow.IsEnabled = _actionButtons.IsEnabled;
        _viewerSeat.IsEnabled = !AutomaticHotseat && !_input.Locked;
    }

    private void RenderActions(GameView view)
    {
        _actionButtons.Children.Clear();
        _actionHelp.Text = string.Empty;
        _actionView = view;
        RenderScoreSummary(view);
        RenderChoices(view);
        ShowCenter(view.Phase is GamePhase.GameComplete or GamePhase.PokerReveal);
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
        if (view is { Phase: GamePhase.PokerReveal, Campaign: { PokerResult: { } poker } reveal })
        {
            RenderPokerReveal(view, reveal, poker);
            return;
        }
        if (view.Phase is GamePhase.HandComplete)
        {
            _actionTitle.Text = "Hand complete";
            var next = ActionButton("Deal next hand");
            var revision = _input.Revision; var generation = _sessionGeneration; var viewer = _viewer;
            next.Click += async (_, _) => { if (AcceptTableInput(revision, generation, viewer, view)) await StartNextHandAsync(next); };
            _actionButtons.Children.Add(next);
            return;
        }
        if (view.Phase is GamePhase.GameComplete)
        {
            _actionTitle.Text = view.Campaign is null
                ? $"{string.Join(" + ", view.Players.Where(player => player.Team == view.GameWinner).Select(player => player.Name))} win"
                : $"Team {view.GameWinner + 1} wins";
            _actionHelp.Text = view.Campaign is null ? $"The race to {view.TargetScore} is complete." : string.Empty;
            if (view.Campaign is null) RenderFreePlayRematch();
            return;
        }
        if (view.Campaign is { } campaign)
        {
            if (_targeting is { } targeting) { RenderTargeting(campaign, targeting); return; }
            _actionTitle.Text = CampaignTableControls.Title(campaign);
            var commands = CampaignTableControls.CenterCommands(campaign).ToArray();
            if (commands.Any(c => c is CampaignCommand.Bid)) { RenderCampaignBid(campaign, commands, view); return; }
            foreach (var command in commands)
            {
                var captured = command;
                AddAction(CampaignTableControls.Describe(command, campaign, view.Players), () => SendCampaignActionAsync(campaign, captured));
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
                if (view.LegalActions.CanPass)
                {
                    AddAction("Pass", () => SendActionAsync(new GameActionRequest(_viewer!.Value, "pass", null, null, null, null)));
                }
                foreach (var bid in view.LegalActions.Bids)
                {
                    var capturedBid = bid;
                    AddAction(BidName(bid), () => SendActionAsync(new GameActionRequest(_viewer!.Value, "bid", capturedBid, null, null, null)));
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

    // Step 1 picks a number; a number with gumballs opens step 2. Trump is chosen later, after the auction is won.
    private void RenderCampaignBid(CampaignGameView campaign, IReadOnlyList<CampaignCommand> commands, GameView view)
    {
        var stepKey = $"{_session?.Id}:{campaign.DealSerial}:{campaign.Revision}";
        if (_bidStepKey != stepKey) { _bidStepKey = stepKey; _bidLevel = null; }
        var bids = commands.OfType<CampaignCommand.Bid>().ToArray();
        if (_bidLevel is { } level && bids.Any(b => b.Level == level))
        {
            _actionTitle.Text = $"Bid {BidName(level)} · add a gumball?";
            var options = bids.Where(b => b.Level == level).OrderBy(b => b.Variant is null ? -1 : 0).ToArray();
            foreach (var option in options)
            {
                var captured = option;
                AddAction(option.Variant is { } variant ? FeatureCatalog.Get(FeatureCategory.Gumball, variant.ToString()).Name : "No gumball",
                    () => SendCampaignActionAsync(campaign, captured));
                if (option.Variant is { } tip)
                    ToolTip.SetTip(_actionButtons.Children[^1], FeatureCatalog.Get(FeatureCategory.Gumball, tip.ToString()).Description);
            }
            AddAction("← Back", () => { _bidLevel = null; RenderActions(_actionView ?? view); return Task.CompletedTask; });
            return;
        }
        _bidLevel = null;
        foreach (var command in commands)
        {
            if (command is CampaignCommand.Bid bid)
            {
                if (_actionButtons.Children.OfType<Button>().Any(b => Equals(b.Tag, bid.Level))) continue;
                var choices = bids.Where(b => b.Level == bid.Level).ToArray();
                // One way to make this bid: send it. Several: ask which gumball.
                var only = choices.Length == 1 ? choices[0].Variant : null;
                AddAction(only is { } forced ? $"{BidName(bid.Level)} · {FeatureCatalog.Get(FeatureCategory.Gumball, forced.ToString()).Name}" : BidName(bid.Level), choices.Length == 1
                    ? () => SendCampaignActionAsync(campaign, choices[0])
                    : () => { _bidLevel = bid.Level; RenderActions(_actionView ?? view); return Task.CompletedTask; });
                _actionButtons.Children[^1].Tag = bid.Level;
                if (only is { } described)
                    ToolTip.SetTip(_actionButtons.Children[^1], FeatureCatalog.Get(FeatureCategory.Gumball, described.ToString()).Description);
            }
            else
            {
                var captured = command;
                AddAction(CampaignTableControls.Describe(command, campaign, view.Players), () => SendCampaignActionAsync(campaign, captured));
            }
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
        if (view.Phase == GamePhase.PokerReveal || campaign.ViewerSeat is not { } seat) return false;
        var hand = campaign.Players[seat].VisibleCards;
        var deal = $"{session.Id}:{campaign.DealSerial}";
        var autoStage = $"{view.Contract?.Mode}:{view.Contract?.Trump}:{_preferences.HandSort}";
        var stage = $"{deal}:{autoStage}";
        if (hand.Count < 2 || _draggedDeal == deal || _autoArranged == stage) return false;
        var sorted = HandOrder.Sort(hand, card => card.Face, _preferences.HandSort, view.Contract).Select(card => card.Id).ToArray();
        var changed = !sorted.SequenceEqual(hand.Select(card => card.Id));
        try { await session.ArrangeCampaignHandAsync(seat, sorted, manuallyArranged: false, autoArrangeStage: autoStage); _autoArranged = stage; return changed; }
        catch (GameRuleException) { return false; }
    }

    private async Task ArrangeCampaignHandAsync(GameSession session, CampaignGameView campaign, int seat, IReadOnlyList<CardId> order)
    {
        try { await session.ArrangeCampaignHandAsync(seat, order); _draggedDeal = $"{session.Id}:{campaign.DealSerial}"; }
        catch (Exception error) { ShowError(error.Message); }
        _renderedView = null;
        await RefreshGameAsync();
    }

    private async Task SendCampaignActionAsync(CampaignGameView view, CampaignCommand command)
    {
        var session = _session; if (session is null || _hotseatPending is not null || _liveSettingsOpen || !_input.TryBeginAction(out var revision)) return;
        var completingPoker = command is CampaignCommand.CompletePoker;
        SyncNativeInput();
        try
        {
            await session.ExecuteCampaignAsync(new CampaignActionRequest(_viewer!.Value, view.Revision, view.DealSerial, command),
                precondition: completingPoker ? () =>
                {
                    if (!CanCompletePoker(session, view)) throw new OperationCanceledException();
                } : null,
                waitForDeal: completingPoker && _motion.Enabled);
            _renderedView = null;
        }
        catch (OperationCanceledException) when (completingPoker) { }
        catch (Exception error) { ShowError(error.Message); }
        finally { _input.CompleteAction(revision); SyncNativeInput(); }
        await RefreshGameAsync();
    }

    private async Task SendActionAsync(GameActionRequest request)
    {
        var session = _session;
        var generation = _sessionGeneration;
        if (session is null || _hotseatPending is not null || _liveSettingsOpen || !_input.TryBeginAction(out var revision)) return;
        SyncNativeInput();
        ClearStatus();
        try
        {
            await session.ExecuteAsync(request, waitForBotObservation: false);
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
        if (session is null || _hotseatPending is not null || _liveSettingsOpen || !_input.TryBeginAction(out var revision)) return;
        SyncNativeInput();
        button.IsEnabled = false;
        ClearStatus();
        try
        {
            await session.StartNextHandAsync(waitForDeal: _motion.Enabled);
            if (generation == _sessionGeneration && ReferenceEquals(session, _session))
            {
                _renderedView = null;
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
            var engine = _repairEngineId is null
                ? await _catalog.LoadAsync(_engineExecutable.Text.Trim(), _engineArguments.Text?.Trim())
                : await _catalog.RepairAsync(_repairEngineId, _engineExecutable.Text.Trim(), _engineArguments.Text?.Trim());
            _repairEngineId = null;
            if (_cancelEngineRepair is not null) _cancelEngineRepair.IsVisible = false;
            _engineExecutable.Text = string.Empty;
            _engineArguments.Text = string.Empty;
            RefreshEngineCatalog();
            _status.Foreground = Green;
            _status.Text = _catalog.PersistenceError ?? $"Loaded {engine.Name} by {engine.Author}.";
            _status.IsVisible = true;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = _repairEngineId is null ? "Handshake & load" : "Handshake & repair";
        }
    }

    private static Border CardFace(Card card, EnhancementId? enhancement = null, SigilId? sigil = null)
    {
        var red = card.Suit is Suit.Hearts or Suit.Diamonds;
        return new Border
        {
            Width = 124,
            Height = 176,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = enhancement is null,
            BorderBrush = Brush.Parse("#D8CDB9"),
            BorderThickness = new Thickness(1),
            Background = enhancement is null ? CardPaper : Brushes.Transparent,
            Child = PixelArt.CardContent(card, enhancement, sigil)
        };
    }

    // A small label on a card's art, such as the order a target was chosen in.
    private static Control WithBadge(Control content, string text, IBrush background, HorizontalAlignment horizontal, VerticalAlignment vertical) => new Grid
    {
        Children =
        {
            content,
            new Border
            {
                Background = background, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3), Margin = new Thickness(6),
                HorizontalAlignment = horizontal, VerticalAlignment = vertical, IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = Paper, FontSize = 18, FontWeight = FontWeight.Bold, FontFamily = PixelArt.Font }
            }
        }
    };

    private static Border WithPlayedAs(Border card, Suit suit)
    {
        var content = card.Child!;
        card.Child = null;
        var symbol = suit switch { Suit.Clubs => "♣", Suit.Diamonds => "♦", Suit.Hearts => "♥", _ => "♠" };
        var badge = new Border
        {
            Background = Paper, BorderBrush = Gold, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"AS {symbol}", FontSize = 20, FontWeight = FontWeight.Bold,
                Foreground = suit is Suit.Hearts or Suit.Diamonds ? Red : Ink
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(badge, $"Played as {suit}");
        card.Child = new Grid { Children = { content, badge } };
        return card;
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
        _screenHost.Content = MainMenu.Build(_campaign.Open, ShowTable, ShowCollection, ShowEngines, ShowRules, ShowSettings, _motion);
    }

    private bool TableShown => !_liveSettingsOpen && ReferenceEquals(_screenHost.Content, _tableScreen)
        && ReferenceEquals(_pageHost.Content, _playHost);

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

    private async void ShowCollection()
    {
        if (_screenHost.Content is not Control menu) return;
        using var preload = new CancellationTokenSource();
        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => preload.Cancel();
        menu.DetachedFromVisualTree += Cancel;
        try
        {
            await PixelArt.PreloadAsync(ScreenArtwork.Collection(FeatureCategory.Joker), preload.Token);
            if (!preload.IsCancellationRequested && ReferenceEquals(_screenHost.Content, menu))
                _screenHost.Content = CollectionPage.Build(ShowMenu, _motion);
        }
        finally { menu.DetachedFromVisualTree -= Cancel; }
    }

    private bool ChangePreferences(GamePreferences preferences)
    {
        _preferences = preferences;
        ApplyGraphics();
        return _preferences.Save(_preferencesPath);
    }

    private void ApplyGraphics()
    {
        if (PixelArt.BackStyle != _preferences.CardBack)
        {
            PixelArt.BackStyle = _preferences.CardBack;
            // Retained hand cards were drawn with the old back; rebuild the table with the new one.
            _liveTable = null;
            _renderedView = null;
        }
        _motion.SetReduced(_preferences.ReducedMotion);
        _motion.SetEnabled(_preferences.MotionEnabled);
        ApplyUiScale();
        WindowState = _preferences.Fullscreen ? WindowState.FullScreen : WindowState.Normal;
        if (!_preferences.Fullscreen)
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            var working = screen?.WorkingArea;
            var scaling = screen?.Scaling ?? 1;
            var width = working is { } area ? area.Width / scaling : _preferences.WindowWidth;
            var height = working is { } area2 ? area2.Height / scaling : _preferences.WindowHeight;
            MinWidth = Math.Min(980, width);
            MinHeight = Math.Min(680, height);
            Width = Math.Min(_preferences.WindowWidth, width);
            Height = Math.Min(_preferences.WindowHeight, height);
        }
    }

    // Keep a minimum logical viewport as the window is resized. The requested scale is retained
    // and restored automatically when enough room becomes available.
    private void ApplyUiScale()
    {
        var width = ClientSize.Width > 0 ? ClientSize.Width : _preferences.WindowWidth;
        var height = ClientSize.Height > 0 ? ClientSize.Height : _preferences.WindowHeight;
        var fitting = Math.Min(width / 980, height / 680);
        var scale = GamePreferences.UiScales.Where(value => value <= _preferences.UiScale && value <= fitting + .001)
            .DefaultIfEmpty(1).Max();
        _scaleHost.LayoutTransform = new ScaleTransform(scale, scale);
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
        if (InCampaignMatch && _campaign.InMatch && _session is { } campaignSession)
        {
            // Quiesce the action gate before deciding; a just-finished match earns its result.
            await campaignSession.PauseAsync();
            var final = await campaignSession.GetViewAsync(0);
            if (final?.Phase == GamePhase.GameComplete) _campaign.MatchCompleted(final.GameWinner == 0, final.Scores, final);
            else _campaign.ForfeitMatch();
        }
        if (_session is not null)
        {
            await LeaveTableAsync(button);
            button.IsEnabled = true;
        }
        if (!_campaign.ShowSaveFailure()) ShowMenu();
    }

    private void ShowSetup()
    {
        _renderedView = null;
        if (_seatNames[0] is not null) _lastTableSetup = CaptureTableSetup();
        _playHost.Content = BuildSetupPage();
        _startButton.IsVisible = true;
        _startButton.IsEnabled = true;
        _startButton.Content = "Start session";
        ClearStatus();
        RefreshEngineCatalog();
    }

    private GameSession? DetachSession()
    {
        ResetHotseat();
        _liveTable = null;
        CancelContractAnnouncement();
        _pokerReveal = null;
        _bidAnnouncement?.Clear();
        _cardFlights?.Cancel(); _deckDeal?.Cancel();
        _input.BeginViewChange();
        _motion.Cancel();
        var session = _session;
        if (session is not null)
        {
            _session = null;
            _sessionGeneration++;
            _renderedWaiting = false;
            _renderedView = null;
        }

        return session;
    }

    private void RenderWaitingTable()
    {
        _liveTable = null;
        ResetScoreHistory();
        _startButton.IsVisible = true;
        _startButton.IsEnabled = true;
        _startButton.Content = "Start session";
        ScoreTally(0).Set(0);
        ScoreTally(1).Set(0);
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
        var revision = _input.Revision; var generation = _sessionGeneration; var viewer = _viewer; var view = _actionView;
        button.Click += async (_, _) => { if (AcceptTableInput(revision, generation, viewer, view)) await action(); };
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

    private static Border ScorePanel(string label, Control score, IBrush accent) => CardPanel(new Grid
    {
        ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,32"), ColumnSpacing = 10, MinWidth = 112,
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
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        HorizontalContentAlignment = HorizontalAlignment.Center,
        MaxWidth = 300,
        MinWidth = 44,
        Margin = new Thickness(3, 0, 3, 6),
        Padding = new Thickness(10, 7),
        Background = Brush.Parse("#143480"),
        Foreground = Brush.Parse("#FFF5DC"),
        BorderBrush = Brush.Parse("#65BFFF"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        FontWeight = FontWeight.Bold,
        FontSize = 13
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
        if (view.Phase == GamePhase.PokerReveal) return "All hands revealed";
        if (view.Contract is null)
        {
            return view.HighBid is null ? "Auction open" : $"{BidName(view.HighBid.Value)} high bid";
        }
        var prefix = BidName(view.Contract.Bid);
        return view.Contract.Mode is ContractMode.Trump
            ? $"{ContractCaller(view)} · {prefix} · {SuitSymbol(view.Contract.Trump!.Value)} {view.Contract.Trump}"
            : $"{ContractCaller(view)} · {prefix} · {view.Contract.Mode}";
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
