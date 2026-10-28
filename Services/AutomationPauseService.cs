namespace HzSwitcher.Services;

public enum AutomationPauseMode { Running, Timed, UntilRestart }
public enum AutomationReleaseResult { NoOwnership, Released, Superseded, Failed }
public enum AutomationReason
{
    Starting, Disabled, Paused, Locked, Busy, Idle, Watching, Game, Cooldown,
    Saver, Restoring, RestoreFailed, ForegroundUnknown, Qualifying, Profile, ManualOverride,
}

/// <summary>Session-only pause state. Call Tick from the existing automation timers.</summary>
public sealed class AutomationPauseService(TimeProvider time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _started;
    public AutomationPauseMode Mode { get; private set; }
    public TimeSpan? Duration { get; private set; }
    public bool RestorationPending { get; private set; }
    public bool IsPaused => Mode != AutomationPauseMode.Running;
    public event Action Changed;

    public TimeSpan? Remaining => Mode != AutomationPauseMode.Timed ? null
        : TimeSpan.FromTicks(Math.Max(0, (Duration.Value - _time.GetElapsedTime(_started)).Ticks));
    public DateTimeOffset? ResumesAt => Remaining is TimeSpan remaining ? _time.GetUtcNow() + remaining : null;
    public string Description => !IsPaused ? "Automation running"
        : RestorationPending ? "Automation paused: restore needs attention"
        : Mode == AutomationPauseMode.UntilRestart ? "Automation paused until restart"
        : $"Automation paused until {ResumesAt.Value.ToLocalTime():t}";

    public void Pause(TimeSpan? duration)
    {
        if (duration.HasValue && (duration.Value <= TimeSpan.Zero || duration.Value > TimeSpan.FromDays(1)))
            throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = duration;
        _started = _time.GetTimestamp();
        Mode = duration.HasValue ? AutomationPauseMode.Timed : AutomationPauseMode.UntilRestart;
        Changed?.Invoke();
    }

    public void SetRestorationPending(bool pending)
    {
        if (RestorationPending == pending) return;
        RestorationPending = pending;
        Changed?.Invoke();
    }

    public bool Resume()
    {
        if (RestorationPending) return false;
        if (!IsPaused) return true;
        Mode = AutomationPauseMode.Running;
        Duration = null;
        Changed?.Invoke();
        return true;
    }

    public void Tick()
    {
        if (Mode == AutomationPauseMode.Timed && Remaining == TimeSpan.Zero && !RestorationPending) Resume();
    }
}
