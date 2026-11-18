using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

// The screens between campaign matches. Each one only displays the run it is given.
internal static class CampaignScreens
{
    public static Control PlayChoice(CampaignRun saved, Action resume, Action newRun, Action back, PixelMotion motion)
    {
        var buttons = new StackPanel { Spacing = 12 };
        // The first set arrives with the rest of the screen; later swaps come in at once.
        var delay = ButtonDelay(1);
        void Show(params Control[] controls)
        {
            buttons.Children.Clear();
            for (var index = 0; index < controls.Length; index++)
                buttons.Children.Add(motion.Enter(controls[index], delay + index * 60, 0, 12));
            delay = 0;
        }
        void ShowChoices()
        {
            Show(
                ScreenButton(motion, "Continue", resume, primary: true, stay: true),
                ScreenButton(motion, "New Run", ConfirmDiscard, stay: true),
                ScreenButton(motion, "Back", back, stay: true));
            MenuParts.FocusFirst(buttons);
        }
        // Starting over destroys the saved run, so it takes a second, explicit press.
        // Focus rests on No: a repeated Enter must not confirm the discard.
        void ConfirmDiscard()
        {
            var no = ScreenButton(motion, "No", ShowChoices, name: "CampaignDiscardNo", stay: true);
            Show(
                Line("Discard the current run?", Ink),
                ScreenButton(motion, "Yes", newRun, primary: true, name: "CampaignDiscardYes"),
                no);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => no.Focus());
        }
        ShowChoices();
        return Frame(motion, "CAMPAIGN",
            [Line($"ANTE {saved.Ante} · MATCH {saved.MatchInAnte}/3 · ♥ {saved.Lives} · ${saved.Money}", Green)],
            buttons, null);
    }

    public static Control Recovery(string message, Action action, Action back, PixelMotion motion, string actionLabel = "New Run") =>
        Frame(motion, "CAMPAIGN", [Line(message, Danger)],
            Buttons(motion, 1, ScreenButton(motion, actionLabel, action, primary: true, stay: true), ScreenButton(motion, "Back", back, stay: true)), null);

    public static Control Result(CampaignRun run, MatchOutcome outcome, int[]? scores, Action next, string? note, PixelMotion motion, GameView? summary = null)
    {
        var lines = new List<Control>();
        if (scores is not null) lines.Add(Line($"{scores[0]} – {scores[1]}", Ink, 28));
        if (outcome.Forfeited) lines.Add(Line("Match forfeited", Danger));
        lines.Add(Line(outcome.LivesLost > 0 ? $"LIVES −{outcome.LivesLost} · ♥ {Math.Max(0, run.Lives)}" : $"♥ {run.Lives}",
            outcome.LivesLost > 0 ? Danger : Green));
        lines.Add(Line($"BASE +${outcome.BaseReward} · WIN MARGIN +${outcome.MarginBonus}", Muted));
        lines.Add(Line("Card and Joker money is included in your balance.", Muted));
        lines.Add(Line($"INTEREST +${outcome.Interest}", Muted));
        lines.Add(Total(outcome, motion, ButtonDelay(lines.Count)));
        if (summary is not null) lines.Add(ScoreHistoryControls.Summary(summary, wholeMatch: true, height: 190));
        return Frame(motion, outcome.Won ? "MATCH WON" : "MATCH LOST", lines,
            Buttons(motion, lines.Count, ScreenButton(motion, "Continue", next, primary: true, stay: true)), note, scroll: true);
    }

    public static Control Shop(CampaignRun run, Action next, PixelMotion motion) =>
        Frame(motion, "SHOP", [Line($"${run.Money}", Ink, 28), Line("Nothing for sale yet", Muted)],
            Buttons(motion, 2, ScreenButton(motion, "Next Match", next, primary: true)), null);

    public static Control Victory(CampaignRun run, Action continueEndless, Action menu, PixelMotion motion)
    {
        List<Control> lines =
        [
            Line("You beat the Ante 5 boss.", Ink),
            Line($"MATCHES WON {run.MatchesWon}", Green),
            Line($"♥ {run.Lives}   ${run.Money}", Ink, 20),
            .. JokerLine(run)
        ];
        var scene = (Grid)Frame(motion, "CAMPAIGN WON", lines,
            Buttons(motion, lines.Count, ScreenButton(motion, "Continue Endless", continueEndless, primary: true, stay: true), ScreenButton(motion, "Main Menu", menu, stay: true)),
            null, halo: true);
        // Behind the content, above the table art: the suits rain down in celebration.
        scene.Children.Insert(2, Confetti(motion));
        return scene;
    }

    public static Control RunOver(CampaignRun run, Action menu, PixelMotion motion)
    {
        List<Control> lines =
        [
            Line($"REACHED ANTE {run.Ante} · MATCH {run.MatchInAnte}", Danger),
            Line($"MATCHES WON {run.MatchesWon}", Ink),
            Line($"${run.Money}", Ink, 20),
            .. JokerLine(run)
        ];
        var scene = (Grid)Frame(motion, "RUN OVER", lines,
            Buttons(motion, lines.Count, ScreenButton(motion, "Main Menu", menu, primary: true)), null, titleMilliseconds: 900);
        // The table dims behind a run that has ended.
        var dusk = new Border { Background = Brush.Parse("#B8050310"), IsHitTestVisible = false, Name = "CampaignRunOverDusk" };
        scene.Children.Insert(2, motion.Enter(dusk, 0, 0, 0, duration: 900));
        return scene;
    }

    private static IEnumerable<Control> JokerLine(CampaignRun run)
    {
        var jokers = run.Resources.Inventory.Jokers;
        if (jokers.Length == 0) yield break;
        yield return Line("JOKERS · " + string.Join(" · ", jokers.Select(joker => FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString()).Name)), Muted);
    }

    private static readonly IBrush[] SuitInks = [Brush.Parse("#FFF5DC"), Brush.Parse("#FF617B"), Brush.Parse("#FFDB62"), Brush.Parse("#55BCFF")];

    /// <summary>Suit symbols falling and turning across the whole screen; scattered and still with motion off.</summary>
    private static Canvas Confetti(PixelMotion motion)
    {
        const int pieces = 42;
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Name = "CampaignConfetti" };
        var random = new Random(52);
        var parts = new (TextBlock Glyph, RotateTransform Turn, double X, double Phase, double Speed, double Spin, double Sway)[pieces];
        for (var i = 0; i < pieces; i++)
        {
            var turn = new RotateTransform();
            var glyph = new TextBlock
            {
                Text = "♠♥♦♣"[i % 4].ToString(), FontSize = 14 + random.Next(16), Foreground = SuitInks[i % 4],
                Opacity = .35 + random.NextDouble() * .45, RenderTransform = turn, RenderTransformOrigin = RelativePoint.Center
            };
            layer.Children.Add(glyph);
            parts[i] = (glyph, turn, random.NextDouble(), random.NextDouble(), 40 + random.NextDouble() * 70,
                (random.NextDouble() - .5) * 220, 8 + random.NextDouble() * 22);
        }
        var started = PixelMotion.Now;
        void Place()
        {
            var width = layer.Bounds.Width;
            var height = layer.Bounds.Height;
            if (width <= 0 || height <= 0) return;
            var seconds = motion.Lively ? PixelMotion.Now - started : 0;
            foreach (var (glyph, turn, x, phase, speed, spin, sway) in parts)
            {
                var span = height + 60;
                var y = motion.Lively ? (phase * span + seconds * speed) % span - 40 : phase * height;
                Canvas.SetLeft(glyph, x * width + (motion.Lively ? Math.Sin(seconds * 1.3 + phase * 9) * sway : 0));
                Canvas.SetTop(glyph, y);
                turn.Angle = spin * seconds + phase * 360;
            }
        }
        motion.Ambient(layer, Place);
        // With motion off nothing ticks, so the scatter must follow the first layout and every resize.
        layer.SizeChanged += (_, _) => Place();
        return layer;
    }

    /// <summary>The balance before this match's rewards, counting up through them once the line has arrived.</summary>
    private static Control Total(MatchOutcome outcome, PixelMotion motion, int delay)
    {
        var tally = new NumberTally(Line("", Ink, 20), value => $"TOTAL ${value}", _ => null);
        tally.Host.Name = "CampaignResultTotal";
        tally.Host.HorizontalAlignment = HorizontalAlignment.Center;
        var earned = outcome.Payout + outcome.Interest;
        if (!motion.Enabled || earned <= 0) { tally.Set(outcome.Money); return tally.Host; }
        tally.Set(outcome.Money - earned);
        var started = false;
        tally.Host.AttachedToVisualTree += async (_, _) =>
        {
            if (started) return;
            started = true;
            try { await Tween.Delay(delay + 160); }
            catch (OperationCanceledException) { }
            tally.Change(outcome.Money, motion.Enabled && tally.Host.IsAttachedToVisualTree());
        };
        return tally.Host;
    }

    private static readonly IBrush Danger = Brush.Parse("#FF617B");

    private static int ButtonDelay(int lines) => 220 + lines * 50;

    // Every screen arrives the same way: title, rule, each line in turn, then the buttons.
    private static Control Frame(PixelMotion motion, string title, IReadOnlyList<Control> lines, Control buttons, string? note, bool scroll = false,
        bool halo = false, int titleMilliseconds = 360)
    {
        var rule = MenuParts.Rule(260);
        rule.Margin = new Thickness(0, 0, 0, 8);
        var stack = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Control heading = MenuParts.Title(motion, title, 46);
        if (halo) heading = Halo(motion, heading);
        stack.Children.Add(motion.Enter(heading, 0, 0, titleMilliseconds > 360 ? -48 : -22, easing: new BackEaseOut(), duration: titleMilliseconds));
        stack.Children.Add(motion.Enter(rule, 140, 0, 0));
        for (var index = 0; index < lines.Count; index++)
            stack.Children.Add(motion.Enter(lines[index], 180 + index * 50, 0, 8));
        buttons.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(buttons);
        if (note is not null)
        {
            var noteLine = Line(note, Danger);
            noteLine.Name = "CampaignNote";
            stack.Children.Add(motion.Enter(noteLine, ButtonDelay(lines.Count) + 200, 0, 0));
        }
        return MenuParts.Scene(motion, stack, scroll ? new ScrollViewer { Content = stack, Padding = new Thickness(20), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } : null);
    }

    /// <summary>Slowly turning rays behind a title, without changing its layout.</summary>
    private static Control Halo(PixelMotion motion, Control title)
    {
        const double size = 300;
        var burst = MachineFx.Burst(size, Gold, 16);
        Canvas.SetLeft(burst, -size / 2);
        Canvas.SetTop(burst, -size / 2);
        var anchor = new Canvas
        {
            Width = 0, Height = 0, IsHitTestVisible = false, ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { burst }
        };
        var started = PixelMotion.Now;
        motion.Ambient(anchor, () =>
        {
            var seconds = PixelMotion.Now - started;
            MachineFx.Spin(burst, motion.Lively ? seconds * 9 : 0, motion.Lively ? 1 + Math.Sin(seconds * 1.6) * .04 : 1, .13);
        });
        return new Grid { ClipToBounds = false, Children = { anchor, title } };
    }

    private static StackPanel Buttons(PixelMotion motion, int lines, params Button[] buttons)
    {
        var panel = new StackPanel { Spacing = 12 };
        for (var index = 0; index < buttons.Length; index++)
            panel.Children.Add(motion.Enter(buttons[index], ButtonDelay(lines) + index * 70, 0, 14));
        return panel;
    }

    private static TextBlock Line(string text, IBrush color, double size = 12) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontFamily = PixelArt.Font,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 560
    };

    private static Button ScreenButton(PixelMotion motion, string text, Action click, bool primary = false,
        string? name = null, bool stay = false) =>
        MenuParts.Button(text, primary, motion, name ?? $"Campaign{text.Replace(" ", "")}", click, stay);
}
