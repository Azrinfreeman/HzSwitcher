using System.Diagnostics;
using HzSwitcher.Native;
using HzSwitcher.Services;
using HzSwitcher.UI;
using Xunit;

namespace HzSwitcher.Tests;

public class WindowFrameTests
{
    [Theory]
    [InlineData(2, true)] // Caption
    [InlineData(3, true)] // System icon
    [InlineData(10, true)] // Resize edge
    [InlineData(17, true)] // Resize corner
    [InlineData(18, true)] // Border
    [InlineData(1, false)] // Client
    [InlineData(5, false)] // App menu bar
    [InlineData(8, false)] // Minimize button
    [InlineData(9, false)] // Maximize button
    [InlineData(20, false)] // Close button
    [InlineData(-1, false)]
    public void FrameRoutingPreservesClientMenusAndCaptionButtons(int hit, bool expected) =>
        Assert.Equal(expected, WindowFrameApi.IsFrameHit(hit));

    [Fact]
    public void CandidateGeometryLeavesClientClicksAloneOnNegativeMonitors()
    {
        var window = new Rectangle(-1600, 200, 800, 600);
        var client = new Rectangle(-1592, 240, 784, 552);
        Assert.True(WindowFrameApi.IsOutsideClient(window, client, new(-1450, 220)));
        Assert.True(WindowFrameApi.IsOutsideClient(window, client, new(-1598, 500)));
        Assert.False(WindowFrameApi.IsOutsideClient(window, client, new(-1400, 400)));
        Assert.False(WindowFrameApi.IsOutsideClient(window, client, new(-1700, 200)));
        int packed = WindowFrameApi.PackPoint(new(-1450, -800)).ToInt32();
        Assert.Equal(-1450, unchecked((short)packed));
        Assert.Equal(-800, unchecked((short)(packed >> 16)));
    }

    [Fact]
    public void FrameGestureNeedsNoShiftButStillHonorsDisableAndInjection()
    {
        var gesture = new MouseGesture();
        Assert.False(gesture.Process(true, false, false, true, false, true));
        Assert.False(gesture.Process(true, false, true, true, false, true, frame: true));
        Assert.False(gesture.Process(true, false, false, false, false, true, frame: true));
        Assert.True(gesture.Process(true, false, false, true, false, true, frame: true));
        Assert.True(gesture.Process(false, true, false, false, false, false));
        Assert.False(gesture.Captured);
    }

    [Theory]
    [InlineData((int)PinStatus.Pinned, false)]
    [InlineData((int)PinStatus.Unpinned, false)]
    [InlineData((int)PinStatus.Removed, false)]
    [InlineData((int)PinStatus.Failed, true)]
    public void SuccessfulChangesAreSilentAndErrorsStillNotify(int status, bool notify) =>
        Assert.Equal(notify, new PinResult((PinStatus)status, new WindowIdentity(new(1), 1, 1), "result").ShowNotification);

    [Fact]
    public void BulkNoOpIsSilentButAlreadyTopmostExplanationRemainsAvailable()
    {
        Assert.False(new PinResult(PinStatus.Unchanged, null, "All managed windows unpinned.").ShowNotification);
        Assert.True(new PinResult(PinStatus.Unchanged, new WindowIdentity(new(1), 1, 1), "Already on top").ShowNotification);
    }

    [Fact]
    public void FrameMenuRetainsWindowCommandsAndTogglesTheClickedWindow()
    {
        OnSta(() =>
        {
            using var target = TestForm(); using var other = TestForm();
            using var pins = new AlwaysOnTopService(new WindowApi());
            var identity = pins.Inspect(target.Handle).Value.Identity;
            var results = new List<PinResult>();
            using var presenter = new WindowPinMenu();
            var menu = presenter.Build(identity, Point.Empty, pins, () => true, results.Add, () => { }, () => { }, frameMenu: true);
            Assert.Contains(menu.Items.Cast<ToolStripItem>(), i => i.Text.Replace("&", "").StartsWith("Close"));
            var pin = Assert.Single(menu.Items.OfType<ToolStripMenuItem>(), i => i.Text == "Always on top");
            Assert.False(pin.Checked);
            pin.PerformClick();
            Assert.True(results.Last().Status == PinStatus.Pinned, results.Last().ToString());
            Assert.True(pins.Inspect(target.Handle).Value.Topmost);
            Assert.False(pins.Inspect(other.Handle).Value.Topmost);
            menu = presenter.Build(identity, Point.Empty, pins, () => true, results.Add, () => { }, () => { }, frameMenu: true);
            pin = Assert.Single(menu.Items.OfType<ToolStripMenuItem>(), i => i.Text == "Always on top");
            Assert.True(pin.Checked);
            pin.PerformClick();
            Assert.Equal(PinStatus.Unpinned, results.Last().Status);
            Assert.False(pins.Inspect(target.Handle).Value.Topmost);
        });
    }

