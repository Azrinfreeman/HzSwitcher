using System.Runtime.InteropServices;
using HzSwitcher.Services;

namespace HzSwitcher.Native;

internal sealed class PinInteractionApi : IPinInteractionApi
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int count);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    // Only bounded window-manager queries here: also used before swallowing mouse input.
    public PinForeground ReadForeground()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (hwnd == IntPtr.Zero || pid == 0) return new(hwnd, pid, null);
        if (!GetWindowRect(hwnd, out var frame) || !GetClientRect(hwnd, out var client)) return new(hwnd, pid, null);
        var origin = new Point(client.Left, client.Top);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!ClientToScreen(hwnd, ref origin) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return new(hwnd, pid, null);
        long style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -16).ToInt64() : GetWindowLong(hwnd, -16);
        var content = new Rectangle(origin, new Size(client.Right - client.Left, client.Bottom - client.Top));
        bool fullscreen = IsFullscreen(frame.Bounds, content, monitor.Monitor.Bounds, WindowGeometry.IsZoomed(hwnd), (style & 0xC00000) == 0xC00000);
        if (fullscreen)
        {
            var name = new System.Text.StringBuilder(128);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") fullscreen = false;
        }
        // Reject a foreground switch that happened in the middle of the queries.
        return GetForegroundWindow() == hwnd ? new(hwnd, pid, fullscreen) : new(IntPtr.Zero, 0, null);
    }
    internal static bool IsFullscreen(Rectangle frame, Rectangle client, Rectangle monitor, bool maximized, bool caption)
    {
        static bool Covers(Rectangle bounds, Rectangle monitor) => bounds.Width > 0 && bounds.Height > 0 &&
            bounds.Left <= monitor.Left && bounds.Top <= monitor.Top && bounds.Right >= monitor.Right && bounds.Bottom >= monitor.Bottom;
        // A maximized captioned window's invisible resize borders can cover the monitor; its client does not.
        return Covers(client, monitor) || (!maximized && !caption && Covers(frame, monitor));
    }
    public string ProcessName(uint pid) => ShellApi.ProcessNameOf((int)pid);
    public bool? ExclusiveFullscreen() => SHQueryUserNotificationState(out int state) == 0 ? state == 3 : null;
}
