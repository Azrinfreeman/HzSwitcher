using System.Text;

namespace HzSwitcher.Services;

public enum OperationKind { RefreshRate, Brightness }
public enum FailureKind { Rejected, DidNotHold, FallbackFailed, WriteFailed, RestoreFailed }
public sealed record OperationFailure(string Device, OperationKind Operation, FailureKind Kind,
    int Target, int? Actual, DateTimeOffset OccurredAt);
public enum BrightnessBackend { NotChecked, Ddc, Wmi, Software, Unsupported }
public sealed record BrightnessSnapshot(BrightnessBackend Backend, int? LastKnownPercent,
    int? RequestedPercent, int? LastReadPercent, DateTimeOffset? ReadAt, bool Refreshing, bool Writing);

public sealed record DiagnosticsSnapshot(
    DateTimeOffset CapturedAt, string AppVersion, string OsVersion, string Architecture,
    string LocalMonitor, string LocalProfile, string Resolution, int CurrentHz, IReadOnlyList<int> SupportedHz,
    int ConfiguredFallbackHz, int ResolvedFallbackHz, BrightnessSnapshot Brightness,
    bool SaverEnabled, bool ProfilesEnabled, AutomationPauseMode PauseMode, DateTimeOffset? ResumesAt,
    bool RestorationPending, SaverState SaverState, AutomationReason SaverReason,
    AutomationReason ProfileReason, double CooldownSeconds, OperationFailure Failure)
{
    /// <summary>Allowlisted report: deliberately excludes device identifiers, process names, paths, and logs.</summary>
    public string ToReport()
    {
        var report = new StringBuilder();
        report.AppendLine($"HzSwitcher {AppVersion}");
        report.AppendLine($"Captured: {CapturedAt:O}");
        report.AppendLine($"Windows: {OsVersion}");
        report.AppendLine($"Architecture: {Architecture}");
        report.AppendLine();
        report.AppendLine("Display");
        report.AppendLine($"Resolution: {Resolution}");
        report.AppendLine($"Current rate: {CurrentHz} Hz");
        report.AppendLine($"Supported rates: {string.Join(", ", SupportedHz)} Hz");
        report.AppendLine($"Fallback: {ConfiguredFallbackHz} Hz configured; {ResolvedFallbackHz} Hz resolved");
        report.AppendLine();
        report.AppendLine("Brightness");
        report.AppendLine($"Backend: {BackendName(Brightness.Backend)}");
        report.AppendLine($"Last known level: {Percent(Brightness.LastKnownPercent)}");
        report.AppendLine($"Last requested level: {Percent(Brightness.RequestedPercent)}");
        report.AppendLine($"Last hardware read: {Percent(Brightness.LastReadPercent)}");
        report.AppendLine($"Hardware read time: {(Brightness.ReadAt.HasValue ? Brightness.ReadAt.Value.ToString("O") : "Not available")}");
        report.AppendLine($"Read pending: {Brightness.Refreshing}; write pending: {Brightness.Writing}");
        report.AppendLine();
        report.AppendLine("Automation");
        report.AppendLine($"Saver enabled: {SaverEnabled}; profiles enabled: {ProfilesEnabled}");
        report.AppendLine($"Pause: {PauseMode}");
        if (ResumesAt.HasValue) report.AppendLine($"Resume time: {ResumesAt.Value:O}");
        report.AppendLine($"Restore needs attention: {RestorationPending}");
        report.AppendLine($"Saver state: {SaverState}; reason: {SaverReason}");
        report.AppendLine($"Profile active: {!string.IsNullOrEmpty(LocalProfile)}");
        report.AppendLine($"Profile reason: {ProfileReason}");
        report.AppendLine($"Automatic cooldown: {Math.Ceiling(CooldownSeconds):0} seconds");
        report.AppendLine();
        report.AppendLine("Recent failure on this monitor");
        if (Failure == null) report.AppendLine("No failure recorded this session.");
        else
        {
            report.AppendLine($"Time: {Failure.OccurredAt:O}");
            report.AppendLine($"Operation: {Failure.Operation}; result: {Failure.Kind}");
            string unit = Failure.Operation == OperationKind.RefreshRate ? "Hz" : "%";
            report.AppendLine($"Target: {Failure.Target} {unit}; reported result: {(Failure.Actual.HasValue ? Failure.Actual.Value + " " + unit : "Not confirmed")}");
        }
        return report.ToString();
    }

    public static string BackendName(BrightnessBackend backend) => backend switch
    {
        BrightnessBackend.Ddc => "DDC/CI",
        BrightnessBackend.Wmi => "WMI",
        BrightnessBackend.Software => "Software dimming",
        BrightnessBackend.Unsupported => "Unsupported",
        _ => "Not checked",
    };
    private static string Percent(int? value) => value.HasValue ? value.Value + "%" : "Not available";
}

/// <summary>A bounded session-only record of typed failures, keyed by monitor rather than UI selection.</summary>
public sealed class DiagnosticsService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationFailure> _failures = new();

    public void Record(OperationFailure failure)
    {
        lock (_gate)
        {
            string device = failure.Device ?? "";
            if (_failures.TryGetValue(device, out var previous) && previous.OccurredAt > failure.OccurredAt) return;
            _failures[device] = failure;
            if (_failures.Count > 8) _failures.Remove(_failures.MinBy(pair => pair.Value.OccurredAt).Key);
        }
    }

    public OperationFailure FailureFor(string device)
    {
        lock (_gate) return _failures.GetValueOrDefault(device ?? "");
    }
}