    [Fact]
    public void StaleFrameMenuCommandCannotActOnAClosedTarget()
    {
        OnSta(() =>
        {
            using var target = TestForm();
            using var pins = new AlwaysOnTopService(new WindowApi());
            var identity = pins.Inspect(target.Handle).Value.Identity;
            var results = new List<PinResult>();
            using var presenter = new WindowPinMenu();
            var menu = presenter.Build(identity, Point.Empty, pins, () => true, results.Add, () => { }, () => { }, frameMenu: true);
            var close = menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text.Replace("&", "").StartsWith("Close"));
            target.Dispose();
            close.PerformClick();
            Assert.Equal(PinStatus.Failed, results.Last().Status);
        });
    }

    [Fact]
    public async Task CrossProcessFrameHitTestingMenuSnapshotAndCommandsWork()
    {
        // An off-screen helper window exercises the actual foreign-process Windows APIs without user input.
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Add-Type -AssemblyName System.Windows.Forms; $form = New-Object System.Windows.Forms.Form; $form.Text = 'HzSwitcher frame test'; $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-30000,-30000); $form.ShowInTaskbar = $false; $form.Show(); [Console]::WriteLine($form.Handle.ToInt64()); [System.Windows.Forms.Application]::Run($form)");
        using var child = Process.Start(info);
        try
        {
            string ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(long.TryParse(ready, out long value), "Window helper did not report its handle");
            var hwnd = new IntPtr(value);
            Assert.True(WindowFrameApi.IsFrameCandidate(hwnd, new(-29900, -29980)));
            Assert.Equal(2, WindowFrameApi.HitTest(hwnd, new(-29900, -29980)));
            Assert.False(WindowFrameApi.IsFrameCandidate(hwnd, new(-29900, -29900)));
            var commands = WindowFrameApi.ReadCommands(hwnd);
            Assert.Contains(commands, c => c.Id == 0xF060 && c.Enabled); // SC_CLOSE
            Assert.Contains(commands, c => c.Id == 0xF010); // SC_MOVE
            Assert.Contains(commands, c => c.Id == 0xF020); // SC_MINIMIZE
            OnSta(() =>
            {
                using var pins = new AlwaysOnTopService(new WindowApi());
                using var presenter = new WindowPinMenu();
                var identity = pins.Inspect(hwnd).Value.Identity;
                var results = new List<PinResult>();
                var menu = presenter.Build(identity, Point.Empty, pins, () => true, results.Add, () => { }, () => { }, frameMenu: true);
                menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Always on top").PerformClick();
                Assert.True(results.Last().Status == PinStatus.Pinned, results.Last().ToString());
                Assert.False(results.Last().ShowNotification);
                menu = presenter.Build(identity, Point.Empty, pins, () => true, results.Add, () => { }, () => { }, frameMenu: true);
                var pin = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Always on top");
                Assert.True(pin.Checked);
                pin.PerformClick();
                Assert.Equal(PinStatus.Unpinned, results.Last().Status);
                Assert.False(results.Last().ShowNotification);
            });
            // Exercise the actual handoff, including ToolStrip capture/focus cleanup.
            try
            {
                OnSta(() =>
                {
                    using var pins = new AlwaysOnTopService(new WindowApi());
                    using var presenter = new WindowPinMenu();
                    var results = new List<PinResult>();
                    var identity = pins.Inspect(hwnd).Value.Identity;
                    var menu = presenter.Build(identity, new(-29999, -29900), pins, () => true,
                        results.Add, () => { }, () => { }, frameMenu: true);
                    menu.Show(new Point(20, 20));
                    Assert.True(menu.Visible);
                    menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Original window menu").PerformClick();
                    Assert.False(menu.Visible);
                    var timeout = Stopwatch.StartNew();
                    while (!NativeMenuOpen(hwnd) && timeout.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert.Empty(results);
                    Assert.True(NativeMenuOpen(hwnd), "The original native window menu never entered menu mode");
                });
            }
            finally { CancelNativeMenu(hwnd); }
            Assert.True(WindowFrameApi.Execute(hwnd, 0xF060));
            Assert.True(child.WaitForExit(5000), "Close command did not reach the helper");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size; public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    private static bool NativeMenuOpen(IntPtr hwnd)
    {
        var info = new GuiThreadInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out _), ref info) && (info.Flags & 0x14) != 0 && info.MenuOwner == hwnd;
    }
    private static void CancelNativeMenu(IntPtr hwnd) => PostMessage(hwnd, 0x1F, IntPtr.Zero, IntPtr.Zero);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);

    [Fact]
    public void SystemMenuSnapshotPreservesCustomSubmenusAndDisabledCommands()
    {
        OnSta(() =>
        {
            using var target = TestForm();
            var menu = GetSystemMenu(target.Handle, false);
            var submenu = CreatePopupMenu();
            for (uint i = 0; i < 8; i++) Assert.True(AppendMenu(submenu, i == 0 ? 2u : 0u, new UIntPtr(100 + i), "Custom " + i));
            Assert.True(AppendMenu(menu, 0x10, new UIntPtr(unchecked((ulong)submenu.ToInt64())), "App tools"));
            var snapshot = WindowFrameApi.ReadCommands(target.Handle);
            var tools = Assert.Single(snapshot, i => i.Text == "App tools");
            Assert.False(tools.Separator);
            Assert.Equal(8, tools.Children.Length);
            Assert.False(tools.Children[0].Enabled);
            Assert.True(tools.Children[1].Enabled);
        });
    }

    private sealed class TestFrameWindow : NativeWindow, IDisposable
    {
        public TestFrameWindow() => CreateHandle(new CreateParams { Caption = "HzSwitcher frame test", ClassName = "STATIC",
            Style = 0x10CF0000, ExStyle = 0x08000080, X = -30000, Y = -30000, Width = 400, Height = 300 });
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }
    private static TestFrameWindow TestForm() => new();
    private static void OnSta(Action test)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Frame menu test timed out");
        Assert.Null(failure);
    }
}
