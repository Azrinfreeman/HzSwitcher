using HzSwitcher.Native;
using NAudio.CoreAudioApi;

namespace HzSwitcher.Services;

/// <summary>
/// Heuristic "is the user watching video?" check: audio is audibly playing from a browser or player,
/// and the foreground window is fullscreen, a known player, or a tab whose title matches the watch list.
/// </summary>
public class MediaDetector : IMediaInfo, IDisposable
{
    private readonly Func<Settings> _settings;
    private readonly MMDeviceEnumerator _enumerator = new();
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _cached;

    public MediaDetector(Func<Settings> settings) => _settings = settings;

    /// <summary>Cached for 3 s so the 250 ms state-machine tick stays cheap.</summary>
    public bool IsWatching()
    {
        if ((DateTime.UtcNow - _lastCheck).TotalSeconds < 3) return _cached;
        _lastCheck = DateTime.UtcNow;
        try { _cached = Compute(); }
        catch (Exception e) { Log.Write("Media check failed: " + e.Message); _cached = false; }
        return _cached;
    }

    public bool IsGameForeground()
    {
        var s = _settings();
        var (_, proc) = ShellApi.GetForeground();
        if (proc.Length > 0 && s.GameProcesses.Any(g => Match.Wildcard(g, proc))) return true;
        // Exclusive-fullscreen D3D app that is not a known video player is treated as a game.
        return ShellApi.IsExclusiveFullscreenApp() && !s.WatchPlayers.Any(p => p.Equals(proc, StringComparison.OrdinalIgnoreCase));
    }

    private bool Compute()
    {
        var s = _settings();
        var (title, proc) = ShellApi.GetForeground();

        bool titleOk = s.WatchTitles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase));
        bool playerFg = s.WatchPlayers.Any(p => p.Equals(proc, StringComparison.OrdinalIgnoreCase));
        bool fullscreen = ShellApi.IsForegroundFullscreen();
        if (!(titleOk || playerFg || fullscreen)) return false;

        return IsWatchAudioPlaying(s);
    }

    private bool IsWatchAudioPlaying(Settings s)
    {
        var names = new HashSet<string>(s.WatchBrowsers.Concat(s.WatchPlayers), StringComparer.OrdinalIgnoreCase);
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
            if (session.AudioMeterInformation.MasterPeakValue < 0.001f) continue;
            string name = ShellApi.ProcessNameOf((int)session.GetProcessID);
            if (names.Contains(name)) return true;
        }
        return false;
    }

    public void Dispose() => _enumerator.Dispose();
}
