using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HzSwitcher.Native;

internal static class ShellApi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    /// <summary>QUNS_RUNNING_D3D_FULL_SCREEN (3) means a real exclusive-fullscreen app, normally a game.</summary>
    public static bool IsExclusiveFullscreenApp()
    {
        return SHQueryUserNotificationState(out int state) == 0 && state == 3;
    }

    public static (string Title, string Process) GetForeground()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "");
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return (sb.ToString(), ProcessNameOf(h));
    }

    public static bool IsForegroundFullscreen()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        string proc = ProcessNameOf(h);
        if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
        if (!GetWindowRect(h, out RECT r)) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr mon = MonitorFromWindow(h, 2);
        if (!GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    public static string ProcessNameOf(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return ProcessNameOf((int)pid);
    }

    public static string ProcessNameOf(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch { return ""; }
    }
}
