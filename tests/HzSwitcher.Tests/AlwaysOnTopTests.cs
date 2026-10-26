using HzSwitcher.Services;
using HzSwitcher.UI;
using Xunit;

namespace HzSwitcher.Tests;

public class AlwaysOnTopTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out System.Drawing.Rectangle rect);

    [Fact]
    public void NativeAdapterChangesOnlyTopmostStyleOfAnOwnedTestWindow()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new NativeWindow();
                window.CreateHandle(new CreateParams { Caption = "HzSwitcher native test", ClassName = "STATIC",
                    Style = unchecked((int)0x90000000), ExStyle = 0x08000080,
                    X = -32000, Y = -32000, Width = 300, Height = 200 });
                using var api = new HzSwitcher.Native.WindowApi();
                var handle = window.Handle;
                var before = api.Inspect(handle);
                Assert.NotNull(before);
                GetWindowRect(handle, out var bounds);
                try
                {
                    Assert.True(api.SetTopmost(handle, true, out int error), $"SetWindowPos failed: {error}");
                    Assert.True(api.Inspect(handle).Value.Topmost);
                    GetWindowRect(handle, out var afterBounds);
                    Assert.Equal(bounds, afterBounds);
                    Assert.Equal(before.Value.Identity, api.Inspect(handle).Value.Identity);
                }
                finally { api.SetTopmost(handle, false, out _); }
                Assert.False(api.Inspect(handle).Value.Topmost);
                window.DestroyHandle();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Native window test timed out");
        Assert.Null(failure);
    }

    private sealed class Windows : IWindowApi
    {
        public event Action<IntPtr> Destroyed;
        public readonly Dictionary<IntPtr, WindowState> Items = new();
        public IntPtr Active = new(1);
        public bool Fail;
        public bool IgnoreChange;
        public int Calls;
        public IntPtr Foreground() => Active;
        public WindowState? Inspect(IntPtr handle) => Items.TryGetValue(handle, out var state) ? state : null;
        public bool SetTopmost(IntPtr handle, bool value, out int error)
        {
            Calls++;
            error = Fail ? 5 : 0;
            if (Fail) return false;
            if (!IgnoreChange) Items[handle] = Items[handle] with { Topmost = value };
            return true;
        }
        public void Add(int handle, bool topmost = false, long started = 1) =>
            Items[new IntPtr(handle)] = new(new(new IntPtr(handle), (uint)handle, started), topmost);
        public void Close(int handle) { Items.Remove(new IntPtr(handle)); Destroyed?.Invoke(new IntPtr(handle)); }
        public void Dispose() { }
    }

    [Fact]
    public void TogglesIndependentWindowsAndRestoresAll()
    {
        var api = new Windows(); api.Add(1); api.Add(2);
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground();
        api.Active = new(2); service.ToggleForeground();
        Assert.Equal(2, service.Count);
        service.ToggleForeground();
        Assert.False(api.Items[new(2)].Topmost);
        Assert.True(api.Items[new(1)].Topmost);
        service.RestoreAll();
        Assert.Equal(0, service.Count);
        Assert.False(api.Items[new(1)].Topmost);
    }

    [Fact]
    public void LeavesExistingTopmostAndInvalidWindowsUntouched()
    {
        var api = new Windows(); api.Add(1, true);
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); api.Active = new(9); service.ToggleForeground(); service.RestoreAll();
        Assert.Equal(0, api.Calls);
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void FailedPinIsNotOwnedAndFailedRestoreCanBeRetried()
    {
        var api = new Windows(); api.Add(1); api.Fail = true;
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); Assert.Equal(0, service.Count);
        api.Fail = false; service.ToggleForeground();
        api.Fail = true; service.RestoreAll(); Assert.Equal(1, service.Count);
        api.Fail = false; service.RestoreAll(); Assert.Equal(0, service.Count);
    }

    [Fact]
    public void DestructionAndDifferentIdentityPreventRestoringReplacement()
    {
        var api = new Windows(); api.Add(1);
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); api.Close(1); api.Add(1, true);
        service.RestoreAll(); Assert.True(api.Items[new(1)].Topmost);
        api.Add(1); service.ToggleForeground(); api.Add(1, true, started: 2);
        service.RestoreAll(); Assert.True(api.Items[new(1)].Topmost);
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void VerifiesStateEvenWhenWindowsReportsSuccess()
    {
        var api = new Windows(); api.Add(1); api.IgnoreChange = true;
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); Assert.Equal(0, service.Count);
        api.IgnoreChange = false; service.ToggleForeground();
        api.IgnoreChange = true; service.RestoreAll(); Assert.Equal(1, service.Count);
        api.IgnoreChange = false; service.RestoreAll(); Assert.Equal(0, service.Count);
    }

    [Fact]
    public void DisposeRestoresOwnedWindows()
    {
        var api = new Windows(); api.Add(1);
        var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); service.Dispose();
        Assert.False(api.Items[new(1)].Topmost);
    }

    [Theory]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+ControlKey")]
    [InlineData("Ctrl+999")]
    [InlineData("")]
    public void RejectsMalformedShortcuts(string spec) => Assert.False(HotkeyWindow.TryParse(spec, out _, out _));

    [Fact]
    public void NormalizesShortcutOrderAndAliases()
    {
        Assert.True(HotkeyWindow.TryParse("Control+Alt+T", out var mods, out var key));
        Assert.True(HotkeyWindow.TryParse("alt+ctrl+t", out var otherMods, out var otherKey));
        Assert.Equal((mods, key), (otherMods, otherKey));
    }

    [Fact]
    public void SettingsDefaultOffAndRoundTrip()
    {
        Assert.False(SettingsStore.Parse("{}").AlwaysOnTopEnabled);
        var settings = new Settings { AlwaysOnTopEnabled = true, AlwaysOnTopShortcutInitialized = true };
        settings.Hotkeys.Add(new() { Keys = "Ctrl+Alt+T", Action = "ToggleAlwaysOnTop" });
        var restored = SettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.True(restored.AlwaysOnTopEnabled);
        Assert.True(restored.AlwaysOnTopShortcutInitialized);
        Assert.Contains(restored.Hotkeys, b => b.Action == "ToggleAlwaysOnTop");
    }

    [Fact]
    public void MenuTargetRemainsIndependentOfForegroundAndRejectsReusedHandle()
    {
        var api = new Windows(); api.Add(1); api.Add(2);
        using var service = new AlwaysOnTopService(api);
        var target = api.Items[new(1)].Identity;
        api.Active = new(2);
        Assert.Equal(PinStatus.Pinned, service.Toggle(target).Status);
        Assert.False(api.Items[new(2)].Topmost);
        api.Add(1, true, started: 2);
        int before = api.Calls;
        Assert.Equal(PinStatus.Failed, service.Toggle(target).Status);
        Assert.Equal(before, api.Calls);
        service.Reconcile();
        Assert.Equal(0, service.Count);
        Assert.True(api.Items[new(1)].Topmost);
    }

    [Fact]
    public void FeedbackTransitionsOnlyFollowConfirmedChanges()
    {
        var api = new Windows(); api.Add(1);
        using var service = new AlwaysOnTopService(api);
        var transitions = new List<PinResult>(); service.Transition += transitions.Add;
        api.Fail = true;
        Assert.Equal(PinStatus.Failed, service.ToggleForeground().Status);
        Assert.Empty(transitions);
        api.Fail = false;
        Assert.Equal(PinStatus.Pinned, service.ToggleForeground().Status);
        api.Fail = true;
        Assert.Equal(PinStatus.Failed, service.ToggleForeground().Status);
        Assert.Single(transitions);
        Assert.Equal(1, service.Count);
        api.Fail = false;
        api.Items[new(1)] = api.Items[new(1)] with { Topmost = false };
        int before = api.Calls;
        service.Reconcile();
        Assert.Equal(before, api.Calls);
        Assert.Equal(PinStatus.Removed, transitions.Last().Status);
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void RestoreAllReportsOneAggregateCueAndNoCueForExternalRemoval()
    {
        var api = new Windows(); api.Add(1); api.Add(2);
        using var service = new AlwaysOnTopService(api);
        service.ToggleForeground(); api.Active = new(2); service.ToggleForeground();
        Assert.True(service.RestoreAll().RestoredAny);
        Assert.False(service.RestoreAll().RestoredAny);
        service.ToggleForeground();
        api.Items[new(2)] = api.Items[new(2)] with { Topmost = false };
        Assert.False(service.RestoreAll().RestoredAny);
    }

    [Fact]
    public void NewPreferencesDefaultOnAndThicknessClampsOnDeserialize()
    {
        var defaults = SettingsStore.Parse("{}");
        Assert.True(defaults.AlwaysOnTopOutline);
        Assert.True(defaults.AlwaysOnTopMouseMenu);
        Assert.True(defaults.AlwaysOnTopSound);
        Assert.Equal(3, defaults.AlwaysOnTopOutlineThickness);
        var custom = SettingsStore.Parse("{\"AlwaysOnTopOutlineThickness\":999,\"AlwaysOnTopSound\":false,\"AlwaysOnTopMouseMenu\":false,\"AlwaysOnTopOutline\":false}");
        Assert.Equal(8, custom.AlwaysOnTopOutlineThickness);
        var restored = SettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(custom));
        Assert.False(restored.AlwaysOnTopSound); Assert.False(restored.AlwaysOnTopMouseMenu); Assert.False(restored.AlwaysOnTopOutline);
        restored.AlwaysOnTopOutlineThickness = -8; Assert.Equal(1, restored.AlwaysOnTopOutlineThickness);
    }

    [Fact]
    public void MouseGesturePairsReleaseDespiteShiftReleaseOrDisable()
    {
        var gesture = new HzSwitcher.Native.MouseGesture();
        Assert.False(gesture.Process(true, false, false, true, false, true));
        Assert.False(gesture.Process(true, false, true, true, true, true));
        Assert.False(gesture.Process(true, false, false, true, true, false));
        Assert.True(gesture.Process(true, false, false, true, true, true));
        Assert.False(gesture.Process(false, true, true, true, true, true));
        Assert.True(gesture.Captured);
        Assert.True(gesture.Process(false, true, false, false, false, false));
        Assert.False(gesture.Captured);
        Assert.False(gesture.Process(false, true, false, true, true, true));
        Assert.False(gesture.Process(true, false, false, false, true, true));
    }

    [Fact]
    public void OutlineGeometryHandlesNegativeCoordinatesAndClipsMaximizedBounds()
    {
        var frame = new System.Drawing.Rectangle(-1500, 100, 900, 600);
        var monitor = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
        Assert.Equal(new System.Drawing.Rectangle(-1506, 94, 912, 612),
            HzSwitcher.Native.WindowGeometry.OutlineBounds(frame, monitor, 6, false));
        Assert.Equal(monitor, HzSwitcher.Native.WindowGeometry.OutlineBounds(monitor, monitor, 6, true));
    }

    [Fact]
    public void EmbeddedCuesAreValidShortWavFiles()
    {
        foreach (var name in new[] { "pin", "unpin" })
        {
            using var stream = typeof(AlwaysOnTopService).Assembly.GetManifestResourceStream($"HzSwitcher.Assets.{name}.wav");
            Assert.NotNull(stream);
            using var reader = new BinaryReader(stream);
            Assert.Equal("RIFF", new string(reader.ReadChars(4)));
            Assert.InRange(stream.Length, 1000, 22050);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [Fact]
    public void NativeOutlineIsHollowDoesNotActivateAndStaysBelowHigherWindow()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            var target = new NativeWindow(); var higher = new NativeWindow();
            using var api = new HzSwitcher.Native.WindowApi();
            try
            {
                var cp = new CreateParams { Caption = "HzSwitcher overlay test", ClassName = "STATIC",
                    Style = unchecked((int)0x90000000), ExStyle = 0x08000080,
                    X = -32000, Y = -32000, Width = 300, Height = 200 };
                target.CreateHandle(cp); higher.CreateHandle(cp);
                Assert.True(api.SetTopmost(target.Handle, true, out _));
                Assert.True(api.SetTopmost(higher.Handle, true, out _));
                var identity = api.Inspect(target.Handle).Value.Identity;
                var foreground = GetForegroundWindow();
                IntPtr overlay;
                using (var border = new AlwaysOnTopBorderWindow(identity, (_, _) => { }))
                {
                    border.UpdateOutline(new System.Drawing.Rectangle(100, 100, 300, 200), 192, 3);
                    overlay = border.Handle;
                    Assert.Equal(new System.Drawing.Rectangle(94, 94, 312, 212), border.Bounds);
                    Assert.False(border.Region.IsVisible(100, 100));
                    Assert.True(border.Region.IsVisible(1, 1));
                    Assert.Equal(foreground, GetForegroundWindow());
                    Assert.Equal(overlay, HzSwitcher.Native.WindowGeometry.GetWindow(target.Handle, 3));
                    Assert.Equal(higher.Handle, HzSwitcher.Native.WindowGeometry.GetWindow(overlay, 3));
                    Assert.True(api.Inspect(overlay).Value.Topmost);
                }
                Assert.False(IsWindow(overlay));
            }
            catch (Exception e) { failure = e; }
            finally
            {
                if (target.Handle != IntPtr.Zero) target.DestroyHandle();
                if (higher.Handle != IntPtr.Zero) higher.DestroyHandle();
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Outline native test timed out");
        Assert.Null(failure);
    }

    [Fact]
    public void NativeMouseHookInstallsAndUnhooksWithoutAnyInput()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var dispatcher = new Control(); _ = dispatcher.Handle;
                using var hook = new HzSwitcher.Native.WindowMouseHook(dispatcher, (_, _, _) => { });
                hook.SetEnabled(true); Assert.True(hook.Available);
                hook.SetEnabled(false); Assert.False(hook.Available);
                hook.SetEnabled(true); Assert.True(hook.Available);
                hook.Dispose(); Assert.False(hook.Available);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Mouse hook native test timed out");
        Assert.Null(failure);
    }
}
