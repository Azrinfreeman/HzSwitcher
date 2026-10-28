using HzSwitcher.Native;
using HzSwitcher.Services;
using HzSwitcher.UI;
using Xunit;

namespace HzSwitcher.Tests;

public class PinInteractionTests
{
    private sealed class InputApi : IPinInteractionApi
    {
        public PinForeground Foreground = new(new(1), 1, false);
        public string Name = "notepad";
        public bool? Exclusive = false;
        public int Classifications;
        public PinForeground ReadForeground() => Foreground;
        public string ProcessName(uint pid) { Classifications++; return Name; }
        public bool? ExclusiveFullscreen() { Classifications++; return Exclusive; }
    }
    private static void Sta(Action test)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Input guard test timed out");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static Control Dispatcher() { var control = new Control(); _ = control.Handle; return control; }

    [Fact]
    public void FullscreenUnknownGamesAndConfiguredWindowedGamesAreProtected()
    {
        Sta(() =>
        {
            using var ui = Dispatcher();
            var api = new InputApi(); var settings = new Settings { GameProcesses = ["MyGame*.exe"] };
            using var guard = new WindowPinInteractionGuard(settings, ui, api, observe: false);
            Assert.True(guard.CanUseMouseMenu());
            api.Foreground = api.Foreground with { Fullscreen = true }; api.Name = "unknown-game";
            Assert.False(guard.Revalidate());
            api.Foreground = api.Foreground with { Fullscreen = false }; api.Name = "mygame64";
            Assert.False(guard.Revalidate());
            api.Name = "browser"; api.Exclusive = true;
            Assert.False(guard.Revalidate());
            api.Exclusive = false;
            Assert.True(guard.Revalidate());
            settings.GameProcesses.Add("browser");
            Assert.False(guard.Revalidate());
        });
    }

    [Fact]
    public void HookCheckRejectsNewForegroundStaleAndUnknownStateWithoutSlowClassification()
    {
        Sta(() =>
        {
            using var ui = Dispatcher(); var api = new InputApi(); long now = 100;
            using var guard = new WindowPinInteractionGuard(new Settings(), ui, api, () => now, observe: false);
            int calls = api.Classifications;
            Assert.True(guard.CanUseMouseMenu());
            api.Foreground = new(new(2), 2, false);
            Assert.False(guard.CanUseMouseMenu());
            Assert.Equal(calls, api.Classifications);
            guard.Refresh(); Assert.True(guard.CanUseMouseMenu());
            now += 751; Assert.False(guard.CanUseMouseMenu());
            guard.Refresh(); Assert.True(guard.CanUseMouseMenu());
            api.Foreground = api.Foreground with { ProcessId = 3 };
            Assert.False(guard.CanUseMouseMenu());
            api.Foreground = api.Foreground with { Fullscreen = null };
            Assert.False(guard.Revalidate());
            api.Foreground = api.Foreground with { Fullscreen = false }; api.Name = "";
            Assert.False(guard.Revalidate());
            api.Name = "notepad"; api.Exclusive = null;
            Assert.False(guard.Revalidate());
        });
    }

    [Fact]
    public void SameHandleFullscreenEntryIsDetectedBeforeTheTimerAndAltTabRestoresMenus()
    {
        Sta(() =>
        {
            using var ui = Dispatcher(); var api = new InputApi();
            using var guard = new WindowPinInteractionGuard(new Settings(), ui, api, observe: false);
            Assert.True(guard.CanUseMouseMenu());
            api.Foreground = api.Foreground with { Fullscreen = true };
            Assert.False(guard.CanUseMouseMenu());
            guard.Refresh(); Assert.True(guard.Protected);
            api.Foreground = new(new(2), 2, false);
            Assert.False(guard.CanUseMouseMenu());
            Assert.True(guard.Revalidate()); Assert.False(guard.Protected);
            guard.SetActive(false); Assert.False(guard.CanUseMouseMenu());
            guard.SetActive(true); Assert.True(guard.CanUseMouseMenu());
            guard.Dispose(); Assert.False(guard.CanUseMouseMenu());
            Application.DoEvents(); // Queued refresh after disposal must be harmless.
        });
    }

    [Fact]
    public void FullscreenGeometryDistinguishesMaximizedFramesAndHandlesNegativeCoordinates()
    {
        var monitor = new Rectangle(-2560, -300, 2560, 1440);
        var oversized = Rectangle.Inflate(monitor, 12, 12);
        var captionedClient = new Rectangle(-2560, -250, 2560, 1350);
        Assert.True(PinInteractionApi.IsFullscreen(monitor, monitor, monitor, false, false));
        Assert.True(PinInteractionApi.IsFullscreen(oversized, monitor, monitor, true, false));
        Assert.False(PinInteractionApi.IsFullscreen(oversized, captionedClient, monitor, true, true));
        Assert.False(PinInteractionApi.IsFullscreen(new(-2500, -200, 1500, 900), new(-2490, -160, 1480, 850), monitor, false, true));
        Assert.True(PinInteractionApi.IsFullscreen(monitor, captionedClient, monitor, false, false));
    }

