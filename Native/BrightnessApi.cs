using System.Runtime.InteropServices;

namespace HzSwitcher.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
internal struct PHYSICAL_MONITOR
{
    public IntPtr hPhysicalMonitor;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
}

internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

internal static class BrightnessApi
{
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hPhysical, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr hPhysical, uint brightness);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] ramp);

    /// <summary>Finds the HMONITOR for a device name such as \\.\DISPLAY1.</summary>
    private static IntPtr FindMonitor(string device)
    {
        IntPtr found = IntPtr.Zero;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (GetMonitorInfo(h, ref info) && string.Equals(info.szDevice, device, StringComparison.OrdinalIgnoreCase))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Runs <paramref name="action"/> with the first physical monitor handle of the device (DDC/CI), then releases it.</summary>
    private static bool WithPhysical(string device, Func<IntPtr, bool> action)
    {
        IntPtr hMon = FindMonitor(device);
        if (hMon == IntPtr.Zero) return false;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) return false;
        var list = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, list)) return false;
        try { return action(list[0].hPhysicalMonitor); }
        finally { DestroyPhysicalMonitors(count, list); }
    }

    public static bool TryGetDdc(string device, out uint min, out uint current, out uint max)
    {
        uint lo = 0, cur = 0, hi = 0;
        bool ok = WithPhysical(device, h => GetMonitorBrightness(h, out lo, out cur, out hi));
        (min, current, max) = (lo, cur, hi);
        return ok && hi > lo;
    }

    public static bool SetDdc(string device, uint raw) => WithPhysical(device, h => SetMonitorBrightness(h, raw));

    /// <summary>Reads the monitor's current gamma ramp (3 x 256 values: red, green, blue), or null.</summary>
    public static ushort[] GetGamma(string device)
    {
        IntPtr dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            var ramp = new ushort[3 * 256];
            return GetDeviceGammaRamp(dc, ramp) ? ramp : null;
        }
        finally { DeleteDC(dc); }
    }

    public static bool SetGamma(string device, ushort[] ramp)
    {
        IntPtr dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try { return SetDeviceGammaRamp(dc, ramp); }
        finally { DeleteDC(dc); }
    }
}
