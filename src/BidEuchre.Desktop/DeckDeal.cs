using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BidEuchre.App;

namespace BidEuchre.Desktop;

/// <summary>A dealer's deck and public, face-down packets on the persistent table overlay.</summary>
internal sealed class DeckDeal(Canvas overlay, Control table, Action changed)
{
    private sealed record Destination(Control Card, double Angle, Func<Control>? Face);
    private sealed class Flight(Border card, ScaleTransform scale, RotateTransform turn, TranslateTransform shift)
    {
        public readonly Border Card = card;
        public readonly ScaleTransform Scale = scale;
        public readonly RotateTransform Turn = turn;
        public readonly TranslateTransform Shift = shift;
        public bool Turned;
    }
    /// <summary>How long a face-up card takes to turn over after it lands.</summary>
    public const int FlipMilliseconds = 200;
    private sealed record Landing(Point Center, double Scale);
    private readonly Dictionary<(int Seat, int Index), Destination> _cards = [];
    private readonly Dictionary<int, Control> _players = [];
    private readonly HashSet<(int Seat, int Index)> _landed = [];
    private readonly List<Flight> _visuals = [];
    private readonly Dictionary<(int Seat, int Index), Landing> _landings = [];
    private IDisposable? _frames;
    private Point _origin;
    private IReadOnlyList<DealPacket> _packets = [];
    private double _started;
    private int _dealer;
    private Action? _finished;
    private Canvas? _deck;
    public bool Running { get; private set; }

    public void BeginFrame() { _cards.Clear(); _players.Clear(); _landings.Clear(); }
    public void Player(int seat, Control control) => _players[seat] = control;
    /// <summary>Registers a hand card. A <paramref name="face"/> makes the dealt back turn over to it on landing.</summary>
    public void Card(int seat, int index, Control control, double angle, Func<Control>? face = null)
    {
        _cards[(seat, index)] = new(control, angle, face);
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
            var flight = new Flight(back, new ScaleTransform(1, 1), new RotateTransform(), new TranslateTransform());
            back.RenderTransform = new TransformGroup { Children = { flight.Scale, flight.Turn, flight.Shift } };
            _visuals.Add(flight);
            overlay.Children.Add(back);
        }
        _started = AnimationClock.Now;
        table.LayoutUpdated += InvalidateLandings;
        _frames = AnimationClock.Shared.Subscribe(Tick);
        changed();
    }

    public void Cancel()
    {
        table.LayoutUpdated -= InvalidateLandings;
        _frames?.Dispose();
        _frames = null;
        Running = false;
        foreach (var card in _cards.Values) card.Card.Opacity = 1;
        foreach (var visual in _visuals) overlay.Children.Remove(visual.Card);
        _visuals.Clear();
        if (_deck is not null) overlay.Children.Remove(_deck);
        _deck = null;
        _landed.Clear();
        _landings.Clear();
        changed();
        var finished = _finished;
        _finished = null;
        finished?.Invoke();
    }

    private void InvalidateLandings(object? sender, EventArgs args) => _landings.Clear();

    private bool MeasureLandings()
    {
        if (!_players.TryGetValue(_dealer, out var dealer) || Center(dealer) is not { } dealerCenter || Center(table) is not { } center)
            return false;
        _origin = new Point(dealerCenter.X * .65 + center.X * .35, dealerCenter.Y * .65 + center.Y * .35);
        var deckWidth = _cards.TryGetValue((_dealer, 0), out var dealerCard) ? Width(dealerCard.Card) : 104d;
        if (deckWidth <= 0) return false;
        if (_deck is not null)
        {
            _deck.RenderTransform = new ScaleTransform(deckWidth / 124, deckWidth / 124);
            Canvas.SetLeft(_deck, _origin.X - deckWidth / 2);
            Canvas.SetTop(_deck, _origin.Y - deckWidth * 88 / 62 / 2);
        }
        foreach (var (key, target) in _cards)
        {
            var width = Width(target.Card);
            if (width > 0 && Center(target.Card) is { } landing)
                _landings[key] = new(landing, width / 124);
        }
        return _landings.Count > 0;
    }

    private void Tick(double now)
    {
        if (!Running) return;
        var elapsed = (now - _started) * 1000;
        if (elapsed > DealSequence.TotalMilliseconds(_packets) + FlipMilliseconds + MotionCurves.LandingFadeMilliseconds)
        { Cancel(); return; }
        if (_landings.Count == 0 && !MeasureLandings()) return;
        var dealt = 0;
        for (var packetIndex = 0; packetIndex < _packets.Count; packetIndex++)
        {
            var packet = _packets[packetIndex];
            for (var offset = 0; offset < packet.Count; offset++)
            {
                var key = (packet.Seat, packet.FirstCard + offset);
                var visual = _visuals[packetIndex * 3 + offset];
                if (!_cards.TryGetValue(key, out var target) || !_landings.TryGetValue(key, out var landing)) continue;
                var flown = elapsed - DealSequence.CardDelay(packet, offset);
                var t = Math.Clamp(flown / DealSequence.FlightMilliseconds, 0, 1);
                if (t <= 0) { visual.Card.Opacity = 0; continue; }
                dealt++;
                var travel = MotionCurves.Settle(t);
                visual.Scale.ScaleY = landing.Scale;
                visual.Scale.ScaleX = landing.Scale;
                visual.Turn.Angle = target.Angle * MotionCurves.EaseOut(t);
                visual.Shift.X = _origin.X + (landing.Center.X - _origin.X) * travel - 62;
                visual.Shift.Y = _origin.Y + (landing.Center.Y - _origin.Y) * travel - MotionCurves.Arc(t) * 14 - 88;
                if (t < 1)
                {
                    visual.Card.Opacity = 1;
                    continue;
                }
                var settled = flown - DealSequence.FlightMilliseconds;
                if (target.Face is { } face)
                {
                    // A card the viewer may see turns over on its own vertical axis: edge-on at the midpoint, face up after.
                    var turn = Math.Clamp(settled / FlipMilliseconds, 0, 1);
                    if (turn < 1)
                    {
                        if (turn >= .5 && !visual.Turned)
                        {
                            visual.Turned = true;
                            var shown = face();
                            shown.Margin = default;
                            visual.Card.Child = new Viewbox { Stretch = Stretch.Fill, Child = shown };
                            visual.Card.BorderThickness = default;
                        }
                        visual.Scale.ScaleX = landing.Scale * Math.Max(.02, Math.Abs(Math.Cos(turn * Math.PI)));
                        visual.Card.Opacity = 1;
                        continue;
                    }
                    settled -= FlipMilliseconds;
                }
                // The real card takes over underneath while the flying card fades off it.
                target.Card.Opacity = 1;
                _landed.Add(key);
                visual.Card.Opacity = Math.Clamp(1 - settled / MotionCurves.LandingFadeMilliseconds, 0, 1);
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
    internal static Border Back() => new()
    {
        Width = 124, Height = 176, BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(10), ClipToBounds = true,
        Background = Brushes.White, Child = new Viewbox { Stretch = Stretch.Fill, Child = PixelArt.Back() }, IsHitTestVisible = false,
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
    };
}
