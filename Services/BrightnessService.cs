using HzSwitcher.Native;
using System.Management;
using System.Runtime.InteropServices;

namespace HzSwitcher.Services;

/// <summary>
/// Brightness of the selected monitor. Prefers DDC/CI, then WMI (laptop panel), then software dimming.
/// Writes happen on a background thread and only the newest requested value is applied.
/// </summary>
public sealed class BrightnessService : IBrightness, IDisposable
{
    /// <summary>Software dimming never goes below this; Windows rejects very dark gamma ramps anyway.</summary>
    public const int SoftwareFloor = 20;

    private enum Method { None, Ddc, Wmi, Gamma }

    private sealed class State
    {
        public Method Method;
        public string Key;       // stable monitor id, used to remember the level per monitor
        public string HardwareId; // e.g. BOE0A1B, matches the WMI InstanceName
        public string Identity;
        public uint Min, Max;
        public int Level = 100;
        public int RequestedLevel = -1;
    }

    private readonly Func<string> _device;
    private readonly Func<string, int> _remembered;
    private readonly Func<string, string> _identity;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current;
    private readonly object _lock = new();
    private readonly Dictionary<string, State> _states = new();
    private readonly Dictionary<string, (string Identity, ushort[] Ramp)> _originalRamps = new();
    private (string Device, int Level, long Generation, bool Automatic, long AutoGeneration)? _pending;
    private bool _running;
    private bool _disposed;
    private long _generation;
    private long _autoGeneration;
    public bool LastWriteSucceeded { get; private set; }
    public string LastError { get; private set; }
    public bool IsBusy { get { lock (_lock) return _running || _pending != null; } }

    /// <summary>Raised on the UI thread after a read or write completes.</summary>
    public event Action Changed;

    /// <param name="device">The monitor being controlled (same as DisplayService.Device).</param>
    /// <param name="remembered">Saved level for a monitor key, or -1; used where the real level cannot be read.</param>
    public BrightnessService(Func<string> device, Func<string, int> remembered, Func<string, string> identity = null)
    {
        _device = device;
        _remembered = remembered;
        _identity = identity ?? (device => device);
    }

    public bool IsSupported => Mode != BrightnessMode.None;

    public BrightnessMode Mode => Current().Method switch
    {
        Method.Ddc or Method.Wmi => BrightnessMode.Hardware,
        Method.Gamma => BrightnessMode.Software,
        _ => BrightnessMode.None,
    };

    /// <summary>Stable id of the controlled monitor (falls back to the device name).</summary>
    public string Key => Current().Key;

    public int Get() => Current().Level;
    public int RequestedLevel => Current().RequestedLevel;

    /// <summary>Forget what was learned about the monitors, e.g. after the selection or topology changed.</summary>
    public void Invalidate()
    {
        lock (_lock) { _generation++; _pending = null; _states.Clear(); LastWriteSucceeded = false; }
    }

    public void CancelPending() { lock (_lock) { _generation++; _pending = null; } }
    public void CancelAutomaticPending()
    {
        lock (_lock) { _autoGeneration++; if (_pending?.Automatic == true) _pending = null; }
    }

    /// <summary>Re-reads the real brightness in the background, then raises Changed.</summary>
    public void RefreshAsync(Action<int> completed = null)
    {
        string device = _device();
        long generation;
        lock (_lock) generation = _generation;
        Task.Run(() =>
        {
            var state = Probe(device);
            lock (_lock)
            {
                // A write in flight is newer than anything just read; keep what the user chose.
                if (_disposed || _running || _pending != null || generation != _generation || state.Identity != _identity(device)) return;
                // Software dimming cannot be read back, so keep the level already in use.
                if (state.Method == Method.Gamma && _states.TryGetValue(device, out var old) && old.Method == Method.Gamma)
                    state.Level = old.Level;
                _states[device] = state;
            }
            Post(() => completed?.Invoke(state.Level));
        });
    }

    public void RequestSet(int percent) => RequestSet(percent, false);

    public void RequestSet(int percent, bool automatic)
    {
        string device = _device();
        var state = Current();
        if (state.Method == Method.None) return;
        int level = Math.Clamp(percent, state.Method == Method.Gamma ? SoftwareFloor : 0, 100);
        state.RequestedLevel = level;
        lock (_lock)
        {
            if (_disposed) return;
            LastWriteSucceeded = false; LastError = null;
            _pending = (device, level, _generation, automatic, _autoGeneration);
            if (_running) return;
            _running = true;
        }
        Task.Run(Pump);
    }

    private State Current()
    {
        string device = _device();
        lock (_lock) { if (_states.TryGetValue(device, out var s) && s.Identity == _identity(device)) return s; }
        var state = Probe(device);
        lock (_lock) { _states[device] = state; }
        return state;
    }

    private void Pump()
    {
        while (true)
        {
            (string Device, int Level, long Generation, bool Automatic, long AutoGeneration) work;
            lock (_lock)
            {
                if (_pending == null || _disposed) { _running = false; break; }
                work = _pending.Value;
                _pending = null;
            }
            try { Apply(work.Device, work.Level, work.Generation, work.Automatic, work.AutoGeneration); }
            catch (Exception e) { LastError = e.Message; LastWriteSucceeded = false; Log.Write($"Brightness set failed on {work.Device}: {e.Message}"); }
        }
        Post();
    }

    private void Post(Action completed = null)
    {
        var handler = Changed;
        if (_ui != null) _ui.Post(_ => { if (!_disposed) { handler?.Invoke(); completed?.Invoke(); } }, null);
        else if (!_disposed) { handler?.Invoke(); completed?.Invoke(); }
    }

