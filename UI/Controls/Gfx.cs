using System.Drawing.Drawing2D;

namespace HzSwitcher.UI.Controls;

internal static class Gfx
{
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill)
    {
        using var path = RoundRect(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
    }

    public static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
    {
        using var path = RoundRect(r, radius);
        using var p = new Pen(color, width);
        g.DrawPath(p, path);
    }
}
