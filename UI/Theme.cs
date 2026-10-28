using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace HzSwitcher.UI;

/// <summary>Colors and native styling shared by every window. Always dark; the accent follows the Windows accent color.</summary>
internal static class Theme
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    /// <summary>Windows high-contrast mode; read live so accessibility changes apply to open windows.</summary>
    public static bool HighContrast => SystemInformation.HighContrast;

    // ----- fixed tokens -----
    public static Color Bg => HighContrast ? SystemColors.Window : Color.FromArgb(0x20, 0x21, 0x24);
    public static Color Surface => HighContrast ? SystemColors.Window : Color.FromArgb(0x29, 0x2B, 0x2F);
    public static Color SurfaceRaised => HighContrast ? SystemColors.ButtonFace : Color.FromArgb(0x35, 0x38, 0x3D);
    public static Color Input => HighContrast ? SystemColors.Window : SurfaceRaised;
    public static Color Border => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x50, 0x55, 0x5D);
    public static Color InputBorder => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x85, 0x8C, 0x96);
    public static Color Track => HighContrast ? SystemColors.GrayText : Color.FromArgb(0x72, 0x79, 0x83);
    public static Color Text => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xF1, 0xF3, 0xF4);
    public static Color TextMuted => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xB5, 0xBA, 0xC1);
    public static Color Saver => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x72, 0xD4, 0x91);
    public static Color Warn => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xE3, 0xB3, 0x41);
    /// <summary>Fill for destructive buttons (white text on it).</summary>
    public static Color Danger => HighContrast ? SystemColors.ButtonFace : Color.FromArgb(0xC0, 0x39, 0x2B);
    /// <summary>Danger color for text and icons on dark surfaces.</summary>
    public static Color DangerText => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xFF, 0x9A, 0x94);

    public static readonly string FontFamilyName = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text")
        ? "Segoe UI Variable Text" : "Segoe UI";
    public static Font Font(float points = 10.5f, FontStyle style = FontStyle.Regular) => new(FontFamilyName, points, style);
    public static Color ReadableText(Color fill) => Contrast(fill, Color.White) >= Contrast(fill, Color.Black) ? Color.White : Color.Black;

    // ----- accent: follows Windows, so it can change while the app runs -----
    private static readonly Color FallbackAccent = Color.FromArgb(0x4F, 0x8C, 0xFF);
    private static Color _accent = LoadAccent();

    public static Color Accent => _accent;
    /// <summary>White when it reads well on the accent (4.5:1), otherwise whichever of black and white reads better.</summary>
    public static Color AccentText => HighContrast ? SystemColors.HighlightText : ReadableText(_accent);

    /// <summary>Raised (on a system thread) after the Windows accent color changed. Marshal to the UI thread before touching controls.</summary>
    public static event Action AccentChanged;

    /// <summary>Starts listening for Windows accent changes. Call once at startup.</summary>
    public static void Watch()
    {
        bool highContrast = HighContrast;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            var now = LoadAccent();
            bool changed = highContrast != HighContrast || e.Category == UserPreferenceCategory.Color;
            highContrast = HighContrast;
            if (now == _accent && !changed) return;
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
        void Apply()
        {
            int round = HighContrast ? 1 : 2;   // DWMWCP_DONOTROUND / ROUND
            DwmSetWindowAttribute(c.Handle, 33, ref round, sizeof(int));
            int border = Border.R | (Border.G << 8) | (Border.B << 16);   // COLORREF
            DwmSetWindowAttribute(c.Handle, 34, ref border, sizeof(int));
        }
        if (c.IsHandleCreated) Apply();
        if (!CornerWatches.TryGetValue(c, out _))
        {
            CornerWatches.Add(c, new object());
            c.HandleCreated += (_, _) => Apply();   // popups can recreate their handle
        }
    }

    private static readonly ConditionalWeakTable<Control, object> CornerWatches = new();

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
        int on = HighContrast ? 0 : 1;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on older Windows 10 builds)
        if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
    }

    /// <summary>Styles a control and its children. Safe to call again for controls added later.</summary>
    public static void Style(Control control)
    {
        switch (control)
        {
            case Label l:
                l.ForeColor = l.Tag is "muted" ? TextMuted : l.Tag is "warning" ? Warn : l.Tag is "error" ? DangerText : Text;
                if (l.Tag is "keycap") l.BackColor = SurfaceRaised;
                break;
            case CheckBox c:
                c.ForeColor = Text;
                break;
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
                control.BackColor = control.Tag is "surface" ? Surface : control.Parent?.BackColor ?? Bg;
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
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, HighContrast ? null : theme, null);
        else if (!(c.Tag is "native-themed"))
        {
            c.Tag = "native-themed";
            c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, HighContrast ? null : theme, null);
        }
    }

    public static void FitWorkingArea(Form form)
    {
        var area = Screen.FromControl(form).WorkingArea;
        var bounds = form.Bounds;
        bounds.Size = new Size(Math.Min(bounds.Width, area.Width), Math.Min(bounds.Height, area.Height));
        bounds.X = Math.Clamp(bounds.X, area.Left, area.Right - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y, area.Top, area.Bottom - bounds.Height);
        form.Bounds = bounds;
    }
}
