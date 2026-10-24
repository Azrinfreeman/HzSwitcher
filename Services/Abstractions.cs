using HzSwitcher.Native;

namespace HzSwitcher.Services;

/// <summary>The monitor operations the services need; faked in tests.</summary>
public interface IDisplay
{
    int GetCurrentHz();
    bool TryApply(int hz);
    int Resolve(int hz);
    List<int> GetSupportedHz();
}

public enum BrightnessMode { None, Hardware, Software }

/// <summary>Brightness of the selected monitor; faked in tests.</summary>
public interface IBrightness
{
    /// <summary>True when the selected monitor can be controlled by any method.</summary>
    bool IsSupported { get; }
    /// <summary>Hardware (DDC/CI or WMI) or Software (gamma-ramp dimming).</summary>
    BrightnessMode Mode { get; }
    /// <summary>Last known brightness, 0-100.</summary>
    int Get();
    /// <summary>Sets brightness 0-100 (clamped) without blocking; newer requests replace older pending ones.</summary>
    void RequestSet(int percent);
}

public interface IMediaInfo
{
    bool IsWatching();
    bool IsGameForeground();
}

public interface IActivity
{
    /// <summary>Milliseconds since the last keyboard or mouse input.</summary>
    uint IdleMilliseconds();
}

public interface IForeground
{
    /// <summary>Process name (no .exe) of the foreground window, or "" if unknown.</summary>
    string ProcessName();
}

public interface IClock
{
    DateTime UtcNow { get; }
}

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

internal sealed class SystemActivity : IActivity
{
    public uint IdleMilliseconds() => InputApi.IdleMilliseconds();
}

internal sealed class SystemForeground : IForeground
{
    public string ProcessName() => ShellApi.GetForeground().Process;
}

/// <summary>Pure rate-selection logic.</summary>
public static class ModeMath
{
    /// <summary>0 = highest supported; otherwise the nearest supported rate (ties go to the higher one).</summary>
    public static int Resolve(IReadOnlyList<int> supported, int hz)
    {
        if (supported.Count == 0) return hz;
        if (hz <= 0) return supported.Max();
        return supported.OrderBy(r => Math.Abs(r - hz)).ThenByDescending(r => r).First();
    }
}
