using HzSwitcher.Native;
using System.Security.Cryptography;

namespace HzSwitcher.Services;

/// <summary>Owns only marked system-menu entries. No input interception, injection, or foreground-target guessing.</summary>
internal sealed class WindowSystemMenuService : IDisposable
{
    private sealed record Entry(WindowIdentity Window, IntPtr Menu, uint Command, UIntPtr Marker, uint Thread, uint PreparedAt);
    private sealed class Session(Entry entry, uint opened)
    {
        public readonly Entry Entry = entry;
        public readonly uint Opened = opened;
        public uint? Closed;
        public bool Consumed;
    }
    private readonly IWindowSystemMenuApi _api;
    private readonly AlwaysOnTopService _pins;
    private readonly Action<PinResult> _feedback;
    private readonly Func<uint> _now;
    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly Dictionary<uint, Session> _sessions = new();
    private readonly Queue<IntPtr> _pending = new();
    private readonly HashSet<IntPtr> _pendingSet = new();
    private bool _enabled, _disposed;
    public int Count => _entries.Count;
    public string LastError { get; private set; }
    public string Status => !_enabled ? (_entries.Count > 0 ? "System-menu cleanup incomplete" : "System menus disabled")
        : $"System menus: {_entries.Count} prepared" + (LastError == null ? "" : " / " + LastError);

