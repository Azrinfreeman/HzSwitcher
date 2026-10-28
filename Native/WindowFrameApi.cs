using System.Runtime.InteropServices;
using System.Text;

namespace HzSwitcher.Native;

internal readonly record struct WindowMenuCommand(string Text, uint Id, bool Enabled, bool Checked,
    bool Separator, WindowMenuCommand[] Children);

internal static class WindowFrameApi
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);

    // Geometry queries only. Never synchronously send messages to another app from the mouse hook.
    public static bool IsFrameCandidate(IntPtr hwnd, Point point)
    {
        if (!GetWindowRect(hwnd, out var window) || !GetClientRect(hwnd, out var client)) return false;
        var origin = new Point(client.Left, client.Top);
        if (!ClientToScreen(hwnd, ref origin)) return false;
        var clientBounds = new Rectangle(origin, new Size(client.Right - client.Left, client.Bottom - client.Top));
        return IsOutsideClient(Rectangle.FromLTRB(window.Left, window.Top, window.Right, window.Bottom), clientBounds, point);
    }
    internal static bool IsOutsideClient(Rectangle window, Rectangle client, Point point) => window.Contains(point) && !client.Contains(point);
    internal static bool IsFrameHit(int hit) => hit is 2 or 3 or >= 10 and <= 18;
    internal static IntPtr PackPoint(Point point) => new(unchecked((int)(((uint)(ushort)point.Y << 16) | (ushort)point.X)));
    public static int? HitTest(IntPtr hwnd, Point point)
    {
        // Runs on the UI thread after the hook returns, with a bound for hung/elevated applications.
        return SendMessageTimeout(hwnd, 0x84, IntPtr.Zero, PackPoint(point), 0x22, 50, out var result) != IntPtr.Zero
            ? unchecked((int)result.ToUInt64()) : null;
    }
    public static void ReplayFrameClick(IntPtr hwnd, Point point, int? hit)
    {
        if (!hit.HasValue) { ShowOriginalMenu(hwnd); return; }
        PostMessage(hwnd, 0xA4, new IntPtr(hit.Value), PackPoint(point));
        PostMessage(hwnd, 0xA5, new IntPtr(hit.Value), PackPoint(point));
    }
    public static bool ShowOriginalMenu(IntPtr hwnd)
    {
        // WM_CONTEXTMENU is ignored by the default procedure outside the caption (e.g. resize edges).
        // Ask for the system menu directly, after the HzSwitcher popup has released menu mode.
        SetForegroundWindow(hwnd);
        return PostMessage(hwnd, 0x112, new IntPtr(0xF100), new IntPtr(' ')); // SC_KEYMENU: native Alt+Space menu
    }
    public static bool Execute(IntPtr hwnd, uint command) => PostMessage(hwnd, 0x112, new IntPtr(command), IntPtr.Zero);
    public static WindowMenuCommand[] ReadCommands(IntPtr hwnd)
    {
        var menu = GetSystemMenu(hwnd, false);
        if (menu == IntPtr.Zero) return [];
        // Let the owner refresh disabled/check states, without changing its menu or installing hooks in it.
        if (SendMessageTimeout(hwnd, 0x116, menu, IntPtr.Zero, 0x22, 50, out _) == IntPtr.Zero ||
            SendMessageTimeout(hwnd, 0x117, menu, new IntPtr(1 << 16), 0x22, 50, out _) == IntPtr.Zero) return [];
        return ReadItems(menu, 0);
    }
    private static WindowMenuCommand[] ReadItems(IntPtr menu, int depth)
    {
        if (depth > 5) return [];
        var items = new List<WindowMenuCommand>();
        int count = Math.Min(GetMenuItemCount(menu), 100);
        for (uint position = 0; position < count; position++)
        {
            uint state = GetMenuState(menu, position, 0x400);
            if (state == uint.MaxValue) continue;
            var text = new StringBuilder(512);
            GetMenuString(menu, position, text, text.Capacity, 0x400);
            var submenu = GetSubMenu(menu, (int)position);
            bool separator = submenu == IntPtr.Zero && (state & 0x800) != 0;
            // Owner-drawn/bitmap-only entries stay available through "Original window menu".
            if (!separator && text.Length == 0) continue;
            items.Add(new(text.ToString(), GetMenuItemID(menu, (int)position), (state & 3) == 0, (state & 8) != 0,
                separator, submenu == IntPtr.Zero ? [] : ReadItems(submenu, depth + 1)));
        }
        return items.ToArray();
    }
}
