using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>Public card flights on an overlay that survives table redraws.</summary>
internal sealed class CardFlights(Canvas overlay)
{
    private sealed record Hand(Control Card, double Angle);
    private sealed record Origin(Point Center, double Width, double Angle);
    private sealed record Target(Control Card, bool Added, Func<Control> Face);
    private sealed class Flight(Control visual, CancellationTokenSource source, Control target)
    {
        public Control Visual = visual;
        public CancellationTokenSource Source = source;
        public Control Target = target;
    }
    private readonly Dictionary<string, Hand> _hands = [];
    private readonly Dictionary<string, Origin> _origins = [];
    private readonly Dictionary<string, Target> _targets = [];
    private readonly Dictionary<string, Flight> _active = [];
    private readonly Dictionary<int, Control> _players = [];
    private CancellationTokenSource? _collection;
    private Action? _collectionDone;
    private Control? _collectionVisual;
    private HashSet<string> _collectionKeys = [];
    public bool Collecting => _collection is not null;
    private long _generation;

    public void BeginFrame()
    {
        _generation++;
        _origins.Clear();
        foreach (var (key, hand) in _hands)
        {
            if (hand.Card.TranslatePoint(new Point(hand.Card.Bounds.Width / 2, hand.Card.Bounds.Height / 2), overlay) is not { } center ||
                hand.Card.TranslatePoint(default, overlay) is not { } a ||
                hand.Card.TranslatePoint(new Point(hand.Card.Bounds.Width, 0), overlay) is not { } b) continue;
            var width = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            if (width > 0) _origins[key] = new Origin(center, width, hand.Angle);
        }
        _hands.Clear();
        _targets.Clear();
        _players.Clear();
    }

    public void HandCard(string key, Control card, double angle) => _hands[key] = new Hand(card, angle);
    public void PlayerSeat(int seat, Control portrait) => _players[seat] = portrait;
    public void PlayedCard(string key, Control card, bool added, Func<Control> face)
    {
        _targets[key] = new Target(card, added, face);
        if (_collectionVisual is not null && _collectionKeys.Contains(key)) card.Opacity = 0;
        if (_active.TryGetValue(key, out var flight))
        {
            flight.Target = card;
            card.Opacity = 0;
        }
    }

