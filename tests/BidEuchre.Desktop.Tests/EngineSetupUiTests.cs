using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Desktop;

internal static class EngineSetupUiTests
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static object? Call(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            using var deadline = new CancellationTokenSource(10000);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { if (task.IsCompleted) deadline.Cancel(); };
            timer.Start(); try { Dispatcher.UIThread.MainLoop(deadline.Token); } finally { timer.Stop(); }
        }
        Require(task.IsCompleted, "The table setup operation must finish."); task.GetAwaiter().GetResult();
    }
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "engine-setup-ui-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "BidEuchre"); Directory.CreateDirectory(folder);
        var prior = Environment.GetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY");
        Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", root);
        MainWindow? window = null;
        try
        {
            File.WriteAllText(Path.Combine(folder, "last-table.json"), """
                {"TableName":"Saturday Table","BotDelayMilliseconds":600,"Seats":[
                  {"Seat":0,"Name":"Alice","Kind":0}, {"Seat":1,"Name":"Bea","Kind":0},
                  {"Seat":2,"Name":"Cy","Kind":0}, {"Seat":3,"Name":"Dee","Kind":0}]}
                """);
            window = new MainWindow(); window.Show(); Dispatcher.UIThread.RunJobs();
            Require(Field<TextBox>(window, "_tableName").Text == "Saturday Table" && Field<ComboBox>(window, "_pace").SelectedIndex == 2
                && Field<TextBox[]>(window, "_seatNames")[1].Text == "Bea" && Field<ComboBox[]>(window, "_seatKinds").All(c => c.SelectedIndex == 0),
                "The real table controls must restore the saved name, pace, player names and kinds.");
            Field<TextBox[]>(window, "_seatNames")[2].Text = "Charlie";
            Call(window, "ShowSetup");
            Require(Field<TextBox[]>(window, "_seatNames")[2].Text == "Charlie", "Reopening setup must retain edits.");
            Await((Task)Call(window, "CreateTableAsync", new Button())!);
            Require(Field<GameSession>(window, "_session").Seats[2].Name == "Charlie", "Creating the restored table must use restored controls.");
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "last-table.json"))))
                Require(saved.RootElement.GetProperty("Seats")[2].GetProperty("Name").GetString() == "Charlie", "Creating a table must persist its current setup.");
            var session = Field<GameSession>(window, "_session"); window.Close(); Await(session.DisposeAsync().AsTask()); window = null;
            Console.WriteLine("PASS  Real table controls restore and persist last setup across setup navigation");

            File.WriteAllText(Path.Combine(folder, "engines.json"), """
                {"Engines":[{"Id":"stable-z","Name":"Zulu Bot","Author":"Tester","IsBuiltIn":false,"Executable":"/missing/bot"},
                {"Id":"stable-a","Name":"Alpha Bot","Author":"Tester","IsBuiltIn":false,"Executable":"/missing/another-bot"}]}
                """);
            File.WriteAllText(Path.Combine(folder, "last-table.json"), """
                {"TableName":"Missing engine","BotDelayMilliseconds":1000,"Seats":[
                  {"Seat":0,"Name":"You","Kind":0}, {"Seat":1,"Name":"West","Kind":1,"EngineId":"stable-z"},
                  {"Seat":2,"Name":"North","Kind":1,"EngineId":"builtin-tablebot"}, {"Seat":3,"Name":"East","Kind":1,"EngineId":"removed-id"}]}
                """);
            window = new MainWindow(); window.Show(); Dispatcher.UIThread.RunJobs();
            var selectors = Field<ComboBox[]>(window, "_seatEngines");
            Require(selectors[1].SelectedItem?.ToString()?.Contains("Zulu Bot") == true && selectors[1].SelectedItem?.ToString()?.Contains("unavailable") == true,
                "A saved engine selection must use its stable ID despite sorted names and visibly mark missing executables.");
            Require(selectors[3].SelectedItem?.ToString()?.Contains("removed-id") == true, "A removed saved engine must remain a visible missing selection.");
            Await((Task)Call(window, "CreateTableAsync", new Button())!);
            Require(Field<GameSession?>(window, "_session") is null && Field<TextBlock>(window, "_status").Text!.Contains("unavailable"),
                "Missing engine selections must give a clear repair error without creating a wrong or invalid-index table.");
            Call(window, "ShowEngines"); Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RepairEnginestable-z").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Field<TextBox>(window, "_engineExecutable").Text == "/missing/bot" && Field<string>(window, "_repairEngineId") == "stable-z",
                "Repair must prefill the saved engine command and retain its stable identity.");
            var cancel = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name == "CancelEngineRepair");
            Require(cancel is not null && cancel.IsVisible, "Repair mode must provide a clear way to cancel before loading another engine.");
            cancel!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Field<string?>(window, "_repairEngineId") is null && Field<TextBox>(window, "_engineExecutable").Text == "", "Cancelling repair must return the form to adding an engine.");
            Field<EngineCatalog>(window, "_catalog").Remove("stable-z"); Call(window, "RefreshEngineCatalog");
            Require(selectors[1].SelectedItem?.ToString()?.Contains("stable-z") == true, "Removing a selected engine must preserve a visible missing ID instead of silently changing bots.");
            Console.WriteLine("PASS  Stable engine selection, visible missing state and safe table validation");
        }
        finally { window?.Close(); Environment.SetEnvironmentVariable("BIDEUCHRE_DATA_DIRECTORY", prior); Directory.Delete(root, true); }
    }
}
