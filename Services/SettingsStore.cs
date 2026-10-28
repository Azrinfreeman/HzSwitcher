using System.Text.Json;

namespace HzSwitcher.Services;

public class HotkeyBinding
{
    /// <summary>For example "Ctrl+Alt+1".</summary>
    public string Keys { get; set; } = "";
    /// <summary>SetHz, SetMax, Preset, Cycle, ToggleAuto or ToggleAlwaysOnTop.</summary>
    public string Action { get; set; } = "SetHz";
    public int Hz { get; set; }
    /// <summary>Preset name when Action is "Preset".</summary>
    public string Preset { get; set; } = "";
}

/// <summary>A named one-click rate. Hz = 0 means "highest the monitor supports".</summary>
public class Preset
{
    public string Name { get; set; } = "";
    public int Hz { get; set; }
}

/// <summary>When this app is in the foreground, use this rate; go back when it leaves. Hz = 0 means highest.</summary>
public class AppProfile
{
    /// <summary>Process name without .exe; * wildcards allowed.</summary>
    public string Process { get; set; } = "";
    public int Hz { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowSaver { get; set; }
}

public class Settings
{
    public int SettingsVersion { get; set; } = 3;
    public bool PreviewMode { get; set; } = true;
    public bool AutomationPaused { get; set; }
    public DateTime? PauseUntilUtc { get; set; }
    public int ManualHoldMinutes { get; set; } = 15;
    /// <summary>Device name of the monitor to control, e.g. \\.\DISPLAY1. Empty = primary.</summary>
    public string MonitorDevice { get; set; } = "";
    public bool AlwaysOnTopEnabled { get; set; }
    public bool AlwaysOnTopOutline { get; set; } = true;
    private int _outlineThickness = 3;
    public int AlwaysOnTopOutlineThickness { get => _outlineThickness; set => _outlineThickness = Math.Clamp(value, 1, 8); }
    public bool AlwaysOnTopSound { get; set; } = true;
    /// <summary>Shift+right-click fallback menu (and the legacy frame menu when system-menu integration is off).</summary>
    public bool AlwaysOnTopMouseMenu { get; set; } = true;
    /// <summary>Adds "Always on top (HzSwitcher)" to other windows' native system menus.</summary>
    public bool AlwaysOnTopSystemMenu { get; set; } = true;
    public bool AlwaysOnTopShortcutInitialized { get; set; }
    public bool ShowAllRates { get; set; }
    public bool NotifyOnAutoSwitch { get; set; } = true;
    /// <summary>Last main-window bounds (0 width = never saved).</summary>
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }

    public bool AutoEnabled { get; set; } = true;
    public int IdleMinutes { get; set; } = 10;
    /// <summary>Activity must continue this long before the previous rate is restored.</summary>
    public int RestoreSeconds { get; set; } = 5;
    /// <summary>A gap in input longer than this resets the restore window.</summary>
    public double ActivityGapSeconds { get; set; } = 1.5;
    public int SaverHz { get; set; } = 60;
    /// <summary>For the "watching" rule: no input at all for this many seconds.</summary>
    public int WatchingQuietSeconds { get; set; } = 60;
    /// <summary>Minimum interval before another automatic profile/saver choice; manual actions and saver restoration bypass it.</summary>
    public int MinSwitchSeconds { get; set; } = 30;

    /// <summary>Last brightness (0-100) per monitor, keyed by the monitor's device id.</summary>
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();
    /// <summary>Re-apply the saved brightness of the selected monitor at startup.</summary>
    public bool RestoreBrightnessOnStart { get; set; }
    /// <summary>Brightness while the power-saver is active; -1 leaves brightness alone.</summary>
    public int SaverBrightness { get; set; } = -1;
    /// <summary>Brightness to put back after a saver that was interrupted by a crash; -1 = none.</summary>
    public int PendingBrightness { get; set; } = -1;
    public string PendingBrightnessTarget { get; set; } = "";

    public List<Preset> Presets { get; set; } = new()
    {
        new() { Name = "Gaming", Hz = 0 },
        new() { Name = "Balanced", Hz = 120 },
        new() { Name = "Battery saver", Hz = 60 },
    };

    public bool ProfilesEnabled { get; set; } = true;
    /// <summary>Seconds the profile app must be out of the foreground before the previous rate returns.</summary>
    public int ProfileLeaveSeconds { get; set; } = 5;
    public List<AppProfile> Profiles { get; set; } = new();
    public List<HotkeyBinding> Hotkeys { get; set; } = new()
    {
        new() { Keys = "Ctrl+Alt+1", Action = "Preset", Preset = "Battery saver" },
        new() { Keys = "Ctrl+Alt+2", Action = "Preset", Preset = "Balanced" },
        new() { Keys = "Ctrl+Alt+3", Action = "Preset", Preset = "Gaming" },
        new() { Keys = "Ctrl+Alt+0", Action = "ToggleAuto" },
    };

    public List<string> WatchTitles { get; set; } = new()
    {
        "YouTube", "Netflix", "Prime Video", "Disney+", "Twitch", "Plex", "Hulu", "Max", "Crunchyroll", "Viu", "iQIYI", "WeTV", "Bilibili", "Vimeo"
    };
    /// <summary>Browsers whose audio counts as media when the foreground title matches.</summary>
    public List<string> WatchBrowsers { get; set; } = new() { "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi" };
    /// <summary>Dedicated players: their audio counts when they are in the foreground.</summary>
    public List<string> WatchPlayers { get; set; } = new() { "vlc", "mpv", "mpc-hc64", "mpc-hc", "potplayermini64", "potplayermini", "PotPlayer", "Plex", "Video.UI", "Netflix" };
    /// <summary>Games are never throttled. Supports * wildcards.</summary>
    public List<string> GameProcesses { get; set; } = new()
    {
        "FortniteClient-Win64-Shipping", "RobloxPlayerBeta", "Roblox*", "javaw", "Minecraft*", "steamapps*", "cs2", "VALORANT*", "RiotClient*", "eldenring"
    };

