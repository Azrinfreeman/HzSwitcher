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

    public string ActiveProcess => _active == null ? null : Match.Normalize(_active.Process);
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
                          bool startTimer = true, IForeground foreground = null, IClock clock = null)
    {
        _settings = settings;
        _display = display;
        _auto = auto;
        _apply = apply;
        _foreground = foreground ?? new SystemForeground();
        _clock = clock ?? new SystemClock();
        if (!startTimer) return;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Tick()
    {
        var s = _settings();
        if (!s.ProfilesEnabled)
        {
            if (_active != null) Leave("profiles turned off");
            _candidate = null;
            return;
        }
        // The power-saver owns the rate while it is active; profiles resume once it has restored.
        if (_auto.State == SaverState.Saver || _auto.Suspended)
        {
            _candidate = null;
            _leaveSince = null;
            return;
        }

        string proc = _foreground.ProcessName();
        if (proc.Length == 0) { _candidate = null; return; }

        if (_active != null && !_overridden && _display.GetCurrentHz() != _applied) _overridden = true;

        var match = s.Profiles.FirstOrDefault(p => p.Enabled && p.Process.Trim().Length > 0 && Match.Wildcard(p.Process, proc));
        var now = _clock.UtcNow;

        if (match != null)
        {
            _leaveSince = null;
            if (_active != null && _active.Process == match.Process)
            {
                _candidate = null;
                return;
            }
            if (_candidate == null || _candidate.Process != match.Process) { _candidate = match; _candidateSince = now; return; }
            if ((now - _candidateSince).TotalSeconds < 1.5) return;   // ignore alt-tab flicker
            Enter(match, proc);
            return;
        }

        _candidate = null;
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
        if (current != target && _auto.AutomaticSwitchWaitSeconds > 0) return;
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

    public void Dispose() => _timer?.Dispose();
}
