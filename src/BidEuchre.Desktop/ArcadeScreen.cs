using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

internal sealed record ArcadeActions(Func<ArcadeGame, int, ArcadeResult?> Play, Action<string> Keep,
    Action<string> Use, Action<string> Dismiss, Action Back, Action Refresh)
{
    public Action<FeatureCategory, int>? Discard { get; init; }
    /// <summary>Open the pending Collector's Pack by picking card 0–2.</summary>
    public Action<string, int>? Pick { get; init; }
}

internal static class ArcadeScreen
{
    public static Control Build(CampaignRun run, ArcadeActions actions, PixelMotion motion, string? note = null, IReadOnlyList<JokerId>? granted = null)
    {
        var arcade = run.Shop!.Arcade ?? new ArcadeState();
        var pending = arcade.Pending;
        var game = pending?.Game ?? ArcadeGame.Plinko;
        var lane = pending?.Game == ArcadeGame.Plinko ? pending.Choice : 4;
        var pocket = pending?.Game == ArcadeGame.Roulette ? pending.Choice : 1;
        var money = run.Money;
        var busy = false;
        var detached = false;
        CancellationTokenSource? animation = null;
        var panel = new StackPanel { Name = "ArcadeScreen", Spacing = 14, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Resources["ButtonBackgroundDisabled"] = MainWindow.Panel;
        panel.Resources["ButtonForegroundDisabled"] = Muted;
        panel.Resources["ButtonBorderBrushDisabled"] = MainWindow.Line;
        panel.DetachedFromVisualTree += (_, _) => { detached = true; animation?.Cancel(); };
        motion.Ambient(panel, () => { if (!motion.Enabled) animation?.Cancel(); });
        var moneyLabel = CampaignShopScreen.Label($"${money}", Gold, 26); moneyLabel.Name = "ArcadeMoney";
        var back = MenuParts.Button("Back to Shop", false, motion, "ArcadeBack", () => { if (!busy && pending is null) actions.Back(); }, true, 220);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 35, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, "ARCADE", 36), moneyLabel, back }
        });
        panel.Children.Add(Text($"${CampaignArcade.Price} per game · Play each game once per shop visit", Gold, 14));
        if (note is not null) { var notice = Text(note, Brush.Parse("#FF617B"), 13); notice.Name = "CampaignNote"; panel.Children.Add(notice); }
        if (granted is { Count: > 0 })
        {
            var names = string.Join(", ", granted.Select(id => FeatureCatalog.Get(FeatureCategory.Joker, id.ToString()).Name));
            var legendary = Text($"LEGENDARY! New Jokers: {names}", Gold, 15); legendary.Name = "CollectorLegendaryNote";
            legendary.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(legendary);
        }
        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var stage = new Border { Width = 420, Height = 460 };
        var detail = new Border
        {
            Width = 344, MinHeight = 450, Background = Brush.Parse("#DD100B28"), BorderBrush = MainWindow.Line,
            BorderThickness = new Thickness(2), Padding = new Thickness(18), CornerRadius = new CornerRadius(6)
        };
        PlinkoControl? plinko = null;
        RouletteControl? roulette = null;
        Button plinkoTab = null!, rouletteTab = null!;
        plinkoTab = MenuParts.Button("Plinko", false, motion, "ArcadePlinko", () => Select(ArcadeGame.Plinko), true, 220);
        rouletteTab = MenuParts.Button("Roulette", false, motion, "ArcadeRoulette", () => Select(ArcadeGame.Roulette), true, 220);
        selectors.Children.Add(plinkoTab); selectors.Children.Add(rouletteTab); panel.Children.Add(selectors);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center, Children = { stage, detail } });

        void Select(ArcadeGame selected)
        {
            if (busy || pending is not null) return;
            game = selected;
            Draw();
        }
        void Draw()
        {
            back.IsEnabled = pending is null && !busy;
            plinkoTab.IsEnabled = rouletteTab.IsEnabled = pending is null && !busy;
            plinkoTab.BorderBrush = game == ArcadeGame.Plinko ? Gold : MainWindow.Line;
            rouletteTab.BorderBrush = game == ArcadeGame.Roulette ? Gold : MainWindow.Line;
            plinkoTab.Background = game == ArcadeGame.Plinko ? Green : MainWindow.Panel;
            rouletteTab.Background = game == ArcadeGame.Roulette ? Green : MainWindow.Panel;
            plinkoTab.Foreground = game == ArcadeGame.Plinko ? Paper : MainWindow.Ink;
            rouletteTab.Foreground = game == ArcadeGame.Roulette ? Paper : MainWindow.Ink;
            if (game == ArcadeGame.Plinko)
            {
                plinko = new PlinkoControl(); plinko.Choose(lane); stage.Child = plinko;
                var playable = pending is null && !busy && !arcade.PlinkoPlayed && money >= CampaignArcade.Price;
                // With motion on, the dropper sweeps the rail and the player times the drop. With motion off,
                // the lane buttons and dragging the dropper pick a lane directly.
                if (playable && motion.Enabled) plinko.Swing(motion);
                plinko.Draggable = playable && !motion.Enabled;
                plinko.LaneDragged = dragged => { if (busy || pending is not null) return; lane = dragged; Choices(); };
                plinko.DropRequested = Play;
            }
            else { roulette = new RouletteControl(); stage.Child = roulette; }
            if (pending is { } result) { Frame(result, 1); Reveal(result); }
            else Choices();
        }
        void Choices(bool focusSelection = false)
        {
            var played = game == ArcadeGame.Plinko ? arcade.PlinkoPlayed : arcade.RoulettePlayed;
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(Text(game == ArcadeGame.Plinko ? "DROP A BALL" : "PICK A POCKET", Gold, 19));
            var timed = game == ArcadeGame.Plinko && plinko?.Swinging == true;
            body.Children.Add(Text(game == ArcadeGame.Plinko
                ? timed ? "The dropper swings along the rail. Time your drop: the ball falls from wherever the dropper is when you press Drop Ball or click the board."
                    : "Choose a drop position. Land in a high-scoring slot for a Sigil or an arcade ticket."
                : "Choose a number from 1 to 12. Match it for a Sigil; match its color for an arcade ticket.", MainWindow.Ink, 14));
            body.Children.Add(Text(game == ArcadeGame.Plinko ? "5–6 points · Sigil\n3–4 points · Ticket\n1–2 points · No prize" : "Exact number · 100 points\nSame color · 50 points\nOther color · No prize", Muted, 13));
            var choices = new WrapPanel { Width = 300, HorizontalAlignment = HorizontalAlignment.Center };
            Button? selectedButton = null;
            var count = game == ArcadeGame.Plinko ? CampaignArcade.PlinkoLaneCount : CampaignArcade.RoulettePocketCount;
            for (var i = 0; i < count; i++)
            {
                var choice = game == ArcadeGame.Plinko ? i : i + 1;
                var chosen = game == ArcadeGame.Plinko ? lane == choice : pocket == choice;
                var button = MenuParts.Button((i + 1).ToString(), chosen, motion,
                    (game == ArcadeGame.Plinko ? "PlinkoLane" : "RoulettePocket") + choice, () =>
                    {
                        if (game == ArcadeGame.Plinko) { lane = choice; plinko!.Choose(lane); } else pocket = choice;
                        Choices(true);
                    }, true, 46);
                button.Margin = new Thickness(2); button.Padding = new Thickness(0, 9);
                if (game == ArcadeGame.Roulette) { button.Background = choice % 2 == 1 ? Brush.Parse("#AD315B") : Brush.Parse("#221A3D"); button.Foreground = Brushes.White; }
                button.BorderBrush = chosen ? Gold : MainWindow.Line;
                button.BorderThickness = new Thickness(chosen ? 3 : 1);
                button.IsEnabled = !played && money >= CampaignArcade.Price;
                AutomationProperties.SetName(button, game == ArcadeGame.Plinko ? $"Lane {i + 1}{(chosen ? ", selected" : "")}" : $"Pocket {choice}, {(choice % 2 == 1 ? "red" : "black")}{(chosen ? ", selected" : "")}");
                choices.Children.Add(button);
                if (chosen) selectedButton = button;
            }
            if (!timed) body.Children.Add(choices);
            else
            {
                var hint = Text("Edge lanes reach the 5 and 6 slots more often.", Gold, 13);
                hint.Name = "PlinkoTimingHint"; body.Children.Add(hint);
            }
            body.Children.Add(Text(played ? "Played this visit. Try the other game." : money < CampaignArcade.Price ? $"You need ${CampaignArcade.Price - money} more." : "One play this visit. Rerolls do not reset the Arcade.", Muted, 12));
            var play = MenuParts.Button(played ? "Played" : game == ArcadeGame.Plinko ? $"Drop Ball · ${CampaignArcade.Price}" : $"Spin Wheel · ${CampaignArcade.Price}", true, motion, "ArcadePlay", Play, true, 300);
            play.IsEnabled = !played && money >= CampaignArcade.Price;
            body.Children.Add(play); detail.Child = body;
            if (focusSelection) Dispatcher.UIThread.Post(() => selectedButton?.Focus());
        }
        async void Play()
        {
            if (busy || pending is not null) return;
            busy = true;
            // A timed drop takes the lane under the nozzle at the moment of the press.
            double? releasedAt = null;
            if (game == ArcadeGame.Plinko && plinko?.Swinging == true) { releasedAt = plinko.DropperX; lane = plinko.StopSwing(); }
            if (actions.Play(game, game == ArcadeGame.Plinko ? lane : pocket) is not { } result) { actions.Refresh(); return; }
            pending = result;
            if (plinko is not null) plinko.Draggable = false;
            money -= CampaignArcade.Price; moneyLabel.Text = $"${money}";
            back.IsEnabled = plinkoTab.IsEnabled = rouletteTab.IsEnabled = false;
            detail.Child = new StackPanel
            {
                Spacing = 16, Children = { Text(game == ArcadeGame.Plinko ? "BALL IN PLAY" : "WHEEL SPINNING", Gold, 19), Text("Good luck!", MainWindow.Ink, 16) }
            };
            if (motion.Enabled)
            {
                animation = new CancellationTokenSource();
                try
                {
                    // The stopped dropper slides the last few pixels onto its lane before the ball falls.
                    if (releasedAt is { } from) await Tween.Run(90, animation.Token, t => plinko!.Settle(from, MachineFx.EaseOut(t)));
                    await Tween.Run(game == ArcadeGame.Plinko ? 3300 : 4000, animation.Token, t => Frame(result, t));
                }
                catch (OperationCanceledException) { if (detached) return; }
                finally { animation.Dispose(); animation = null; }
            }
            if (detached) return;
            Frame(result, 1); busy = false; Reveal(result);
            MenuParts.FocusFirst(detail);
        }
        void Frame(ArcadeResult result, double t)
        {
            if (result.Game == ArcadeGame.Plinko) plinko!.Frame(result, t); else roulette!.Frame(result, t);
        }
        void Reveal(ArcadeResult result)
        {
            var body = new StackPanel { Spacing = 10 };
            var heading = Text($"{result.Score} POINTS", Gold, 24); heading.Name = "ArcadeScore"; body.Children.Add(heading);
            var landing = Text(result.Game == ArcadeGame.Plinko ? $"Landed in slot {result.Landing + 1}" : $"Winning pocket {result.Landing} · {(result.Landing % 2 == 1 ? "Red" : "Black")}", MainWindow.Ink, 14);
            landing.Name = "ArcadeLanding"; body.Children.Add(landing);
            if (CampaignArcade.IsPack(result))
            {
                body.Children.Add(Text("COLLECTOR'S PACK!", CampaignShopScreen.Ink(FeatureCategory.Sigil), 13));
                body.Children.Add(Text("Choose one card. A Sigil must go on one of six offered cards; it cannot be kept.", MainWindow.Ink, 13));
                body.Children.Add(CollectorPackView.Build(result, motion, index => actions.Pick?.Invoke(result.PlayId, index),
                    CampaignArcade.JokerSlots - run.Resources.Inventory.Jokers.Length));
                if (result.PackCards!.Contains(CampaignArcade.LegendaryKey))
                    body.Children.Add(Text("Legendary: fills your empty Joker slots with random Jokers.", Gold, 12));
            }
            else if (result.PrizeCategory is { } category)
            {
                var definition = FeatureCatalog.Get(category, result.FeatureId!);
                var artwork = PixelArt.CampaignIcon(definition, 110); artwork.HorizontalAlignment = HorizontalAlignment.Center;
                body.Children.Add(artwork);
                body.Children.Add(Text(category == FeatureCategory.Sigil ? "SIGIL WON!" : "TICKET WON!", CampaignShopScreen.Ink(category), 13));
                var prize = Text(definition.Name, Gold, 19); prize.Name = "ArcadePrize"; body.Children.Add(prize);
                body.Children.Add(Text(definition.Description, MainWindow.Ink, 13));
                body.Children.Add(Text(run.Resources.Inventory.HasItemRoom
                    ? "Keep for a match, or use on six offered cards."
                    : "Your card slots are full. Use this prize now, or discard a stored card to keep it.", Muted, 12));
                var keep = MenuParts.Button("Keep", false, motion, "ArcadeKeep", () => actions.Keep(result.PlayId), true, 300);
                keep.IsEnabled = run.Resources.Inventory.HasItemRoom; body.Children.Add(keep);
                body.Children.Add(MenuParts.Button("Use Now", true, motion, "ArcadeUse", () => actions.Use(result.PlayId), true, 300));
                body.Children.Add(Text("Use Now commits the prize. Inventory tickets can share its six cards.", Muted, 11));
                if (!run.Resources.Inventory.HasItemRoom && actions.Discard is { } discard)
                {
                    var stored = new StackPanel { Spacing = 4 };
                    foreach (var (itemCategory, slot, item) in CampaignTableControls.Items(run.Resources.Inventory))
                    {
                        var button = MenuParts.Button("Discard " + item.Name, false, motion,
                            $"ArcadeDiscard{itemCategory}{slot}", () => discard(itemCategory, slot), true, 280);
                        button.Padding = new Thickness(10, 9);
                        foreach (var label in ((Grid)button.Content!).Children.OfType<TextBlock>()) label.FontSize = 13;
                        stored.Children.Add(button);
                    }
                    body.Children.Add(new Expander
                    {
                        Name = "ArcadeFreeSlot", Header = Text("Free a card slot", Gold, 13),
                        HorizontalAlignment = HorizontalAlignment.Stretch, Content = stored
                    });
                }
            }
            else
            {
                body.Children.Add(Text("No prize this time", Muted, 18));
                body.Children.Add(MenuParts.Button("Done", true, motion, "ArcadeDone", () => actions.Dismiss(result.PlayId), true, 300));
            }
            detail.Child = body;
        }
        Draw();
        return MenuParts.Scene(motion, panel, new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
    }

    private static TextBlock Text(string text, IBrush ink, double size) => new()
    {
        Text = text, Foreground = ink, FontSize = size, FontFamily = size >= 18 ? PixelArt.Font : FontFamily.Default,
        TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.4
    };
}