    /// <summary>Rate the monitor is reset to if a switch fails or the display misbehaves.</summary>
    public int FallbackHz { get; set; } = 240;
    /// <summary>Set while a change is being verified; used for crash recovery.</summary>
    public bool PendingChange { get; set; }
}

public static class SettingsStore
{
    public static string LastError { get; private set; }
    private static bool _readOnly;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(Log.Dir, "settings.json");

    public static string Path_ => FilePath;

    /// <summary>Parses a settings file and migrates older versions. Throws JsonException on bad JSON.</summary>
    public static Settings Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Settings must be an object.");
        bool versioned = document.RootElement.TryGetProperty("SettingsVersion", out var versionElement);
        int version = versioned ? versionElement.GetInt32() : 1;
        if (version > 3) throw new NotSupportedException("These settings require a newer HzSwitcher. The file has been preserved.");
        var loaded = JsonSerializer.Deserialize<Settings>(text, Options) ?? new Settings();
        // Files from before system-menu integration inherit the old combined mouse-menu choice, so opt-outs stay off.
        if (!document.RootElement.TryGetProperty(nameof(Settings.AlwaysOnTopSystemMenu), out _))
            loaded.AlwaysOnTopSystemMenu = loaded.AlwaysOnTopMouseMenu;
        if (!versioned)
        {
            // Pre-1.1 file: move to the preset-based default hotkeys.
            loaded.Hotkeys = new Settings().Hotkeys;
        }
        if (version < 3)
        {
            loaded.PreviewMode = false;
            foreach (var profile in loaded.Profiles ?? new()) profile.AllowSaver = true;
        }
        loaded.SettingsVersion = 3;
        loaded.Presets ??= new(); loaded.Profiles ??= new(); loaded.Hotkeys ??= new();
        loaded.MonitorBrightness ??= new(); loaded.WatchTitles ??= new(); loaded.WatchBrowsers ??= new();
        loaded.WatchPlayers ??= new(); loaded.GameProcesses ??= new();
        loaded.Presets.RemoveAll(p => p == null); loaded.Profiles.RemoveAll(p => p == null); loaded.Hotkeys.RemoveAll(p => p == null);
        loaded.MonitorDevice ??= "";
        loaded.PendingBrightnessTarget ??= "";
        foreach (var p in loaded.Presets) { p.Name ??= "Preset"; p.Hz = Math.Clamp(p.Hz, 0, 1000); }
        foreach (var h in loaded.Hotkeys) { h.Keys ??= ""; h.Action ??= ""; h.Preset ??= ""; }
        foreach (var p in loaded.Profiles) { p.Process ??= ""; p.Hz = Math.Clamp(p.Hz, 0, 1000); }
        loaded.WatchTitles.RemoveAll(s => string.IsNullOrWhiteSpace(s));
        loaded.WatchBrowsers.RemoveAll(s => string.IsNullOrWhiteSpace(s));
        loaded.WatchPlayers.RemoveAll(s => string.IsNullOrWhiteSpace(s));
        loaded.GameProcesses.RemoveAll(s => string.IsNullOrWhiteSpace(s));
        loaded.IdleMinutes = Math.Clamp(loaded.IdleMinutes, 1, 240);
        loaded.RestoreSeconds = Math.Clamp(loaded.RestoreSeconds, 1, 60);
        loaded.ActivityGapSeconds = double.IsFinite(loaded.ActivityGapSeconds) ? Math.Clamp(loaded.ActivityGapSeconds, .1, 60) : 1.5;
        loaded.MinSwitchSeconds = Math.Clamp(loaded.MinSwitchSeconds, 0, 300);
        loaded.ProfileLeaveSeconds = Math.Clamp(loaded.ProfileLeaveSeconds, 1, 120);
        loaded.WatchingQuietSeconds = Math.Clamp(loaded.WatchingQuietSeconds, 1, 3600);
        loaded.SaverHz = Math.Clamp(loaded.SaverHz, 1, 1000);
        loaded.SaverBrightness = Math.Clamp(loaded.SaverBrightness, -1, 100);
        loaded.ManualHoldMinutes = Math.Clamp(loaded.ManualHoldMinutes, 1, 1440);
        return loaded;
    }

    public static Settings Load()
    {
        _readOnly = false;
        LastError = null;
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            string text = File.ReadAllText(FilePath);
            var loaded = Parse(text);
            using var document = JsonDocument.Parse(text);
            bool migrated = !document.RootElement.TryGetProperty("SettingsVersion", out var v) || v.GetInt32() < 3;
            if (migrated)
            {
                if (!File.Exists(FilePath + ".v2-backup")) File.Copy(FilePath, FilePath + ".v2-backup");
                Save(loaded);
            }
            return loaded;
        }
        catch (NotSupportedException e)
        {
            _readOnly = true;
            LastError = e.Message;
            return new Settings { AutomationPaused = true };
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Log.Write("Settings load failed: " + e.Message);
            try { File.Copy(FilePath, FilePath + ".bad", true); Log.Write("Unreadable settings kept as settings.json.bad"); } catch { }
            return new Settings();
        }
    }

    public static void Save(Settings s) => TrySave(s);

    public static bool TrySave(Settings s)
    {
        if (_readOnly) return false;
        try
        {
            AtomicJsonFile.Write(FilePath, s);
            LastError = null;
            return true;
        }
        catch (Exception e) { LastError = e.Message; Log.Write("Settings save failed: " + e.Message); return false; }
    }
}
