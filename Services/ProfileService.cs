namespace HzSwitcher.Services;

/// <summary>
/// Per-app rates: while a profiled app is in the foreground its rate is used; once it has been out of the
/// foreground for ProfileLeaveSeconds the previous rate returns (unless the user changed the rate meanwhile).
/// </summary>
public class ProfileService : IDisposable
{
    private readonly Func<Settings> _settings;
    private readonly IDisplay _display;
    private readonly AutoSaverService _auto;
    private readonly Func<int, bool> _apply;
    private readonly IForeground _foreground;
    private readonly IClock _clock;
    private readonly System.Windows.Forms.Timer _timer;

    private AppProfile _active;
    private int _before;
    private int _applied;
    private bool _overridden;
    private AppProfile _candidate;
    private DateTime _candidateSince;
    private DateTime? _leaveSince;
    private readonly AutomationPauseService _pause;
    private readonly Func<string> _device;
    private string _ownedDevice;
    private bool _wasPaused;

    public string ActiveProcess => _active == null ? null : Match.Normalize(_active.Process);
    public AutomationReason Reason { get; private set; } = AutomationReason.Starting;
    public event Action<string> Notice;
    public event Action Changed;

    /// <summary>Record explicit intent even when the user chooses the already-active rate.</summary>
    public void OnManualChange()
    {
        _candidate = null;
        if (_active == null) return;
        _before = _display.GetCurrentHz();
        _overridden = true;
    }

    /// <param name="startTimer">False in tests: Tick() is driven by hand.</param>
    public ProfileService(Func<Settings> settings, IDisplay display, AutoSaverService auto, Func<int, bool> apply,
                          bool startTimer = true, IForeground foreground = null, IClock clock = null,
                          AutomationPauseService pause = null, Func<string> device = null)
    {
        _settings = settings;
        _display = display;
        _auto = auto;
        _apply = apply;
        _foreground = foreground ?? new SystemForeground();
        _clock = clock ?? new SystemClock();
        _auto.OwnershipSuperseded += OnManualChange;
        _pause = pause;
        _device = device ?? (() => _settings().MonitorDevice);
        if (_pause != null) { _wasPaused = _pause.IsPaused; _pause.Changed += OnPauseChanged; }
        if (!startTimer) return;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Tick()
    {
        _pause?.Tick();
        if (_pause?.IsPaused == true) { ResetQualification(); Reason = AutomationReason.Paused; return; }
        if (_active != null && _ownedDevice != _device()) ClearOwnership();
        var s = _settings();
        // Lock/confirmation also blocks release after a preference changes.
        if (_auto.Suspended)
        {
            Reason = _auto.Busy ? AutomationReason.Busy : AutomationReason.Locked;
            ResetQualification();
            return;
        }
        if (!s.ProfilesEnabled)
        {
            Reason = AutomationReason.Disabled;
            if (_active != null) Leave("profiles turned off");
            _candidate = null;
            return;
        }
        // The power-saver owns the rate while it is active; profiles resume once it has restored.
        if (_auto.State == SaverState.Saver)
        {
            Reason = _auto.Reason;
            _candidate = null;
            _leaveSince = null;
            return;
        }

        string proc = _foreground.ProcessName();
        if (proc.Length == 0) { _candidate = null; Reason = AutomationReason.ForegroundUnknown; return; }

        if (_active != null && !_overridden && _display.GetCurrentHz() != _applied) _overridden = true;

        var match = s.Profiles.FirstOrDefault(p => p.Enabled && p.Process.Trim().Length > 0 && Match.Wildcard(p.Process, proc));
        var now = _clock.UtcNow;

        if (match != null)
        {
            _leaveSince = null;
            if (_active != null && _active.Process == match.Process)
            {
                Reason = _overridden ? AutomationReason.ManualOverride : AutomationReason.Profile;
                _candidate = null;
                return;
            }
            Reason = AutomationReason.Qualifying;
            if (_candidate == null || _candidate.Process != match.Process) { _candidate = match; _candidateSince = now; return; }
            if ((now - _candidateSince).TotalSeconds < 1.5) return;   // ignore alt-tab flicker
            Enter(match, proc);
            return;
        }

        _candidate = null;
        Reason = AutomationReason.Idle;
        if (_active != null)
        {
            _leaveSince ??= now;
            if ((now - _leaveSince.Value).TotalSeconds >= s.ProfileLeaveSeconds) Leave("left the app");
        }
    }

    private void Enter(AppProfile profile, string proc)
    {
        int current = _display.GetCurrentHz();
        int target = _display.Resolve(profile.Hz);
        // Keep only the latest foreground candidate while cooling down. No intermediate rate is applied.
        if (current != target && _auto.AutomaticSwitchWaitSeconds > 0) { Reason = AutomationReason.Cooldown; return; }
        if (current != target)
        {
            if (!_apply(target)) { _auto.OnAutomaticChange(); Log.Write($"Profile {proc}: could not apply {target} Hz"); return; }
            _auto.OnAutomaticChange();
            Log.Write($"Profile {proc}: {current} -> {target} Hz");
            Notice?.Invoke($"{proc}: {target} Hz");
        }
        if (_active == null) _before = current;      // moving between two profiled apps keeps the original rate
        _overridden = false;
        _candidate = null;
        _active = profile;
        _ownedDevice = _device();
        Reason = AutomationReason.Profile;
        _applied = target;
        Changed?.Invoke();
    }

    private void Leave(string reason)
    {
        int back = _before;
        bool restore = !_overridden && back > 0 && back != _applied && _display.GetCurrentHz() == _applied;
        if (restore && reason != "profiles turned off" && _auto.AutomaticSwitchWaitSeconds > 0) return;
        if (restore && _apply(back))
        {
            _auto.OnAutomaticChange();
            Log.Write($"Profile ended ({reason}): back to {back} Hz");
            Notice?.Invoke($"Back to {back} Hz");
        }
        _active = null;
        _leaveSince = null;
        Changed?.Invoke();
    }

    public void ResetQualification() { _candidate = null; _leaveSince = null; }

    private void OnPauseChanged()
    {
        if (_wasPaused && !_pause.IsPaused) ResetQualification();
        _wasPaused = _pause.IsPaused;
        if (_wasPaused) ResetQualification();
        Reason = _wasPaused ? AutomationReason.Paused : AutomationReason.Starting;
    }

    public AutomationReleaseResult ReleaseForPause()
    {
        ResetQualification();
        if (_active == null) return AutomationReleaseResult.NoOwnership;
        if (_ownedDevice != _device() || _overridden || _display.GetCurrentHz() != _applied)
        { ClearOwnership(); return AutomationReleaseResult.Superseded; }
        if (_before > 0 && _before != _applied && !_apply(_before))
        { Reason = AutomationReason.RestoreFailed; return AutomationReleaseResult.Failed; }
        _auto.OnAutomaticChange();
        ClearOwnership();
        return AutomationReleaseResult.Released;
    }

    private void ClearOwnership()
    {
        _active = null;
        _ownedDevice = null;
        _overridden = false;
        ResetQualification();
        Reason = _pause?.IsPaused == true ? AutomationReason.Paused : AutomationReason.Idle;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_pause != null) _pause.Changed -= OnPauseChanged;
        _auto.OwnershipSuperseded -= OnManualChange;
        _timer?.Dispose();
    }
}
