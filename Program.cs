using HzSwitcher;
using HzSwitcher.Services;

internal static class Program
{
    public const string ShowEventName = @"Local\HzSwitcher.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\HzSwitcher.SingleInstance", out bool first);
        if (!first)
        {
            // Already running: ask the existing instance to bring its window forward.
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // A bug should be logged, not silently kill the tray app.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("Unhandled UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal exception: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("Unobserved task exception: " + e.Exception); e.SetObserved(); };

        Application.Run(new TrayApp(args.Contains("--minimized")));
    }
}
