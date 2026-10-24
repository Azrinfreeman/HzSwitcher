namespace HzSwitcher.UI.Controls;

/// <summary>Small pill with a colored dot and text, so state is never conveyed by color alone.</summary>
internal sealed class StatusChip : Control
{
    private readonly Color _tone;

    public StatusChip(string text, Color tone)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _tone = tone;
        Text = text;
        AutoSize = true;
        Margin = new Padding(0, 0, 8, 6);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        AccessibleRole = AccessibleRole.StaticText;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, 0), TextFormatFlags.NoPrefix);
        return new Size(Theme.Dp(this, 30) + t.Width, t.Height + Theme.Dp(this, 8));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, r, r.Height / 2, Color.FromArgb(40, _tone));
        float d = Theme.Dp(this, 8);
        using (var b = new SolidBrush(_tone)) g.FillEllipse(b, Theme.Dp(this, 10), (Height - d) / 2, d, d);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.Dp(this, 24), 0, Width - Theme.Dp(this, 28), Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
