using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BidEuchre.Desktop;

/// <summary>Retains cards and their hit areas as a hand changes shape.</summary>
internal sealed class HandFan
{
    public const double CardScale = .84;
    public const string SlotClass = "hand-slot";

    private readonly int _seat;
    private readonly PixelMotion _motion;
    private readonly Canvas _fan;
    private readonly double _step;
    private readonly List<Border> _placed = [];
    private readonly Dictionary<Control, Border> _slots = [];
    private Action<int, int>? _reorder;
    private double _midpoint;

    public Canvas Panel { get; }

    public HandFan(int seat, Control portrait, PixelMotion motion)
    {
        _seat = seat;
        _motion = motion;
        var side = seat is 1 or 3;
        Panel = new Canvas { Width = side ? 230 : 500, Height = side ? 400 : 208 };
        portrait.Width = side ? 76 : 260;
        portrait.ZIndex = 30;
        if (side && portrait is Border { Child: StackPanel content })
            content.Orientation = Avalonia.Layout.Orientation.Vertical;
        Canvas.SetLeft(portrait, seat switch { 1 => 0, 3 => 154, _ => 120 });
        Canvas.SetTop(portrait, side ? 120 : seat == 0 ? 136 : 0);
        Panel.Children.Add(portrait);

        _step = side ? 48 : 64;
        _fan = new Canvas
        {
            Width = side ? 400 : 500, Height = 180,
            RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative),
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(CardScale, CardScale),
                    new TranslateTransform(0, 180 * (1 - CardScale) / 2),
                    new RotateTransform(Facing(seat))
                }
            }
        };
        Canvas.SetLeft(_fan, seat switch { 1 => -40, 3 => -130, _ => 0 });
        Canvas.SetTop(_fan, side ? 70 : seat == 0 ? 0 : 62);
        Panel.Children.Add(_fan);
    }

    private static double Facing(int seat) => seat switch { 1 => 90, 2 => 180, 3 => -90, _ => 0 };
    public static double Angle(int seat, int index, int count) => (index - (count - 1) / 2d) * 6 + Facing(seat);
    private double Tilt(int index) => (index - _midpoint) * 6;
    private Point Slot(int index) => new(_fan.Width / 2 - 64 + (index - _midpoint) * _step,
        4 + (index - _midpoint) * (index - _midpoint) * 2);

    public void Update(IReadOnlyList<Control> cards, Action<int, int>? reorder)
    {
        _reorder = reorder;
        _midpoint = (cards.Count - 1) / 2d;
        foreach (var obsolete in _slots.Keys.Where(card => !cards.Contains(card)).ToArray())
        {
            var slot = _slots[obsolete];
            _fan.Children.Remove(slot);
            // A card can return to a hand later, for example after an Eagle play.
            if (slot.Child is Border ambient) ambient.Child = null;
            _slots.Remove(obsolete);
        }

        _placed.Clear();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (!_slots.TryGetValue(card, out var slot))
            {
                _slots[card] = slot = CreateSlot(card, index);
                _fan.Children.Add(slot);
            }
            _placed.Add(slot);
            Canvas.SetLeft(slot, Slot(index).X);
            Canvas.SetTop(slot, Slot(index).Y);
            ((RotateTransform)slot.RenderTransform!).Angle = Tilt(index);
            slot.ZIndex = slot.IsPointerOver || slot.IsKeyboardFocusWithin ? 20 : index;
        }
    }

    private Border CreateSlot(Control card, int index)
    {
        // The resting slot keeps its hit area while its card lifts or floats.
        var slot = new Border
        {
            Background = Brushes.Transparent, Classes = { SlotClass },
            Width = 128, Height = 180, Child = _motion.Float(card, _seat * 7 + index),
            RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative),
            RenderTransform = new RotateTransform()
        };
        void UpdateLayer()
        {
            if (!HandDrag.Active)
                slot.ZIndex = slot.IsPointerOver || slot.IsKeyboardFocusWithin ? 20 : _placed.IndexOf(slot);
        }
        slot.PointerEntered += (_, _) => UpdateLayer();
        slot.PointerExited += (_, _) => UpdateLayer();
        slot.GotFocus += (_, _) => UpdateLayer();
        slot.LostFocus += (_, _) => UpdateLayer();
        if (_seat == 0)
            HandDrag.Attach(_fan, slot, _placed, Slot, Tilt,
                x => Math.Clamp((int)Math.Round((x - _fan.Width / 2) / _step + _midpoint), 0, _placed.Count - 1),
                () => _reorder is not null && _placed.Count > 1, () => _motion.Enabled,
                (from, to) => _reorder?.Invoke(from, to));
        return slot;
    }

    public static Control Arrange(int seat, Control portrait, IReadOnlyList<Control> cards, PixelMotion motion, Action<int, int>? reorder = null)
    {
        var fan = new HandFan(seat, portrait, motion);
        fan.Update(cards, reorder);
        return fan.Panel;
    }
}