    public void EndFrame(bool enabled, int? collectWinner = null, Action? collected = null)
    {
        foreach (var key in _active.Keys.Except(_targets.Keys).ToArray()) Stop(key);
        if (!enabled) { Cancel(); collected?.Invoke(); return; }
        var generation = _generation;
        // Hide new destinations until layout supplies their actual screen positions.
        foreach (var (key, target) in _targets)
            if (target.Added && FindOrigin(key) is not null) target.Card.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _generation) return;
            foreach (var (key, target) in _targets)
                if (target.Added && !_active.ContainsKey(key)) _ = Fly(key, target);
        }, DispatcherPriority.Loaded);
        if (collectWinner is { } winner && !Collecting)
        {
            _collection = new CancellationTokenSource();
            _collectionDone = collected;
            _collectionKeys = _targets.Keys.ToHashSet();
            _ = Collect(winner, _collection);
        }
    }

    public void Cancel()
    {
        _generation++;
        if (_collection is { } collection)
        {
            collection.Cancel();
            FinishCollection(collection);
        }
        foreach (var key in _active.Keys.ToArray()) Stop(key);
        foreach (var target in _targets.Values) target.Card.Opacity = 1;
    }

    private async Task Collect(int winner, CancellationTokenSource source)
    {
        try
        {
            // Let the fourth card land, then briefly show the result.
            await Task.Delay(MotionCurves.FlightMilliseconds + MotionCurves.TrickHoldMilliseconds, source.Token);
            if (!_players.TryGetValue(winner, out var portrait) ||
                portrait.TranslatePoint(new Point(portrait.Bounds.Width / 2, portrait.Bounds.Height / 2), overlay) is not { } destination) return;
            var cards = new List<(Target Target, Point Center, double Width)>();
            foreach (var key in _collectionKeys)
            {
                if (!_targets.TryGetValue(key, out var target)) continue;
                var card = target.Card;
                if (card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), overlay) is not { } point ||
                    card.TranslatePoint(default, overlay) is not { } a ||
                    card.TranslatePoint(new Point(card.Bounds.Width, 0), overlay) is not { } b) continue;
                var width = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                if (width > 0) cards.Add((target, point, width));
            }
            if (cards.Count == 0) return;
            var center = new Point(cards.Average(card => card.Center.X), cards.Average(card => card.Center.Y));
            var left = cards.Min(card => card.Center.X - card.Width / 2);
            var top = cards.Min(card => card.Center.Y - card.Width * 88 / 62 / 2);
            var right = cards.Max(card => card.Center.X + card.Width / 2);
            var bottom = cards.Max(card => card.Center.Y + card.Width * 88 / 62 / 2);
            var shift = new TranslateTransform();
            var scale = new ScaleTransform(1, 1);
            var group = new Canvas
            {
                Width = right - left, Height = bottom - top, IsHitTestVisible = false, ClipToBounds = false,
                RenderTransformOrigin = new RelativePoint(center.X - left, center.Y - top, RelativeUnit.Absolute),
                RenderTransform = new TransformGroup { Children = { scale, shift } }
            };
            Canvas.SetLeft(group, left);
            Canvas.SetTop(group, top);
            foreach (var (target, point, width) in cards)
            {
                var face = target.Face();
                face.Margin = new Thickness(0);
                var visual = new Viewbox { Width = width, Height = width * 88 / 62, Child = face };
                Canvas.SetLeft(visual, point.X - left - width / 2);
                Canvas.SetTop(visual, point.Y - top - visual.Height / 2);
                group.Children.Add(visual);
                target.Card.Opacity = 0;
            }
            _collectionVisual = group;
            overlay.Children.Add(group);
            // Squeeze the trick into a loose pile, then sweep it to the winner.
            var pile = group.Children.Select(child => (Child: child, Left: Canvas.GetLeft(child), Top: Canvas.GetTop(child),
                X: center.X - left - child.Width / 2, Y: center.Y - top - child.Height / 2)).ToArray();
            await Tween.Run(MotionCurves.GatherMilliseconds, source.Token, t =>
            {
                var pull = MotionCurves.EaseInOut(t) * .4;
                foreach (var card in pile)
                {
                    Canvas.SetLeft(card.Child, card.Left + (card.X - card.Left) * pull);
                    Canvas.SetTop(card.Child, card.Top + (card.Y - card.Top) * pull);
                }
            });
            await Tween.Run(MotionCurves.CollectMilliseconds, source.Token, t =>
            {
                var travel = MotionCurves.EaseInOut(t);
                shift.X = (destination.X - center.X) * travel;
                shift.Y = (destination.Y - center.Y) * travel;
                scale.ScaleX = scale.ScaleY = 1 - .76 * travel;
                group.Opacity = 1 - MotionCurves.EaseIn(t);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            FinishCollection(source);
            source.Dispose();
        }
    }

    private void FinishCollection(CancellationTokenSource source)
    {
        if (!ReferenceEquals(_collection, source)) return;
        if (_collectionVisual is not null) overlay.Children.Remove(_collectionVisual);
        _collectionVisual = null;
        _collection = null;
        _collectionKeys.Clear();
        var done = _collectionDone;
        _collectionDone = null;
        done?.Invoke();
    }

    private Origin? FindOrigin(string key) => _origins.GetValueOrDefault(key) ?? _origins.GetValueOrDefault(key.Split(':')[0] + ":back");

    private void Stop(string key)
    {
        if (!_active.Remove(key, out var flight)) return;
        flight.Source.Cancel();
        overlay.Children.Remove(flight.Visual);
        flight.Target.Opacity = 1;
    }

    private async Task Fly(string key, Target target)
    {
        var origin = FindOrigin(key);
        var control = target.Card;
        if (origin is null || control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), overlay) is not { } destination ||
            control.TranslatePoint(default, overlay) is not { } a ||
            control.TranslatePoint(new Point(control.Bounds.Width, 0), overlay) is not { } b)
        { control.Opacity = 1; return; }
        var width = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        if (width <= 0) { control.Opacity = 1; return; }
        var height = width * 88 / 62;
        var shift = new TranslateTransform(origin.Center.X - destination.X, origin.Center.Y - destination.Y);
        // Take the short turn from the far player's upside-down hand.
        var angle = origin.Angle > 180 ? origin.Angle - 360 : origin.Angle;
        var rotation = new RotateTransform(angle);
        var scale = new ScaleTransform(origin.Width / width, origin.Width / width);
        var transforms = new TransformGroup { Children = { scale, rotation, shift } };
        var face = target.Face();
        face.Margin = new Thickness(0);
        var visual = new Viewbox
        {
            Width = width, Height = height, Child = face, IsHitTestVisible = false,
            RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative), RenderTransform = transforms
        };
        Canvas.SetLeft(visual, destination.X - width / 2);
        Canvas.SetTop(visual, destination.Y - height / 2);
        using var source = new CancellationTokenSource();
        var flight = new Flight(visual, source, control);
        _active[key] = flight;
        overlay.Children.Add(visual);
        control.Opacity = 0;
        var x = shift.X;
        var y = shift.Y;
        // Bow away from the straight line: upward across the table, sideways along it.
        var (arcX, arcY) = Math.Abs(x) > Math.Abs(y) ? (0d, -18d) : (Math.Sign(x == 0 ? 1 : x) * 16d, 0d);
        var startScale = scale.ScaleX;
        try
        {
            // One curve from hand to table; a keyframe per phase would stop the card at each one.
            await Tween.Run(MotionCurves.FlightMilliseconds, source.Token, t =>
            {
                var travel = 1 - MotionCurves.Settle(t);
                var eased = MotionCurves.EaseOut(t);
                var lift = MotionCurves.Arc(t);
                shift.X = x * travel + arcX * lift;
                shift.Y = y * travel + arcY * lift;
                rotation.Angle = angle * (1 - eased);
                // The card rises slightly off the felt in mid-flight.
                scale.ScaleX = scale.ScaleY = (startScale + (1 - startScale) * eased) * (1 + .05 * lift);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            overlay.Children.Remove(visual);
            if (_active.GetValueOrDefault(key) == flight)
            {
                _active.Remove(key);
                flight.Target.Opacity = 1;
            }
        }
    }
}
