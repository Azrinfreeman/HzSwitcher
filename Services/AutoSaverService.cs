using HzSwitcher.Native;
using Microsoft.Win32;

namespace HzSwitcher.Services;

public enum SaverState { Active, Saver }

/// <summary>
/// Drops to the saver rate after idle / watching, and restores the previous rate once the user
/// has been continuously active for RestoreSeconds.
/// </summary>
public class AutoSaverService : IDisposable
{
    private readonly Func<Settings> _settings;
    private readonly IDisplay _display;
    private readonly IMediaInfo _media;
    private readonly IActivity _activity;
    private readonly IClock _clock;
    private readonly Func<int, bool> _apply;
    private readonly Control _ui;
    private readonly System.Windows.Forms.Timer _timer;

    private DateTime? _activityWindowStart;
    private DateTime? _mediaSince;
    private DateTime _lastRestore = DateTime.MinValue;
    private DateTime _lastExternalCheck = DateTime.MinValue;
    private bool _locked;
    private readonly AutomationPauseService _pause;
    private readonly Func<string> _device;
    private string _ownedDevice;
    private int _appliedHz;
    private DateTime? _qualifiedAfter;
    private bool _wasPaused;

    public SaverState State { get; private set; } = SaverState.Active;
    public int RestoreHz { get; private set; }
    /// <summary>Set while a manual change dialog is open.</summary>
    public bool Busy { get; set; }
    /// <summary>Human-readable progress shown in the main window.</summary>
    public string Status { get; private set; } = "Starting…";
    /// <summary>0..1 progress toward dropping (when active) or toward restoring (when in saver).</summary>
    public double Progress { get; private set; }
    public AutomationReason Reason { get; private set; } = AutomationReason.Starting;
    public event Action Changed;
    /// <summary>Raised after the saver rate was applied, and when the saver state ends for any reason.</summary>
    public event Action SaverStarted;
    public event Action SaverEnded;
    /// <summary>Existing automation ownership was superseded by an external rate or monitor change.</summary>
    public event Action OwnershipSuperseded;
    /// <summary>Raised after an automatic switch so the UI can show a toast.</summary>
    public event Action<string> Notice;

