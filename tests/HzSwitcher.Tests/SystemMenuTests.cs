using HzSwitcher.Native;
using HzSwitcher.Services;
using Xunit;

namespace HzSwitcher.Tests;

public class SystemMenuTests
{
    private sealed class Windows : IWindowApi
    {
        public event Action<IntPtr> Destroyed;
        public readonly Dictionary<IntPtr, WindowState> Items = new();
        public IntPtr Active = new(1);
        public IntPtr Foreground() => Active;
        public WindowState? Inspect(IntPtr handle) => Items.TryGetValue(handle, out var state) ? state : null;
        public bool SetTopmost(IntPtr handle, bool value, out int error)
        { error = 0; Items[handle] = Items[handle] with { Topmost = value }; return true; }
        public void Add(int handle, bool topmost = false, long started = 1) =>
            Items[new IntPtr(handle)] = new(new(new IntPtr(handle), (uint)handle, started), topmost);
        public void Close(int handle) { Items.Remove(new IntPtr(handle)); Destroyed?.Invoke(new IntPtr(handle)); }
        public void Dispose() { }
    }

    /// <summary>Thread id is 100 + handle; each window gets menu 1000 + handle unless recreated.</summary>
    private sealed class Menus : IWindowSystemMenuApi
    {
        public readonly Dictionary<IntPtr, IntPtr> MenuOf = new();
        public readonly Dictionary<IntPtr, List<SystemMenuItem>> Items = new();
        public readonly Dictionary<uint, IntPtr> Owner = new();
        public bool DenyWrites;
        public void Add(int handle, params uint[] appIds)
        {
            var menu = new IntPtr(1000 + handle);
            MenuOf[new IntPtr(handle)] = menu;
            Items[menu] = appIds.Select(id => new SystemMenuItem(id, UIntPtr.Zero, true, false)).ToList();
        }
        public void Recreate(int handle, int menu)
        {
            MenuOf[new IntPtr(handle)] = new IntPtr(menu);
            Items[new IntPtr(menu)] = new() { new(0xF060, UIntPtr.Zero, true, false) };
        }
        public IEnumerable<IntPtr> Windows() => MenuOf.Keys.ToArray();
        public IntPtr Foreground() => IntPtr.Zero;
        public bool Eligible(IntPtr window) => MenuOf.ContainsKey(window);
        public uint Thread(IntPtr window) => (uint)(100 + window.ToInt32());
        public IntPtr MenuOwner(uint thread) => Owner.TryGetValue(thread, out var owner) ? owner : IntPtr.Zero;
        public IntPtr Menu(IntPtr window) => MenuOf.TryGetValue(window, out var menu) ? menu : IntPtr.Zero;
        public IReadOnlyList<uint> CommandIds(IntPtr menu) => Items[menu].Select(i => i.Id).ToArray();
        public SystemMenuItem? Item(IntPtr menu, uint id) =>
            Items.TryGetValue(menu, out var list) && list.Any(i => i.Id == id) ? list.First(i => i.Id == id) : null;
        public bool Insert(IntPtr menu, uint id, UIntPtr marker, bool enabled, bool check)
        {
            if (DenyWrites) return false;
            Items[menu].Add(new(id, marker, enabled, check)); return true;
        }
        public bool Update(IntPtr menu, uint id, bool enabled, bool check)
        {
            int i = Items[menu].FindIndex(x => x.Id == id);
            Items[menu][i] = Items[menu][i] with { Enabled = enabled, Checked = check }; return true;
        }
        public bool Remove(IntPtr menu, uint id) => Items[menu].RemoveAll(x => x.Id == id) > 0;
        public int LastError => 5;
        public SystemMenuItem[] Owned(int handle) => Items[MenuOf[new IntPtr(handle)]].Where(i => i.Marker != UIntPtr.Zero).ToArray();
    }

