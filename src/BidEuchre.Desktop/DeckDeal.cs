using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>A dealer's deck and public, face-down packets on the persistent table overlay.</summary>
internal sealed class DeckDeal(Canvas overlay, Control table, Action changed)
{
    private sealed record Destination(Control Card, double Angle);
    private readonly Dictionary<(int Seat, int Index), Destination> _cards = [];
    private readonly Dictionary<int, Control> _players = [];
    private readonly HashSet<(int Seat, int Index)> _landed = [];
    private readonly List<Control> _visuals = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IReadOnlyList<DealPacket> _packets = [];
    private long _started;
    private int _dealer;
    private Action? _finished;
    private Canvas? _deck;
    public bool Running { get; private set; }

    public void BeginFrame() { _cards.Clear(); _players.Clear(); }
    public void Player(int seat, Control control) => _players[seat] = control;
    public void Card(int seat, int index, Control control, double angle)
    {
        _cards[(seat, index)] = new(control, angle);
        if (Running && !_landed.Contains((seat, index))) control.Opacity = 0;
    }

    public void Start(int dealer, Action finished)
    {
        Cancel();
        _finished = finished;
        _dealer = dealer;
        _packets = DealSequence.Packets(dealer);
        _landed.Clear();
        Running = true;
        foreach (var card in _cards.Values) card.Card.Opacity = 0;
        _deck = new Canvas { Width = 132, Height = 188, IsHitTestVisible = false };
        for (var i = 0; i < 4; i++)
        {
            var back = Back();
            Canvas.SetLeft(back, i * 2); Canvas.SetTop(back, -i * 2);
            _deck.Children.Add(back);
        }
        overlay.Children.Add(_deck);
        for (var i = 0; i < 24; i++)
        {
            var back = Back();
            back.Opacity = 0;
            _visuals.Add(back);
            overlay.Children.Add(back);
        }
        _started = Stopwatch.GetTimestamp();
        _timer.Tick += Tick;
        _timer.Start();
        changed();
    }

    public void Cancel()
    {
        _timer.Stop(); _timer.Tick -= Tick;
        Running = false;
        foreach (var card in _cards.Values) card.Card.Opacity = 1;
        foreach (var visual in _visuals) overlay.Children.Remove(visual);
        _visuals.Clear();
        if (_deck is not null) overlay.Children.Remove(_deck);
        _deck = null;
        _landed.Clear();
        changed();
        var finished = _finished;
        _finished = null;
        finished?.Invoke();
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (!Running) return;
        var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        if (elapsed > DealSequence.TotalMilliseconds(_packets) + MotionCurves.LandingFadeMilliseconds)
        { Cancel(); return; }
        if (!_players.TryGetValue(_dealer, out var dealer) || Center(dealer) is not { } dealerCenter || Center(table) is not { } center)
        { Cancel(); return; }
        // The deck sits between the dealer's portrait and the center of the table.
        var origin = new Point(dealerCenter.X * .65 + center.X * .35, dealerCenter.Y * .65 + center.Y * .35);
        var deckWidth = 104d;
        if (_cards.TryGetValue((_dealer, 0), out var dealerCard)) deckWidth = Width(dealerCard.Card);
        if (deckWidth <= 0) return; // Wait for layout before measuring a scaled table.
        if (_deck is not null)
        {
            _deck.RenderTransform = new ScaleTransform(deckWidth / 124, deckWidth / 124);
            Canvas.SetLeft(_deck, origin.X - deckWidth / 2);
            Canvas.SetTop(_deck, origin.Y - deckWidth * 88 / 62 / 2);
        }
        var dealt = 0;
        for (var packetIndex = 0; packetIndex < _packets.Count; packetIndex++)
        {
            var packet = _packets[packetIndex];
            for (var offset = 0; offset < packet.Count; offset++)
            {
                var key = (packet.Seat, packet.FirstCard + offset);
                var visual = _visuals[packetIndex * 3 + offset];
                if (!_cards.TryGetValue(key, out var target) || Center(target.Card) is not { } destination) continue;
                var flown = elapsed - DealSequence.CardDelay(packet, offset);
                var t = Math.Clamp(flown / DealSequence.FlightMilliseconds, 0, 1);
                if (t <= 0) { visual.Opacity = 0; continue; }
                dealt++;
                var width = Width(target.Card);
                visual.Width = width; visual.Height = width * 88 / 62;
                if (t >= 1)
                {
                    // The real card takes over underneath while the flying back fades off it.
                    target.Card.Opacity = 1; _landed.Add(key);
                    Canvas.SetLeft(visual, destination.X - width / 2); Canvas.SetTop(visual, destination.Y - visual.Height / 2);
                    visual.RenderTransform = new RotateTransform(target.Angle);
                    visual.Opacity = Math.Clamp(1 - (flown - DealSequence.FlightMilliseconds) / MotionCurves.LandingFadeMilliseconds, 0, 1);
                    continue;
                }
                var eased = MotionCurves.EaseOut(t);
                var travel = MotionCurves.Settle(t);
                var x = origin.X + (destination.X - origin.X) * travel;
                var y = origin.Y + (destination.Y - origin.Y) * travel - MotionCurves.Arc(t) * 14;
                Canvas.SetLeft(visual, x - width / 2); Canvas.SetTop(visual, y - visual.Height / 2);
                visual.RenderTransform = new RotateTransform(target.Angle * eased);
                visual.Opacity = 1;
            }
        }
        // The deck thins as cards leave it and is gone with the last one.
        if (_deck is not null)
        {
            var layers = DealSequence.DeckLayers(dealt, _deck.Children.Count);
            for (var i = 0; i < _deck.Children.Count; i++) _deck.Children[i].Opacity = i < layers ? 1 : 0;
        }
    }

    private Point? Center(Control card) => card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), overlay);
    private double Width(Control card)
    {
        var a = card.TranslatePoint(default, overlay);
        var b = card.TranslatePoint(new Point(card.Bounds.Width, 0), overlay);
        return a is { } p && b is { } q ? Math.Sqrt(Math.Pow(q.X - p.X, 2) + Math.Pow(q.Y - p.Y, 2)) : 0;
    }
    private static Border Back() => new()
    {
        Width = 124, Height = 176, BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
        Background = Brush.Parse("#153583"), Child = new Viewbox { Stretch = Stretch.Fill, Child = PixelArt.Back() }, IsHitTestVisible = false,
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
    };
}