    // ---------- probing ----------

    private State Probe(string device)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        string key = DisplayApi.EnumDisplayDevices(device, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceID) ? dd.DeviceID : device;
        var parts = key.Split('\\');
        var state = new State { Key = key, HardwareId = parts.Length > 1 ? parts[1] : "", Identity = _identity(device) };
        if (string.IsNullOrEmpty(state.Identity)) return state;

        try
        {
            if (BrightnessApi.TryGetDdc(device, out uint min, out uint cur, out uint max))
            {
                state.Method = Method.Ddc;
                (state.Min, state.Max) = (min, max);
                state.Level = (int)Math.Round((cur - min) * 100.0 / (max - min));
                Log.Write($"Brightness: DDC/CI on {device}, range {min}-{max}, now {cur}");
                return state;
            }
        }
        catch (Exception e) { Log.Write("Brightness DDC probe failed: " + e.Message); }

        try
        {
            int level = ReadWmi(state.HardwareId);
            if (level >= 0)
            {
                state.Method = Method.Wmi;
                (state.Min, state.Max) = (0, 100);
                state.Level = level;
                Log.Write($"Brightness: WMI on {device}, now {level}");
                return state;
            }
        }
        catch (Exception e) { Log.Write("Brightness WMI probe failed: " + e.Message); }

        try
        {
            var ramp = BrightnessApi.GetGamma(device);
            if (ramp != null)
            {
                lock (_lock)
                {
                    if (!_originalRamps.TryGetValue(device, out var original) || original.Identity != state.Identity)
                        _originalRamps[device] = (state.Identity, ramp);
                }
                state.Method = Method.Gamma;
                int saved = _remembered(key);
                state.Level = saved >= SoftwareFloor ? saved : 100;
                Log.Write($"Brightness: software dimming on {device}");
                return state;
            }
        }
        catch (Exception e) { Log.Write("Brightness gamma probe failed: " + e.Message); }

        Log.Write($"Brightness: not available on {device}");
        return state;
    }

    private static int ReadWmi(string hardwareId)
    {
        if (string.IsNullOrEmpty(hardwareId)) return -1;
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o)
                if (InstanceMatches((string)o["InstanceName"], hardwareId))
                    return Convert.ToInt32(o["CurrentBrightness"]);
        }
        return -1;
    }

    /// <summary>WMI instance names look like DISPLAY\BOE0A1B\4&amp;...; the middle part is the monitor's hardware id.</summary>
    private static bool InstanceMatches(string instanceName, string hardwareId)
    {
        var p = instanceName?.Split('\\');
        return p is { Length: > 1 } && p[1].Equals(hardwareId, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- applying ----------

    private void Apply(string device, int level, long generation, bool automatic, long autoGeneration)
    {
        State state;
        lock (_lock)
        {
            if (generation != _generation || (automatic && autoGeneration != _autoGeneration) || !_states.TryGetValue(device, out state)) return;
        }
        if (_device() != device || state.Identity != _identity(device)) return;
        bool ok = false;

        switch (state.Method)
        {
            case Method.Ddc:
                uint raw = state.Min + (uint)Math.Round(level / 100.0 * (state.Max - state.Min));
                ok = BrightnessApi.SetDdc(device, raw);
                if (ok && BrightnessApi.TryGetDdc(device, out uint min, out uint cur, out uint max) && max > min)
                { state.Level = (int)Math.Round((cur - min) * 100.0 / (max - min)); ok = Math.Abs(state.Level - level) <= 2; }
                else ok = false;
                break;

            case Method.Wmi:
                SetWmi(state.HardwareId, level);
                int observed = ReadWmi(state.HardwareId);
                ok = observed >= 0 && Math.Abs(observed - level) <= 2;
                if (observed >= 0) state.Level = observed;
                break;

            case Method.Gamma:
                ushort[] original;
                lock (_lock)
                {
                    if (!_originalRamps.TryGetValue(device, out var saved) || saved.Identity != state.Identity) return;
                    original = saved.Ramp;
                }
                var ramp = new ushort[original.Length];
                for (int i = 0; i < ramp.Length; i++) ramp[i] = (ushort)(original[i] * level / 100);
                ok = BrightnessApi.SetGamma(device, ramp);
                if (ok) state.Level = level; // software API acceptance, not physical backlight readback
                break;
        }
        lock (_lock)
        {
            if (generation != _generation) return;
            LastWriteSucceeded = ok;
            LastError = ok ? null : "Brightness change could not be confirmed.";
        }
    }

    private static void SetWmi(string hardwareId, int level)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        foreach (ManagementObject o in searcher.Get())
        {
            using (o)
                if (InstanceMatches((string)o["InstanceName"], hardwareId))
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
        }
    }

    /// <summary>Waits (briefly) for queued writes to finish; call before exiting so a restored level is not lost.</summary>
    public void Flush(int timeoutMs = 1500)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            lock (_lock) { if (!_running && _pending == null) return; }
            Thread.Sleep(20);
        }
    }

    public void Dispose()
    {
        Dictionary<string, (string Identity, ushort[] Ramp)> ramps;
        lock (_lock)
        {
            _disposed = true;
            _pending = null;
            ramps = new(_originalRamps);
        }
        // Leave no monitor stuck dimmed after the app exits.
        foreach (var (device, saved) in ramps)
            try { if (_identity(device) == saved.Identity) BrightnessApi.SetGamma(device, saved.Ramp); } catch { }
    }
}
