using System.Runtime.InteropServices;

namespace HzSwitcher.Native;

internal readonly record struct WindowMenuEvent(uint Event, IntPtr Window, int Object, int Child, uint Thread, uint Time);

/// <summary>Callbacks only enqueue. Menu queries and writes run after the callback returns.</summary>
internal sealed class WindowMenuEventObserver : IDisposable
{
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max,
        IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly List<IntPtr> _hooks = new();
    private readonly Queue<WindowMenuEvent> _queue = new();
    private readonly Control _ui;
    private readonly Action<WindowMenuEvent> _handle;
    private readonly Action _overflow;
    private readonly WinEventProc _callback;
    private bool _queued, _disposed, _lost;
    public WindowMenuEventObserver(Control ui, Action<WindowMenuEvent> handle, Action overflow, uint process = 0)
    {
        _ui = ui; _handle = handle; _overflow = overflow;
        _callback = (_, evt, hwnd, obj, child, thread, time) =>
        {
            if (_disposed) return;
            if (evt is >= 0x8000 and <= 0x8002 && (obj != 0 || child != 0)) return;
            if (_queue.Count >= 1024) { _queue.Clear(); _lost = true; }
            _queue.Enqueue(new(evt, hwnd, obj, child, thread, time));
            Schedule();
        };
        try
        {
            foreach (var range in new (uint Min, uint Max)[] { (3, 3), (6, 7), (0x8000, 0x8002), (0x8013, 0x8013) })
            {
                var hook = SetWinEventHook(range.Min, range.Max, IntPtr.Zero, _callback, process, 0, 2);
                if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Native menu event registration failed");
                _hooks.Add(hook);
            }
        }
        catch { Dispose(); throw; }
    }
    private void Schedule()
    {
        if (_queued || _disposed || _ui.IsDisposed) return;
        _queued = true;
        try { _ui.BeginInvoke(Drain); }
        catch (InvalidOperationException) { _queued = false; }
    }
    private void Drain()
    {
        _queued = false;
        if (_disposed) return;
        if (_lost) { _lost = false; _overflow(); }
        for (int i = 0; i < 64 && _queue.TryDequeue(out var evt); i++) _handle(evt);
        if (_queue.Count > 0) Schedule();
    }
    public void Dispose()
    {
        _disposed = true; _queue.Clear();
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
