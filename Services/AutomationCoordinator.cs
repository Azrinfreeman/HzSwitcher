using System.Diagnostics;

namespace HzSwitcher.Services;

/// <summary>Single live automation owner. The legacy saver/profile services are not started by the application.</summary>
public sealed class AutomationCoordinator : IDisposable
{
    private readonly Settings _settings;
    private readonly IDisplay _display;
    private readonly IMediaInfo _media;
    private readonly IActivity _activity;
    private readonly IForeground _foreground;
    private readonly IClock _clock;
    private readonly Func<double> _elapsed;
    private readonly Func<string> _target;
    private readonly Func<bool> _save;
    private readonly System.Windows.Forms.Timer _timer;
    private string _identity;
    private int _baseline, _expected, _restore;
    private IReadOnlyList<int> _rates = Array.Empty<int>();
    private double _lastSwitch = double.NegativeInfinity;
    private double? _mediaSince, _restoreSince, _candidateSince, _leaveSince;
    private AppProfile _candidate, _profile;
    private bool _locked, _suspended, _externalHold;
    private DateTime? _manualUntil;
    private bool _saver;
    private string _lastStatus;
    private bool _brightnessRecovery;
    public bool RecordingSession { get; private set; }
    public bool SaverActive => _saver;
    public int RestoreHz => _restore;
    public string ActiveProcess => _profile == null ? null : Match.Normalize(_profile.Process);
    public double Progress { get; private set; }
    public string Status { get; private set; } = "Reading display state...";
    public AutomationDecision Decision { get; private set; }
    public DisplayTransitionService Transitions { get; }
    public TransitionHistoryStore History { get; }
    public bool CanWriteAutomation => !_locked && !_suspended && !_settings.PreviewMode && !RecordingSession &&
        !_settings.AutomationPaused && !(_settings.PauseUntilUtc > _clock.UtcNow) && !_externalHold;
    public event Action Changed;
    public event Action<bool> SaverChanged;
    public event Action<string> Notice;
    public event Action CancelDeferredWrites;

    public AutomationCoordinator(Settings settings, IDisplay display, IMediaInfo media, Func<string> target,
        string dataDirectory, bool startTimer = true, IActivity activity = null, IForeground foreground = null,
        IClock clock = null, Func<double> elapsed = null, Func<bool> save = null)
    {
        _settings = settings; _display = display; _media = media; _target = target;
        _activity = activity ?? new SystemActivity(); _foreground = foreground ?? new SystemForeground();
        _clock = clock ?? new SystemClock();
        var watch = Stopwatch.StartNew(); _elapsed = elapsed ?? (() => watch.Elapsed.TotalSeconds);
        _save = save ?? (() => SettingsStore.TrySave(settings));
        _brightnessRecovery = settings.PendingBrightness >= 0;
        History = new(Path.Combine(dataDirectory, "transitions.json"), _clock);
        Transitions = new(display, target, _elapsed, Path.Combine(dataDirectory, "pending-transition.json"), History);
        if (startTimer)
        {
            _timer = new() { Interval = 250 };
            _timer.Tick += (_, _) => Tick(); _timer.Start();
        }
    }

