using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

internal sealed class AlwaysOnTopSettingsForm : DialogShell
{
    private readonly TrayApp _app;
    private readonly Toggle _outline = new() { Text = "", AccessibleName = "Show pinned window outlines" };
    private readonly Toggle _sound = new() { Text = "", AccessibleName = "Play pin and unpin sounds" };
    private readonly Toggle _mouse = new() { Text = "", AccessibleName = "Enable window frame and Shift right click menus" };
    private readonly NumberBox _thickness = new() { Minimum = 1, Maximum = 8, AccessibleName = "Outline thickness in logical pixels" };
    private readonly Label _status = MainForm.TextLabel("", muted: true);
    private readonly PillButton _unpin = new() { Text = "Unpin all" };

    public AlwaysOnTopSettingsForm(TrayApp app) : base("Always on top", new Size(580, 500))
    {
        _app = app;
        var settings = app.Settings;
        _outline.Checked = settings.AlwaysOnTopOutline;
        _sound.Checked = settings.AlwaysOnTopSound;
        _mouse.Checked = settings.AlwaysOnTopMouseMenu;
        _thickness.Value = settings.AlwaysOnTopOutlineThickness;
        var stack = new LayoutStack { Dock = DockStyle.Top };
        _status.Margin = new Padding(0, 0, 0, 20);
        stack.Controls.Add(_status);
        stack.Controls.Add(MainForm.Setting("Show pinned window outlines", _outline));
        stack.Controls.Add(MainForm.Setting("Outline thickness", _thickness, "1–8 logical pixels; scales with the monitor."));
        stack.Controls.Add(MainForm.Setting("Play pin and unpin sounds", _sound));
        stack.Controls.Add(MainForm.Setting("Enable mouse menus", _mouse));
        var instructions = MainForm.TextLabel("Right-click a standard title bar or window frame. For custom title bars, use Shift+right-click anywhere. You can also right-click a pinned outline.\n\nMouse menus and outlines pause in fullscreen apps and configured games. Keyboard shortcuts remain available.", muted: true, size: 9.5f);
        instructions.Margin = new Padding(0, 16, 0, 0);
        stack.Controls.Add(instructions);
        Body.Controls.Add(stack);
        var shortcut = new PillButton { Text = "Edit shortcuts…" };
        shortcut.Click += (_, _) => { _app.EditHotkeys(this); RefreshStatus(); };
        _unpin.Click += (_, _) => { _app.UnpinAll(); RefreshStatus(); };
        LeftActions.Controls.Add(shortcut);
        LeftActions.Controls.Add(_unpin);
        _outline.CheckedChanged += (_, _) => _thickness.Enabled = _outline.Checked;
        _thickness.Enabled = _outline.Checked;
        RefreshStatus();
        Finish();
    }

    internal void RefreshStatus()
    {
        _status.Text = _app.AlwaysOnTopStatus;
        _unpin.Enabled = _app.HasPinnedWindows;
    }

    protected override bool OnSave()
    {
        _app.Settings.AlwaysOnTopOutline = _outline.Checked;
        _app.Settings.AlwaysOnTopSound = _sound.Checked;
        _app.Settings.AlwaysOnTopMouseMenu = _mouse.Checked;
        _app.Settings.AlwaysOnTopOutlineThickness = _thickness.Value;
        _app.UpdatePinPreferences();
        return true;
    }
}