    [Fact]
    public void ProtectedGesturePassesBothButtonsAndDoesNotCaptureReleaseAfterAltTab()
    {
        var gesture = new MouseGesture();
        Assert.False(gesture.Process(true, false, false, true, true, true, true, protectedInput: true));
        Assert.False(gesture.Process(false, true, false, true, true, true, true, protectedInput: false));
        Assert.True(gesture.Process(true, false, false, true, false, true, true));
        Assert.True(gesture.Process(false, true, false, true, false, false, protectedInput: true));
        Assert.False(gesture.Captured);
    }

    [Fact]
    public void ProtectedAndCancelledHookGesturesNeverQueueMenus()
    {
        Sta(() =>
        {
            using var ui = Dispatcher(); bool allowed = true; int menus = 0;
            using var hook = new WindowMouseHook(ui, (_, _, _) => menus++, () => allowed);
            hook.SetEnabled(true);
            bool Button(bool down, bool allow, bool injected = false) => hook.ProcessButton(down, injected, true, new(1), Point.Empty, false, true, allow);
            Assert.False(Button(true, false)); Assert.False(Button(false, true));
            Assert.True(Button(true, true)); Assert.True(Button(false, false));
            Application.DoEvents(); Assert.Equal(0, menus);
            Assert.True(Button(true, true)); Assert.True(Button(false, true));
            allowed = false; Application.DoEvents(); Assert.Equal(0, menus);
            allowed = true;
            Assert.True(Button(true, true)); hook.CancelPendingGesture();
            Assert.True(Button(false, true)); Application.DoEvents(); Assert.Equal(0, menus);
            Assert.False(Button(true, true, injected: true));
            Assert.False(Button(false, true, injected: true));
            Assert.True(Button(true, true)); Assert.True(Button(false, true));
            Application.DoEvents(); Assert.Equal(1, menus);
        });
    }

    [Fact]
    public void ProtectionBlocksMenuBuildAndEveryAlreadyBuiltMenuActionSilently()
    {
        Sta(() =>
        {
            using var target = new NativeWindowScope();
            using var pins = new AlwaysOnTopService(new WindowApi());
            var identity = pins.Inspect(target.Handle).Value.Identity;
            using var presenter = new WindowPinMenu(); bool allowed = false; int actions = 0;
            var results = new List<PinResult>();
            Assert.Null(presenter.Build(identity, Point.Empty, pins, () => allowed, results.Add, () => actions++, () => actions++, true));
            allowed = true;
            pins.Toggle(identity);
            var menu = presenter.Build(identity, Point.Empty, pins, () => allowed, results.Add, () => actions++, () => actions++, true);
            _ = menu.Handle;
            allowed = false;
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>().ToArray()) item.PerformClick();
            Application.DoEvents();
            Assert.Empty(results); Assert.Equal(0, actions);
            Assert.True(pins.Owns(identity)); Assert.True(pins.Inspect(target.Handle).Value.Topmost);
        });
    }

    [Fact]
    public void OutlinesHideAndReturnWithoutChangingPinOwnership()
    {
        Sta(() =>
        {
            using var target = new NativeWindowScope();
            using var pins = new AlwaysOnTopService(new WindowApi());
            var identity = pins.Inspect(target.Handle).Value.Identity;
            bool protect = false;
            using var borders = new AlwaysOnTopBorderService(pins, new Settings(), (_, _) => { }, () => protect);
            pins.Toggle(identity);
            bool HasOutline() => Application.OpenForms.OfType<AlwaysOnTopBorderWindow>().Any(f => f.Visible && borders.TargetForOverlay(f.Handle) == identity);
            Assert.True(HasOutline());
            protect = true; borders.Refresh(); Assert.False(HasOutline());
            Assert.True(pins.Owns(identity)); Assert.True(pins.Inspect(target.Handle).Value.Topmost);
            protect = false; borders.Refresh(); Assert.True(HasOutline());
        });
    }
    private sealed class NativeWindowScope : NativeWindow, IDisposable
    {
        public NativeWindowScope() => CreateHandle(new CreateParams { Caption = "HzSwitcher gameplay regression", ClassName = "STATIC",
            Style = 0x10CF0000, ExStyle = 0x08000080, X = 100, Y = 100, Width = 400, Height = 300 });
        public void Dispose() => DestroyHandle();
    }
}
