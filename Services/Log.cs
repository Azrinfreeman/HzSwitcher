namespace HzSwitcher.Services;

internal static class Log
{
    /// <summary>Data folder. Tests point this at a temp folder.</summary>
    public static string Dir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HzSwitcher");
    public static string FilePath => Path.Combine(Dir, "log.txt");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    File.WriteAllText(FilePath, "");
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
