using HzSwitcher.Native;
using HzSwitcher.UI;

namespace HzSwitcher.Services;

internal sealed class AlwaysOnTopBorderService : IDisposable
{
    private readonly AlwaysOnTopService _pins;
    private readonly Settings _settings;
    private readonly Action<WindowIdentity, Point> _menu;
    private readonly Dictionary<WindowIdentity, AlwaysOnTopBorderWindow> _borders = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private bool _updating, _failed, _queued, _disposed;
    private bool _locked;
    private readonly Control _dispatcher = new();
    private readonly WindowEventObserver _events;
    public bool Available => !_failed;
    public WindowIdentity? TargetForOverlay(IntPtr handle)
    {
        foreach (var entry in _borders) if (entry.Value.Handle == handle) return entry.Key;
        return null;
    }
    public AlwaysOnTopBorderService(AlwaysOnTopService pins, Settings settings, Action<WindowIdentity, Point> menu)
    {
        _pins = pins; _settings = settings; _menu = menu;
        _ = _dispatcher.Handle;
        _events = new WindowEventObserver(handle =>
        {
            if (_pins.Count > 0 && (handle == IntPtr.Zero || _pins.Managed.Any(i => i.Handle == handle))) QueueRefresh();
        });
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        pins.Changed += Refresh;
        _timer.Tick += (_, _) => Refresh();
    }
    private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (_disposed || _dispatcher.IsDisposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            _locked = e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect;
            Refresh();
        });
    }
    private void QueueRefresh()
    {
        if (_queued || _disposed) return;
        _queued = true;
        _dispatcher.BeginInvoke(() => { _queued = false; if (!_disposed) Refresh(); });
    }
    public void Refresh()
    {
        if (_updating || _disposed) return;
        _updating = true;
        try
        {
            _pins.Reconcile();
            var managed = _pins.Managed;
            foreach (var identity in _borders.Keys.Except(managed).ToArray()) { _borders[identity].Dispose(); _borders.Remove(identity); }
            foreach (var identity in managed)
            {
                if (!_settings.AlwaysOnTopOutline || _locked || !Environment.UserInteractive ||
                    !WindowGeometry.TryBounds(identity.Handle, out var bounds, out int dpi))
                { if (_borders.TryGetValue(identity, out var hidden)) hidden.Hide(); continue; }
                try
                {
                    if (!_borders.TryGetValue(identity, out var border))
                        _borders[identity] = border = new AlwaysOnTopBorderWindow(identity, _menu);
                    border.UpdateOutline(bounds, dpi, _settings.AlwaysOnTopOutlineThickness);
                }
                catch (Exception e)
                {
                    if (!_failed) Log.Write("Pinned window outline unavailable: " + e.Message);
                    _failed = true;
                    if (_borders.Remove(identity, out var broken)) broken.Dispose();
                }
            }
            _timer.Enabled = managed.Length > 0;
        }
        finally { _updating = false; }
    }
    public void Dispose()
    {
        _disposed = true;
        Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
        _events.Dispose(); _dispatcher.Dispose();
        _pins.Changed -= Refresh; _timer.Dispose();
        foreach (var border in _borders.Values) border.Dispose();
        _borders.Clear();
    }
}
