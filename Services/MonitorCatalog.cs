using HzSwitcher.Native;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace HzSwitcher.Services;

public record MonitorTarget(string Device, string DevicePath, string Connection, string Driver, bool Independent);

/// <summary>Read-only CCD map. Ambiguous or mirrored paths are not eligible for independent automation.</summary>
public sealed class MonitorCatalog
{
    public IReadOnlyList<MonitorTarget> Targets { get; private set; } = Array.Empty<MonitorTarget>();
    public string Error { get; private set; }

    public void Refresh()
    {
        try
        {
            const uint activePaths = 2;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int rc = DisplayConfigApi.GetDisplayConfigBufferSizes(activePaths, out uint paths, out uint modes);
                if (rc != 0) throw new IOException("Display path enumeration failed: " + rc);
                if (paths > 256 || modes > 1024) throw new IOException("Unexpected display path count.");
                var buffer = Marshal.AllocHGlobal(checked((int)Math.Max(1, modes) * 64));
                try
                {
                    var data = new DisplayConfigApi.Path[paths];
                    rc = DisplayConfigApi.QueryDisplayConfig(activePaths, ref paths, data, ref modes, buffer, IntPtr.Zero);
                    if (rc == 122) continue; // topology changed between sizing and query
                    if (rc != 0) throw new IOException("Display path query failed: " + rc);
                    var targets = new List<MonitorTarget>();
                    foreach (var path in data.Take((int)paths))
                    {
                        var source = new DisplayConfigApi.SourceName { Header = new() { Type = 1,
                            Size = (uint)Marshal.SizeOf<DisplayConfigApi.SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
                        var target = new DisplayConfigApi.TargetName { Header = new() { Type = 2,
                            Size = (uint)Marshal.SizeOf<DisplayConfigApi.TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                        if (DisplayConfigApi.GetSourceName(ref source) != 0 || DisplayConfigApi.GetTargetName(ref target) != 0 ||
                            string.IsNullOrEmpty(source.Device) || string.IsNullOrEmpty(target.DevicePath)) continue;
                        string connection = $"{path.Target.Adapter.High:X8}{path.Target.Adapter.Low:X8}:{path.Target.Id}:{target.Connector}";
                        targets.Add(new(source.Device, target.DevicePath, connection, DriverVersion(source.Device), true));
                    }
                    Targets = Classify(targets); Error = null; return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new IOException("Display topology did not settle.");
        }
        catch (Exception e) { Targets = Array.Empty<MonitorTarget>(); Error = e.Message; }
    }

    internal static IReadOnlyList<MonitorTarget> Classify(IEnumerable<MonitorTarget> targets)
    {
        var list = targets.ToList();
        return list.Select(t => t with { Independent = list.Count(other => other.Device == t.Device) == 1 &&
            list.Count(other => other.DevicePath == t.DevicePath) == 1 }).ToArray();
    }

    private static string DriverVersion(string device)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; DisplayApi.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if (dd.DeviceName != device) continue;
            const string prefix = @"\Registry\Machine\";
            try
            {
                if (dd.DeviceKey?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(dd.DeviceKey[prefix.Length..]);
                    return key?.GetValue("DriverVersion")?.ToString() ?? "unknown-driver";
                }
            }
            catch { }
        }
        return "unknown-driver";
    }
}
