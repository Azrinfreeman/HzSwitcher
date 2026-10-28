using HzSwitcher.Native;

namespace HzSwitcher.Services;

internal readonly record struct PinForeground(IntPtr Handle, uint ProcessId, bool? Fullscreen);
internal interface IPinInteractionApi
{
    PinForeground ReadForeground();
    string ProcessName(uint pid);
    bool? ExclusiveFullscreen();
}

// Owned and called on the WinForms thread. Slow classification never runs inside the input hook.
internal sealed class WindowPinInteractionGuard : IDisposable
{
    private readonly IPinInteractionApi _api;
    private readonly Settings _settings;
    private readonly Control _dispatcher;
    private readonly Func<long> _clock;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly WindowEventObserver _events;
    private PinForeground _snapshot;
    private long _checkedAt;
    private bool _protected = true, _ready, _queued, _disposed, _active = true;
    private const int MaxAgeMs = 750;
    public bool Protected => _protected;
    public event Action Changed;

    public WindowPinInteractionGuard(Settings settings, Control dispatcher, IPinInteractionApi api = null,
        Func<long> clock = null, bool observe = true)
    {
        _settings = settings; _dispatcher = dispatcher; _api = api ?? new PinInteractionApi();
        _clock = clock ?? (() => Environment.TickCount64);
        if (observe)
        {
            _events = new WindowEventObserver(hwnd => { if (hwnd == IntPtr.Zero || hwnd == _snapshot.Handle) QueueRefresh(); });
            _timer = new System.Windows.Forms.Timer { Interval = 250 };
            _timer.Tick += (_, _) => Refresh();
        }
        Refresh();
        _timer?.Start();
    }
    public void SetActive(bool active)
    {
        if (_active == active || _disposed) return;
        _active = active;
        if (_timer != null) _timer.Enabled = active;
        if (active) Refresh();
        else { _ready = false; SetProtected(true); }
    }
    public void Refresh()
    {
        if (_disposed || !_active) return;
        var current = _api.ReadForeground();
        string name = current.ProcessId == 0 ? "" : _api.ProcessName(current.ProcessId);
        bool? exclusive = _api.ExclusiveFullscreen();
        bool protect = current.Handle == IntPtr.Zero || current.Fullscreen != false || string.IsNullOrEmpty(name) || exclusive != false ||
            (_settings.GameProcesses?.Any(pattern => !string.IsNullOrWhiteSpace(pattern) && Match.Wildcard(pattern, name)) ?? false);
        _snapshot = current; _checkedAt = _clock(); _ready = true;
        SetProtected(protect);
    }
    // Do not refresh classification synchronously here: this method runs inside WH_MOUSE_LL.
    public bool CanUseMouseMenu()
    {
        if (_disposed || !_active) return false;
        var current = _api.ReadForeground();
        if (!_ready || current.Handle != _snapshot.Handle || current.ProcessId != _snapshot.ProcessId ||
            current.Fullscreen != false || _clock() - _checkedAt > MaxAgeMs)
        {
            // Notify presenters only after returning from the low-level hook.
            QueueRefresh();
            return false;
        }
        return !_protected;
    }
    public bool Revalidate()
    {
        Refresh();
        return CanUseMouseMenu();
    }
    private void SetProtected(bool value)
    {
        if (_protected == value) return;
        _protected = value; Changed?.Invoke();
    }
    private void QueueRefresh()
    {
        if (_disposed || !_active || _queued) return;
        _queued = true;
        _dispatcher.BeginInvoke(() => { _queued = false; if (!_disposed) Refresh(); });
    }
    public void Dispose() { _disposed = true; _timer?.Dispose(); _events?.Dispose(); }
}