    private sealed class Rig
    {
        public readonly Windows Windows = new();
        public readonly Menus Menus = new();
        public readonly AlwaysOnTopService Pins;
        public readonly WindowSystemMenuService Service;
        public readonly List<PinResult> Feedback = new();
        public uint Now = 1000;
        public Rig(params int[] handles)
        {
            foreach (var h in handles) { Windows.Add(h); Menus.Add(h, 0xF000, 0xF060); }
            Pins = new AlwaysOnTopService(Windows);
            Service = new WindowSystemMenuService(Menus, Pins, Feedback.Add, () => Now);
        }
        public void Enable() { Service.SetEnabled(true); for (int i = 0; i < 10; i++) Service.Pump(); Now += 50; }
        public uint Command(int handle) => Menus.Owned(handle).Single().Id;
        /// <summary>Replays the sequence observed on Windows 11: popup start, invoked (OBJID_SYSMENU, child = command), popup end.</summary>
        public void Choose(int handle, uint? command = null, uint thread = 0, int popup = 9999)
        {
            thread = thread == 0 ? (uint)(100 + handle) : thread;
            Menus.Owner[thread] = new IntPtr(handle);
            Service.Handle(new(6, new IntPtr(popup), -4, 0, thread, Now++));
            Service.Handle(new(0x8013, new IntPtr(popup), -1, (int)(command ?? Command(handle)), thread, Now++));
            Service.Handle(new(7, new IntPtr(popup), -4, 0, thread, Now++));
        }
    }

    [Fact]
    public void PreparesEachWindowOnceWithAnUnusedCommandId()
    {
        var rig = new Rig(1, 2);
        rig.Menus.Items[new IntPtr(1001)].Add(new(0xE003, UIntPtr.Zero, true, false)); // app already uses the 0xE000 block
        rig.Enable();
        rig.Service.Rescan(); for (int i = 0; i < 5; i++) rig.Service.Pump();
        Assert.Equal(0xE010u, rig.Command(1));
        Assert.Equal(0xE000u, rig.Command(2));
        Assert.Equal(2, rig.Service.Count);
        Assert.Equal(4, rig.Menus.Items[new IntPtr(1001)].Count);
    }

    [Fact]
    public void ChoosingTheEntryTogglesExactlyOnceAndUpdatesTheCheck()
    {
        var rig = new Rig(1);
        rig.Enable();
        rig.Choose(1);
        Assert.True(rig.Pins.Owns(rig.Windows.Items[new IntPtr(1)].Identity));
        Assert.True(rig.Menus.Owned(1).Single().Checked);
        // A duplicate accessibility event for the same menu session is ignored.
        rig.Service.Handle(new(0x8013, new IntPtr(9999), -1, (int)rig.Command(1), 101, rig.Now++));
        Assert.Equal(1, rig.Pins.Count);
        rig.Now += 1000; rig.Service.Pump();
        rig.Choose(1);
        Assert.Equal(0, rig.Pins.Count);
        Assert.False(rig.Menus.Owned(1).Single().Checked);
        Assert.Equal(new[] { PinStatus.Pinned, PinStatus.Unpinned }, rig.Feedback.Select(f => f.Status));
    }

    [Fact]
    public void NeverPinsTheForegroundWindowInsteadOfTheMenuOwner()
    {
        var rig = new Rig(1, 2);
        rig.Enable();
        rig.Windows.Active = new IntPtr(2);
        rig.Choose(1);
        Assert.True(rig.Pins.Owns(rig.Windows.Items[new IntPtr(1)].Identity));
        Assert.False(rig.Pins.Owns(rig.Windows.Items[new IntPtr(2)].Identity));
    }

    [Fact]
    public void IgnoresOtherCommandsAndUnrelatedControls()
    {
        var rig = new Rig(1);
        rig.Enable();
        rig.Choose(1, command: 0xF060);
        // A client control on another thread reporting our numeric ID is not our menu.
        rig.Service.Handle(new(0x8013, new IntPtr(555), -4, (int)rig.Command(1), 777, rig.Now++));
        Assert.Equal(0, rig.Pins.Count);
        Assert.Empty(rig.Feedback);
    }

    [Fact]
    public void RejectsRecycledWindowsAndRecreatedMenus()
    {
        var rig = new Rig(1, 2);
        rig.Enable();
        uint stale = rig.Command(1);
        rig.Windows.Add(1, started: 99); // same HWND, different process lifetime
        rig.Menus.Owner[101] = new IntPtr(1);
        rig.Service.Handle(new(0x8013, new IntPtr(1), -1, (int)stale, 101, rig.Now++));
        Assert.Equal(0, rig.Pins.Count);

        uint stale2 = rig.Command(2);
        rig.Menus.Recreate(2, 5000); // the app reset its system menu
        rig.Menus.Owner[102] = new IntPtr(2);
        rig.Service.Handle(new(0x8013, new IntPtr(2), -1, (int)stale2, 102, rig.Now++));
        Assert.Equal(0, rig.Pins.Count);
        // The next pass prepares the replacement menu with one fresh entry, which then works.
        rig.Service.Rescan(); rig.Service.Pump(); rig.Now += 1000; rig.Service.Pump();
        Assert.Single(rig.Menus.Owned(2));
        rig.Choose(2);
        Assert.True(rig.Pins.Owns(rig.Windows.Items[new IntPtr(2)].Identity));
    }

