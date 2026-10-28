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
    internal sealed record ProbeResult(BrightnessBackend Backend, int Level = 100, string Key = "test", string HardwareId = "", uint Min = 0, uint Max = 100);
    /// <summary>Software dimming never goes below this; Windows rejects very dark gamma ramps anyway.</summary>
    public const int SoftwareFloor = 20;

    private enum Method { None, Ddc, Wmi, Gamma }

    private sealed class State
    {
        public Method Method;
        public string Key;       // stable monitor id, used to remember the level per monitor
        public string HardwareId; // e.g. BOE0A1B, matches the WMI InstanceName
        public uint Min, Max;
        public int Level = 100;
        public int? Requested, LastRead;
        public DateTimeOffset? ReadAt;
    }

    private readonly Func<string> _device;
    private readonly Func<string, int> _remembered;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current;
    private readonly object _lock = new();
    private readonly Dictionary<string, State> _states = new();
    private readonly Dictionary<string, ushort[]> _originalRamps = new();
    private (string Device, int Level)? _pending;
    private bool _running;
    private bool _disposed;
    private long _generation;
    private long _writeRevision;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, Task> _refreshes = new();
    private readonly Func<string, ProbeResult> _probeOverride;
    private readonly Func<string, int, bool> _writeOverride;
    private string _writingDevice;

    /// <summary>Raised on the UI thread after a read or write completes.</summary>
    public event Action Changed;
    public event Action<OperationFailure> Failed;

    /// <param name="device">The monitor being controlled (same as DisplayService.Device).</param>
    /// <param name="remembered">Saved level for a monitor key, or -1; used where the real level cannot be read.</param>
    public BrightnessService(Func<string> device, Func<string, int> remembered) : this(device, remembered, null, null) { }

    internal BrightnessService(Func<string> device, Func<string, int> remembered,
        Func<string, ProbeResult> probe, Func<string, int, bool> write)
    {
        _device = device;
        _remembered = remembered;
        _probeOverride = probe;
        _writeOverride = write;
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

    /// <summary>Forget what was learned about the monitors, e.g. after the selection or topology changed.</summary>
    public void Invalidate()
    {
        lock (_lock) { _generation++; _states.Clear(); _refreshes.Clear(); }
    }

    /// <summary>Reads only cached state. Opening diagnostics must never probe the monitor synchronously.</summary>
    public BrightnessSnapshot GetSnapshot()
    {
        string device = _device();
        lock (_lock)
        {
            _states.TryGetValue(device, out var state);
            bool reading = _refreshes.TryGetValue(device, out var task) && !task.IsCompleted;
            bool writing = _running && _writingDevice == device || _pending?.Device == device;
            return new(state == null ? BrightnessBackend.NotChecked : Backend(state.Method), state?.Method == Method.None ? null : state?.Level,
                state?.Requested, state?.LastRead, state?.ReadAt, reading, writing);
        }
    }

    /// <summary>Re-reads the real brightness in the background, then raises Changed.</summary>
    public Task RefreshAsync()
    {
        string device = _device();
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            if (_refreshes.TryGetValue(device, out var active) && !active.IsCompleted) return active;
            foreach (string key in _refreshes.Where(p => p.Value.IsCompleted).Select(p => p.Key).ToArray()) _refreshes.Remove(key);
            long generation = _generation;
            return _refreshes[device] = Task.Run(async () =>
            {
                await _refreshGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    long revision;
                    lock (_lock)
                    {
                        if (_disposed || generation != _generation || device != _device()) return;
                        revision = _writeRevision;
                    }
                    var state = Probe(device);
                    lock (_lock)
                    {
                        if (_disposed || generation != _generation || device != _device() || _running || _pending != null || revision != _writeRevision) return;
                        if (_states.TryGetValue(device, out var old))
                        {
                            if (state.Method == Method.Gamma && old.Method == Method.Gamma) state.Level = old.Level;
                            state.Requested = old.Requested;
                        }
                        _states[device] = state;
                    }
                }
                catch (Exception e) { Log.Write("Brightness refresh failed: " + e.Message); }
                finally { _refreshGate.Release(); Post(); }
            });
        }
    }

    public void RequestSet(int percent)
    {
        string device = _device();
        var state = Current();
        if (state.Method == Method.None) return;
        int level = Math.Clamp(percent, state.Method == Method.Gamma ? SoftwareFloor : 0, 100);
        lock (_lock)
        {
            if (_disposed) return;
            state.Level = level;
            state.Requested = level;
            _writeRevision++;
            _pending = (device, level);
            if (_running) return;
            _running = true;
        }
        Task.Run(Pump);
    }

    private State Current()
    {
        string device = _device();
        lock (_lock) { if (_states.TryGetValue(device, out var s)) return s; }
        var state = Probe(device);
        lock (_lock) { _states[device] = state; }
        return state;
    }

    private void Pump()
    {
        while (true)
        {
            (string Device, int Level) work;
            lock (_lock)
            {
                if (_pending == null || _disposed) { _running = false; _writingDevice = null; break; }
                work = _pending.Value;
                _pending = null;
                _writingDevice = work.Device;
            }
            bool applied = false;
            try { applied = Apply(work.Device, work.Level); }
            catch (Exception e) { Log.Write($"Brightness set failed on {work.Device}: {e.Message}"); }
            if (!applied)
            {
                Log.Write($"Brightness: write of {work.Level}% failed on {work.Device}");
                Failed?.Invoke(new(work.Device, OperationKind.Brightness, FailureKind.WriteFailed, work.Level, null, DateTimeOffset.UtcNow));
            }
        }
        Post();
    }

    private void Post()
    {
        var handler = Changed;
        if (handler == null) return;
        if (_ui != null) _ui.Post(_ => { if (!_disposed) handler(); }, null); else if (!_disposed) handler();
    }

    // ---------- probing ----------

    private State Probe(string device)
    {
        State state;
        if (_probeOverride == null) state = ProbeHardware(device);
        else
        {
            var result = _probeOverride(device);
            state = new State { Method = result.Backend switch
            {
                BrightnessBackend.Ddc => Method.Ddc, BrightnessBackend.Wmi => Method.Wmi,
                BrightnessBackend.Software => Method.Gamma, _ => Method.None,
            }, Level = result.Level, Key = result.Key, HardwareId = result.HardwareId, Min = result.Min, Max = result.Max };
        }
        if (state.Method is Method.Ddc or Method.Wmi)
        { state.LastRead = state.Level; state.ReadAt = DateTimeOffset.UtcNow; }
        return state;
    }

    private static BrightnessBackend Backend(Method method) => method switch
    {
        Method.Ddc => BrightnessBackend.Ddc, Method.Wmi => BrightnessBackend.Wmi,
        Method.Gamma => BrightnessBackend.Software, _ => BrightnessBackend.Unsupported,
    };

    private State ProbeHardware(string device)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        string key = DisplayApi.EnumDisplayDevices(device, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceID) ? dd.DeviceID : device;
        var parts = key.Split('\\');
        var state = new State { Key = key, HardwareId = parts.Length > 1 ? parts[1] : "" };

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
                lock (_lock) { if (!_originalRamps.ContainsKey(device)) _originalRamps[device] = ramp; }
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

    private bool Apply(string device, int level)
    {
        State state;
        lock (_lock) { if (!_states.TryGetValue(device, out state)) return false; }
        if (_writeOverride != null) return _writeOverride(device, level);

        switch (state.Method)
        {
            case Method.Ddc:
                uint raw = state.Min + (uint)Math.Round(level / 100.0 * (state.Max - state.Min));
                return BrightnessApi.SetDdc(device, raw);

            case Method.Wmi:
                return SetWmi(state.HardwareId, level);

            case Method.Gamma:
                ushort[] original;
                lock (_lock) { if (!_originalRamps.TryGetValue(device, out original)) return false; }
                var ramp = new ushort[original.Length];
                for (int i = 0; i < ramp.Length; i++) ramp[i] = (ushort)(original[i] * level / 100);
                return BrightnessApi.SetGamma(device, ramp);
        }
        return false;
    }

    private static bool SetWmi(string hardwareId, int level)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        foreach (ManagementObject o in searcher.Get())
        {
            using (o)
                if (InstanceMatches((string)o["InstanceName"], hardwareId))
                    return Convert.ToUInt32(o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level })) == 0;
        }
        return false;
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
        Dictionary<string, ushort[]> ramps;
        lock (_lock)
        {
            _disposed = true;
            _pending = null;
            ramps = new(_originalRamps);
        }
        // Leave no monitor stuck dimmed after the app exits.
        foreach (var (device, ramp) in ramps)
            try { BrightnessApi.SetGamma(device, ramp); } catch { }
    }
}
