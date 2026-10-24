using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>
/// Shared look for the editing dialogs: dark window, short intro text, a body area and a Save / Cancel bar.
/// A derived dialog adds its controls to <see cref="Body"/>, then calls <see cref="Finish"/> as the last step of its constructor.
/// </summary>
internal abstract class DialogShell : Form
{
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, Padding = new Padding(20, 4, 20, 8) };
    /// <summary>Extra buttons on the left of the bar (for example "Add").</summary>
    protected readonly FlowLayoutPanel LeftActions = new() { Dock = DockStyle.Left, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(16, 12, 0, 0) };
    protected readonly PillButton SaveButton = new() { Text = "Save", Style = PillStyle.Primary, AutoSize = false, Size = new Size(96, 36), Margin = new Padding(0, 0, 8, 0) };
    protected readonly PillButton CancelButtonControl = new() { Text = "Cancel", AutoSize = false, Size = new Size(96, 36), DialogResult = DialogResult.Cancel, Margin = new Padding(0) };

    protected DialogShell(string title, Size clientSize96, string intro)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = clientSize96;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 12, 16, 0) };
        buttons.Controls.Add(CancelButtonControl);
        buttons.Controls.Add(SaveButton);
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Surface };
        bar.Controls.Add(buttons);
        bar.Controls.Add(LeftActions);
        bar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });   // top border

        var head = new Label
        {
            Text = intro, AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.TextMuted,
            MaximumSize = new Size(clientSize96.Width, 0), Padding = new Padding(20, 16, 20, 8),
        };

        SaveButton.Click += (_, _) => { if (OnSave()) DialogResult = DialogResult.OK; };
        AcceptButton = SaveButton;
        CancelButton = CancelButtonControl;

        Controls.Add(Body);   // fill first, edge-docked controls after
        Controls.Add(head);
        Controls.Add(bar);
    }

    /// <summary>Validate and store the edits. Return false to keep the dialog open.</summary>
    protected abstract bool OnSave();

    /// <summary>Call last in the derived constructor: scales for the display and applies the theme to everything added.</summary>
    protected void Finish()
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Theme.RoundCorners(this);
        BackColor = Theme.Bg;
    }
}
