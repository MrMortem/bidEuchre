using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>Lets the player drag a card in their own fan to a new place. A press that does not move
/// stays an ordinary click, so playing a card works as before.</summary>
internal static class HandDrag
{
    // A card must travel this far (in fan units) before a press becomes a drag instead of a click.
    private const double Threshold = 10;

    /// <summary>True while a card is held. The table should not be redrawn under the player's pointer.</summary>
    public static bool Active { get; private set; }

    /// <param name="slot">Where the card at a position rests, as its fan-canvas left/top.</param>
    /// <param name="angle">The tilt of the card at a position.</param>
    /// <param name="positionAt">The position whose slot is centred nearest a fan x coordinate.</param>
    /// <param name="reorder">Called with the card's old and new position once it is dropped somewhere else.</param>
    public static void Attach(Canvas fan, IReadOnlyList<Border> cards, Func<int, Point> slot, Func<int, double> angle,
        Func<double, int> positionAt, bool glide, Action<int, int> reorder)
    {
        void Place(Border card, int position)
        {
            Canvas.SetLeft(card, slot(position).X);
            Canvas.SetTop(card, slot(position).Y);
            if (card.RenderTransform is RotateTransform tilt) tilt.Angle = angle(position);
        }
        void Layout(int moving, int target)
        {
            var order = Enumerable.Range(0, cards.Count).Where(i => i != moving).ToList();
            order.Insert(target, moving);
            for (var position = 0; position < order.Count; position++)
                if (order[position] != moving) { Place(cards[order[position]], position); cards[order[position]].ZIndex = position; }
        }

        for (var index = 0; index < cards.Count; index++)
        {
            var from = index;
            var card = cards[index];
            Point? start = null;
            var dragging = false;
            var target = from;

            void Finish(IPointer? pointer, bool drop)
            {
                var moved = dragging;
                start = null; dragging = false; Active = false;
                // A plain click must leave the pointer with the card's button, or the button cancels its click.
                if (!moved) return;
                foreach (var other in cards) other.Transitions = null;
                pointer?.Capture(null);
                if (drop && target != from) { reorder(from, target); return; }
                // Dropped where it started or cancelled: everything goes back to its own slot.
                Layout(from, from); Place(card, from); card.ZIndex = from;
            }

            card.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(fan).Properties.IsLeftButtonPressed) return;
                start = e.GetPosition(fan); dragging = false; target = from;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            card.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
            {
                if (start is not { } origin) return;
                var at = e.GetPosition(fan);
                var dx = at.X - origin.X; var dy = at.Y - origin.Y;
                if (!dragging)
                {
                    if (Math.Abs(dx) < Threshold && Math.Abs(dy) < Threshold) return;
                    dragging = true; Active = true;
                    // Taking the pointer from the card's button cancels its click.
                    e.Pointer.Capture(card);
                    if (glide)
                        foreach (var other in cards.Where(c => c != card))
                            other.Transitions =
                            [
                                new DoubleTransition { Property = Canvas.LeftProperty, Duration = TimeSpan.FromMilliseconds(120) },
                                new DoubleTransition { Property = Canvas.TopProperty, Duration = TimeSpan.FromMilliseconds(120) }
                            ];
                }
                var home = slot(from);
                Canvas.SetLeft(card, home.X + dx);
                Canvas.SetTop(card, home.Y + Math.Clamp(dy, -48, 24));
                if (card.RenderTransform is RotateTransform tilt) tilt.Angle = 0;
                card.ZIndex = 40;
                var next = positionAt(home.X + dx + card.Width / 2);
                if (next != target) { target = next; Layout(from, target); }
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            card.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (start is null) return;
                if (dragging) e.Handled = true;
                Finish(e.Pointer, drop: true);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            card.PointerCaptureLost += (_, e) => { if (dragging && ReferenceEquals(e.Source, card)) Finish(null, drop: false); };
            // A redraw that removes the fan mid-drag must not leave the table frozen.
            card.DetachedFromVisualTree += (_, _) => { if (dragging) { dragging = false; start = null; Active = false; } };
        }
    }
}
