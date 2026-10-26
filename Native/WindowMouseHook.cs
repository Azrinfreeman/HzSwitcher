using System.Runtime.InteropServices;

namespace HzSwitcher.Native;

internal sealed class MouseGesture
{
    public bool Captured { get; private set; }
    public bool Process(bool down, bool up, bool injected, bool enabled, bool shift, bool eligible)
    {
        if (injected) return false;
        if (up && Captured) { Captured = false; return true; }
        if (down && enabled && shift && eligible) { Captured = true; return true; }
        return false;
    }
}
internal sealed class WindowMouseHook : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Point Point; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int count);
    private static bool Eligible(IntPtr target)
    {
        if (target == IntPtr.Zero) return false;
        var name = new System.Text.StringBuilder(128);
        GetClassName(target, name, name.Capacity);
        return name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "#32768");
    }
    private readonly HookProc _callback;
    private readonly Control _ui;
    private readonly Action<IntPtr, Point> _open;
    private readonly MouseGesture _gesture = new();
    private IntPtr _hook, _target;
    private Point _point;
    private bool _enabled;
    public bool Available => _hook != IntPtr.Zero;
    public WindowMouseHook(Control ui, Action<IntPtr, Point> open) { _ui = ui; _open = open; _callback = Callback; }
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled && _hook == IntPtr.Zero)
        {
            _hook = SetWindowsHookEx(14, _callback, IntPtr.Zero, 0);
            if (_hook == IntPtr.Zero) LogFailure();
        }
        if (!enabled && !_gesture.Captured) Unhook();
    }
    private static void LogFailure() => Services.Log.Write("Always on top mouse hook unavailable: " + Marshal.GetLastWin32Error());
    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(_hook, code, message, data);
        int msg = message.ToInt32();
        if (msg != 0x204 && msg != 0x205) return CallNextHookEx(_hook, code, message, data);
        var mouse = Marshal.PtrToStructure<MouseData>(data);
        bool down = msg == 0x204, injected = (mouse.Flags & 3) != 0;
        bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        IntPtr target = down && !injected && _enabled && shift ? GetAncestor(WindowFromPoint(mouse.Point), 2) : IntPtr.Zero;
        if (!_gesture.Process(down, !down, injected, _enabled, shift, target != IntPtr.Zero && Eligible(target)))
            return CallNextHookEx(_hook, code, message, data);
        if (down) { _target = target; _point = mouse.Point; }
        else
        {
            var captured = _target; var point = _point;
            if (_enabled && !_ui.IsDisposed) _ui.BeginInvoke(() => { if (_enabled) _open(captured, point); });
            if (!_enabled) Unhook();
        }
        return new IntPtr(1);
    }
    private void Unhook() { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    public void Dispose() => SetEnabled(false);
}
