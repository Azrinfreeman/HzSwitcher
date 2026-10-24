using Xunit;
using HzSwitcher.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // tests share the static log / settings folder

namespace HzSwitcher.Tests;

internal sealed class FakeDisplay : IDisplay
{
    public int Hz = 240;
    public List<int> Supported = new() { 60, 120, 240 };
    public int GetCurrentHz() => Hz;
    public bool TryApply(int hz) { Hz = hz; return true; }
    public int Resolve(int hz) => ModeMath.Resolve(Supported, hz);
    public List<int> GetSupportedHz() => Supported;
}

internal sealed class FakeMedia : IMediaInfo
{
    public bool Watching, Game;
    public bool IsWatching() => Watching;
    public bool IsGameForeground() => Game;
}

internal sealed class FakeActivity : IActivity
{
    public uint Idle;
    public uint IdleMilliseconds() => Idle;
}

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}
