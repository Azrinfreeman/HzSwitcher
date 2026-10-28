using HzSwitcher.Native;
using HzSwitcher.Services;
using HzSwitcher.UI;
using System.Runtime.InteropServices;
using Xunit;

namespace HzSwitcher.Tests;

public sealed class AutomationUiTests
{
    [Fact]
    public void NativeDisplayPathLayoutMatchesWindowsAbi()
    {
        Assert.Equal(72, Marshal.SizeOf<DisplayConfigApi.Path>());
        Assert.Equal(84, Marshal.SizeOf<DisplayConfigApi.SourceName>());
        Assert.Equal(420, Marshal.SizeOf<DisplayConfigApi.TargetName>());
    }

    [Fact]
    public void MirroredOrDuplicatePathsAreNotIndependentTargets()
    {
        var targets = MonitorCatalog.Classify(new[] {
            new MonitorTarget("DISPLAY1", "panel-A", "connection-1", "driver", true),
            new MonitorTarget("DISPLAY1", "panel-B", "connection-2", "driver", true),
            new MonitorTarget("DISPLAY2", "panel-C", "connection-3", "driver", true) });
        Assert.False(targets[0].Independent); Assert.False(targets[1].Independent); Assert.True(targets[2].Independent);
    }

}
