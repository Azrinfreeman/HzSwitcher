using HzSwitcher.Native;
using System.Runtime.InteropServices;

namespace HzSwitcher.Services;

/// <summary>Reads and changes the refresh rate of the primary monitor at its current resolution.</summary>
public record MonitorInfo(string Device, string Label, bool Primary);

public class DisplayService : IDisplay
{
    private readonly Func<string> _preferred;

    public DisplayService(Func<string> preferredDevice) => _preferred = preferredDevice;

    /// <summary>The monitor being controlled: the chosen one if it is still attached, otherwise the primary.</summary>
    public string Device
    {
        get
        {
            string pref = _preferred();
            if (!string.IsNullOrEmpty(pref) && Screen.AllScreens.Any(s => s.DeviceName == pref)) return pref;
            return Screen.PrimaryScreen?.DeviceName ?? @"\\.\DISPLAY1";
        }
    }

    public List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        foreach (var scr in Screen.AllScreens)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            string name = DisplayApi.EnumDisplayDevices(scr.DeviceName, 0, ref dd, 0) ? dd.DeviceString : "Monitor";
            string shortDev = scr.DeviceName.TrimStart('\\', '.');
            list.Add(new MonitorInfo(scr.DeviceName,
                $"{shortDev} - {name} - {scr.Bounds.Width}x{scr.Bounds.Height}" + (scr.Primary ? " (primary)" : ""), scr.Primary));
        }
        return list;
    }

    /// <summary>Maps a requested rate to one the monitor supports: 0 = highest, otherwise the nearest.</summary>
    public int Resolve(int hz) => ModeMath.Resolve(GetSupportedHz(), hz);

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE { dmDeviceName = new string('\0', 32), dmFormName = new string('\0', 32) };
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return dm;
    }

    public int GetCurrentHz()
    {
        var dm = NewDevMode();
        return DisplayApi.EnumDisplaySettingsEx(Device, DisplayApi.ENUM_CURRENT_SETTINGS, ref dm, 0)
            ? (int)dm.dmDisplayFrequency : 0;
    }

    /// <summary>Distinct refresh rates available at the current resolution, ascending.</summary>
    public List<int> GetSupportedHz()
    {
        var cur = NewDevMode();
        var rates = new SortedSet<int>();
        if (!DisplayApi.EnumDisplaySettingsEx(Device, DisplayApi.ENUM_CURRENT_SETTINGS, ref cur, 0))
            return new List<int>();

        var dm = NewDevMode();
        for (int i = 0; DisplayApi.EnumDisplaySettingsEx(Device, i, ref dm, 0); i++)
        {
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight
                && dm.dmBitsPerPel == cur.dmBitsPerPel && dm.dmDisplayOrientation == cur.dmDisplayOrientation
                && dm.dmDisplayFrequency > 1)
                rates.Add((int)dm.dmDisplayFrequency);
        }
        return rates.ToList();
    }

    public string CurrentModeText()
    {
        var dm = NewDevMode();
        return DisplayApi.EnumDisplaySettingsEx(Device, DisplayApi.ENUM_CURRENT_SETTINGS, ref dm, 0)
            ? $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency} Hz" : "unknown";
    }

    /// <summary>Asks Windows whether the mode would be accepted, without applying it.</summary>
    public bool CanApply(int hz)
    {
        var built = Build(hz);
        if (!built.HasValue) return false;
        var dm = built.Value;
        return DisplayApi.ChangeDisplaySettingsEx(Device, ref dm, IntPtr.Zero, DisplayApi.CDS_TEST, IntPtr.Zero) == DisplayApi.DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Tests then applies the rate for this session only (not written to the registry).</summary>
    public bool TryApply(int hz)
    {
        // Even an identical mode request can make a driver resynchronize the display.
        if (hz > 0 && GetCurrentHz() == hz) return true;
        var built = Build(hz);
        if (!built.HasValue) return false;
        var dm = built.Value;
        if (DisplayApi.ChangeDisplaySettingsEx(Device, ref dm, IntPtr.Zero, DisplayApi.CDS_TEST, IntPtr.Zero) != DisplayApi.DISP_CHANGE_SUCCESSFUL)
        {
            Log.Write($"Mode test rejected for {hz} Hz");
            return false;
        }
        int rc = DisplayApi.ChangeDisplaySettingsEx(Device, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        Log.Write($"Apply {hz} Hz -> result {rc}");
        return rc == DisplayApi.DISP_CHANGE_SUCCESSFUL;
    }

    private DEVMODE? Build(int hz)
    {
        var dm = NewDevMode();
        if (!DisplayApi.EnumDisplaySettingsEx(Device, DisplayApi.ENUM_CURRENT_SETTINGS, ref dm, 0)) return null;
        dm.dmDisplayFrequency = (uint)hz;
        dm.dmFields |= DisplayApi.DM_DISPLAYFREQUENCY;
        return dm;
    }
}
