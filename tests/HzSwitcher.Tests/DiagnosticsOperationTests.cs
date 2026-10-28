using HzSwitcher.Services;
using HzSwitcher.UI;
using HzSwitcher.UI.Controls;
using Xunit;

namespace HzSwitcher.Tests;

public sealed class BrightnessDiagnosticsTests : IDisposable
{
    private readonly ReliabilityLogScope _log = new();

    [Fact]
    public void SnapshotNeverProbesOrWrites()
    {
        int reads = 0, writes = 0;
        using var brightness = new BrightnessService(() => "a", _ => -1,
            _ => { reads++; return new(BrightnessBackend.Ddc, 70); }, (_, _) => { writes++; return true; });
        Assert.Equal(BrightnessBackend.NotChecked, brightness.GetSnapshot().Backend);
        Assert.Null(brightness.GetSnapshot().LastKnownPercent);
        Assert.Equal(0, reads);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(BrightnessBackend.Ddc, "DDC/CI", true)]
    [InlineData(BrightnessBackend.Wmi, "WMI", true)]
    [InlineData(BrightnessBackend.Software, "Software dimming", false)]
    [InlineData(BrightnessBackend.Unsupported, "Unsupported", false)]
    public async Task RefreshReportsActualBackendWithoutAnyWrites(BrightnessBackend backend, string label, bool hardware)
    {
        int writes = 0;
        using var brightness = new BrightnessService(() => "a", _ => -1, _ => new(backend, 70),
            (_, _) => { writes++; return true; });
        await brightness.RefreshAsync();
        var snapshot = brightness.GetSnapshot();
        Assert.Equal(backend, snapshot.Backend);
        Assert.Equal(label, DiagnosticsSnapshot.BackendName(snapshot.Backend));
        Assert.Equal(hardware ? 70 : (int?)null, snapshot.LastReadPercent);
        Assert.Equal(hardware, snapshot.ReadAt.HasValue);
        Assert.Null(snapshot.RequestedPercent);
        Assert.Equal(backend == BrightnessBackend.Unsupported ? null : (int?)70, snapshot.LastKnownPercent);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task RepeatedRefreshSharesOneProbeAndDiscardsOldMonitorCompletion()
    {
        string device = "a";
        int probes = 0, writes = 0;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var brightness = new BrightnessService(() => device, _ => -1, d =>
        {
            Interlocked.Increment(ref probes);
            if (d == "a") { started.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            return new(BrightnessBackend.Ddc, d == "a" ? 20 : 80);
        }, (_, _) => { writes++; return true; });
        var old = brightness.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(old, brightness.RefreshAsync());
            Assert.True(brightness.GetSnapshot().Refreshing);
            device = "b";
            brightness.Invalidate();
            var current = brightness.RefreshAsync();
            release.Set();
            await Task.WhenAll(old, current).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(80, brightness.GetSnapshot().LastReadPercent);
            Assert.Equal(2, probes);
            Assert.Equal(0, writes);
        }
        finally { release.Set(); await old; }
    }

    [Fact]
    public async Task AReadStartedBeforeAWriteCannotOverwriteTheRequestedLevel()
    {
        int probes = 0;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var brightness = new BrightnessService(() => "a", _ => -1, _ =>
        {
            if (Interlocked.Increment(ref probes) > 1)
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return new(BrightnessBackend.Ddc, 90);
            }
            return new(BrightnessBackend.Ddc, 70);
        }, (_, _) => true);
        await brightness.RefreshAsync();
        var refresh = brightness.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            brightness.RequestSet(20);
            brightness.Flush();
            release.Set();
            await refresh;
            var snapshot = brightness.GetSnapshot();
            Assert.Equal(20, snapshot.LastKnownPercent);
            Assert.Equal(20, snapshot.RequestedPercent);
            Assert.Equal(70, snapshot.LastReadPercent);
            Assert.False(snapshot.Writing);
        }
        finally { release.Set(); await refresh; }
    }

    [Fact]
    public async Task NativeWriteRejectionIsRecordedForItsOriginalMonitorAfterSelectionChanges()
    {
        string device = "a";
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<OperationFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var brightness = new BrightnessService(() => device, _ => -1, _ => new(BrightnessBackend.Wmi, 70), (_, _) =>
        {
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return false;
        });
        brightness.Failed += failure => failed.SetResult(failure);
        await brightness.RefreshAsync();
        brightness.RequestSet(40);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(brightness.GetSnapshot().Writing);
            device = "b";
            release.Set();
            var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("a", failure.Device);
            Assert.Equal(FailureKind.WriteFailed, failure.Kind);
            Assert.Equal(40, failure.Target);
            Assert.Null(failure.Actual);
            Assert.Equal(BrightnessBackend.NotChecked, brightness.GetSnapshot().Backend);
        }
        finally { release.Set(); brightness.Flush(); }
    }

    public void Dispose() => _log.Dispose();
}

