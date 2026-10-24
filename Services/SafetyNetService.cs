namespace HzSwitcher.Services;

/// <summary>
/// Switches the rate immediately (no dialog) and puts the monitor back on the fallback rate
/// (240 Hz by default) if a change leaves the display at an unexpected rate.
/// </summary>
public class SafetyNetService : IDisposable
{
    private const int VerifyDelayMs = 3000;

    private readonly IDisplay _display;
    private readonly Settings _settings;
    private readonly Func<string> _device;
    private readonly System.Windows.Forms.Timer _verify = new() { Interval = VerifyDelayMs };
    private int _expectedHz;
    private string _expectedDevice;
    private bool _verifying;

    public SafetyNetService(IDisplay display, Settings settings, Func<string> device = null)
    {
        _display = display;
        _settings = settings;
        _device = device ?? (() => settings.MonitorDevice);
        _verify.Tick += (_, _) => { _verify.Stop(); Verify(); };
    }

    /// <summary>Fallback rate: the configured one if the monitor supports it, otherwise the highest supported.</summary>
    public int FallbackHz()
    {
        var supported = _display.GetSupportedHz();
        if (supported.Contains(_settings.FallbackHz)) return _settings.FallbackHz;
        return supported.Count > 0 ? supported.Max() : _settings.FallbackHz;
    }

    /// <summary>If the last run died mid-change, put the fallback rate back.</summary>
    public void RecoverIfNeeded()
    {
        if (!_settings.PendingChange) return;
        int fb = FallbackHz();
        Log.Write($"Recovering from unfinished change; resetting to {fb} Hz");
        if (_display.GetCurrentHz() != fb) _display.TryApply(fb);
        _settings.PendingChange = false;
        SettingsStore.Save(_settings);
    }

    /// <summary>Returns true if the requested rate is now active.</summary>
    public bool Apply(int hz)
    {
        int before = _display.GetCurrentHz();
        string device = _device();
        if (hz > 0 && before == hz)
        {
            // Keep verification of this same request, but retire an older, superseded one.
            if (_verifying && (_expectedHz != hz || _expectedDevice != device)) CancelVerification();
            return true;
        }

        _verify.Stop();
        _verifying = false;
        _settings.PendingChange = true;       // cleared once the mode has held (or been reset)
        SettingsStore.Save(_settings);

        bool ok = _display.TryApply(hz) && _display.GetCurrentHz() == hz;
        if (!ok)
        {
            if (before > 0 && _display.GetCurrentHz() == before)
            {
                Log.Write($"{hz} Hz was rejected; keeping the unchanged {before} Hz mode");
                CancelVerification();
                return false;
            }
            Log.Write($"{hz} Hz did not apply; resetting to fallback");
            ResetToFallback();
            return false;
        }

        _expectedHz = hz;
        _expectedDevice = device;
        _verifying = true;
        _verify.Stop();
        _verify.Start();                      // re-check shortly after the change
        return true;
    }

    internal void Verify()
    {
        if (!_verifying) return;
        _verify.Stop();
        if (_device() != _expectedDevice)
        {
            Log.Write("Monitor changed during verification; skipping the stale fallback");
            CancelVerification();
            return;
        }
        int now = _display.GetCurrentHz();
        if (now == _expectedHz)
        {
            CancelVerification();
            return;
        }
        Log.Write($"Display did not hold {_expectedHz} Hz (now {now}); resetting to fallback");
        ResetToFallback();
    }

    private void ResetToFallback()
    {
        int fb = FallbackHz();
        if (_display.GetCurrentHz() != fb) _display.TryApply(fb);
        CancelVerification();
    }

    internal void CancelVerification()
    {
        _verify.Stop();
        _verifying = false;
        _settings.PendingChange = false;
        SettingsStore.Save(_settings);
    }

    public void Dispose() => _verify.Dispose();
}
