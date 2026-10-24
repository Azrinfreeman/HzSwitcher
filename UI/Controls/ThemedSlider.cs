namespace HzSwitcher.UI.Controls;

/// <summary>Horizontal slider with an accent fill and round thumb. Same shape of API as TrackBar.</summary>
internal sealed class ThemedSlider : Control
{
    private int _min, _max = 100, _value;
    private bool _hover, _drag;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SmallChange { get; set; } = 5;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int LargeChange { get; set; } = 10;

    /// <summary>Raised when the user (mouse, wheel or keyboard) changes the value.</summary>
    public event EventHandler Scroll;
    public event EventHandler ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Minimum { get => _min; set { _min = value; if (_max < _min) _max = _min; Value = _value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => _max; set { _max = value; if (_min > _max) _min = _max; Value = _value; Invalidate(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ThemedSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Height = 32;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

    private sealed class SliderAccessible : ControlAccessibleObject
    {
        private readonly ThemedSlider _owner;
        public SliderAccessible(ThemedSlider owner) : base(owner) => _owner = owner;
        public override string Value
        {
            get => _owner.Value.ToString();
            set { if (int.TryParse(value, out int v)) _owner.Value = v; }
        }
    }

    private float Pad => Theme.Dp(this, 11);

    private float ThumbX => Pad + (_max == _min ? 0 : (float)(_value - _min) / (_max - _min) * Math.Max(1, Width - 2 * Pad));

    private void SetFromX(int x)
    {
        double f = Math.Clamp((x - Pad) / Math.Max(1, Width - 2 * Pad), 0, 1);
        int v = _min + (int)Math.Round(f * (_max - _min));
        if (v == _value) return;
        Value = v;
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    private void Step(int delta)
    {
        int before = _value;
        Value = _value + delta;
        if (_value != before) Scroll?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _drag = true; Capture = true; Focus(); SetFromX(e.X); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Step(e.Delta > 0 ? SmallChange : -SmallChange); base.OnMouseWheel(e); }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Step(-SmallChange); break;
            case Keys.Right or Keys.Up: Step(SmallChange); break;
            case Keys.PageDown: Step(-LargeChange); break;
            case Keys.PageUp: Step(LargeChange); break;
            case Keys.Home: Step(_min - _value); break;
            case Keys.End: Step(_max - _value); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float cy = Height / 2f;
        float th = Theme.Dp(this, 6);
        float x = ThumbX;
        var track = new RectangleF(Pad, cy - th / 2, Width - 2 * Pad, th);
        Color fill = Enabled ? Theme.Accent : Theme.TextMuted;
        Gfx.FillRound(g, track, th / 2, Theme.Track);
        if (x > Pad) Gfx.FillRound(g, new RectangleF(Pad, track.Top, x - Pad, th), th / 2, fill);

        float r = Theme.Dp(this, _drag || _hover ? 9 : 8);
        var thumb = new RectangleF(x - r, cy - r, 2 * r, 2 * r);
        using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.TextMuted)) g.FillEllipse(b, thumb);
        using (var p = new Pen(fill, 2f)) g.DrawEllipse(p, thumb);
        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Theme.Text, 1.5f);
            g.DrawEllipse(p, RectangleF.Inflate(thumb, 3, 3));
        }
    }
}
