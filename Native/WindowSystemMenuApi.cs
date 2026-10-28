using System.Runtime.InteropServices;
using HzSwitcher.Services;

namespace HzSwitcher.Native;

internal readonly record struct SystemMenuItem(uint Id, UIntPtr Marker, bool Enabled, bool Checked);
internal interface IWindowSystemMenuApi
{
    IEnumerable<IntPtr> Windows();
    IntPtr Foreground();
    bool Eligible(IntPtr window);
    uint Thread(IntPtr window);
    IntPtr MenuOwner(uint thread);
    IntPtr Menu(IntPtr window);
    IReadOnlyList<uint> CommandIds(IntPtr menu);
    SystemMenuItem? Item(IntPtr menu, uint id);
    bool Insert(IntPtr menu, uint id, UIntPtr marker, bool enabled, bool check);
    bool Update(IntPtr menu, uint id, bool enabled, bool check);
    bool Remove(IntPtr menu, uint id);
    int LastError { get; }
}

internal sealed class WindowSystemMenuApi(uint onlyProcess = 0) : IWindowSystemMenuApi
{
    internal const string Label = "Always on top (HzSwitcher)";
    [StructLayout(LayoutKind.Sequential)] internal struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public IntPtr Submenu, CheckedBitmap, UncheckedBitmap;
        public UIntPtr Data;
        public IntPtr Text;
        public uint TextLength;
        public IntPtr Bitmap;
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW", SetLastError = true)] private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool position, ref MenuItemInfo info);
    [DllImport("user32.dll", EntryPoint = "InsertMenuItemW", SetLastError = true)] private static extern bool InsertMenuItem(IntPtr menu, uint item, bool position, ref MenuItemInfo info);
    [DllImport("user32.dll", EntryPoint = "SetMenuItemInfoW", SetLastError = true)] private static extern bool SetMenuItemInfo(IntPtr menu, uint item, bool position, ref MenuItemInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveMenu(IntPtr menu, uint item, uint flags);
    public int LastError { get; private set; }
    private bool Result(bool result) { LastError = result ? 0 : Marshal.GetLastWin32Error(); return result; }

    public IEnumerable<IntPtr> Windows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => { if (windows.Count < 4096 && Eligible(window)) windows.Add(window); return true; }, IntPtr.Zero);
        return windows;
    }
    public IntPtr Foreground() => GetForegroundWindow();
    public bool Eligible(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window) || (GetWindowLong(window, -16) & 0x80000) == 0) return false;
        GetWindowThreadProcessId(window, out uint pid);
        return pid != (uint)Environment.ProcessId && (onlyProcess == 0 || pid == onlyProcess);
    }
    public uint Thread(IntPtr window) => GetWindowThreadProcessId(window, out _);
    public IntPtr MenuOwner(uint thread)
    {
        if (thread == 0) return IntPtr.Zero;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.MenuOwner : IntPtr.Zero;
    }
    public IntPtr Menu(IntPtr window) => GetSystemMenu(window, false);
    public IReadOnlyList<uint> CommandIds(IntPtr menu)
    {
        var result = new List<uint>();
        var visited = new HashSet<IntPtr>();
        Read(menu, 0); return result;
        void Read(IntPtr current, int depth)
        {
            if (current == IntPtr.Zero || depth > 5 || !visited.Add(current)) return;
            int count = Math.Clamp(GetMenuItemCount(current), 0, 256);
            for (int i = 0; i < count && result.Count < 2048; i++)
            { result.Add(GetMenuItemID(current, i)); Read(GetSubMenu(current, i), depth + 1); }
        }
    }
    public SystemMenuItem? Item(IntPtr menu, uint id)
    {
        var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 0x23 };
        return GetMenuItemInfo(menu, id, false, ref info)
            ? new SystemMenuItem(info.Id, info.Data, (info.State & 3) == 0, (info.State & 8) != 0) : null;
    }
    public bool Insert(IntPtr menu, uint id, UIntPtr marker, bool enabled, bool check)
    {
        var text = Marshal.StringToHGlobalUni(Label);
        try
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 0x63,
                Id = id, Data = marker, Text = text, State = (enabled ? 0u : 3u) | (check ? 8u : 0u) };
            bool hasClose = Item(menu, 0xF060).HasValue;
            return Result(InsertMenuItem(menu, hasClose ? 0xF060u : (uint)Math.Max(0, GetMenuItemCount(menu)), !hasClose, ref info));
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    public bool Update(IntPtr menu, uint id, bool enabled, bool check)
    {
        var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = 1,
            State = (enabled ? 0u : 3u) | (check ? 8u : 0u) };
        return Result(SetMenuItemInfo(menu, id, false, ref info));
    }
    public bool Remove(IntPtr menu, uint id) => Result(RemoveMenu(menu, id, 0));
}