    public void Tick()
    {
        if (_settings.PendingChange || _brightnessRecovery)
        { SetStatus("Legacy recovery needs review. Open Why this rate? and keep the current settings before continuing."); return; }
        string identity = _target();
        int current = _display.GetCurrentHz();
        if (string.IsNullOrEmpty(identity) || current <= 0)
        { SetStatus("Configured monitor unavailable. No changes will be redirected."); return; }
        if (_identity != identity)
        {
            _identity = identity; _baseline = _expected = current;
            _rates = _display.GetSupportedHz().ToArray();
            _profile = _candidate = null; _candidateSince = _leaveSince = _restoreSince = _mediaSince = null;
            _saver = false; _restore = 0; _externalHold = false; _manualUntil = null;
        }
        if (_locked || _suspended) { _restoreSince = _mediaSince = null; SetStatus("Automation paused while locked or suspended."); return; }
        Transitions.Tick();
        if (Transitions.NeedsRecovery)
        { SetStatus(Transitions.Error ?? "An interrupted display change needs review. Open Why this rate? to recover or keep the current rate."); return; }
        if (Transitions.Busy) { SetStatus($"Verifying {Transitions.Pending.RequestedHz} Hz by readback..."); return; }
        if (current != _expected)
        {
            History.Add(identity, _expected, current, current, "External change (source unknown)", "Automation yielded");
            _baseline = _expected = current; _externalHold = true; _saver = false;
            _restoreSince = _mediaSince = null;
        }
        var now = _clock.UtcNow;
        if (_settings.PauseUntilUtc <= now)
        { _settings.PauseUntilUtc = null; _save(); }
        if (_settings.AutomationPaused || _settings.PauseUntilUtc > now || RecordingSession || _externalHold || _manualUntil > now)
        {
            _restoreSince = _mediaSince = null; _candidateSince = null;
            SetStatus(RecordingSession ? "Recording / presentation session: automatic changes paused."
                : _externalHold ? "External rate change observed. Automation is paused until you Resume."
                : _manualUntil > now ? $"Your manual choice is held until {_manualUntil.Value.ToLocalTime():HH:mm}."
                : _settings.PauseUntilUtc > now ? $"Automation paused until {_settings.PauseUntilUtc.Value.ToLocalTime():HH:mm}."
                : "Automation paused until you Resume.");
            return;
        }
        _manualUntil = null;
        double t = _elapsed();
        uint idle = _activity.IdleMilliseconds();
        bool game = _media.IsGameForeground();
        bool watching = _media.IsWatching();
        if (watching) _mediaSince ??= t; else _mediaSince = null;
        bool saverDue = idle >= _settings.IdleMinutes * 60000u ||
            (_mediaSince.HasValue && t - _mediaSince.Value >= _settings.IdleMinutes * 60 && idle >= _settings.WatchingQuietSeconds * 1000u);
        if (_saver && idle < _settings.ActivityGapSeconds * 1000) _restoreSince ??= t;
        else _restoreSince = null;
        bool restoreReady = _restoreSince.HasValue && t - _restoreSince.Value >= _settings.RestoreSeconds;
        Progress = _saver ? (_restoreSince.HasValue ? Math.Min(1, (t - _restoreSince.Value) / _settings.RestoreSeconds) : 0)
            : Math.Min(1, idle / (_settings.IdleMinutes * 60000.0));
        UpdateProfile(t);
        var context = new AutomationContext(current, _baseline, _restore, _saver, game, saverDue, restoreReady,
            _settings.ProfilesEnabled ? _profile : null, ActiveProcess, _rates);
        Decision = AutomationPolicyEngine.Evaluate(_settings, context);
        int targetHz = Decision.TargetHz;
        if (!_rates.Contains(targetHz)) { SetStatus("Requested rate is unavailable; keeping the current mode."); return; }
        if (_settings.PreviewMode)
        {
            SetStatus($"Preview: {targetHz} Hz suggested. {Decision.Explanation} No automatic changes.");
            return;
        }
        if (current != targetHz)
        {
            bool restore = Decision.Reason is AutomationReason.Game or AutomationReason.Restoring || (_saver && !Decision.Saver);
            double wait = _settings.MinSwitchSeconds - (t - _lastSwitch);
            if (!restore && wait > 0) { SetStatus($"Waiting {Math.Ceiling(wait)} s before {targetHz} Hz. {Decision.Explanation}"); return; }
            if (History.IsBlocked(identity, current, targetHz))
            { SetStatus($"Automatic {current} -> {targetHz} Hz is disabled by your feedback. Choose a rate manually or review history."); return; }
            _lastSwitch = t;
            if (!Transitions.Apply(targetHz, Decision.Explanation))
            { SetStatus(Transitions.Error ?? "Refresh-rate change failed."); return; }
            _expected = targetHz;
            Notice?.Invoke($"{targetHz} Hz: {Decision.Explanation}");
        }
        if (Decision.Saver != _saver)
        {
            // Do not claim saver ownership if no actual lowering occurred.
            if (Decision.Saver && current <= targetHz) { SetStatus(Decision.Explanation); return; }
            if (Decision.Saver) _restore = current;
            _saver = Decision.Saver;
            if (!_saver) _restore = 0;
            SaverChanged?.Invoke(_saver);
        }
        SetStatus($"{targetHz} Hz. {Decision.Explanation}");
    }

    private void UpdateProfile(double t)
    {
        string process = _foreground.ProcessName();
        var match = _settings.ProfilesEnabled ? _settings.Profiles.FirstOrDefault(p => p.Enabled &&
            p.Process.Length > 0 && Match.Wildcard(p.Process, process)) : null;
        if (match == _profile) { _candidate = null; _candidateSince = _leaveSince = null; return; }
        if (match == null)
        {
            _candidate = null; _candidateSince = null;
            if (_profile != null)
            {
                _leaveSince ??= t;
                if (!_settings.ProfilesEnabled || t - _leaveSince.Value >= _settings.ProfileLeaveSeconds) _profile = null;
            }
            return;
        }
        _leaveSince = null;
        if (match != _candidate || !_candidateSince.HasValue) { _candidate = match; _candidateSince = t; return; }
        if (t - _candidateSince.Value >= 1.5) { _profile = match; _candidate = null; _candidateSince = null; }
    }