    /// <param name="ui">UI control for marshaling system events. Pass null in tests: no timer or system hooks are started and Tick() is driven by hand.</param>
    public AutoSaverService(Func<Settings> settings, IDisplay display, IMediaInfo media, Control ui = null,
                            IActivity activity = null, IClock clock = null, Func<int, bool> apply = null,
                            AutomationPauseService pause = null, Func<string> device = null)
    {
        _settings = settings;
        _display = display;
        _media = media;
        _activity = activity ?? new SystemActivity();
        _clock = clock ?? new SystemClock();
        _apply = apply ?? display.TryApply;
        _ui = ui;
        _pause = pause;
        _device = device ?? (() => _settings().MonitorDevice);
        if (_pause != null) { _wasPaused = _pause.IsPaused; _pause.Changed += OnPauseChanged; }
        if (ui == null) return;
        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public bool Suspended => Busy || _locked || _pause?.IsPaused == true;

    /// <summary>Shared cooldown for new automatic choices; restoration and manual actions stay responsive.</summary>
    public double AutomaticSwitchWaitSeconds => Math.Max(0,
        Math.Clamp(_settings().MinSwitchSeconds, 0, 300) - (_clock.UtcNow - _lastRestore).TotalSeconds);

    public void OnAutomaticChange() => _lastRestore = _clock.UtcNow;

    /// <summary>One state-machine step; the UI timer calls this every 250 ms.</summary>
    public void Tick()
    {
        _pause?.Tick();
        if (State == SaverState.Saver && _ownedDevice != _device()) ReleaseSuperseded();
        if (_pause?.IsPaused == true)
        { ResetWindows(); Reason = AutomationReason.Paused; Status = _pause.Description; Progress = 0; return; }
        var s = _settings();
        if (!s.AutoEnabled) { _mediaSince = null; Reason = AutomationReason.Disabled; Status = "Auto power-saver is off"; Progress = 0; return; }
        if (Busy) { _mediaSince = null; Reason = AutomationReason.Busy; Status = "Waiting for confirmation…"; Progress = 0; return; }
        if (_locked) { _mediaSince = null; Reason = AutomationReason.Locked; Status = "Paused while the screen is locked"; Progress = 0; return; }

        uint idle = _activity.IdleMilliseconds();
        var now = _clock.UtcNow;
        if (_qualifiedAfter.HasValue)
            idle = Math.Min(idle, (uint)Math.Clamp((now - _qualifiedAfter.Value).TotalMilliseconds, 0, uint.MaxValue));

        if (State == SaverState.Active)
        {
            bool idleLong = idle >= s.IdleMinutes * 60_000u;

            bool watching = false;
            if (_media.IsWatching())
            {
                _mediaSince ??= now;
                watching = (now - _mediaSince.Value).TotalMinutes >= s.IdleMinutes
                           && idle >= s.WatchingQuietSeconds * 1000u;
            }
            else _mediaSince = null;

            Status = _mediaSince.HasValue
                ? $"Watching video: {Fmt(now - _mediaSince.Value)} of {s.IdleMinutes} min"
                : $"No input for {Fmt(TimeSpan.FromMilliseconds(idle))} of {s.IdleMinutes} min";
            Reason = _mediaSince.HasValue ? AutomationReason.Watching : AutomationReason.Idle;

            Progress = _mediaSince.HasValue
                ? Math.Min(1, (now - _mediaSince.Value).TotalMinutes / s.IdleMinutes)
                : Math.Min(1, idle / (s.IdleMinutes * 60_000.0));

            if (idleLong || watching)
            {
                if (_media.IsGameForeground()) { Reason = AutomationReason.Game; Status = "Paused: a game is in the foreground"; }
                else if (AutomaticSwitchWaitSeconds <= 0)
                    EnterSaver(idleLong ? "idle" : "watching");
                else { Reason = AutomationReason.Cooldown; Status = $"Waiting {Math.Ceiling(AutomaticSwitchWaitSeconds):0} s before another automatic switch"; }
            }
            return;
        }

        // Saver state: someone changed the rate behind our back (Windows settings, Radeon, hotkey)?
        if ((now - _lastExternalCheck).TotalSeconds >= 2)
        {
            _lastExternalCheck = now;
            if (_display.GetCurrentHz() != _appliedHz)
            {
                Log.Write("Rate changed externally while in saver; leaving saver state");
                ReleaseSuperseded();
                OnManualChange();
                return;
            }
        }

        // Restore only after activity has continued for RestoreSeconds with no gap above ActivityGapSeconds.
        if (idle < s.ActivityGapSeconds * 1000)
        {
            _activityWindowStart ??= now;
            double secs = (now - _activityWindowStart.Value).TotalSeconds;
            Progress = Math.Min(1, secs / s.RestoreSeconds);
            Reason = AutomationReason.Restoring;
            Status = $"Saver on: activity {secs:0.0} of {s.RestoreSeconds} s, then {RestoreHz} Hz returns";
            if (secs >= s.RestoreSeconds)
                ExitSaver("activity");
        }
        else
        {
            _activityWindowStart = null;
            Progress = 0;
            Reason = AutomationReason.Saver;
            Status = $"Saver on ({s.SaverHz} Hz): keep moving the mouse for {s.RestoreSeconds} s to restore {RestoreHz} Hz";
        }
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{t.Seconds}s";

    private void EnterSaver(string reason)
    {
        var s = _settings();
        int current = _display.GetCurrentHz();
        if (current <= s.SaverHz) { _lastRestore = _clock.UtcNow; return; }   // already low; don't retry every tick
        if (!_apply(s.SaverHz)) { _lastRestore = _clock.UtcNow; return; }

        RestoreHz = current;
        _ownedDevice = _device();
        _appliedHz = s.SaverHz;
        State = SaverState.Saver;
        Reason = AutomationReason.Saver;
        _activityWindowStart = null;
        _lastExternalCheck = _clock.UtcNow;
        OnAutomaticChange();
        Log.Write($"Saver ({reason}): {current} -> {s.SaverHz} Hz");
        SaverStarted?.Invoke();
        Notice?.Invoke(reason == "watching" ? $"Watching video: dropped to {s.SaverHz} Hz" : $"Idle: dropped to {s.SaverHz} Hz");
        Changed?.Invoke();
    }

    public void ExitSaver(string reason)
    {
        if (State != SaverState.Saver) return;
        if (!OwnsCurrentRate()) { ReleaseSuperseded(); return; }
        if (_apply(RestoreHz))
        {
            Log.Write($"Restored {RestoreHz} Hz ({reason})");
            int restored = RestoreHz;
            SetActive();
            Notice?.Invoke($"Back to {restored} Hz");
            _lastRestore = _clock.UtcNow;
        }
        else
        {
            Reason = AutomationReason.RestoreFailed;
            Log.Write($"Restore to {RestoreHz} Hz failed; will retry on next activity window");
            _activityWindowStart = null;
        }
    }

    private bool OwnsCurrentRate() => _ownedDevice == _device() && _display.GetCurrentHz() == _appliedHz;

    public AutomationReleaseResult ReleaseForPause()
    {
        ResetWindows();
        if (State != SaverState.Saver) return AutomationReleaseResult.NoOwnership;
        if (!OwnsCurrentRate()) { ReleaseSuperseded(); return AutomationReleaseResult.Superseded; }
        ExitSaver("automation paused");
        return State == SaverState.Active ? AutomationReleaseResult.Released : AutomationReleaseResult.Failed;
    }

    private void ResetWindows() { _mediaSince = null; _activityWindowStart = null; }

    private void ReleaseSuperseded()
    {
        OwnershipSuperseded?.Invoke();
        SetActive();
    }

    public void ResetQualification()
    {
        ResetWindows();
        _qualifiedAfter = _clock.UtcNow;
        Progress = 0;
    }

    private void OnPauseChanged()
    {
        if (_wasPaused && !_pause.IsPaused) ResetQualification();
        _wasPaused = _pause.IsPaused;
        if (_wasPaused)
        {
            ResetWindows();
            Reason = AutomationReason.Paused;
            Status = _pause.Description;
            Progress = 0;
        }
        else { Reason = AutomationReason.Starting; Status = "Checking automation…"; }
    }

    /// <summary>A manual rate choice cancels the saver state; the chosen rate becomes the active one.</summary>
    public void OnManualChange()
    {
        if (State == SaverState.Saver) SetActive();
        _mediaSince = null;
        _lastRestore = _clock.UtcNow;
    }

    private void SetActive()
    {
        bool wasSaver = State == SaverState.Saver;
        State = SaverState.Active;
        _ownedDevice = null;
        Reason = _pause?.IsPaused == true ? AutomationReason.Paused : AutomationReason.Idle;
        _activityWindowStart = null;
        _mediaSince = null;
        if (wasSaver) SaverEnded?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Screen lock pauses the saver; unlocking restores the previous rate.</summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (!locked)
        {
            _pause?.Tick();
            if (_pause?.IsPaused != true) ExitSaver("unlock");
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _ui.BeginInvoke(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) SetLocked(true);
            else if (e.Reason == SessionSwitchReason.SessionUnlock) SetLocked(false);
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _ui.BeginInvoke(() => { _pause?.Tick(); if (_pause?.IsPaused != true) ExitSaver("resume"); });
    }

    public void Dispose()
    {
        if (_pause != null) _pause.Changed -= OnPauseChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer?.Dispose();
    }
}
