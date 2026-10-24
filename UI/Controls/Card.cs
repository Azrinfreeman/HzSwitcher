namespace HzSwitcher.UI.Controls;

/// <summary>A rounded, bordered surface that stacks its children top to bottom.</summary>
internal sealed class Card : FlowLayoutPanel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float radius = Theme.Dp(this, 8);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, r, radius, Theme.Surface);
        Gfx.StrokeRound(g, r, radius, Theme.Border, 1f);
    }
}
