namespace HzSwitcher.Services;

internal readonly record struct WindowIdentity(IntPtr Handle, uint ProcessId, long Started);
internal readonly record struct WindowState(WindowIdentity Identity, bool Topmost);
internal enum PinStatus { Pinned, Unpinned, Removed, Unchanged, Failed }
internal readonly record struct PinResult(PinStatus Status, WindowIdentity? Target, string Message, int NativeError = 0, bool RestoredAny = false);
internal interface IWindowApi : IDisposable
{
    event Action<IntPtr> Destroyed;
    IntPtr Foreground();
    WindowState? Inspect(IntPtr handle);
    bool SetTopmost(IntPtr handle, bool value, out int error);
}

internal sealed class AlwaysOnTopService : IDisposable
{
    private readonly IWindowApi _api;
    private readonly Dictionary<IntPtr, WindowIdentity> _managed = new();
    public int Count => _managed.Count;
    public WindowIdentity[] Managed => _managed.Values.ToArray();
    public event Action Changed;
    public event Action<PinResult> Transition;
    public AlwaysOnTopService(IWindowApi api) { _api = api; _api.Destroyed += OnDestroyed; }
    public WindowState? Inspect(IntPtr handle) => _api.Inspect(handle);
    public bool Owns(WindowIdentity identity) => _managed.TryGetValue(identity.Handle, out var owned) && owned == identity;
    private void OnDestroyed(IntPtr handle)
    {
        if (_managed.TryGetValue(handle, out var identity)) Remove(identity);
    }
    private void Remove(WindowIdentity identity)
    {
        _managed.Remove(identity.Handle);
        Transition?.Invoke(new(PinStatus.Removed, identity, "Window released."));
        Changed?.Invoke();
    }
    public void Reconcile()
    {
        foreach (var identity in Managed)
        {
            var state = _api.Inspect(identity.Handle);
            if (state == null || state.Value.Identity != identity || !state.Value.Topmost) Remove(identity);
        }
    }
    public PinResult ToggleForeground()
    {
        var state = _api.Inspect(_api.Foreground());
        return state == null ? new(PinStatus.Failed, null, "This window cannot be pinned.") : Toggle(state.Value.Identity);
    }
    public PinResult Toggle(WindowIdentity identity)
    {
        var state = _api.Inspect(identity.Handle);
        if (state == null || state.Value.Identity != identity)
            return new(PinStatus.Failed, identity, "The original window has closed or changed.");
        if (Owns(identity)) return Restore(identity);
        if (_managed.ContainsKey(identity.Handle)) Remove(_managed[identity.Handle]);
        if (state.Value.Topmost) return new(PinStatus.Unchanged, identity, "This window is already on top; HzSwitcher has left it unchanged.");
        if (_api.Inspect(identity.Handle) != state) return new(PinStatus.Failed, identity, "The window changed before it could be pinned. Try again.");
        if (!_api.SetTopmost(identity.Handle, true, out int error)) return Failure(identity, error);
        var pinned = _api.Inspect(identity.Handle);
        if (pinned == null || pinned.Value.Identity != identity || !pinned.Value.Topmost)
            return new(PinStatus.Failed, identity, "The window did not stay on top. Its application may manage its own window order.");
        _managed[identity.Handle] = identity;
        var result = new PinResult(PinStatus.Pinned, identity, "Window pinned above ordinary windows.");
        Transition?.Invoke(result); Changed?.Invoke();
        return result;
    }
    private PinResult Restore(WindowIdentity identity)
    {
        var current = _api.Inspect(identity.Handle);
        if (current == null || current.Value.Identity != identity || !current.Value.Topmost)
        {
            Remove(identity);
            return new(PinStatus.Removed, identity, "The window has closed or is no longer on top.");
        }
        if (!_api.SetTopmost(identity.Handle, false, out int error)) return Failure(identity, error);
        var restored = _api.Inspect(identity.Handle);
        if (restored != null && restored.Value.Identity == identity && restored.Value.Topmost)
            return new(PinStatus.Failed, identity, "The window is still on top. Try Unpin all again.");
        _managed.Remove(identity.Handle);
        var result = new PinResult(PinStatus.Unpinned, identity, "Window unpinned.");
        Transition?.Invoke(result); Changed?.Invoke();
        return result;
    }
    public PinResult RestoreAll()
    {
        bool restored = false;
        foreach (var identity in Managed)
        {
            var result = Restore(identity);
            restored |= result.Status == PinStatus.Unpinned;
            if (Owns(identity)) Log.Write("Always on top cleanup: " + result.Message);
        }
        return new(Count > 0 ? PinStatus.Failed : restored ? PinStatus.Unpinned : PinStatus.Unchanged, null,
            Count == 0 ? "All managed windows unpinned." : $"Could not unpin {Count} window(s). Try Unpin all again.", RestoredAny: restored);
    }
    private static PinResult Failure(WindowIdentity target, int error) => new(PinStatus.Failed, target,
        $"Windows could not change this window (error {error}). The app may be protected or elevated.", error);
    public void Dispose() { RestoreAll(); _api.Destroyed -= OnDestroyed; _api.Dispose(); }
}
