using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace HzSwitcher.UI;

/// <summary>Colors and native styling shared by every window. Always dark; the accent follows the Windows accent color.</summary>
internal static class Theme
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    /// <summary>Windows high-contrast mode: use system colors instead of the palette (checked at launch).</summary>
    public static readonly bool HighContrast = SystemInformation.HighContrast;

    // ----- fixed tokens -----
    public static readonly Color Bg = HighContrast ? SystemColors.Window : Color.FromArgb(0x16, 0x18, 0x1D);
    public static readonly Color Surface = HighContrast ? SystemColors.Window : Color.FromArgb(0x1F, 0x22, 0x29);
    public static readonly Color SurfaceRaised = HighContrast ? SystemColors.ButtonFace : Color.FromArgb(0x2A, 0x2E, 0x37);
    public static readonly Color Input = HighContrast ? SystemColors.Window : Color.FromArgb(0x16, 0x18, 0x1D);
    public static readonly Color Border = HighContrast ? SystemColors.WindowText : Color.FromArgb(0x34, 0x39, 0x45);
    public static readonly Color Track = HighContrast ? SystemColors.GrayText : Color.FromArgb(0x3A, 0x3F, 0x4B);
    public static readonly Color Text = HighContrast ? SystemColors.WindowText : Color.FromArgb(0xEC, 0xEE, 0xF3);
    public static readonly Color TextMuted = HighContrast ? SystemColors.GrayText : Color.FromArgb(0x9A, 0xA1, 0xB2);
    public static readonly Color Saver = HighContrast ? SystemColors.Highlight : Color.FromArgb(0x3F, 0xB9, 0x50);
    public static readonly Color Warn = HighContrast ? SystemColors.WindowText : Color.FromArgb(0xE3, 0xB3, 0x41);
    /// <summary>Fill for destructive buttons (white text on it).</summary>
    public static readonly Color Danger = HighContrast ? SystemColors.ButtonFace : Color.FromArgb(0xC0, 0x39, 0x2B);
    /// <summary>Danger color for text and icons on dark surfaces.</summary>
    public static readonly Color DangerText = HighContrast ? SystemColors.WindowText : Color.FromArgb(0xF8, 0x51, 0x49);

    // ----- accent: follows Windows, so it can change while the app runs -----
    private static readonly Color FallbackAccent = Color.FromArgb(0x4F, 0x8C, 0xFF);
    private static Color _accent = LoadAccent();

    public static Color Accent => _accent;
    /// <summary>White when it reads well on the accent (4.5:1), otherwise whichever of black and white reads better.</summary>
    public static Color AccentText => Contrast(_accent, Color.White) >= 4.5 || Contrast(_accent, Color.White) >= Contrast(_accent, Color.Black) ? Color.White : Color.Black;

    /// <summary>Raised (on a system thread) after the Windows accent color changed. Marshal to the UI thread before touching controls.</summary>
    public static event Action AccentChanged;

    /// <summary>Starts listening for Windows accent changes. Call once at startup.</summary>
    public static void Watch()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;
            var now = LoadAccent();
            if (now == _accent) return;
            _accent = now;
            AccentChanged?.Invoke();
        };
    }

    private static Color LoadAccent()
    {
        if (HighContrast) return SystemColors.Highlight;
        Color c = FallbackAccent;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)   // stored as 0xAABBGGRR
                c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
        catch { }
        // A very dark accent would vanish on the dark surfaces: lighten it until it has 3:1 contrast.
        for (int i = 0; i < 10 && Contrast(c, Surface) < 3.0; i++) c = Shift(c, 0.12f);
        return c;
    }

    private static double Luminance(Color c)
    {
        static double Lin(int v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>WCAG contrast ratio between two colors (1 to 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Asks Windows 11 for rounded corners and a themed border on a popup or window (ignored on older Windows).</summary>
    public static void RoundCorners(Control c)
    {
        if (HighContrast) return;
        void Apply()
        {
            int round = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(c.Handle, 33, ref round, sizeof(int));
            int border = Border.R | (Border.G << 8) | (Border.B << 16);   // COLORREF
            DwmSetWindowAttribute(c.Handle, 34, ref border, sizeof(int));
        }
        if (c.IsHandleCreated) Apply(); else c.HandleCreated += (_, _) => Apply();
        c.HandleCreated += (_, _) => Apply();   // popups can recreate their handle
    }

    /// <summary>Scales a 96-dpi pixel size for the control's current monitor.</summary>
    public static int Dp(Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96.0);

    /// <summary>Applies colors, the dark title bar and native dark controls to a form and everything on it.</summary>
    public static void Apply(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        if (form.IsHandleCreated) TitleBar(form); else form.HandleCreated += (_, _) => TitleBar(form);
        Style(form);
    }

    private static void TitleBar(Form form)
    {
        int on = 1;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on older Windows 10 builds)
        if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
    }

    /// <summary>Styles a control and its children. Safe to call again for controls added later.</summary>
    public static void Style(Control control)
    {
        switch (control)
        {
            case Button b when !(b.Tag is "styled"):
                StyleButton(b, b.Tag is "primary" ? Accent : SurfaceRaised);
                break;
            case ComboBox cb:
                cb.BackColor = Input; cb.ForeColor = Text;
                Native(cb, "DarkMode_CFD");
                break;
            case TrackBar tb:
                tb.BackColor = Surface;
                break;
            case NumericUpDown n:
                n.BackColor = Input; n.ForeColor = Text;
                Native(n, "DarkMode_Explorer");
                break;
            case TextBox t:
                t.BackColor = Input; t.ForeColor = Text;
                Native(t, "DarkMode_CFD");
                break;
            case FlowLayoutPanel or TableLayoutPanel or Panel:
                if (((ScrollableControl)control).AutoScroll) Native(control, "DarkMode_Explorer");
                break;
        }
        foreach (Control child in control.Controls) Style(child);
    }

    public static void StyleButton(Button b, Color back)
    {
        b.Tag = "styled";
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = back;
        b.ForeColor = back == Accent ? AccentText : back == Danger ? Color.White : Text;
        b.FlatAppearance.MouseOverBackColor = Shift(back, 0.12f);
        b.FlatAppearance.MouseDownBackColor = Shift(back, -0.12f);
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
    }

    public static Color Shift(Color c, float amount)
    {
        int Ch(int v) => Math.Clamp((int)(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount)), 0, 255);
        return Color.FromArgb(Ch(c.R), Ch(c.G), Ch(c.B));
    }

    private static void Native(Control c, string theme)
    {
        if (HighContrast) return;
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, theme, null);
        else c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, theme, null);
    }
}
