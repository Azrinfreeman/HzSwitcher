using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HzSwitcher.Services;

namespace HzSwitcher.Native;

internal sealed class WindowApi : IWindowApi
{
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    private readonly WinEventProc _callback;
    private readonly IntPtr _hook;
    public event Action<IntPtr> Destroyed;

    // Constructed on the WinForms thread: out-of-context callbacks run on this same thread's message loop.
    public WindowApi()
    {
        _callback = (_, _, hwnd, objectId, childId, _, _) =>
        {
            if (objectId == 0 && childId == 0 && hwnd != IntPtr.Zero) Destroyed?.Invoke(hwnd);
        };
        _hook = SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, _callback, 0, 0, 0);
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not track window lifetime");
    }

    public IntPtr Foreground()
    {
        var handle = GetAncestor(GetForegroundWindow(), 2);
        return IsWindowVisible(handle) ? handle : IntPtr.Zero;
    }

    public WindowState? Inspect(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || handle == GetDesktopWindow() || handle == GetShellWindow()) return null;
        var name = new StringBuilder(256);
        GetClassName(handle, name, name.Capacity);
        if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return null;
        GetWindowThreadProcessId(handle, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            long started = process.StartTime.ToUniversalTime().Ticks;
            long style = IntPtr.Size == 8 ? GetWindowLongPtr(handle, -20).ToInt64() : GetWindowLong(handle, -20);
            return new WindowState(new WindowIdentity(handle, pid, started), (style & 8) != 0);
        }
        catch { return null; }
    }

    public bool SetTopmost(IntPtr handle, bool value, out int error)
    {
        bool success = SetWindowPos(handle, new IntPtr(value ? -1 : -2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        error = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    public void Dispose() => UnhookWinEvent(_hook);
}
