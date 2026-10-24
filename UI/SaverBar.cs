namespace HzSwitcher.UI;

/// <summary>Thin rounded progress bar used for the idle countdown and the restore window.</summary>
internal sealed class SaverBar : Control
{
    private double _value;
    private Color _fill = Theme.Accent;

    public SaverBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    public void Set(double value, Color fill)
    {
        value = Math.Clamp(value, 0, 1);
        if (Math.Abs(value - _value) < 0.002 && fill == _fill) return;
        _value = value;
        _fill = fill;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new RectangleF(0, 0, Width, Height);
        float radius = r.Height / 2;
        HzSwitcher.UI.Controls.Gfx.FillRound(g, r, radius, Theme.Track);
        float w = r.Width * (float)_value;
        if (w > 0) HzSwitcher.UI.Controls.Gfx.FillRound(g, new RectangleF(0, 0, Math.Max(w, r.Height), r.Height), radius, _fill);
    }
}
