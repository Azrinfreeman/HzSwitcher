using HzSwitcher.Services;
using Xunit;

namespace HzSwitcher.Tests;

internal sealed class PauseClock : TimeProvider, IClock
{
    private long _timestamp;
    private DateTimeOffset _utc = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => _utc;
    public DateTime UtcNow => _utc.UtcDateTime;
    public void Advance(double seconds) { _timestamp += TimeSpan.FromSeconds(seconds).Ticks; _utc = _utc.AddSeconds(seconds); }
    public void AdjustWallClock(double seconds) => _utc = _utc.AddSeconds(seconds);
}

internal sealed class ReliabilityLogScope : IDisposable
{
    private readonly string _previous = Log.Dir;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HzSwitcherReliability-" + Guid.NewGuid().ToString("N"));
    public ReliabilityLogScope() => Log.Dir = _directory;
    public void Dispose()
    {
        Log.Dir = _previous;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

public sealed class AutomationPauseTests
{
    [Fact]
    public void TimedPauseUsesElapsedTimeAndReplacingItStartsANewDeadline()
    {
        var clock = new PauseClock();
        var pause = new AutomationPauseService(clock);
        pause.Pause(TimeSpan.FromMinutes(15));
        clock.Advance(60);
        clock.AdjustWallClock(-3600);
        Assert.Equal(TimeSpan.FromMinutes(14), pause.Remaining);
        pause.Pause(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromHours(1), pause.Remaining);
        clock.Advance(3600); // Includes an interval with no ticks, as when asleep.
        pause.Tick();
        Assert.False(pause.IsPaused);
        Assert.Null(pause.Duration);
        Assert.Null(pause.ResumesAt);
    }

    [Fact]
    public void PendingRestorationHoldsExpiryAndResumeUntilAnExplicitRetrySucceeds()
    {
        var clock = new PauseClock();
        var pause = new AutomationPauseService(clock);
        pause.Pause(TimeSpan.FromMinutes(15));
        pause.SetRestorationPending(true);
        clock.Advance(1000);
        pause.Tick();
        Assert.True(pause.IsPaused);
        Assert.False(pause.Resume());
        pause.SetRestorationPending(false);
        pause.Tick();
        Assert.False(pause.IsPaused);
    }

    [Fact]
    public void UntilRestartIsSessionOnlyAndRepeatedResumeIsIdempotent()
    {
        var clock = new PauseClock();
        var pause = new AutomationPauseService(clock);
        pause.Pause(null);
        clock.Advance(86400);
        pause.Tick();
        Assert.True(pause.IsPaused);
        Assert.Null(pause.ResumesAt);
        Assert.False(new AutomationPauseService(clock).IsPaused);
        int changes = 0;
        pause.Changed += () => changes++;
        Assert.True(pause.Resume());
        Assert.True(pause.Resume());
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public void InvalidDurationDoesNotChangeState(int seconds)
    {
        var pause = new AutomationPauseService();
        Assert.Throws<ArgumentOutOfRangeException>(() => pause.Pause(TimeSpan.FromSeconds(seconds)));
        Assert.False(pause.IsPaused);
    }
}

public sealed class PauseIntegrationTests : IDisposable
{
    private readonly ReliabilityLogScope _log = new();
    private readonly Settings _settings = new()
    {
        AutoEnabled = true, ProfilesEnabled = true, IdleMinutes = 1, SaverHz = 60,
        MinSwitchSeconds = 0, ProfileLeaveSeconds = 1,
        Profiles = [new AppProfile { Process = "browser", Hz = 120, Enabled = true }],
    };
    private readonly FakeDisplay _display = new();
    private readonly FakeActivity _activity = new();
    private readonly FakeMedia _media = new();
    private readonly PauseClock _clock = new();
    private readonly Foreground _foreground = new();
    private readonly AutomationPauseService _pause;
    private readonly AutoSaverService _saver;
    private readonly ProfileService _profiles;
    private readonly List<int> _writes = [];
    private readonly HashSet<int> _reject = [];
    private string _device = "monitor-a";
    private int _saverEnded;

    private sealed class Foreground : IForeground
    {
        public string Process = "browser";
        public string ProcessName() => Process;
    }

    public PauseIntegrationTests()
    {
        _pause = new AutomationPauseService(_clock);
        bool Apply(int hz)
        {
            _writes.Add(hz);
            if (_reject.Contains(hz)) return false;
            return _display.TryApply(hz);
        }
        _saver = new(() => _settings, _display, _media, activity: _activity, clock: _clock,
            apply: Apply, pause: _pause, device: () => _device);
        _saver.SaverEnded += () => _saverEnded++;
        _profiles = new(() => _settings, _display, _saver, Apply, startTimer: false,
            foreground: _foreground, clock: _clock, pause: _pause, device: () => _device);
    }

    private void EnterProfile()
    {
        _profiles.Tick();
        _clock.Advance(2);
        _profiles.Tick();
        Assert.Equal(120, _display.Hz);
    }

    private void EnterSaver()
    {
        _activity.Idle = 61000;
        _saver.Tick();
        Assert.Equal(SaverState.Saver, _saver.State);
    }

    private void Pause(TimeSpan? duration = null)
    {
        _pause.Pause(duration);
        TrayApp.RestoreAutomationForPause(_pause, _saver, _profiles);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PauseReleasesSaverThenProfileOnceWithoutChangingPreferences(bool profile, bool saver)
    {
        if (profile) EnterProfile();
        if (saver) EnterSaver();
        _writes.Clear();
        Pause();
        Assert.Equal(profile && saver ? new[] { 120, 240 } : profile || saver ? new[] { 240 } : Array.Empty<int>(), _writes);
        Assert.Equal(240, _display.Hz);
        Assert.Equal(saver ? 1 : 0, _saverEnded);
        Assert.Null(_profiles.ActiveProcess);
        Assert.False(_pause.RestorationPending);
        Assert.True(_settings.AutoEnabled);
        Assert.True(_settings.ProfilesEnabled);
        _writes.Clear();
        Pause();
        _clock.Advance(100);
        _saver.Tick(); _profiles.Tick();
        Assert.Empty(_writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedReleaseKeepsOwnershipAndStopsTheChainUntilRetry(bool failSaver)
    {
        EnterProfile(); EnterSaver();
        _writes.Clear();
        _reject.Add(failSaver ? 120 : 240);
        Pause(TimeSpan.FromMinutes(15));
        Assert.Equal(failSaver ? new[] { 120 } : new[] { 120, 240 }, _writes);
        Assert.True(_pause.RestorationPending);
        Assert.Equal("browser", _profiles.ActiveProcess);
        _writes.Clear();
        _clock.Advance(1000);
        _saver.Tick(); _profiles.Tick();
        Assert.Empty(_writes);
        Assert.False(_pause.Resume());
        _reject.Clear();
        TrayApp.RestoreAutomationForPause(_pause, _saver, _profiles);
        Assert.False(_pause.RestorationPending);
        Assert.Equal(240, _display.Hz);
        Assert.True(_pause.Resume());
    }

    [Fact]
    public void ExplicitSameRateChoiceAndExternalChangesSupersedeRestoration()
    {
        EnterProfile();
        _profiles.OnManualChange(); // Even choosing 120 Hz again expresses manual intent.
        _saver.OnManualChange();
        _writes.Clear();
        Pause();
        Assert.Empty(_writes);
        Assert.Equal(120, _display.Hz);
        _pause.Resume();
        _clock.Advance(61);
        EnterSaver();
        _display.Hz = 100; // External change while saver owns the original 60 Hz.
        _writes.Clear();
        Pause();
        Assert.Empty(_writes);
        Assert.Equal(100, _display.Hz);
        Assert.Equal(1, _saverEnded);
    }

    [Fact]
    public void MonitorChangeNeverReceivesAnOldRestoreEvenAtTheSameRate()
    {
        EnterProfile(); EnterSaver();
        _device = "monitor-b";
        _writes.Clear();
        Pause();
        Assert.Empty(_writes);
        Assert.Equal(60, _display.Hz);
        Assert.Equal(1, _saverEnded);
        Assert.Null(_profiles.ActiveProcess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalChoiceOfThePreviousProfileRateAlsoSupersedesProfileOwnership(bool detectBeforePause)
    {
        EnterProfile(); EnterSaver();
        _display.Hz = 120; // Matches the old profile rate but is now an external choice.
        _writes.Clear();
        if (detectBeforePause) { _clock.Advance(2); _saver.Tick(); }
        Pause();
        Assert.Empty(_writes);
        Assert.Equal(120, _display.Hz);
        Assert.Null(_profiles.ActiveProcess);
    }

    [Fact]
    public void TimerAlsoDropsOwnershipWhenTheSelectedMonitorChanges()
    {
        EnterProfile(); EnterSaver();
        _device = "monitor-b";
        _writes.Clear();
        _saver.Tick(); _profiles.Tick();
        Assert.Equal(SaverState.Active, _saver.State);
        Assert.Equal(1, _saverEnded);
        Assert.Null(_profiles.ActiveProcess);
        Assert.Empty(_writes);
    }

    [Fact]
    public void ExpiryStartsIdleQualificationAgainInsteadOfConsumingPausedIdle()
    {
        Pause(TimeSpan.FromMinutes(15));
        _activity.Idle = 2_000_000;
        _clock.Advance(901);
        _saver.Tick();
        Assert.False(_pause.IsPaused);
        Assert.Equal(SaverState.Active, _saver.State);
        _clock.Advance(59);
        _saver.Tick();
        Assert.Empty(_writes);
        _clock.Advance(1);
        _saver.Tick();
        Assert.Equal(new[] { 60 }, _writes);
    }

    [Fact]
    public void ResumeDiscardsForegroundAndVideoCandidates()
    {
        _profiles.Tick(); // Candidate starts before pausing.
        _media.Watching = true;
        _activity.Idle = 10000;
        _saver.Tick();
        Pause();
        _clock.Advance(100);
        _pause.Resume();
        _saver.Tick(); _profiles.Tick();
        Assert.Empty(_writes);
        _clock.Advance(1);
        _saver.Tick(); _profiles.Tick();
        Assert.Empty(_writes);
        _clock.Advance(1);
        _profiles.Tick();
        Assert.Equal(new[] { 120 }, _writes);
        Assert.Equal(SaverState.Active, _saver.State);
    }

    [Fact]
    public void ManualChoicesAndDisabledFlagsDuringPauseRemainInEffectOnResume()
    {
        Pause();
        _settings.AutoEnabled = false;
        _settings.ProfilesEnabled = false;
        _display.TryApply(100);
        _saver.OnManualChange(); _profiles.OnManualChange();
        _pause.Resume();
        _clock.Advance(120);
        _activity.Idle = 120000;
        _saver.Tick(); _profiles.Tick();
        Assert.Empty(_writes);
        Assert.Equal(100, _display.Hz);
        Assert.Equal(AutomationReason.Disabled, _saver.Reason);
        Assert.Equal(AutomationReason.Disabled, _profiles.Reason);
    }

    [Fact]
    public void ExpiryWhileLockedDoesNotResumeAutomaticChoices()
    {
        Pause(TimeSpan.FromMinutes(15));
        _saver.SetLocked(true);
        _clock.Advance(1000);
        _saver.Tick(); _profiles.Tick();
        Assert.False(_pause.IsPaused);
        Assert.True(_saver.Suspended);
        Assert.Empty(_writes);
        Assert.Equal(AutomationReason.Locked, _profiles.Reason);
        _saver.SetLocked(false);
        _profiles.Tick();
        Assert.Empty(_writes);
        _clock.Advance(2);
        _profiles.Tick();
        Assert.Equal(new[] { 120 }, _writes);
    }

    [Fact]
    public void ResumeKeepsTheSharedCooldownAndGameExclusion()
    {
        _settings.MinSwitchSeconds = 30;
        EnterProfile();
        Pause();
        _pause.Resume();
        _profiles.Tick();
        _clock.Advance(2);
        _writes.Clear();
        _profiles.Tick();
        Assert.Empty(_writes);
        Assert.Equal(AutomationReason.Cooldown, _profiles.Reason);
        _media.Game = true;
        _activity.Idle = 100000;
        _clock.Advance(61);
        _saver.Tick();
        Assert.Empty(_writes);
        Assert.Equal(AutomationReason.Game, _saver.Reason);
    }

    public void Dispose() { _profiles.Dispose(); _saver.Dispose(); _log.Dispose(); }
}

public sealed class DiagnosticsTests
{
    internal static DiagnosticsSnapshot Snapshot() => new(
        DateTimeOffset.UnixEpoch, "1.7.3", "Windows 10.0.26100", "X64", @"monitor serial C:\Users\someone", "private-app.exe",
        "1920x1080", 120, new[] { 60, 120, 240 }, 144, 240,
        new(BrightnessBackend.Ddc, 40, 40, 70, DateTimeOffset.UnixEpoch, false, false),
        true, true, AutomationPauseMode.UntilRestart, null, false, SaverState.Active,
        AutomationReason.Paused, AutomationReason.Paused, 0,
        new(@"device C:\Users\someone", OperationKind.Brightness, FailureKind.WriteFailed, 40, null, DateTimeOffset.UnixEpoch));

    [Fact]
    public void CopiedReportIsDeterministicAndExcludesLocalIdentifiers()
    {
        var snapshot = Snapshot();
        string report = snapshot.ToReport();
        Assert.Equal(report, snapshot.ToReport());
        Assert.DoesNotContain(snapshot.LocalMonitor, report);
        Assert.DoesNotContain(snapshot.LocalProfile, report);
        Assert.DoesNotContain(snapshot.Failure.Device, report);
        Assert.DoesNotContain("someone", report);
        Assert.Contains("DDC/CI", report);
        Assert.Contains("Last requested level: 40%", report);
        Assert.Contains("Last hardware read: 70%", report);
        Assert.Contains("WriteFailed", report);
        Assert.Contains("Not confirmed", report);
    }

    [Fact]
    public void FailureHistoryIsBoundedDeviceScopedAndIgnoresLateOlderResults()
    {
        var diagnostics = new DiagnosticsService();
        var failure = Snapshot().Failure;
        diagnostics.Record(failure with { Device = "a", OccurredAt = DateTimeOffset.UnixEpoch.AddMinutes(1) });
        diagnostics.Record(failure with { Device = "a" });
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), diagnostics.FailureFor("a").OccurredAt);
        Assert.Null(diagnostics.FailureFor("b"));
        for (int i = 0; i < 8; i++) diagnostics.Record(failure with { Device = "b" + i, OccurredAt = DateTimeOffset.UnixEpoch.AddMinutes(i + 2) });
        Assert.Null(diagnostics.FailureFor("a"));
        Assert.NotNull(diagnostics.FailureFor("b7"));
    }

    [Fact]
    public void UnavailableValuesAndNoFailureAreExplicit()
    {
        string report = (Snapshot() with
        {
            Brightness = new(BrightnessBackend.NotChecked, null, null, null, null, false, false), Failure = null,
        }).ToReport();
        Assert.Contains("Not checked", report);
        Assert.Contains("Last known level: Not available", report);
        Assert.Contains("No failure recorded this session.", report);
    }
}
