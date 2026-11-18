using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class HandHoverTests
{
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static Point At(Control card, Visual surface, double x, double y) => card.TranslatePoint(new Point(x * card.Bounds.Width, y * card.Bounds.Height), surface)!.Value;

    // A card lifts when hovered. Near its lower edge the lift used to carry the card off a still pointer,
    // dropping it back under the pointer again, so hover flickered between the card and its neighbour.
    public static void Run()
    {
        var builder = new MainWindow();
        var motion = (PixelMotion)typeof(MainWindow).GetField("_motion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(builder)!;
        motion.SetEnabled(false);
        var view = new GameEngine(["A", "B", "C", "D"], 1).CreateView(0);
        var make = typeof(MainWindow).GetMethod("CardButton", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cards = Enumerable.Range(0, 6).Select(index => (Button)make.Invoke(builder,
            [new Card((Suit)(index % 4), (Rank)(9 + index)), true, view, null, null, null, null])!).ToArray();
        var hand = HandFan.Arrange(0, new Border(), cards, motion);
        var window = new Window { Width = 900, Height = 500, Content = new Grid { Children = { hand } } };
        try
        {
            window.Show(); Flush();
            double Lift(Button card) => ((TranslateTransform)card.RenderTransform!).Y;
            var card = cards[2];
            // Just inside the resting card's lower edge, where an 8px lift moves the card away.
            var point = At(card, window, .5, .97);
            window.MouseMove(default); Flush();
            for (var step = 0; step < 6; step++)
            {
                window.MouseMove(point + new Point(step % 2, 0)); Flush();
                if (Lift(card) != -8 || cards.Where(other => other != card).Any(other => Lift(other) != 0))
                    throw new Exception($"A still pointer near a card's edge must keep hovering that card alone (step {step}).");
            }
            window.MouseMove(default); Flush();
            if (cards.Any(other => Lift(other) != 0)) throw new Exception("Leaving the hand must lower every card.");
            Console.WriteLine("PASS  Hand hover stays on one card while it lifts under a still pointer");
        }
        finally { window.Close(); builder.Close(); }
    }
}
