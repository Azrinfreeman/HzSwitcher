using HzSwitcher.Services;
using Xunit;

namespace HzSwitcher.Tests;

public sealed class AutomationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HzAutomation-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly FakeActivity _activity = new();
    private readonly FakeMedia _media = new();
    private readonly CountingDisplay _display = new();
    private readonly Foreground _foreground = new();
    private readonly Settings _settings = new() { PreviewMode = false, IdleMinutes = 1, MinSwitchSeconds = 0 };
    private string _target = "display-A|connection-1";
    private double _elapsed;
    private AutomationCoordinator _coordinator;

    private AutomationCoordinator App => _coordinator ??= new(_settings, _display, _media, () => _target,
        _dir, false, _activity, _foreground, _clock, () => _elapsed, () => true);
    private void Step(double seconds = 0) { _elapsed += seconds; _clock.Advance(seconds); App.Tick(); }
    private void Finish() => Step(3.1);

    [Fact] public void PreviewNeverCallsRateWriterOrStartsSaver()
    {
        _settings.PreviewMode = true; _settings.SaverBrightness = 20;
        _activity.Idle = 61000;
        int events = 0; App.SaverChanged += _ => events++;
        for (int i = 0; i < 100; i++) Step(1);
        Assert.Equal(0, _display.Writes); Assert.Equal(0, events); Assert.False(App.SaverActive);
        Assert.Contains("60 Hz suggested", App.Status);
    }

    [Fact] public void SaverRestoresAfterContinuousActivity()
    {
        _activity.Idle = 61000; Step(); Assert.Equal(60, _display.Hz); Assert.True(App.SaverActive);
        Finish(); _activity.Idle = 0; Step(); Step(5.1);
        Assert.Equal(240, _display.Hz); Assert.False(App.SaverActive);
    }

    [Fact] public void InterruptedActivityDoesNotRestoreEarly()
    {
        _activity.Idle = 61000; Step(); Finish(); _activity.Idle = 0; Step(); Step(3);
        _activity.Idle = 2000; Step(); _activity.Idle = 0; Step(); Step(3);
        Assert.Equal(60, _display.Hz);
    }

    [Fact] public void GameRestoresSaverImmediatelyAfterVerification()
    {
        _activity.Idle = 61000; Step(); Finish(); _media.Game = true; Step();
        Assert.Equal(240, _display.Hz); Assert.Contains("Game protection", App.Status);
    }

    [Fact] public void ManualChoiceHoldsAgainstSaverAndGame()
    {
        Step(); Assert.True(App.SetManualRate(120)); Finish();
        _activity.Idle = 61000; _media.Game = true; Step(30);
        Assert.Equal(120, _display.Hz); Assert.Contains("manual choice", App.Status);
    }

    [Fact] public void ManualChoiceOfExistingRateStillCreatesHold()
    {
        Step(); Assert.True(App.SetManualRate(240)); _activity.Idle = 61000; Step();
        Assert.Equal(0, _display.Writes); Assert.Contains("manual choice", App.Status);
    }

    [Fact] public void TimedPauseExpiresByWallClock()
    {
        Step(); App.Pause(15); _activity.Idle = 61000; Step(899);
        Assert.Equal(0, _display.Writes); Step(2); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void IndefinitePauseRequiresResume()
    {
        Step(); App.Pause(null); _activity.Idle = 61000; Step(5000);
        Assert.Equal(0, _display.Writes); App.Resume(); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void RecordingPausesWithoutChangingRate()
    {
        Step(); App.SetRecording(true); _activity.Idle = 61000; Step(1000);
        Assert.Equal(0, _display.Writes); App.SetRecording(false); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void ExternalChangeYieldsUntilExplicitResume()
    {
        Step(); _display.Hz = 120; _activity.Idle = 61000; Step(); Step(60);
        Assert.Equal(0, _display.Writes); Assert.Contains("External rate change", App.Status);
        App.Resume(); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void PreviewEnteredDuringSaverNeverRestoresAutomatically()
    {
        _activity.Idle = 61000; Step(); Finish(); App.SetPreview(true);
        _activity.Idle = 0; Step(); Step(10);
        Assert.Equal(1, _display.Writes); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void ProfileWinsUnlessSaverIsExplicitlyAllowed()
    {
        _settings.Profiles.Add(new() { Process = "editor", Hz = 120 });
        _foreground.Process = "editor"; Step(); Step(1.6); Finish();
        _activity.Idle = 61000; Step(); Assert.Equal(120, _display.Hz);
        _settings.Profiles[0].AllowSaver = true; Step(); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void BriefFocusNeverAppliesProfile()
    {
        _settings.Profiles.Add(new() { Process = "editor", Hz = 120 });
        _foreground.Process = "editor"; Step(); Step(1);
        _foreground.Process = "other"; Step(1);
        Assert.Equal(0, _display.Writes);
    }

    [Fact] public void ProfileLeaveUsesConfiguredDelay()
    {
        _settings.Profiles.Add(new() { Process = "editor", Hz = 120 });
        _foreground.Process = "editor"; Step(); Step(1.6); Finish();
        _foreground.Process = "other"; Step(); Step(4); Assert.Equal(120, _display.Hz);
        Step(1.1); Assert.Equal(240, _display.Hz);
    }

    [Fact] public void LockedAndSuspendedNeverWrite()
    {
        Step(); App.SetLocked(true); _activity.Idle = 61000; Step(60); Assert.Equal(0, _display.Writes);
        App.SetSuspended(true); App.SetLocked(false); Step(60); Assert.Equal(0, _display.Writes);
    }

    [Fact] public void MissingTargetNeverRedirects()
    {
        Step(); _target = ""; _activity.Idle = 61000; Step();
        Assert.Equal(0, _display.Writes); Assert.Contains("unavailable", App.Status);
        Assert.False(App.SetManualRate(120));
    }

    [Fact] public void TargetChangedDuringVerificationRetainsJournal()
    {
        _activity.Idle = 61000; Step(); _target = "display-B|connection-2"; Finish();
        Assert.True(App.Transitions.NeedsRecovery); Assert.Equal(1, _display.Writes);
        Assert.False(App.Transitions.Recover());
    }

    [Fact] public void ManualTransitionCannotOverwriteInFlightRecovery()
    {
        _activity.Idle = 61000; Step(); Assert.False(App.SetManualRate(120));
        Assert.Equal(1, _display.Writes); Assert.Equal(60, App.Transitions.Pending.RequestedHz);
    }

    [Fact] public void UnavailableRecoveryCanBeExplicitlyArchivedWithoutRedirectingWrites()
    {
        _activity.Idle = 61000; Step(); _target = "display-B"; Finish();
        Assert.True(App.Transitions.ArchiveUnavailable()); Assert.Equal(1, _display.Writes);
        Assert.Null(App.Transitions.Pending);
        Assert.Single(Directory.GetFiles(_dir, "pending-transition.json.unavailable-*"));
        Assert.Contains(App.History.Records, r => r.Outcome == "Original monitor was not restored");
    }

    [Fact] public void WriteFailureIsRecordedAndRateRemainsUnchanged()
    {
        _display.Reject = true; _activity.Idle = 61000; Step();
        Assert.Equal(240, _display.Hz); Assert.False(App.SaverActive);
        Assert.Single(App.History.Records); Assert.Equal("Application failed", App.History.Records[0].Outcome);
    }

    [Fact] public void JournalFailurePreventsHardwareWrite()
    {
        Directory.CreateDirectory(_dir); Directory.CreateDirectory(Path.Combine(_dir, "pending-transition.json"));
        _activity.Idle = 61000; Step(); Assert.Equal(0, _display.Writes);
        Assert.Contains("could not be saved", App.Status);
    }

    [Fact] public void InterruptedTransitionRequiresExplicitMatchingRecovery()
    {
        _activity.Idle = 61000; Step(); App.Dispose(); _coordinator = null;
        Step(); Assert.True(App.Transitions.NeedsRecovery); Assert.Equal(1, _display.Writes);
        Assert.True(App.Transitions.Recover()); Assert.Equal(240, _display.Hz);
    }

    [Fact] public void FailedRecoveryRetainsJournal()
    {
        _activity.Idle = 61000; Step(); App.Dispose(); _coordinator = null;
        Step(); _display.Reject = true;
        Assert.False(App.Transitions.Recover()); Assert.NotNull(App.Transitions.Pending);
        Assert.True(File.Exists(Path.Combine(_dir, "pending-transition.json")));
    }

    [Fact] public void InvalidJournalBlocksWritesRatherThanDisappearing()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(Path.Combine(_dir, "pending-transition.json"), "broken");
        _activity.Idle = 61000; Step(); Assert.True(App.Transitions.NeedsRecovery); Assert.Equal(0, _display.Writes);
    }

    [Fact] public void FeedbackBlocksOnlyRecordedDirectionAndTarget()
    {
        Step(); var record = App.History.Add(_target, 240, 60, 60, "Saver", "Verified by readback");
        Assert.True(App.History.Feedback(record.Id, "Visible flicker", true));
        _activity.Idle = 61000; Step(); Assert.Equal(0, _display.Writes);
        Assert.True(App.SetManualRate(60)); // manual retry remains explicit
    }

    [Fact] public void LegacyPendingRecoveryBlocksAutomaticAndManualChanges()
    {
        _settings.PendingChange = true; _activity.Idle = 61000; Step();
        Assert.Equal(0, _display.Writes); Assert.False(App.SetManualRate(120));
    }

    [Fact] public void CooldownUsesMonotonicTimeNotClockChanges()
    {
        _settings.MinSwitchSeconds = 30; Step(); Assert.True(App.SetManualRate(240));
        _clock.Advance(16 * 60); _activity.Idle = 61000; Step(1);
        Assert.Equal(0, _display.Writes); Step(30); Assert.Equal(60, _display.Hz);
    }

    [Fact] public void PreviewAfterRestartDoesNotRecoverInterruptedModeAutomatically()
    {
        _activity.Idle = 61000; Step(); App.Dispose(); _coordinator = null; _settings.PreviewMode = true;
        Step(10); Assert.Equal(1, _display.Writes); Assert.True(App.Transitions.NeedsRecovery);
    }

    [Fact] public void CorruptJournalCanBeArchivedByExplicitKeepCurrent()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(Path.Combine(_dir, "pending-transition.json"), "broken");
        Step(); Assert.True(App.Transitions.KeepCurrent());
        Assert.False(App.Transitions.NeedsRecovery);
        Assert.Single(Directory.GetFiles(_dir, "pending-transition.json.unreadable-*"));
        Assert.Equal(0, _display.Writes);
    }

    [Fact] public void PausePreviewRecordingAndLockCancelDeferredAutomaticWork()
    {
        int cancellations = 0; App.CancelDeferredWrites += () => cancellations++;
        App.Pause(null); App.SetPreview(true); App.SetRecording(true); App.SetLocked(true); App.SetSuspended(true);
        Assert.Equal(5, cancellations); Assert.False(App.CanWriteAutomation); Assert.Equal(0, _display.Writes);
    }

    [Fact] public void SettingsFailureCannotActivateAutomation()
    {
        _settings.PreviewMode = true; _activity.Idle = 61000;
        using var app = new AutomationCoordinator(_settings, _display, _media, () => _target, _dir,
            false, _activity, _foreground, _clock, () => _elapsed, () => false);
        app.SetPreview(false); app.Resume(); app.Tick();
        Assert.True(_settings.PreviewMode); Assert.True(_settings.AutomationPaused);
        Assert.Equal(0, _display.Writes);
    }

    [Fact] public void StartupBrightnessRecoveryCannotBeBypassedByResume()
    {
        _settings.PendingBrightness = 70; _settings.PendingBrightnessTarget = _target;
        _activity.Idle = 61000; Step(); App.Resume();
        Assert.Equal(0, _display.Writes); Assert.False(App.SetManualRate(120));
    }

    private sealed class Foreground : IForeground { public string Process = ""; public string ProcessName() => Process; }
    private sealed class CountingDisplay : IDisplay
    {
        public int Hz = 240, Writes; public bool Reject;
        public int GetCurrentHz() => Hz;
        public bool TryApply(int hz) { Writes++; if (Reject) return false; Hz = hz; return true; }
        public List<int> GetSupportedHz() => new() { 60, 120, 240 };
        public int Resolve(int hz) => ModeMath.Resolve(GetSupportedHz(), hz);
    }

    public void Dispose() { _coordinator?.Dispose(); if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
}

public sealed class AutomationStorageTests
{
    [Fact] public void NewerSettingsRemainByteForByteUnchangedAfterAttemptedSave()
    {
        string dir = Path.Combine(Path.GetTempPath(), "HzSchema-" + Guid.NewGuid().ToString("N"));
        string old = Log.Dir;
        try
        {
            Directory.CreateDirectory(dir); Log.Dir = dir;
            const string original = """{"SettingsVersion":999,"FuturePreference":"keep me"}""";
            File.WriteAllText(SettingsStore.Path_, original);
            var settings = SettingsStore.Load();
            Assert.True(settings.AutomationPaused); Assert.False(SettingsStore.TrySave(new Settings()));
            Assert.Equal(original, File.ReadAllText(SettingsStore.Path_));
        }
        finally
        {
            Log.Dir = Path.Combine(dir, "reset"); SettingsStore.Load();
            Log.Dir = old; if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact] public void V2MigrationPreservesSaverPriorityAndEnablesExistingAutomation()
    {
        var settings = SettingsStore.Parse("""{"SettingsVersion":2,"Profiles":[{"Process":"vlc","Hz":120}],"AlwaysOnTopEnabled":true}""");
        Assert.Equal(3, settings.SettingsVersion); Assert.False(settings.PreviewMode);
        Assert.True(settings.Profiles[0].AllowSaver); Assert.True(settings.AlwaysOnTopEnabled);
    }
    [Fact] public void NewSettingsStartInPreviewAndNewProfilesHoldPriority()
    { Assert.True(new Settings().PreviewMode); Assert.False(new AppProfile().AllowSaver); }
    [Fact] public void UnknownSchemaIsRejected()
    { Assert.Throws<NotSupportedException>(() => SettingsStore.Parse("""{"SettingsVersion":999}""")); }
    [Fact] public void NullCollectionsAndInvalidRangesAreNormalized()
    {
        var s = SettingsStore.Parse("""{"SettingsVersion":3,"Profiles":null,"Hotkeys":null,"IdleMinutes":0,"RestoreSeconds":-2,"ManualHoldMinutes":99999}""");
        Assert.Empty(s.Profiles); Assert.Empty(s.Hotkeys); Assert.Equal(1, s.IdleMinutes);
        Assert.Equal(1, s.RestoreSeconds); Assert.Equal(1440, s.ManualHoldMinutes);
    }
    [Fact] public void HistoryRetentionRedactionAndFeedbackSurviveReload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "HzHistory-" + Guid.NewGuid().ToString("N"));
        var clock = new FakeClock();
        try
        {
            var store = new TransitionHistoryStore(Path.Combine(dir, "history.json"), clock);
            var record = store.Add("private-device-path", 240, 60, 60, "sensitive-app", "Verified by readback");
            store.Feedback(record.Id, "Black screen", true);
            string json = store.ExportRedacted(); Assert.DoesNotContain("private-device-path", json); Assert.DoesNotContain("sensitive-app", json);
            var reloaded = new TransitionHistoryStore(Path.Combine(dir, "history.json"), clock);
            Assert.True(reloaded.IsBlocked("private-device-path", 240, 60)); Assert.False(reloaded.IsBlocked("other", 240, 60));
            Assert.False(reloaded.IsBlocked("private-device-path", 60, 240));
            var retry = reloaded.Add("private-device-path", 240, 60, 60, "Manual retry", "Verified by readback");
            reloaded.Feedback(retry.Id, "Looked fine", null);
            Assert.True(reloaded.IsBlocked("private-device-path", 240, 60));
            reloaded.Feedback(retry.Id, "Retry explicitly allowed", false);
            Assert.False(reloaded.IsBlocked("private-device-path", 240, 60));
            clock.Advance(31 * 86400); Assert.Empty(reloaded.Records);
            reloaded.Clear(); Assert.Empty(new TransitionHistoryStore(Path.Combine(dir, "history.json"), clock).Records);
            Assert.False(File.Exists(Path.Combine(dir, "history.json")));
            Assert.False(File.Exists(Path.Combine(dir, "history.json.bak")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
