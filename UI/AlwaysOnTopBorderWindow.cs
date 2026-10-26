using HzSwitcher.Native;
using HzSwitcher.Services;

namespace HzSwitcher.UI;

// A real hollow region: the center has no HWND surface to intercept another app's clicks.
internal sealed class AlwaysOnTopBorderWindow : Form
{
    private readonly WindowIdentity _target;
    private readonly Action<WindowIdentity, Point> _menu;
    private Rectangle _lastBounds;
    private int _lastThickness;
    public AlwaysOnTopBorderWindow(WindowIdentity target, Action<WindowIdentity, Point> menu)
    {
        _target = target; _menu = menu;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Accent;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) _menu(_target, PointToScreen(e.Location));
    }
    public void UpdateOutline(Rectangle frame, int dpi, int logicalThickness)
    {
        int thickness = Math.Max(1, (int)Math.Round(logicalThickness * dpi / 96.0));
        var bounds = WindowGeometry.OutlineBounds(frame, Screen.FromRectangle(frame).Bounds, thickness, WindowGeometry.IsZoomed(_target.Handle));
        if (bounds.Width <= thickness * 2 || bounds.Height <= thickness * 2) { Hide(); return; }
        if (_lastBounds != bounds || _lastThickness != thickness)
        {
            Bounds = bounds;
            var region = new Region(new Rectangle(Point.Empty, bounds.Size));
            region.Exclude(new Rectangle(thickness, thickness, bounds.Width - 2 * thickness, bounds.Height - 2 * thickness));
            var old = Region; Region = region; old?.Dispose();
            _lastBounds = bounds; _lastThickness = thickness;
        }
        BackColor = Theme.Accent;
        if (!Visible) Show();
        // Place immediately above the target, below every unrelated window already above it.
        var previous = WindowGeometry.GetWindow(_target.Handle, 3); // GW_HWNDPREV
        if (previous != Handle && !WindowGeometry.SetWindowPos(Handle, previous == IntPtr.Zero ? new IntPtr(-1) : previous, 0, 0, 0, 0, 0x13))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
}