    public WindowSystemMenuService(IWindowSystemMenuApi api, AlwaysOnTopService pins, Action<PinResult> feedback, Func<uint> now = null)
    {
        _api = api; _pins = pins; _feedback = feedback;
        _now = now ?? (() => unchecked((uint)Environment.TickCount));
        pins.Changed += RefreshStates;
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || (enabled && _enabled)) return;
        _enabled = enabled; InvalidateSessions();
        if (!enabled) { _pending.Clear(); _pendingSet.Clear(); Cleanup(); return; }
        Rescan();
    }

    /// <summary>Re-covers every window, e.g. after dropped events. Prepared entries are only updated.</summary>
    public void Rescan()
    {
        if (!_enabled) return;
        Prepare(_api.Foreground());
        foreach (var window in _api.Windows()) Queue(window);
    }

    public void Queue(IntPtr window)
    {
        if (!_enabled || window == IntPtr.Zero || _pending.Count >= 1024 || !_pendingSet.Add(window)) return;
        _pending.Enqueue(window);
    }

    public void Pump()
    {
        if (!_enabled) return;
        for (int i = 0; i < 8 && _pending.TryDequeue(out var window); i++)
        { _pendingSet.Remove(window); Prepare(window); }
        foreach (var thread in _sessions.Where(s => s.Value.Closed.HasValue && Age(_now(), s.Value.Closed.Value) > 500).Select(s => s.Key).ToArray())
            _sessions.Remove(thread);
    }

    internal bool Prepare(IntPtr window)
    {
        if (!_enabled || _disposed || !_api.Eligible(window)) return false;
        var state = _pins.Inspect(window);
        if (!state.HasValue) return false;
        var menu = _api.Menu(window);
        if (menu == IntPtr.Zero) return false;
        if (_entries.TryGetValue(window, out var existing))
        {
            if (existing.Window == state.Value.Identity && Matches(existing)) return Update(existing, state.Value);
            Forget(window);
        }
        var ids = _api.CommandIds(menu).ToHashSet();
        uint command = 0;
        for (uint id = 0xE000; id < 0xF000; id += 0x10)
            if (!ids.Any(used => (used & 0xFFF0) == id)) { command = id; break; }
        if (command == 0) { LastError = "No free system-menu command ID"; return false; }
        var marker = new UIntPtr((uint)RandomNumberGenerator.GetInt32(0x10000, int.MaxValue));
        bool owned = _pins.Owns(state.Value.Identity);
        if (!_api.Insert(menu, command, marker, owned || !state.Value.Topmost, owned))
        { LastError = $"Menu write unavailable (Windows error {_api.LastError})"; return false; }
        var entry = new Entry(state.Value.Identity, menu, command, marker, _api.Thread(window), _now());
        if (_pins.Inspect(window)?.Identity != entry.Window || !Matches(entry))
        { LastError = "Window/menu changed during preparation"; return false; }
        _entries[window] = entry;
        return true;
    }

    private bool Update(Entry entry, WindowState state)
    {
        bool owned = _pins.Owns(entry.Window);
        var item = _api.Item(entry.Menu, entry.Command);
        bool enabled = owned || !state.Topmost;
        if (item.HasValue && item.Value.Checked == owned && item.Value.Enabled == enabled) return true;
        if (_api.Update(entry.Menu, entry.Command, enabled, owned)) return true;
        LastError = $"Menu state unavailable (Windows error {_api.LastError})";
        return false;
    }

    private bool Matches(Entry entry) => _api.Menu(entry.Window.Handle) == entry.Menu &&
        _api.Item(entry.Menu, entry.Command) is { } item && item.Marker == entry.Marker && item.Id == entry.Command;

    private void RefreshStates()
    {
        if (!_enabled) return;
        foreach (var entry in _entries.Values.ToArray())
        {
            var state = _pins.Inspect(entry.Window.Handle);
            if (state?.Identity != entry.Window || !Matches(entry)) Forget(entry.Window.Handle);
            else Update(entry, state.Value);
        }
    }

    public void Handle(WindowMenuEvent evt)
    {
        if (!_enabled || _disposed) return;
        if (evt.Event == 0x8001 && evt.Object == 0 && evt.Child == 0)
        {
            // Destruction is an observed lifetime boundary, even if a process later reuses the same HWND.
            Forget(evt.Window); _pendingSet.Remove(evt.Window); return;
        }
        if (evt.Event is 3 or 0x8000 or 0x8002)
        {
            if (evt.Event == 3) Prepare(evt.Window); else Queue(evt.Window);
            return;
        }
        if (evt.Event == 6)
        {
            IntPtr owner = ResolveOwner(evt);
            if (owner == IntPtr.Zero) return;
            Prepare(owner);
            if (_entries.TryGetValue(owner, out var entry) && Matches(entry))
                _sessions[evt.Thread] = new Session(entry, evt.Time);
            return;
        }
        if (evt.Event == 7)
        {
            if (_sessions.TryGetValue(evt.Thread, out var session)) session.Closed = evt.Time;
            return;
        }
        if (evt.Event != 0x8013 || evt.Child <= 0 || evt.Object is not (-1 or -3 or -4)) return;
        Entry candidate = null;
        Session active = null;
        if (_sessions.TryGetValue(evt.Thread, out var open))
        {
            // Do not accept arbitrary client controls that happen to reuse this numeric command ID.
            if (evt.Window != open.Entry.Window.Handle && _api.MenuOwner(evt.Thread) != open.Entry.Window.Handle) return;
            if (open.Consumed || Age(evt.Time, open.Opened) < 0 ||
                (open.Closed.HasValue && Age(evt.Time, open.Closed.Value) > 500)) return;
            candidate = open.Entry; active = open;
        }
        else if (evt.Object == -1 && _entries.TryGetValue(evt.Window, out var direct))
        {
            // Some native providers report an invocation directly on OBJID_SYSMENU without a popup-start event.
            candidate = direct;
            active = new Session(direct, evt.Time); _sessions[evt.Thread] = active;
        }
        if (candidate == null || candidate.Command != (uint)evt.Child || candidate.Thread != evt.Thread ||
            Age(evt.Time, candidate.PreparedAt) <= 0 ||
            !_entries.TryGetValue(candidate.Window.Handle, out var current) || current != candidate ||
            _pins.Inspect(candidate.Window.Handle)?.Identity != candidate.Window || !Matches(candidate) ||
            _api.Item(candidate.Menu, candidate.Command)?.Enabled != true) return;
        active.Consumed = true; // Set before pin events can re-enter state reconciliation.
        _feedback(_pins.Toggle(candidate.Window));
        RefreshStates();
    }

    private IntPtr ResolveOwner(WindowMenuEvent evt)
    {
        var owner = _api.MenuOwner(evt.Thread);
        if (owner != IntPtr.Zero && _api.Thread(owner) == evt.Thread && _api.Eligible(owner)) return owner;
        return evt.Object == -1 && _api.Thread(evt.Window) == evt.Thread && _api.Eligible(evt.Window) ? evt.Window : IntPtr.Zero;
    }

    private static int Age(uint later, uint earlier) => unchecked((int)(later - earlier));
    public void InvalidateSessions() => _sessions.Clear();

    private void Forget(IntPtr window)
    {
        _entries.Remove(window);
        foreach (var thread in _sessions.Where(s => s.Value.Entry.Window.Handle == window).Select(s => s.Key).ToArray()) _sessions.Remove(thread);
    }

    private void Cleanup()
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            if (_pins.Inspect(entry.Window.Handle)?.Identity != entry.Window || !Matches(entry)) { Forget(entry.Window.Handle); continue; }
            if (_api.Remove(entry.Menu, entry.Command)) Forget(entry.Window.Handle);
            else LastError = $"Menu cleanup unavailable (Windows error {_api.LastError})";
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        SetEnabled(false); _pins.Changed -= RefreshStates; _disposed = true;
    }
}
