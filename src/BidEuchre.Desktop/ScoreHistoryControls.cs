using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;

namespace BidEuchre.Desktop;

// Public, structured result data is the only source of the arithmetic shown here.
internal static class ScoreHistoryControls
{
    public static ScrollViewer Summary(GameView view, bool wholeMatch, double height = 210) => new()
    {
        Name = "HandScoreSummary", MaxHeight = height, Width = 500,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = Content(view, wholeMatch)
    };

    public static Control Content(GameView view, bool wholeMatch)
    {
        var content = new StackPanel { Spacing = 10 };
        if (wholeMatch) content.Children.Add(Line($"Final scores · Team 1 {view.Scores[0]} · Team 2 {view.Scores[1]}", Ink, true));
        var hands = wholeMatch ? view.HandHistory : view.HandHistory.Where(hand => hand.HandNumber == view.HandNumber);
        foreach (var hand in hands) content.Children.Add(Hand(view, hand));
        if (!view.HandHistory.Any(hand => hand.HandNumber == view.HandNumber))
            content.Children.Add(Line(view.CompletionReason ?? "This hand ended without a scored contract.", Ink));
        if (wholeMatch)
            content.Children.Add(new Expander
            {
                Header = "Public event log", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock { Text = string.Join("\n", Events(view)), TextWrapping = TextWrapping.Wrap,
                    FontFamily = FontFamily.Default, FontSize = 12, Foreground = Ink, Margin = new Thickness(8) }
            });
        return content;
    }

    public static IReadOnlyList<string> Events(GameView view) => view.PublicEvents.Count > 0 ? view.PublicEvents : view.Events;

    public static IEnumerable<string> PokerWinners(GameView view, PokerRoundResult poker) =>
        poker.Winners.Select(winner => $"{view.Players[winner.Seat].Name} · {winner.Category}");

    private static Control Hand(GameView view, HandResult hand)
    {
        var panel = new StackPanel { Spacing = 5 };
        if (hand.PokerResult is { } poker)
        {
            panel.Children.Add(Line($"Hand {hand.HandNumber} · Poker", Gold, true));
            foreach (var winner in PokerWinners(view, poker)) panel.Children.Add(Line(winner, Ink));
        }
        else if (hand.Contract is { } contract)
        {
            var tricks = hand.Bidder % 2 == 0 ? $"{hand.BiddingTeamTricks}–{hand.DefendingTeamTricks}" : $"{hand.DefendingTeamTricks}–{hand.BiddingTeamTricks}";
            var mode = contract.Mode == ContractMode.Trump ? contract.Trump.ToString() : contract.Mode.ToString();
            var bid = contract.Bid switch { BidLevel.PartnersBest => "Partner's Best", BidLevel.Alone => "Alone", _ => ((int)contract.Bid).ToString() };
            panel.Children.Add(Line($"Hand {hand.HandNumber} · {view.Players[hand.Bidder].Name} · {bid} · {mode} · {(hand.MadeContract ? "MADE" : "SET")}", Gold, true));
            panel.Children.Add(Line($"TRICKS · Team 1–Team 2: {tricks}", Muted));
        }
        foreach (var part in hand.ScoreBreakdowns)
        {
            var names = string.Join(" + ", view.Players.Where(player => player.Team == part.Team).Select(player => player.Name));
            panel.Children.Add(Line($"Team {part.Team + 1} · {names}: {Signed(part.FinalDelta)}", part.Team == 0 ? Green : Gold, true));
            if (hand.PokerResult is not null)
            {
                if (part.PokerPoints != 0) panel.Children.Add(Line($"Poker {Signed(part.PokerPoints)}", Ink));
                foreach (var bonus in part.GumballBonuses.Where(bonus => bonus.Id == GumballId.Poker))
                    panel.Children.Add(Line($"Poker copies {Signed(bonus.Points)}", Muted));
                continue;
            }
            panel.Children.Add(Line($"contract {Signed(part.ContractPoints)} · cards {Signed(part.CardPoints)} · Jokers {Signed(part.JokerPoints)} · trick modifiers {Signed(part.TrickModifierPoints)}", Ink));
            var subtotal = part.ContractPoints + part.CardPoints + part.JokerPoints + part.TrickModifierPoints;
            panel.Children.Add(Line($"Subtotal {Signed(subtotal)} · hand ×{part.HandMultiplier} = {Signed(subtotal * part.HandMultiplier)}", Muted));
            var fixedRewards = new[] { $"opponent bid {Signed(part.OpponentBidBonus)}" }.Concat(part.GumballBonuses.Select(bonus =>
                $"{FeatureCatalog.Get(FeatureCategory.Gumball, bonus.Id.ToString()).Name} {Signed(bonus.Points)}"));
            panel.Children.Add(Line($"Fixed rewards after hand multiplier: {string.Join(" · ", fixedRewards)}", Muted));
        }
        // Older protocol senders may omit the additive breakdown; their actual recorded deltas remain useful.
        if (hand.ScoreBreakdowns.Count == 0)
            panel.Children.Add(Line($"Team 1 {Signed(hand.TeamZeroDelta)} · Team 2 {Signed(hand.TeamOneDelta)}", Ink));
        panel.Children.Add(Line(hand.Reason, Muted));
        return new Border { Padding = new Thickness(8), BorderBrush = Brush.Parse("#66594380"), BorderThickness = new Thickness(1), Child = panel };
    }

    private static string Signed(int value) => value < 0 ? $"−{-(long)value}" : $"+{value}";
    private static TextBlock Line(string text, IBrush color, bool bold = false) => new()
    {
        Text = text, FontFamily = FontFamily.Default, FontSize = 12, Foreground = color,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap
    };
}