    public bool SetManualRate(int hz)
    {
        if (_locked || _suspended || _settings.PendingChange || _brightnessRecovery)
        { SetStatus("Review pending recovery before choosing a new rate."); return false; }
        TickIdentity();
        if (!Transitions.Apply(hz, "Manual refresh-rate selection")) { SetStatus(Transitions.Error); return false; }
        _expected = _baseline = hz;
        _manualUntil = _clock.UtcNow.AddMinutes(_settings.ManualHoldMinutes);
        _externalHold = false; _profile = _candidate = null;
        if (_saver) { _saver = false; _restore = 0; SaverChanged?.Invoke(false); }
        _lastSwitch = _elapsed();
        SetStatus($"Your manual choice is held until {_manualUntil.Value.ToLocalTime():HH:mm}.");
        return true;
    }

    private void TickIdentity()
    {
        // Establish the baseline without evaluating or executing any rule.
        var identity = _target();
        if (_identity == identity) return;
        _identity = identity; _baseline = _expected = _display.GetCurrentHz();
        _rates = _display.GetSupportedHz().ToArray();
        _saver = false; _restore = 0; _candidate = _profile = null;
    }

    public void Pause(int? minutes)
    {
        CancelDeferredWrites?.Invoke();
        _settings.AutomationPaused = !minutes.HasValue;
        _settings.PauseUntilUtc = minutes.HasValue ? _clock.UtcNow.AddMinutes(minutes.Value) : null;
        _save(); Tick();
    }

    public void Resume()
    {
        _settings.AutomationPaused = false; _settings.PauseUntilUtc = null;
        RecordingSession = false; _manualUntil = null; _externalHold = false;
        _candidate = _profile = null; _candidateSince = _restoreSince = _mediaSince = null;
        if (!_save())
        {
            _settings.AutomationPaused = true;
            SetStatus("Automation remains paused because settings could not be saved.");
            return;
        }
        Tick();
    }

    public void SetRecording(bool active)
    {
        if (active) CancelDeferredWrites?.Invoke();
        RecordingSession = active; _candidateSince = _mediaSince = _restoreSince = null; Tick();
    }

    public void SetPreview(bool preview)
    {
        if (preview) CancelDeferredWrites?.Invoke();
        // Caller explicitly chooses whether to retain an existing temporary mode before entering preview.
        bool previous = _settings.PreviewMode;
        _settings.PreviewMode = preview; _mediaSince = _restoreSince = _candidateSince = null;
        if (!_save())
        {
            _settings.PreviewMode = previous;
            _settings.AutomationPaused = true;
            SetStatus("Automation remains paused because its mode could not be saved.");
            return;
        }
        Tick();
    }

    public bool RestoreOwnedMode()
    {
        if (_settings.PreviewMode || _locked || _suspended || _target() != _identity || Transitions.Pending != null) return false;
        if (_display.GetCurrentHz() != _expected) return false;
        if (_expected == _baseline) return true;
        if (!Transitions.Apply(_baseline, "Explicit restore of owned mode")) return false;
        _expected = _baseline; _saver = false; _restore = 0; SaverChanged?.Invoke(false);
        _manualUntil = _clock.UtcNow.AddMinutes(_settings.ManualHoldMinutes);
        SetStatus("Restoring your previous mode; automatic choices will be held temporarily.");
        return true;
    }

    public void RecoveryFinished()
    {
        _brightnessRecovery = _settings.PendingBrightness >= 0;
        _expected = _baseline = _display.GetCurrentHz(); _saver = false; _restore = 0;
        _externalHold = true; SetStatus("Recovery reviewed. Resume automation when ready.");
    }

    public void BrightnessRecoveryCompleted()
    {
        if (_brightnessRecovery) RecoveryFinished();
    }

    public void SetLocked(bool value) { if (value) CancelDeferredWrites?.Invoke(); _locked = value; _restoreSince = _mediaSince = null; Tick(); }
    public void SetSuspended(bool value) { if (value) CancelDeferredWrites?.Invoke(); _suspended = value; _restoreSince = _mediaSince = null; Tick(); }
    private void SetStatus(string status)
    {
        Status = status ?? "Refresh-rate operation failed.";
        if (_lastStatus == Status) return;
        _lastStatus = Status; Changed?.Invoke();
    }
    public void Dispose() => _timer?.Dispose();
}
