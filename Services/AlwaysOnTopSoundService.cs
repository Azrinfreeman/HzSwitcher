using System.Media;

namespace HzSwitcher.Services;

internal sealed class AlwaysOnTopSoundService : IDisposable
{
    private readonly SoundPlayer _pin;
    private readonly SoundPlayer _unpin;
    private readonly Stream _pinStream, _unpinStream;
    private SoundPlayer _playing;
    private bool _failed;
    public AlwaysOnTopSoundService()
    {
        var assembly = typeof(AlwaysOnTopSoundService).Assembly;
        _pinStream = assembly.GetManifestResourceStream("HzSwitcher.Assets.pin.wav");
        _unpinStream = assembly.GetManifestResourceStream("HzSwitcher.Assets.unpin.wav");
        _pin = new SoundPlayer(_pinStream); _unpin = new SoundPlayer(_unpinStream);
        try { _pin.Load(); _unpin.Load(); } catch (Exception e) { Fail(e); }
    }
    public void Play(PinResult result, bool enabled)
    {
        if (!enabled || _failed || (result.Status is not (PinStatus.Pinned or PinStatus.Unpinned) && !result.RestoredAny)) return;
        try { _playing?.Stop(); _playing = result.Status == PinStatus.Pinned ? _pin : _unpin; _playing.Play(); }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception e) { if (!_failed) Log.Write("Pin sound unavailable: " + e.Message); _failed = true; }
    public void Dispose() { _playing?.Stop(); _pin.Dispose(); _unpin.Dispose(); _pinStream?.Dispose(); _unpinStream?.Dispose(); }
}
