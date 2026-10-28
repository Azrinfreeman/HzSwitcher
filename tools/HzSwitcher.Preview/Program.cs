using HzSwitcher.Services;
using HzSwitcher.UI;

namespace HzSwitcher.Preview;

// Runs in its own process: WinForms class registration and message-loop state are isolated from native-window unit tests.
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.FirstOrDefault() == "--catalog")
        {
            var display = new DisplayService(() => "");
            var monitors = display.GetMonitors();
            foreach (var monitor in monitors)
                Console.WriteLine($"{monitor.Device}: identity confirmed = {!string.IsNullOrEmpty(display.TargetIdentity(monitor.Device))}");
            Console.WriteLine($"Read-only catalog check: {monitors.Count} displays. No hardware writes.");
            return 0;
        }
        string output = args.Length == 0 ? Path.GetFullPath("artifacts/qa") : Path.GetFullPath(args[0]);
        string dir = Path.Combine(Path.GetTempPath(), "HzPreview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new Settings { IdleMinutes = 1 };
            using var coordinator = new AutomationCoordinator(settings, new Display(), new Media(), () => "synthetic-monitor",
                dir, false, new Activity(), new Foreground(), elapsed: () => 100, save: () => true);
            coordinator.History.Add("synthetic-monitor", 240, 60, 60, "Synthetic saver example", "Verified by readback");
            coordinator.Tick();
            using var form = new AutomationDetailsForm(coordinator, settings, coordinator.SetPreview);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            using var timer = new System.Windows.Forms.Timer { Interval = 300 };
            Exception failure = null;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var controls = Descendants(form).ToArray();
                    var grid = controls.OfType<DataGridView>().Single();
                    if (grid.Height < 80 || grid.Width < 400 || grid.Rows.Count != 1)
                        throw new InvalidOperationException($"History layout invalid: {grid.Width}x{grid.Height}, rows {grid.Rows.Count}.");
                    if (!controls.OfType<Label>().Any(l => l.Text.Contains("PREVIEW") && l.Text.Contains("60 Hz")))
                        throw new InvalidOperationException("Preview explanation is missing.");
                    Directory.CreateDirectory(output);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(output, "automation-details-synthetic.png"));
                    Console.WriteLine("Synthetic UI layout passed. No display settings or brightness were accessed.");
                }
                catch (Exception e) { failure = e; }
                finally { form.Close(); }
            };
            form.Shown += (_, _) => timer.Start();
            Application.Run(form);
            if (failure != null) throw failure;
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private sealed class Display : IDisplay
    {
        public int GetCurrentHz() => 240;
        public bool TryApply(int hz) => throw new InvalidOperationException("Preview must never write hardware.");
        public int Resolve(int hz) => ModeMath.Resolve(GetSupportedHz(), hz);
        public List<int> GetSupportedHz() => new() { 60, 120, 240 };
    }
    private sealed class Activity : IActivity { public uint IdleMilliseconds() => 61000; }
    private sealed class Foreground : IForeground { public string ProcessName() => ""; }
    private sealed class Media : IMediaInfo { public bool IsWatching() => false; public bool IsGameForeground() => false; }
}
