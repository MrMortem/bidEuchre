using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    private LiveTable? _liveTable;
    private LiveTable? _buildingTable;

    private sealed class LiveTable(long generation, int? viewer, long deal, bool motion)
    {
        public bool Matches(long currentGeneration, int? currentViewer, long currentDeal, bool currentMotion) =>
            (generation, viewer, deal, motion) == (currentGeneration, currentViewer, currentDeal, currentMotion);
        public Control? Root;
        public readonly SeatControls?[] Seats = new SeatControls?[4];
        public readonly Dictionary<string, Control> HandCards = [];
        public readonly HashSet<string> SeenCards = [];
        public Border? Trick;
        public int TrickNumber;
        public readonly Dictionary<int, TrickCard> TrickCards = [];
        public Contract? Contract;
        public ContentControl? Sort;
        public HandSort? SortOrder;

        public Control HandCard(string key, Func<Control> create)
        {
            SeenCards.Add(key);
            if (!HandCards.TryGetValue(key, out var card)) HandCards.Add(key, card = create());
            return card;
        }
    }

    private sealed record TrickCard(string Key, Card Face, CampaignCardView? Physical, Suit? PlayedAs, Border Control);

    private sealed record SeatControls(Border Tag, TextBlock Name, HandFan Fan, ThinkingDots Thinking, TrickPile Pile);

    /// <summary>The tricks a seat has taken this hand, as a loose face-down pile on its name tag.
    /// Trick collection sweeps onto it, so it keeps its size even while empty.</summary>
    private sealed class TrickPile
    {
        private const int Shown = 6;
        public readonly Canvas Panel = new() { Width = 48, Height = 44, IsHitTestVisible = true, VerticalAlignment = VerticalAlignment.Center };
        private int _count = -1;
        private CompletedTrick? _latest;

        public TrickPile() => Panel.Classes.Add("trick-pile");

        public void Update(int count, CompletedTrick? latest, Func<CompletedTrick, Control> describe)
        {
            if (count == _count && latest == _latest) return;
            _latest = latest;
            ToolTip.SetTip(Panel, count > 0 && latest is not null ? describe(latest) : null);
            if (count == _count) return;
            _count = count;
            Panel.Children.Clear();
            // Each trick sits a little askew on the last; a long run shows only its top few.
            for (var index = 0; index < Math.Min(count, Shown); index++)
            {
                var back = new Border
                {
                    Width = 24, Height = 34, CornerRadius = new CornerRadius(3), ClipToBounds = true,
                    BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Background = Brushes.White,
                    Child = PixelArt.Back(22, 32), RenderTransformOrigin = RelativePoint.Center,
                    RenderTransform = new RotateTransform(index % 2 == 0 ? -6 + index : 5 - index)
                };
                Canvas.SetLeft(back, 3 + index * 4);
                Canvas.SetTop(back, 7 - index * 1.5);
                Panel.Children.Add(back);
            }
            var label = count == 1 ? "1 trick won" : $"{count} tricks won";
            Avalonia.Automation.AutomationProperties.SetName(Panel, label);
        }
    }

    /// <summary>Three dots on a deciding bot's tag, so the table shows that the game is still running.</summary>
    private sealed class ThinkingDots
    {
        public readonly TextBlock Text = new()
        {
            Text = "···", FontSize = 12, FontWeight = FontWeight.Bold, Width = 22, IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        public bool Active;

        public ThinkingDots(PixelMotion motion)
        {
            Text.Classes.Add("thinking-dots");
            Avalonia.Automation.AutomationProperties.SetName(Text, "thinking");
            motion.Ambient(Text, () =>
            {
                Text.IsVisible = Active;
                if (!Active) return;
                // One dot, two, three, held a beat; three still dots when motion is off.
                var step = motion.Enabled ? (int)(PixelMotion.Now / .32 % 4) : 3;
                Text.Text = step switch { 0 => "·", 1 => "··", _ => "···" };
                Text.Opacity = motion.Enabled ? .55 + .45 * Math.Abs(Math.Sin(PixelMotion.Now * Math.PI / 1.28)) : 1;
            });
        }
    }

    private Control BuildTable(GameView view)
    {
        var deal = view.DealSerial ?? view.HandNumber;
        if (_liveTable?.Matches(_sessionGeneration, _viewer, deal, _motion.Enabled) != true)
            _liveTable = new(_sessionGeneration, _viewer, deal, _motion.Enabled);
        var table = _liveTable;
        if (table.Root is null || table.Contract != view.Contract)
        {
            table.Contract = view.Contract;
            _motion.SetContract(view.Contract);
        }
        _buildingTable = table;
        try
        {
            table.SeenCards.Clear();
            var players = view.Players.Select(player => PlayerPanel(player, view)).ToArray();
            var trick = TrickPanel(view);
            table.Sort ??= new ContentControl();
            table.Sort.IsVisible = view.Phase != GamePhase.PokerReveal;
            if (table.SortOrder != _preferences.HandSort)
            {
                table.Sort.Content = SortButtons();
                table.SortOrder = _preferences.HandSort;
            }
            foreach (var key in table.HandCards.Keys.Where(key => !table.SeenCards.Contains(key)).ToArray())
                table.HandCards.Remove(key);
            return table.Root ??= PixelTable(players, trick, table.Sort);
        }
        finally { _buildingTable = null; }
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
                _renderedView = null;
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
        var surface = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = new Grid
            {
                ClipToBounds = true,
                Children = { PixelArt.TableScene(_motion, InCampaignMatch ? _campaign.Run : null),
                new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = canvas } }
            }
        };
        // A boss arena keeps its own art; any other table takes the chosen felt beneath the cards.
        if (!(InCampaignMatch && _campaign.Run is { } run && PixelArt.BossArenaName(run) is not null) &&
            PixelArt.Felt(_preferences.Felt) is { } felt)
            ((Grid)surface.Child!).Children.Insert(1, felt);
        return surface;
    }

    private static readonly IBrush SeatOpponentBrush = Brush.Parse("#740D2D");

    private Control PlayerPanel(PlayerView player, GameView view)
    {
        var current = view.Phase != GamePhase.PokerReveal && view.CurrentSeat == player.Seat;
        var table = _buildingTable;
        var seat = table?.Seats[player.Seat];
        if (seat is null)
        {
            var label = new TextBlock
            {
                MaxWidth = 130, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10, FontWeight = FontWeight.Bold
            };
            var portrait = new Border
            {
                CornerRadius = new CornerRadius(2), Padding = new Thickness(10, 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { PixelArt.Portrait(player.Seat), label }
                }
            };
            var thinking = new ThinkingDots(_motion);
            var pile = new TrickPile();
            ((StackPanel)portrait.Child).Children.Add(thinking.Text);
            ((StackPanel)portrait.Child).Children.Add(pile.Panel);
            seat = new(portrait, label, new HandFan(player.Seat, portrait, _motion), thinking, pile);
            if (table is not null) table.Seats[player.Seat] = seat;
        }
        var tag = seat.Tag;
        tag.Background = current ? Gold : player.Team == 0 ? GreenDark : SeatOpponentBrush;
        seat.Name.Text = $"{(current ? "▶ " : string.Empty)}{player.Name}{(view.Dealer == player.Seat ? "  D" : string.Empty)}{(player.IsSittingOut ? "  · sitting out" : string.Empty)}";
        seat.Name.Foreground = current ? Paper : Ink;
        seat.Thinking.Active = current && _session?.Seats[player.Seat].Kind is { } kind && kind != PlayerKind.Human &&
            view.Phase is not (GamePhase.GameComplete or GamePhase.PokerReveal);
        seat.Thinking.Text.Foreground = Paper;
        seat.Thinking.Text.IsVisible = seat.Thinking.Active;
        // Tricks still showing on the table join the pile when they are collected.
        var showing = view.CurrentTrick.Count == 0 && !_trickCollection.Hidden ? view.CompletedTricks.LastOrDefault() : null;
        var taken = view.CompletedTricks.Where(trick => trick.Winner == player.Seat && trick != showing).ToList();
        seat.Pile.Update(taken.Count, taken.LastOrDefault(), trick => LastTrickView(view, trick, 44));
        _cardFlights?.PlayerSeat(player.Seat, seat.Pile.Panel);
        _deckDeal?.Player(player.Seat, tag);
        if (_motionCue.WinnerSeat == player.Seat) _motion.Pulse(tag);
        else if (current && _motionCue.TurnSeat == player.Seat) _motion.Nudge(tag);
        var cards = new List<Control>();
        var glide = _motion.Enabled && TableShown && _deckDeal?.Running != true;
        Control Slot(string key, Control card) => _handGlide?.Card($"{player.Seat}:{key}", card, glide) ?? card;
        Control Reuse(string key, Func<Control> create) => table?.HandCard($"{player.Seat}:{key}", create) ?? create();
        Button FaceButton(string key, Card face, bool legal, Func<Task>? action = null,
            EnhancementId? enhancement = null, SigilId? sigil = null, int? chosen = null, bool pokerWinner = false)
        {
            var button = (HandCardButton)Reuse(key, CreateCardButton);
            UpdateCardButton(button, face, legal, view, action, enhancement, sigil, chosen, pokerWinner, player.Seat == _viewer);
            return button;
        }
        Action<int, int>? reorder = null;
        if (view.Campaign is { } campaign)
        {
            // The viewer's own hand is shown in the engine's order, which is the order they arranged.
            var own = campaign.ViewerSeat == player.Seat;
            var poker = campaign.PokerResult;
            var winningCards = poker?.Winners.FirstOrDefault(winner => winner.Seat == player.Seat)?.CardIds;
            var shown = campaign.Players[player.Seat].VisibleCards;
            var visible = own ? shown : HandOrder.Sort(shown, card => card.Face, _preferences.HandSort, view.Contract);
            if (own && poker is null && _session is { } session)
                reorder = (from, to) => _ = ArrangeCampaignHandAsync(session, campaign, player.Seat, Move(visible.Select(card => card.Id).ToList(), from, to));
            for (var i = 0; i < player.CardCount; i++)
            {
                if (i < visible.Count)
                {
                    var card = visible[i];
                    var key = $"id{card.Id.Value}";
                    Button button;
                    if (own && _targeting is { } targeting)
                    {
                        // Chosen cards show their result and remain available for deselection at the target limit.
                        var position = targeting.Chosen.IndexOf(card.Id);
                        var result = position < 0 ? card : Preview(targeting, card);
                        var id = card.Id;
                        var canChoose = targeting.Required <= 1 || targeting.Chosen.Count < targeting.Required || position >= 0;
                        button = FaceButton(key, result.Face, canChoose, () => { ToggleTarget(id); return Task.CompletedTask; }, result.Enhancement, result.Sigil,
                            chosen: position < 0 ? null : targeting.Required > 1 ? position + 1 : 0);
                        button.IsEnabled = canChoose;
                    }
                    else
                    {
                        var choices = campaign.LegalActions.Where(c => CampaignTableControls.CardOf(c) == card.Id).ToArray();
                        button = FaceButton(key, card.Face, choices.Length > 0,
                            choices.Length == 1 ? () => SendCampaignActionAsync(campaign, choices[0]) : () => Task.CompletedTask,
                            card.Enhancement, card.Sigil, pokerWinner: winningCards?.Contains(card.Id) == true);
                        if (choices.Length > 1)
                        {
                            var flyout = new Flyout();
                            var options = new StackPanel { Spacing = 6, MaxWidth = 250 };
                            foreach (var choice in choices)
                            {
                                var captured = choice;
                                var option = ActionButton(CampaignTableControls.Describe(choice, campaign, view.Players));
                                option.Click += async (_, _) => { flyout.Hide(); await SendCampaignActionAsync(campaign, captured); };
                                options.Children.Add(option);
                            }
                            flyout.Content = options;
                            button.Flyout = flyout;
                        }
                    }
                    button.TabIndex = i;
                    ToolTip.SetTip(button, CampaignTableControls.CardTip(card));
                    ToolTip.SetShowDelay(button, 120);
                    _cardFlights?.HandCard($"{player.Seat}:{key}", button, HandFan.Angle(player.Seat, i, player.CardCount));
                    var dealt = card;
                    _deckDeal?.Card(player.Seat, i, button, HandFan.Angle(player.Seat, i, player.CardCount),
                        () => CardFace(dealt.Face, dealt.Enhancement, dealt.Sigil));
                    cards.Add(Slot(key, button));
                }
                else { var back = Reuse($"#{i}", FanCardBack); _deckDeal?.Card(player.Seat, i, back, HandFan.Angle(player.Seat, i, player.CardCount)); cards.Add(Slot($"#{i}", back)); }
            }
        }
        else if (player.Cards is null)
        {
            for (var index = 0; index < player.CardCount; index++)
            {
                var back = Reuse($"#{index}", FanCardBack);
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
                _renderedView = null;
                _ = RefreshGameAsync();
            };
            foreach (var card in ordered)
            {
                var legal = current && view.LegalActions.Cards.Contains(card);
                var button = FaceButton(card.Code, card, legal);
                button.TabIndex = index;
                _cardFlights?.HandCard($"{player.Seat}:{card.Code}", button,
                    HandFan.Angle(player.Seat, index, player.Cards.Count));
                var dealt = card;
                _deckDeal?.Card(player.Seat, index, button, HandFan.Angle(player.Seat, index, player.Cards.Count), () => CardFace(dealt));
                cards.Add(Slot(card.Code, button));
                index++;
            }
        }

        seat.Fan.Update(cards, reorder);
        seat.Fan.Panel.Opacity = player.IsSittingOut ? 0.5 : 1;
        return seat.Fan.Panel;
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

        var table = _buildingTable;
        var panel = table?.Trick ?? CreateTrickPanel();
        var content = (StackPanel)panel.Child!;
        var cards = (Grid)content.Children[0];
        var label = (TextBlock)content.Children[1];
        var trickNumber = completed?.Number ?? view.CompletedTricks.Count + 1;
        if (table is not null)
        {
            table.Trick = panel;
            if (table.TrickNumber != trickNumber || plays.Count == 0)
            {
                cards.Children.Clear();
                table.TrickCards.Clear();
                table.TrickNumber = trickNumber;
            }
        }
        foreach (var play in plays)
        {
            // Match the public physical card by seat, not its printed face: copies may have different enhancements.
            var physical = view.Campaign is { } cv ? (cv.Trick.Count > 0 ? cv.Trick : cv.LastTrick).FirstOrDefault(p => p.Seat == play.Seat) : null;
            var enhancement = physical?.Card.Enhancement;
            var sigil = physical?.Card.Sigil;
            // A Wild card shows the suit it was played as.
            var playedAs = enhancement == EnhancementId.Wild && physical is not null && physical.EffectiveSuit != play.Card.Suit ? physical.EffectiveSuit : (Suit?)null;
            Border Face() => playedAs is { } suit ? WithPlayedAs(CardFace(play.Card, enhancement, sigil), suit) : CardFace(play.Card, enhancement, sigil);
            var key = physical is null ? $"{play.Seat}:{play.Card.Code}" : $"{play.Seat}:id{physical.Card.Id.Value}";
            var existing = table?.TrickCards.GetValueOrDefault(play.Seat);
            Border card;
            if (existing is not null && existing.Key == key && existing.Face == play.Card && existing.Physical == physical?.Card && existing.PlayedAs == playedAs)
                card = existing.Control;
            else
            {
                if (existing is not null) cards.Children.Remove(existing.Control);
                card = Face();
                if (physical is not null)
                {
                    ToolTip.SetTip(card, CampaignTableControls.CardTip(physical.Card, playedAs));
                    ToolTip.SetShowDelay(card, 120);
                }
                card.ZIndex = play.Seat switch { 2 => 0, 1 => 1, 3 => 2, _ => 3 };
                card.HorizontalAlignment = play.Seat switch { 1 => HorizontalAlignment.Left, 3 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
                card.VerticalAlignment = play.Seat switch { 2 => VerticalAlignment.Top, 0 => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
                cards.Children.Add(card);
                if (table is not null) table.TrickCards[play.Seat] = new(key, play.Card, physical?.Card, playedAs, card);
            }
            // A scoring card jolts once per effect it triggers; that replaces the winner's pulse.
            var jolting = _scoreJolts.Apply(key, card, _motion);
            if (completed?.Winner == play.Seat)
            {
                card.BorderBrush = Gold;
                card.BorderThickness = new Thickness(3);
                // The winner glows and sits above the pile so it stays readable through the hold. The face clips
                // to its corners, which would hide its own shadow, so the glow is a layer just beneath it.
                card.ZIndex = 5;
                if (!cards.Children.OfType<Border>().Any(child => child.Classes.Contains("winning-trick-glow")))
                {
                    var glow = CardShadow.Glow(card, WinningTrickGlow);
                    glow.Classes.Add("winning-trick-glow");
                    cards.Children.Add(glow);
                }
                if (_motionCue.WinnerSeat is not null && !jolting) _motion.Pulse(card, 1.065);
            }
            else if (completed is not null && card.Child is { } ink)
            {
                // Losing cards fade toward the paper; the card's own opacity belongs to the flights.
                ink.Opacity = .55;
                card.Classes.Add("losing-trick-card");
            }
            _cardFlights?.PlayedCard(key, card, _motionCue.AddedPlays.Contains(key), Face);
        }

        label.IsVisible = view.Phase != GamePhase.PokerReveal;
        label.Text = completed is null
            ? (plays.Count == 0 ? "Cards played here" : "Current trick")
                + (view.CompletedTricks.Count > 0 ? " · hold L for last trick" : "")
            : _trickCollection.Hidden ? "Trick collected · hold L to review"
            : $"{view.Players[completed.Winner].Name} won trick {completed.Number}";
        label.Foreground = completed is null ? TrickLabelBrush : Gold;
        label.FontWeight = completed is null ? FontWeight.Normal : FontWeight.Bold;
        return panel;
    }

    private static readonly BoxShadows WinningTrickGlow = BoxShadows.Parse("0 0 18 4 #C8FFDB62");
    private static readonly IBrush TrickLabelBrush = Brush.Parse("#AFC4B8");

    private static Border CreateTrickPanel() => new()
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
                Children =
                {
                    new Grid
                    {
                        Width = 330, Height = 250, RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
                        RenderTransform = new ScaleTransform(HandFan.CardScale, HandFan.CardScale)
                    },
                    new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
    };

    private sealed class HandCardButton : Button
    {
        public readonly Border Surface = new();
        public readonly ContentPresenter Face = new();
        public readonly TranslateTransform Lift = new();
        /// <summary>Squashes the card a little while it is held down, separately from its hover lift.</summary>
        public readonly ScaleTransform Press = new(1, 1);
        /// <summary>Darkens a card that cannot be played on the viewer's turn without making the fan see-through.</summary>
        public readonly Border Shade = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(120, 12, 16, 28)), IsHitTestVisible = false, Opacity = 0,
            CornerRadius = new CornerRadius(8)
        };
        public bool Dimmed;
        public (Card Card, EnhancementId? Enhancement, SigilId? Sigil, int? Chosen)? Art;
        public Func<Task>? Activate;
        public bool Playable;
        public bool PokerWinner;
        public Control? Slot;

        public void RefreshHighlight()
        {
            var highlighted = Playable && ((Slot?.IsPointerOver ?? IsPointerOver) || IsKeyboardFocusWithin);
            Lift.Y = Art?.Chosen is not null || PokerWinner ? -16 : highlighted ? -8 : Dimmed ? 4 : 0;
            Shade.Opacity = Dimmed ? 1 : 0;
            Surface.BorderBrush = highlighted && Art?.Chosen is null ? Gold : BorderBrush;
        }
    }

    private Button CardButton(Card card, bool legal, GameView view, Func<Task>? campaignAction = null,
        EnhancementId? enhancement = null, SigilId? sigil = null, int? chosen = null)
    {
        var button = CreateCardButton();
        UpdateCardButton(button, card, legal, view, campaignAction, enhancement, sigil, chosen, pokerWinner: false);
        return button;
    }

    private HandCardButton CreateCardButton()
    {
        var button = new HandCardButton
        {
            Width = 124, Height = 176, Margin = new Thickness(2), Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        button.Face.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Face.VerticalAlignment = VerticalAlignment.Stretch;
        button.Surface.CornerRadius = button.CornerRadius;
        button.Surface.Child = new Grid { Children = { button.Face, button.Shade } };
        button.Surface.RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative);
        button.Surface.RenderTransform = button.Press;
        button.Template = new FuncControlTemplate<Button>((_, _) => button.Surface);
        button.RenderTransform = button.Lift;
        if (_motion.Enabled)
            button.Lift.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.YProperty,
                    Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() }
            };
        if (_motion.Enabled)
            button.Press.Transitions =
            [
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(90), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(90), Easing = new CubicEaseOut() }
            ];
        // A playable card gives under the pointer or Space before it leaves the hand.
        button.PropertyChanged += (_, args) =>
        {
            if (args.Property != Button.IsPressedProperty) return;
            var squash = button.IsPressed && button.Playable && _motion.Enabled;
            button.Press.ScaleX = squash ? .95 : 1;
            button.Press.ScaleY = squash ? .93 : 1;
        };
        if (_motion.Enabled)
            button.Shade.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];

        void SlotPointer(object? sender, PointerEventArgs args) => button.RefreshHighlight();
        button.AttachedToVisualTree += (_, _) =>
        {
            button.Slot = button.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Classes.Contains(HandFan.SlotClass));
            if (button.Slot is not { } slot) return;
            slot.PointerEntered += SlotPointer;
            slot.PointerExited += SlotPointer;
            button.RefreshHighlight();
        };
        button.DetachedFromVisualTree += (_, _) =>
        {
            if (button.Slot is not { } slot) return;
            slot.PointerEntered -= SlotPointer;
            slot.PointerExited -= SlotPointer;
            button.Slot = null;
        };
        button.PointerEntered += (_, _) => button.RefreshHighlight();
        button.PointerExited += (_, _) => button.RefreshHighlight();
        button.GotFocus += (_, _) => button.RefreshHighlight();
        button.LostFocus += (_, _) => button.RefreshHighlight();
        button.Click += async (_, _) => { if (button.Activate is { } activate) await activate(); };
        return button;
    }

    private static readonly IBrush IdleCardBorder = Brush.Parse("#D8CDB9");
    private static readonly Cursor PlayCardCursor = new(StandardCursorType.Hand);

    private void UpdateCardButton(HandCardButton button, Card card, bool legal, GameView view,
        Func<Task>? action, EnhancementId? enhancement, SigilId? sigil, int? chosen, bool pokerWinner, bool ownHand = false)
    {
        var art = (card, enhancement, sigil, chosen);
        if (button.Art?.Card != card || button.PokerWinner != pokerWinner)
            Avalonia.Automation.AutomationProperties.SetName(button,
                $"{RankText(card.Rank)} of {card.Suit}{(pokerWinner ? " · winning Poker card" : "")}");
        if (button.Art != art)
        {
            button.Art = art;
            var content = PixelArt.CardContent(card, enhancement, sigil);
            if (chosen is > 0) content = WithBadge(content, chosen.Value.ToString(), Green, HorizontalAlignment.Right, VerticalAlignment.Top);
            button.Content = button.Face.Content = content;
            button.Background = button.Surface.Background = enhancement is null ? CardPaper : Brushes.Transparent;
            button.Surface.ClipToBounds = enhancement is null;
            button.Foreground = card.Suit is Suit.Hearts or Suit.Diamonds ? CardRed : CardInk;
        }
        button.Flyout = null;
        button.IsEnabled = true;
        button.Playable = button.Focusable = legal;
        button.Cursor = legal ? PlayCardCursor : null;
        button.PokerWinner = pokerWinner;
        // Only the viewer's own turn to play separates legal from illegal cards; otherwise the hand stays bright.
        button.Dimmed = ownHand && !legal && !pokerWinner && chosen is null && view.Phase is GamePhase.Playing && view.CurrentSeat == _viewer;
        button.Classes.Set("unplayable-card", button.Dimmed);
        button.Classes.Set("poker-winning-card", pokerWinner);
        button.BorderBrush = chosen is not null ? Green : legal || pokerWinner ? Gold : IdleCardBorder;
        button.BorderThickness = button.Surface.BorderThickness = new Thickness(chosen is not null || pokerWinner ? 5 : legal ? 3 : 1);
        button.RefreshHighlight();

        var revision = _input.Revision;
        var generation = _sessionGeneration;
        var viewer = _viewer;
        button.Activate = legal ? async () =>
        {
            if (!AcceptTableInput(revision, generation, viewer, view)) return;
            if (action is not null) { await action(); return; }
            var type = view.Phase is GamePhase.Playing ? "play" : "exchange";
            await SendActionAsync(new GameActionRequest(viewer!.Value, type, null, null, null, card.Code));
        } : null;
    }
}
