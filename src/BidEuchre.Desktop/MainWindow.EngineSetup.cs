using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BidEuchre.App;

namespace BidEuchre.Desktop;

public sealed partial class MainWindow
{
    private readonly string _lastTableSetupPath = Path.Combine(DataDirectory, "BidEuchre", "last-table.json");
    private LastTableSetup _lastTableSetup = LastTableSetup.Default;
    private string? _repairEngineId;
    private Button? _engineLoadButton;
    private Button? _cancelEngineRepair;
    private sealed record EngineChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private LastTableSetup CaptureTableSetup() => new(
        _tableName.Text ?? "", (_pace.SelectedItem as PaceOption)?.Milliseconds ?? 1000,
        Enumerable.Range(0, 4).Select(seat => new SeatConfiguration(seat, _seatNames[seat].Text ?? "",
            _seatKinds[seat].SelectedIndex == 1 ? PlayerKind.Bot : PlayerKind.Human,
            (_seatEngines[seat].SelectedItem as EngineChoice)?.Id ?? _lastTableSetup.Seats[seat].EngineId)).ToArray());

    private void SaveTableSetup()
    {
        if (_seatNames[0] is null) return;
        _lastTableSetup = CaptureTableSetup().Normalized();
        if (!_lastTableSetup.Save(_lastTableSetupPath)) ShowError("Could not save the last table setup. Changes will be lost on restart.");
    }

    private SeatConfiguration[] ReadTableSeats()
    {
        var setup = CaptureTableSetup().Normalized();
        foreach (var seat in setup.Seats.Where(seat => seat.Kind == PlayerKind.Bot))
        {
            var engine = _catalog.List().FirstOrDefault(engine => engine.Id == seat.EngineId);
            if (engine is null || !_catalog.Availability(engine.Id).Available)
                throw new InvalidOperationException($"Engine for seat {seat.Seat + 1} is unavailable. Repair it on the Engines page or choose another engine.");
        }
        return setup.Seats;
    }

    private void RefreshEngineCatalog()
    {
        var engines = _catalog.List();
        _engineList.Children.Clear();
        foreach (var engine in engines)
        {
            var state = _catalog.Availability(engine.Id);
            var status = engine.IsBuiltIn ? "built in" : !state.Available ? $"Unavailable: {state.Error}"
                : state.Verified ? "BEUCI process · verified" : "Not checked · handshake on first use";
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = engine.Name, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Ink },
                    new TextBlock { Text = $"by {engine.Author} · {status}", Foreground = state.Available ? Muted : Red,
                        FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                }
            });
            if (!engine.IsBuiltIn)
            {
                var repair = QuietButton("Repair");
                repair.Name = "RepairEngine" + engine.Id;
                repair.Click += (_, _) =>
                {
                    _repairEngineId = engine.Id;
                    if (_engineLoadButton is not null) _engineLoadButton.Content = "Handshake & repair";
                    if (_cancelEngineRepair is not null) _cancelEngineRepair.IsVisible = true;
                    _engineExecutable.Text = engine.Executable;
                    _engineArguments.Text = engine.Arguments;
                    _status.Foreground = Gold;
                    _status.Text = $"Repairing {engine.Name}. Update its executable or arguments, then handshake to keep its saved seat selections.";
                    _status.IsVisible = true;
                };
                var remove = QuietButton("Remove");
                remove.Name = "RemoveEngine" + engine.Id;
                remove.Foreground = Red;
                remove.Click += (_, _) =>
                {
                    _catalog.Remove(engine.Id);
                    if (_repairEngineId == engine.Id)
                    {
                        _repairEngineId = null;
                        if (_engineLoadButton is not null) _engineLoadButton.Content = "Handshake & load";
                        if (_cancelEngineRepair is not null) _cancelEngineRepair.IsVisible = false;
                    }
                    RefreshEngineCatalog();
                };
                row.Children.Add(Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { repair, remove } }, column: 1));
            }
            _engineList.Children.Add(new Border
            {
                BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                Padding = new Thickness(12), Child = row
            });
        }
        for (var seat = 0; seat < _seatEngines.Length; seat++)
        {
            var selector = _seatEngines[seat];
            if (selector is null) continue;
            var selectedId = (selector.SelectedItem as EngineChoice)?.Id ?? _lastTableSetup.Seats[seat].EngineId ?? EngineCatalog.BuiltInEngineId;
            var choices = engines.Select(engine => new EngineChoice(engine.Id,
                engine.Name + (_catalog.Availability(engine.Id).Available ? "" : " · unavailable"))).ToList();
            if (choices.All(choice => choice.Id != selectedId)) choices.Add(new(selectedId, $"Missing engine · {selectedId} · unavailable"));
            selector.ItemsSource = choices;
            selector.SelectedItem = choices.First(choice => choice.Id == selectedId);
        }
        if (_catalog.PersistenceError is { } error) ShowError(error);
    }
}
