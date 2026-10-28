using System.Runtime.InteropServices;

namespace HzSwitcher.Native;

internal sealed class MouseGesture
{
    public bool Captured { get; private set; }
    public bool Process(bool down, bool up, bool injected, bool enabled, bool shift, bool eligible, bool frame = false, bool protectedInput = false)
    {
        if (injected) return false;
        if (up && Captured) { Captured = false; return true; }
        if (down && enabled && !protectedInput && (shift || frame) && eligible) { Captured = true; return true; }
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
    private readonly Action<IntPtr, Point, bool> _open;
    private readonly Func<bool> _canUseMouseMenu;
    private int _generation, _capturedGeneration;
    private readonly MouseGesture _gesture = new();
    private IntPtr _hook, _target;
    private Point _point;
    private bool _enabled, _frame;
    public bool Available => _hook != IntPtr.Zero;
    public WindowMouseHook(Control ui, Action<IntPtr, Point, bool> open, Func<bool> canUseMouseMenu = null)
    { _ui = ui; _open = open; _callback = Callback; _canUseMouseMenu = canUseMouseMenu ?? (() => true); }
    public void CancelPendingGesture() => _generation++;
    public void SetEnabled(bool enabled)
    {
        if (_enabled != enabled) CancelPendingGesture();
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
        bool allowed = !injected && _enabled && _canUseMouseMenu();
        IntPtr target = down && allowed ? GetAncestor(WindowFromPoint(mouse.Point), 2) : IntPtr.Zero;
        bool eligible = target != IntPtr.Zero && Eligible(target);
        bool frame = eligible && !shift && WindowFrameApi.IsFrameCandidate(target, mouse.Point);
        return ProcessButton(down, injected, shift, target, mouse.Point, frame, eligible, allowed)
            ? new IntPtr(1) : CallNextHookEx(_hook, code, message, data);
    }
    internal bool ProcessButton(bool down, bool injected, bool shift, IntPtr target, Point point, bool frame, bool eligible, bool allowed)
    {
        if (!allowed && !injected) CancelPendingGesture();
        if (!_gesture.Process(down, !down, injected, _enabled, shift, eligible, frame, protectedInput: !allowed)) return false;
        if (down) { _target = target; _point = point; _frame = frame; _capturedGeneration = _generation; }
        else
        {
            var captured = _target; var capturedPoint = _point; bool frameClick = _frame; int generation = _capturedGeneration;
            if (allowed && generation == _generation && !_ui.IsDisposed)
                _ui.BeginInvoke(() => { if (_enabled && generation == _generation && _canUseMouseMenu()) _open(captured, capturedPoint, frameClick); });
            if (!_enabled) Unhook();
        }
        return true;
    }
    private void Unhook() { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    public void Dispose() => SetEnabled(false);
}
