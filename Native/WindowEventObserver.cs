using System.Runtime.InteropServices;

namespace HzSwitcher.Native;

internal sealed class WindowEventObserver : IDisposable
{
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly WinEventProc _callback;
    private readonly IntPtr _foreground, _objects;
    public WindowEventObserver(Action<IntPtr> changed)
    {
        _callback = (_, evt, hwnd, objectId, childId, _, _) =>
        {
            if (evt == 3 || (objectId == 0 && childId == 0 && evt is 0x8002 or 0x8003 or 0x8004 or 0x800B or 0x8017 or 0x8018)) changed(evt == 3 ? IntPtr.Zero : hwnd);
        };
        _foreground = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 0);
        _objects = SetWinEventHook(0x8002, 0x8018, IntPtr.Zero, _callback, 0, 0, 0);
        if (_foreground == IntPtr.Zero || _objects == IntPtr.Zero) Services.Log.Write("Outline event tracking unavailable; using reconciliation timer.");
    }
    public void Dispose() { if (_foreground != IntPtr.Zero) UnhookWinEvent(_foreground); if (_objects != IntPtr.Zero) UnhookWinEvent(_objects); }
}
