namespace HzSwitcher.UI.Controls;

/// <summary>A switch with a text label. Behaves like a CheckBox (Checked, CheckedChanged, Space to toggle).</summary>
internal sealed class Toggle : CheckBox
{
    private bool _hover;

    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
    }

    private Size SwitchSize => new(Theme.Dp(this, 36), Theme.Dp(this, 20));
    private int Gap => Theme.Dp(this, 10);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, 0), TextFormatFlags.NoPrefix);
        var sw = SwitchSize;
        int w = sw.Width + (text.Width > 0 ? Gap + text.Width : 0) + 2;
        int h = Math.Max(sw.Height + Theme.Dp(this, 6), text.Height + 2);
        return new Size(w, h);
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); PerformLayout(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var sz = SwitchSize;
        var sw = new RectangleF(1, (Height - sz.Height) / 2f, sz.Width - 2, sz.Height);
        Color track = !Enabled ? Theme.Track : Checked ? Theme.Accent : (_hover ? Theme.Shift(Theme.Track, 0.12f) : Theme.Track);
        Gfx.FillRound(g, sw, sw.Height / 2, track);
        if (!Checked) Gfx.StrokeRound(g, sw, sw.Height / 2, Theme.TextMuted, 1f);

        float pad = Theme.Dp(this, 4), d = sw.Height - 2 * pad;
        float tx = Checked ? sw.Right - pad - d : sw.Left + pad;
        using (var b = new SolidBrush(!Enabled ? Theme.TextMuted : Checked ? Theme.AccentText : Theme.TextMuted))
            g.FillEllipse(b, tx, sw.Top + pad, d, d);

        if (Focused && ShowFocusCues)
            Gfx.StrokeRound(g, RectangleF.Inflate(sw, 2.5f, 2.5f), sw.Height / 2 + 2, Theme.Text, 1.5f);

        var textRect = new Rectangle(sz.Width + Gap, 0, Math.Max(0, Width - sz.Width - Gap), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
