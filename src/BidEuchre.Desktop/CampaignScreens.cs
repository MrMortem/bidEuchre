using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
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
                ScreenButton(motion, "Continue", resume, primary: true),
                ScreenButton(motion, "New Run", ConfirmDiscard, stay: true),
                ScreenButton(motion, "Back", back));
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

    public static Control Intro(CampaignRun run, Action start, Action back, string? note, PixelMotion motion)
    {
        var lines = new List<Control> { Line($"MATCH {run.MatchInAnte} OF 3", Green) };
        if (run.IsBoss) lines.Add(Line("BOSS MATCH · LOSING COSTS 2 LIVES", Danger));
        lines.Add(Line($"♥ {run.Lives}   ${run.Money}", Ink, 20));
        lines.Add(Line(
            $"BOTH TEAMS START AT 0 · ±{run.Threshold} POINTS · 8 HANDS (TIES CONTINUE)",
            Muted));
        return Frame(motion, $"ANTE {run.Ante}", lines,
            Buttons(motion, lines.Count, ScreenButton(motion, "Start Match", start, primary: true), ScreenButton(motion, "Main Menu", back)), note);
    }

    public static Control Result(CampaignRun run, MatchOutcome outcome, int[]? scores, Action next, string? note, PixelMotion motion)
    {
        var lines = new List<Control>();
        if (scores is not null) lines.Add(Line($"{scores[0]} – {scores[1]}", Ink, 28));
        if (outcome.Forfeited) lines.Add(Line("Match forfeited", Danger));
        lines.Add(Line(outcome.LivesLost > 0 ? $"LIVES −{outcome.LivesLost} · ♥ {Math.Max(0, run.Lives)}" : $"♥ {run.Lives}",
            outcome.LivesLost > 0 ? Danger : Green));
        lines.Add(Line($"BASE +${outcome.BaseReward} · WIN MARGIN +${outcome.MarginBonus}", Muted));
        lines.Add(Line("Card and Joker money is included in your balance.", Muted));
        lines.Add(Line($"INTEREST +${outcome.Interest}", Muted));
        lines.Add(Line($"TOTAL ${outcome.Money}", Ink, 20));
        return Frame(motion, outcome.Won ? "MATCH WON" : "MATCH LOST", lines,
            Buttons(motion, lines.Count, ScreenButton(motion, "Continue", next, primary: true)), note);
    }

    public static Control Shop(CampaignRun run, Action next, PixelMotion motion) =>
        Frame(motion, "SHOP", [Line($"${run.Money}", Ink, 28), Line("Nothing for sale yet", Muted)],
            Buttons(motion, 2, ScreenButton(motion, "Next Match", next, primary: true)), null);

    public static Control Victory(CampaignRun run, Action continueEndless, Action menu, PixelMotion motion) =>
        Frame(motion, "CAMPAIGN WON", [Line("You beat the Ante 5 boss.", Ink), Line($"♥ {run.Lives}   ${run.Money}", Green)],
            Buttons(motion, 2, ScreenButton(motion, "Continue Endless", continueEndless, primary: true), ScreenButton(motion, "Main Menu", menu)), null);

    public static Control RunOver(CampaignRun run, Action menu, PixelMotion motion) =>
        Frame(motion, "RUN OVER",
            [
                Line($"REACHED ANTE {run.Ante} · MATCH {run.MatchInAnte}", Green),
                Line($"MATCHES WON {run.MatchesWon}", Ink),
                Line($"${run.Money}", Ink, 20)
            ],
            Buttons(motion, 3, ScreenButton(motion, "Main Menu", menu, primary: true)), null);

    private static readonly IBrush Danger = Brush.Parse("#FF617B");

    private static int ButtonDelay(int lines) => 220 + lines * 50;

    // Every screen arrives the same way: title, rule, each line in turn, then the buttons.
    private static Control Frame(PixelMotion motion, string title, IReadOnlyList<Control> lines, Control buttons, string? note)
    {
        var rule = MenuParts.Rule(260);
        rule.Margin = new Thickness(0, 0, 0, 8);
        var stack = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(motion.Enter(MenuParts.Title(motion, title, 46), 0, 0, -22, easing: new BackEaseOut(), duration: 360));
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
        return MenuParts.Scene(motion, stack);
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
