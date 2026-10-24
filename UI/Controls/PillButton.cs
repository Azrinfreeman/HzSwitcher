namespace HzSwitcher.UI.Controls;

internal enum PillStyle { Neutral, Primary, Danger }

/// <summary>Rounded button. Neutral by default; Selected (or Primary) uses the accent color.</summary>
internal sealed class PillButton : Button
{
    private bool _hover, _down, _selected;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PillStyle Style { get; set; } = PillStyle.Neutral;

    /// <summary>Shows the button as the current choice (filled with the accent).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); } }

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Tag = "styled";   // Theme.Style leaves it alone
        AutoSize = true;
        Padding = new Padding(12, 7, 12, 7);
        Margin = new Padding(0, 0, 8, 8);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    /// <summary>Text size plus padding, measured here so the size is right at any DPI (the stock Button measures differently).</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, 0), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        return new Size(Math.Max(t.Width + Padding.Horizontal, Theme.Dp(this, 34)), t.Height + Padding.Vertical);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    private static bool IsArrow(Keys k) => (k & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down;

    protected override bool IsInputKey(Keys keyData) => (Parent is ChipGroup && IsArrow(keyData)) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Parent is ChipGroup group && IsArrow(e.KeyData))
        {
            group.MoveFocus(this, e.KeyCode is Keys.Left or Keys.Up ? -1 : 1);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Color fill = Style == PillStyle.Danger ? Theme.Danger
                   : Style == PillStyle.Primary || _selected ? Theme.Accent
                   : Theme.SurfaceRaised;
        Color text = Style == PillStyle.Danger ? Color.White
                   : Style == PillStyle.Primary || _selected ? Theme.AccentText
                   : Theme.Text;
        if (!Enabled) { fill = Theme.SurfaceRaised; text = Theme.TextMuted; }
        else if (_down) fill = Theme.Shift(fill, -0.12f);
        else if (_hover) fill = Theme.Shift(fill, 0.12f);

        float radius = Theme.Dp(this, 8);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, r, radius, fill);
        if (Focused && ShowFocusCues)
            Gfx.StrokeRound(g, RectangleF.Inflate(r, -1.5f, -1.5f), radius - 1, Theme.Text, 2f);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
