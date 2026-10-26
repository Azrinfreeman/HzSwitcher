using System.Runtime.InteropServices;

namespace HzSwitcher.UI;

/// <summary>Message-only window that owns the global hotkeys.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly List<int> _ids = new();
    public event Action<int> HotkeyPressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public void UnregisterAll()
    {
        foreach (int id in _ids) UnregisterHotKey(Handle, id);
        _ids.Clear();
    }

    /// <summary>Returns false if the spec is invalid or the combination is already taken.</summary>
    public bool Register(int id, string spec)
    {
        if (!TryParse(spec, out uint mods, out uint vk)) return false;
        if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk)) return false;
        _ids.Add(id);
        return true;
    }

    internal static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        foreach (var raw in spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
                case "win": mods |= MOD_WIN; continue;
            }
            string name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
            if (vk != 0 || !Enum.TryParse<Keys>(name, true, out var key) || !Enum.IsDefined(key)
                || key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
                || (int)key > 255) return false;
            vk = (uint)key;
        }
        return mods != 0 && vk != 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in _ids) UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}
