using System.Runtime.InteropServices;
using HzSwitcher.Services;

namespace HzSwitcher.Native;

internal static class WindowGeometry
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Rect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetInt(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    public static bool TryBounds(IntPtr hwnd, out Rectangle bounds, out int dpi)
    {
        bounds = default; dpi = 96;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (DwmGetInt(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0) return false;
        if (DwmGetWindowAttribute(hwnd, 9, out var rect, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(hwnd, out rect)) return false;
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        dpi = (int)GetDpiForWindow(hwnd); if (dpi == 0) dpi = 96;
        return bounds.Width > 0 && bounds.Height > 0;
    }
    internal static Rectangle OutlineBounds(Rectangle frame, Rectangle monitor, int thickness, bool maximized)
    {
        if (!maximized) frame.Inflate(thickness, thickness);
        return Rectangle.Intersect(frame, monitor);
    }
}
