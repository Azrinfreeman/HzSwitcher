using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Owned editor with a scrolling body and a reachable Save/Cancel footer.</summary>
internal abstract class DialogShell : Form
{
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16), AutoScroll = true };
    protected readonly FlowLayoutPanel LeftActions = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
    protected readonly PillButton SaveButton = new() { Text = "Save", Style = PillStyle.Primary, Margin = new Padding(8, 0, 0, 0) };
    protected readonly PillButton CancelButtonControl = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
    private readonly Label _error = MainForm.TextLabel("");
    private readonly ErrorProvider _errors;

    protected DialogShell(string title, Size clientSize96, string intro = null, bool readOnly = false)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.Font();
        ClientSize = clientSize96;
        _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(SaveButton);
        buttons.Controls.Add(CancelButtonControl);
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(24, 12, 24, 12), Tag = "surface" };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(LeftActions);
        bar.Controls.Add(buttons);
        _error.Tag = "error";
        _error.Dock = DockStyle.Bottom;
        _error.Padding = new Padding(24, 8, 24, 8);
        _error.Visible = false;
        _error.AccessibleRole = AccessibleRole.Alert;
        var head = new Label { Text = intro ?? "", AutoSize = true, Dock = DockStyle.Top,
            ForeColor = Theme.TextMuted, Tag = "muted", Padding = new Padding(24, 20, 24, 0),
            MaximumSize = new Size(clientSize96.Width, 0), Visible = !string.IsNullOrEmpty(intro) };
        ClientSizeChanged += (_, _) =>
        {
            head.MaximumSize = new Size(Math.Max(1, ClientSize.Width), 0);
            _error.MaximumSize = new Size(Math.Max(1, ClientSize.Width), 0);
        };
        SaveButton.Click += (_, _) =>
        {
            ClearError();
            if (OnSave()) DialogResult = DialogResult.OK;
        };
        SaveButton.Visible = !readOnly;
        if (readOnly) CancelButtonControl.Text = "Close";
        AcceptButton = readOnly ? CancelButtonControl : SaveButton;
        CancelButton = CancelButtonControl;
        Controls.Add(Body);
        Controls.Add(head);
        Controls.Add(_error);
        Controls.Add(bar);
        Body.TabIndex = 0;
        bar.TabIndex = 1;
        Shown += (_, _) => Theme.FitWorkingArea(this);
        DpiChanged += (_, _) => BeginInvoke(() => Theme.FitWorkingArea(this));
    }

    protected bool ShowError(Control input, string message)
    {
        _errors.SetError(input, message);
        _error.Text = message;
        _error.Visible = true;
        input.Focus();
        _error.AccessibilityObject.RaiseLiveRegionChanged();
        return false;
    }

    protected void ClearError()
    {
        _errors.Clear();
        _error.Visible = false;
    }

    protected abstract bool OnSave();

    protected void Finish()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Theme.RoundCorners(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _errors.Dispose();
        base.Dispose(disposing);
    }
}