    [Fact]
    public void DisablesTheEntryForExternallyTopmostWindows()
    {
        var rig = new Rig(1);
        rig.Windows.Add(1, topmost: true);
        rig.Enable();
        Assert.False(rig.Menus.Owned(1).Single().Enabled);
        rig.Choose(1);
        Assert.Equal(0, rig.Pins.Count);
        Assert.True(rig.Windows.Items[new IntPtr(1)].Topmost);
    }

    [Fact]
    public void DisableRemovesOnlyItsOwnEntries()
    {
        var rig = new Rig(1, 2);
        rig.Enable();
        // Another tool replaced our item on window 2 with one using the same ID.
        var menu2 = rig.Menus.MenuOf[new IntPtr(2)];
        uint id2 = rig.Command(2);
        rig.Menus.Items[menu2].RemoveAll(i => i.Id == id2);
        rig.Menus.Items[menu2].Add(new(id2, new UIntPtr(7), true, false));
        rig.Service.SetEnabled(false);
        Assert.Equal(new uint[] { 0xF000, 0xF060 }, rig.Menus.Items[rig.Menus.MenuOf[new IntPtr(1)]].Select(i => i.Id));
        Assert.Contains(rig.Menus.Items[menu2], i => i.Id == id2 && i.Marker == new UIntPtr(7));
        Assert.Equal(0, rig.Service.Count);
    }

    [Fact]
    public void ReportsDeniedMenuWritesWithoutTracking()
    {
        var rig = new Rig(1);
        rig.Menus.DenyWrites = true;
        rig.Enable();
        Assert.Equal(0, rig.Service.Count);
        Assert.Contains("Menu write unavailable", rig.Service.Status);
    }

    [Fact]
    public void DestroyedWindowsAreForgotten()
    {
        var rig = new Rig(1);
        rig.Enable();
        rig.Service.Handle(new(0x8001, new IntPtr(1), 0, 0, 101, rig.Now++));
        Assert.Equal(0, rig.Service.Count);
    }

    [Fact]
    public void SystemMenuPreferenceInheritsOlderMouseMenuChoice()
    {
        Assert.True(SettingsStore.Parse("{}").AlwaysOnTopSystemMenu);
        Assert.False(SettingsStore.Parse("""{"SettingsVersion":3,"AlwaysOnTopMouseMenu":false}""").AlwaysOnTopSystemMenu);
        var split = SettingsStore.Parse("""{"SettingsVersion":3,"AlwaysOnTopMouseMenu":false,"AlwaysOnTopSystemMenu":true}""");
        Assert.True(split.AlwaysOnTopSystemMenu); Assert.False(split.AlwaysOnTopMouseMenu);
    }

    [Fact]
    public void NativeAdapterInsertsAboveCloseAndRemovesOnlyItsItem()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new NativeWindow();
                window.CreateHandle(new CreateParams { Caption = "HzSwitcher menu test", ClassName = "STATIC",
                    Style = unchecked((int)0x00CF0000), X = -32000, Y = -32000, Width = 300, Height = 200 });
                var api = new WindowSystemMenuApi();
                var menu = api.Menu(window.Handle);
                Assert.NotEqual(IntPtr.Zero, menu);
                var before = api.CommandIds(menu).ToArray();
                Assert.True(api.Insert(menu, 0xE010, new UIntPtr(0x5A5A5), true, false), $"insert failed: {api.LastError}");
                var ids = api.CommandIds(menu);
                Assert.Equal(0xE010u, ids[ids.Count - 2]);
                Assert.Equal(0xF060u, ids[ids.Count - 1]);
                Assert.Equal(new UIntPtr(0x5A5A5), api.Item(menu, 0xE010).Value.Marker);
                Assert.True(api.Update(menu, 0xE010, false, true));
                Assert.Equal((false, true), (api.Item(menu, 0xE010).Value.Enabled, api.Item(menu, 0xE010).Value.Checked));
                Assert.True(api.Remove(menu, 0xE010));
                Assert.Equal(before, api.CommandIds(menu));
                window.DestroyHandle();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
