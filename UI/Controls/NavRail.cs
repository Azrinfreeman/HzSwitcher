namespace HzSwitcher.UI.Controls;

/// <summary>Vertical page switcher: app name on top, icon + label rows, small footer text at the bottom.</summary>
internal sealed class NavRail : Control
{
    private readonly List<(string Text, string Glyph)> _items = new();
    private int _selected, _hover = -1;
    private static readonly string IconFont = PickIconFont();

    public event EventHandler SelectedIndexChanged;

    /// <summary>Small muted text at the bottom (for example the version).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Footer { get; set; } = "";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _items.Count || value == _selected) return;
            _selected = value;
            AccessibleName = $"Pages, {_items[_selected].Text} selected";
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NavRail()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Dock = DockStyle.Left;
        Width = 180;
        Cursor = Cursors.Default;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Pages";
    }

    public void Add(string text, string glyph)
    {
        _items.Add((text, glyph));
        if (_items.Count == 1) AccessibleName = $"Pages, {text} selected";
        Invalidate();
    }

    private static string PickIconFont()
    {
        var names = new HashSet<string>(FontFamily.Families.Select(f => f.Name));
        return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : names.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
    }

    private int RowTop => Theme.Dp(this, 68);
    private int RowHeight => Theme.Dp(this, 40);
    private int RowGap => Theme.Dp(this, 4);

    private Rectangle RowRect(int i) =>
        new(Theme.Dp(this, 8), RowTop + i * (RowHeight + RowGap), Width - Theme.Dp(this, 16) - 1, RowHeight);

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++) if (RowRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h >= 0) { Focus(); SelectedIndex = h; }
        base.OnMouseDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(_items.Count - 1, _selected + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        else base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);

        // app name
        using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
        {
            int x = Theme.Dp(this, 18), y = Theme.Dp(this, 20);
            TextRenderer.DrawText(g, "Hz", f, new Point(x, y), Theme.Accent, TextFormatFlags.NoPadding);
            int w = TextRenderer.MeasureText(g, "Hz", f, new Size(int.MaxValue, 0), TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, "Switcher", f, new Point(x + w, y), Theme.Text, TextFormatFlags.NoPadding);
        }

        using var labelFont = new Font("Segoe UI", 10.5f);
        using var iconFont = IconFont == null ? null : new Font(IconFont, 12f);
        for (int i = 0; i < _items.Count; i++)
        {
            var r = RowRect(i);
            bool sel = i == _selected;
            if (sel) Gfx.FillRound(g, r, Theme.Dp(this, 8), Theme.SurfaceRaised);
            else if (i == _hover) Gfx.FillRound(g, r, Theme.Dp(this, 8), Color.FromArgb(90, Theme.SurfaceRaised));
            if (sel)
            {
                float bh = Theme.Dp(this, 16), bw = Theme.Dp(this, 3);
                Gfx.FillRound(g, new RectangleF(r.Left + 2, r.Top + (r.Height - bh) / 2, bw, bh), bw / 2, Theme.Accent);
            }
            if (Focused && ShowFocusCues && sel)
                Gfx.StrokeRound(g, RectangleF.Inflate(r, -1, -1), Theme.Dp(this, 8), Theme.Text, 1.5f);

            int textX = r.Left + Theme.Dp(this, 16);
            if (iconFont != null)
            {
                var ir = new Rectangle(r.Left + Theme.Dp(this, 12), r.Top, Theme.Dp(this, 28), r.Height);
                TextRenderer.DrawText(g, _items[i].Glyph, iconFont, ir, sel ? Theme.Accent : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                textX = ir.Right + Theme.Dp(this, 6);
            }
            TextRenderer.DrawText(g, _items[i].Text, labelFont, new Rectangle(textX, r.Top, r.Right - textX, r.Height),
                sel ? Theme.Text : Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (!string.IsNullOrEmpty(Footer))
            using (var f = new Font("Segoe UI", 9f))
                TextRenderer.DrawText(g, Footer, f, new Rectangle(Theme.Dp(this, 18), Height - Theme.Dp(this, 36), Width - Theme.Dp(this, 30), Theme.Dp(this, 24)),
                    Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
