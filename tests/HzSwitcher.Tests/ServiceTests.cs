using HzSwitcher.Services;
using Xunit;

namespace HzSwitcher.Tests;

public class ModeMathTests
{
    private static readonly int[] Rates = { 60, 100, 120, 240 };

    [Fact] public void ZeroMeansHighest() => Assert.Equal(240, ModeMath.Resolve(Rates, 0));
    [Fact] public void ExactRateIsKept() => Assert.Equal(120, ModeMath.Resolve(Rates, 120));
    [Fact] public void NearestRateWins() => Assert.Equal(100, ModeMath.Resolve(Rates, 90));
    [Fact] public void TieGoesToTheHigherRate() => Assert.Equal(120, ModeMath.Resolve(new[] { 60, 120 }, 90));
    [Fact] public void EmptyListReturnsRequestedRate() => Assert.Equal(144, ModeMath.Resolve(Array.Empty<int>(), 144));
}

public class MatchTests
{
    [Theory]
    [InlineData("chrome", "Chrome", true)]
    [InlineData("chrome.exe", "chrome", true)]
    [InlineData("Roblox*", "RobloxPlayerBeta", true)]
    [InlineData("cs2", "cs20", false)]
    [InlineData("*", "anything", true)]
    public void WildcardMatches(string pattern, string text, bool expected) => Assert.Equal(expected, Match.Wildcard(pattern, text));

    [Fact] public void NormalizeStripsExtensionAndSpaces() => Assert.Equal("a", Match.Normalize(" a.EXE "));
}

public class SettingsTests
{
    [Fact]
    public void FileWithoutVersionGetsDefaultHotkeys()
    {
        var s = SettingsStore.Parse("{}");
        Assert.Equal(3, s.SettingsVersion);
        Assert.Equal(4, s.Hotkeys.Count);
    }

    [Fact]
    public void BrightnessSettingsDefaultWhenMissing()
    {
        var s = SettingsStore.Parse("{ \"SettingsVersion\": 2 }");
        Assert.Equal(-1, s.SaverBrightness);
        Assert.Equal(-1, s.PendingBrightness);
        Assert.False(s.RestoreBrightnessOnStart);
        Assert.Empty(s.MonitorBrightness);
    }

    [Fact]
    public void BrightnessSettingsAreRead()
    {
        var s = SettingsStore.Parse("{ \"SettingsVersion\": 2, \"SaverBrightness\": 30, \"MonitorBrightness\": { \"MONITOR\\\\ABC\": 40 } }");
        Assert.Equal(30, s.SaverBrightness);
        Assert.Equal(40, s.MonitorBrightness[@"MONITOR\ABC"]);
    }

    [Fact]
    public void SaveThenLoadKeepsValues()
    {
        string dir = Path.Combine(Path.GetTempPath(), "HzSwitcherTests-" + Guid.NewGuid().ToString("N"));
        string old = Log.Dir;
        Log.Dir = dir;
        try
        {
            var s = new Settings { SaverBrightness = 25, RestoreBrightnessOnStart = true };
            s.MonitorBrightness["id"] = 70;
            SettingsStore.Save(s);
            var back = SettingsStore.Load();
            Assert.Equal(25, back.SaverBrightness);
            Assert.True(back.RestoreBrightnessOnStart);
            Assert.Equal(70, back.MonitorBrightness["id"]);
        }
        finally
        {
            Log.Dir = old;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

public class AutoSaverTests
{
    private readonly Settings _settings = new() { IdleMinutes = 1, SaverHz = 60, RestoreSeconds = 5, MinSwitchSeconds = 0, AutoEnabled = true };
    private readonly FakeDisplay _display = new();
    private readonly FakeMedia _media = new();
    private readonly FakeActivity _activity = new();
    private readonly FakeClock _clock = new();
    private readonly AutoSaverService _auto;
    private int _started, _ended;

    public AutoSaverTests()
    {
        _auto = new AutoSaverService(() => _settings, _display, _media, null, _activity, _clock);
        _auto.SaverStarted += () => _started++;
        _auto.SaverEnded += () => _ended++;
    }

    private void GoIdle()
    {
        _activity.Idle = 61_000;
        _auto.Tick();
    }

    [Fact]
    public void LongIdleEntersSaver()
    {
        GoIdle();
        Assert.Equal(SaverState.Saver, _auto.State);
        Assert.Equal(60, _display.Hz);
        Assert.Equal(240, _auto.RestoreHz);
        Assert.Equal(1, _started);
    }

    [Fact]
    public void ShortIdleStaysActive()
    {
        _activity.Idle = 10_000;
        _auto.Tick();
        Assert.Equal(SaverState.Active, _auto.State);
        Assert.Equal(240, _display.Hz);
        Assert.Equal(0, _started);
    }

    [Fact]
    public void WatchingVideoLongEnoughEntersSaver()
    {
        _settings.WatchingQuietSeconds = 5;
        _media.Watching = true;
        _activity.Idle = 10_000;   // quiet, but far below the plain idle timeout
        _auto.Tick();
        Assert.Equal(SaverState.Active, _auto.State);   // video only just started
        _clock.Advance(61);
        _auto.Tick();
        Assert.Equal(SaverState.Saver, _auto.State);
        Assert.Equal(60, _display.Hz);
    }

    [Fact]
    public void GameInForegroundBlocksSaver()
    {
        _media.Game = true;
        GoIdle();
        Assert.Equal(SaverState.Active, _auto.State);
        Assert.Equal(0, _started);
    }

    [Fact]
    public void ContinuousActivityRestoresAndRaisesEnded()
    {
        GoIdle();
        _activity.Idle = 0;
        for (int i = 0; i < 8; i++) { _clock.Advance(1); _auto.Tick(); }
        Assert.Equal(SaverState.Active, _auto.State);
        Assert.Equal(240, _display.Hz);
        Assert.Equal(1, _ended);
    }

    [Fact]
    public void ManualChangeEndsSaver()
    {
        GoIdle();
        _auto.OnManualChange();
        Assert.Equal(SaverState.Active, _auto.State);
        Assert.Equal(1, _ended);
    }

    [Fact]
    public void ManualChangeWhileActiveRaisesNothing()
    {
        _auto.OnManualChange();
        Assert.Equal(0, _ended);
    }

    [Fact]
    public void DisabledAutoNeverEntersSaver()
    {
        _settings.AutoEnabled = false;
        GoIdle();
        Assert.Equal(SaverState.Active, _auto.State);
    }
}
