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
        public uint Min, Max;
        public int Level = 100;
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

    /// <summary>Raised on the UI thread after a read or write completes.</summary>
    public event Action Changed;

    /// <param name="device">The monitor being controlled (same as DisplayService.Device).</param>
    /// <param name="remembered">Saved level for a monitor key, or -1; used where the real level cannot be read.</param>
    public BrightnessService(Func<string> device, Func<string, int> remembered)
    {
        _device = device;
        _remembered = remembered;
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
        lock (_lock) _states.Clear();
    }

    /// <summary>Re-reads the real brightness in the background, then raises Changed.</summary>
    public void RefreshAsync()
    {
        string device = _device();
        Task.Run(() =>
        {
            var state = Probe(device);
            lock (_lock)
            {
                // A write in flight is newer than anything just read; keep what the user chose.
                if (_disposed || _running || _pending != null) return;
                // Software dimming cannot be read back, so keep the level already in use.
                if (state.Method == Method.Gamma && _states.TryGetValue(device, out var old) && old.Method == Method.Gamma)
                    state.Level = old.Level;
                _states[device] = state;
            }
            Post();
        });
    }

    public void RequestSet(int percent)
    {
        string device = _device();
        var state = Current();
        if (state.Method == Method.None) return;
        int level = Math.Clamp(percent, state.Method == Method.Gamma ? SoftwareFloor : 0, 100);
        state.Level = level;
        lock (_lock)
        {
            if (_disposed) return;
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
                if (_pending == null || _disposed) { _running = false; break; }
                work = _pending.Value;
                _pending = null;
            }
            try { Apply(work.Device, work.Level); }
            catch (Exception e) { Log.Write($"Brightness set failed on {work.Device}: {e.Message}"); }
        }
        Post();
    }

    private void Post()
    {
        var handler = Changed;
        if (handler == null) return;
        if (_ui != null) _ui.Post(_ => handler(), null); else handler();
    }

    // ---------- probing ----------

    private State Probe(string device)
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

    private void Apply(string device, int level)
    {
        State state;
        lock (_lock) { if (!_states.TryGetValue(device, out state)) return; }

        switch (state.Method)
        {
            case Method.Ddc:
                uint raw = state.Min + (uint)Math.Round(level / 100.0 * (state.Max - state.Min));
                if (!BrightnessApi.SetDdc(device, raw)) Log.Write($"Brightness: DDC/CI rejected {raw} on {device}");
                break;

            case Method.Wmi:
                SetWmi(state.HardwareId, level);
                break;

            case Method.Gamma:
                ushort[] original;
                lock (_lock) { if (!_originalRamps.TryGetValue(device, out original)) return; }
                var ramp = new ushort[original.Length];
                for (int i = 0; i < ramp.Length; i++) ramp[i] = (ushort)(original[i] * level / 100);
                if (!BrightnessApi.SetGamma(device, ramp)) Log.Write($"Brightness: gamma ramp rejected on {device}");
                break;
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