public sealed class SafetyDiagnosticsTests : IDisposable
{
    private readonly ReliabilityLogScope _log = new();
    private sealed class Display : IDisplay
    {
        public int Hz = 240;
        public Func<int, bool> Apply;
        public readonly List<int> Writes = [];
        public int GetCurrentHz() => Hz;
        public bool TryApply(int hz) { Writes.Add(hz); if (Apply != null) return Apply(hz); Hz = hz; return true; }
        public int Resolve(int hz) => ModeMath.Resolve(GetSupportedHz(), hz);
        public List<int> GetSupportedHz() => [60, 120, 240];
    }

    [Fact]
    public void DelayedRateFailureIsRecordedAndSafetyFallbackStillRunsWhilePaused()
    {
        var display = new Display();
        using var safety = new SafetyNetService(display, new Settings(), () => "a");
        var diagnostics = new DiagnosticsService();
        safety.Failed += diagnostics.Record;
        Assert.True(safety.Apply(120));
        var pause = new AutomationPauseService();
        pause.Pause(null);
        display.Hz = 60;
        safety.Verify();
        Assert.True(pause.IsPaused);
        Assert.Equal(new[] { 120, 240 }, display.Writes);
        Assert.Equal(240, display.Hz);
        Assert.Equal(FailureKind.DidNotHold, diagnostics.FailureFor("a").Kind);
        Assert.Equal(60, diagnostics.FailureFor("a").Actual);
        Assert.Equal(120, diagnostics.FailureFor("a").Target);
    }

    [Fact]
    public void StaleVerificationDoesNotApplyFallbackToAnotherMonitor()
    {
        string device = "a";
        var display = new Display();
        using var safety = new SafetyNetService(display, new Settings(), () => device);
        var diagnostics = new DiagnosticsService();
        safety.Failed += diagnostics.Record;
        safety.Apply(120);
        device = "b"; display.Hz = 60;
        safety.Verify();
        Assert.Equal(new[] { 120 }, display.Writes);
        Assert.Null(diagnostics.FailureFor("a"));
        Assert.Null(diagnostics.FailureFor("b"));
    }

    [Fact]
    public void RejectionAndFailedFallbackAreAuthoritativeFailures()
    {
        var display = new Display { Apply = _ => false };
        using var safety = new SafetyNetService(display, new Settings(), () => "a");
        var failures = new List<OperationFailure>();
        safety.Failed += failures.Add;
        Assert.False(safety.Apply(120));
        Assert.Equal(FailureKind.Rejected, Assert.Single(failures).Kind);
        Assert.Equal(new[] { 120 }, display.Writes); // Unchanged rate needs no fallback write.
        display.Apply = null;
        Assert.True(safety.Apply(120));
        display.Hz = 60; display.Apply = _ => false;
        safety.Verify();
        Assert.Equal(FailureKind.DidNotHold, failures[1].Kind);
        Assert.Equal(FailureKind.FallbackFailed, failures[2].Kind);
        Assert.Equal(240, failures[2].Target);
    }

    [Fact]
    public void FailureAfterADeviceChangeRetainsItsOriginalDeviceAndDoesNotClaimANewReading()
    {
        string device = "a";
        var display = new Display();
        display.Apply = _ => { device = "b"; display.Hz = 60; return false; };
        using var safety = new SafetyNetService(display, new Settings(), () => device);
        OperationFailure failure = null;
        safety.Failed += result => failure = result;
        Assert.False(safety.Apply(120));
        Assert.Equal("a", failure.Device);
        Assert.Null(failure.Actual);
        Assert.Equal(new[] { 120 }, display.Writes);
    }

    public void Dispose() => _log.Dispose();
}

public sealed class DiagnosticsDialogTests
{
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Fact]
    public void OpeningTheReadOnlyDialogDoesNotStartRefreshAndShowsExactlyTheCopyableReport()
    {
        int captures = 0, refreshes = 0;
        var snapshot = DiagnosticsTests.Snapshot();
        using var form = new DiagnosticsForm(() => { captures++; return snapshot; }, () => { refreshes++; return Task.CompletedTask; });
        Assert.Equal(1, captures);
        Assert.Equal(0, refreshes);
        var report = Assert.Single(Descendants(form).OfType<TextBox>());
        Assert.True(report.ReadOnly);
        Assert.Equal(snapshot.ToReport(), report.Text);
        Assert.Equal("Diagnostics report to copy", report.AccessibleName);
        Assert.DoesNotContain(snapshot.LocalMonitor, report.Text);
        var close = Assert.IsType<PillButton>(form.CancelButton);
        Assert.Equal("Close", close.Text);
        Assert.Same(close, form.AcceptButton);
        Assert.Equal(DialogResult.Cancel, close.DialogResult);
        Assert.Equal(2, Descendants(form).OfType<PillButton>().Count(b => b.Text is "Refresh" or "Copy diagnostics"));
    }
}
