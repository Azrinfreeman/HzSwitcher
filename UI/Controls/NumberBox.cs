namespace HzSwitcher.UI.Controls;

/// <summary>Whole-number input with − and + buttons. Type digits, use Up/Down or the wheel, or click the buttons.</summary>
internal sealed class NumberBox : Control
{
    private int _min, _max = 100, _value;
    private string _edit;       // digits typed but not yet committed
    private int _hover;         // 0 none, 1 minus, 2 plus

    public event EventHandler ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get => _min; set { _min = value; if (_max < _min) _max = _min; SetValue(_value); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => _max; set { _max = value; if (_min > _max) _min = _max; SetValue(_value); } }

    /// <summary>The committed value, or the clamped number being typed.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _edit != null && int.TryParse(_edit, out int typed) ? Math.Clamp(typed, _min, _max) : _value;
        set { _edit = null; SetValue(value); }
    }

    public NumberBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(112, 36);
        Cursor = Cursors.IBeam;
        AccessibleRole = AccessibleRole.SpinButton;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new NumberAccessible(this);

    private sealed class NumberAccessible : ControlAccessibleObject
    {
        private readonly NumberBox _owner;
        public NumberAccessible(NumberBox owner) : base(owner) => _owner = owner;
        public override string Value
        {
            get => _owner.Value.ToString();
            set { if (_owner.Enabled && int.TryParse(value, out int v)) _owner.Value = v; }
        }
    }

    private void SetValue(int v)
    {
        v = Math.Clamp(v, _min, _max);
        Invalidate();
        if (v == _value) return;
        _value = v;
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Commit()
    {
        if (_edit == null) return;
        int v = int.TryParse(_edit, out int typed) ? typed : _value;
        _edit = null;
        SetValue(v);
        Invalidate();
    }

    private void Step(int delta)
    {
        int basis = Value;
        _edit = null;
        SetValue(basis + delta);
    }

    private Rectangle MinusRect => new(Width - 2 * Height + Theme.Dp(this, 2), Theme.Dp(this, 3), Height - Theme.Dp(this, 4), Height - Theme.Dp(this, 6));
    private Rectangle PlusRect => new(Width - Height + Theme.Dp(this, 1), Theme.Dp(this, 3), Height - Theme.Dp(this, 4), Height - Theme.Dp(this, 6));

    private int HitTest(Point p) => MinusRect.Contains(p) ? 1 : PlusRect.Contains(p) ? 2 : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Cursor = h != 0 ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = 0; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int h = HitTest(e.Location);
        if (e.Button == MouseButtons.Left)
        { if (h == 1) Step(-1); else if (h == 2) Step(1); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Commit(); Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: Step(1); break;
            case Keys.Down: Step(-1); break;
            case Keys.PageUp: Step(10); break;
            case Keys.PageDown: Step(-10); break;
            case Keys.Escape when _edit != null: _edit = null; Invalidate(); break;
            case Keys.Enter: Commit(); return;   // not handled: a dialog's default button still fires
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsDigit(e.KeyChar))
        {
            string next = (_edit ?? "") + e.KeyChar;
            _edit = next.Length > 4 ? next[^4..] : next;
            Invalidate();
            e.Handled = true;
        }
        else if (e.KeyChar == '\b' && _edit != null)
        {
            _edit = _edit.Length > 1 ? _edit[..^1] : null;
            Invalidate();
            e.Handled = true;
        }
        base.OnKeyPress(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float radius = Theme.Dp(this, 4);
        var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Gfx.FillRound(g, box, radius, Theme.Input);
        Gfx.StrokeRound(g, box, radius, Focused ? Theme.Accent : Theme.InputBorder, Focused ? 1.5f : 1f);

        string text = _edit ?? _value.ToString();
        var textRect = new Rectangle(Theme.Dp(this, 10), 0, Width - 2 * Height, Height);
        TextRenderer.DrawText(g, text, Font, textRect, !Enabled ? Theme.TextMuted : _edit != null ? Theme.Accent : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        DrawStepper(g, MinusRect, false, _hover == 1, Value > _min);
        DrawStepper(g, PlusRect, true, _hover == 2, Value < _max);
    }

    private void DrawStepper(Graphics g, Rectangle r, bool plus, bool hover, bool available)
    {
        bool on = Enabled && available;
        if (hover && on) Gfx.FillRound(g, r, Theme.Dp(this, 5), Theme.SurfaceRaised);
        using var pen = new Pen(on ? Theme.Text : Theme.Track, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, a = Theme.Dp(this, 4);
        g.DrawLine(pen, cx - a, cy, cx + a, cy);
        if (plus) g.DrawLine(pen, cx, cy - a, cx, cy + a);
    }
}
