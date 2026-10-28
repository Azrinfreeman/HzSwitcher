using HzSwitcher.Services;
using HzSwitcher.Native;

namespace HzSwitcher.UI;

internal sealed class WindowPinMenu : IDisposable
{
    private readonly ContextMenuStrip _menu = new() { BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new ThemedMenuRenderer() };
    public bool IsOpen => _menu.Visible;
    public void Show(WindowIdentity target, Point point, AlwaysOnTopService pins, Func<bool> enabled,
        Action<PinResult> feedback, Action unpinAll, Action settings, bool frameMenu = false)
        => Build(target, point, pins, enabled, feedback, unpinAll, settings, frameMenu)?.Show(point);

    internal ContextMenuStrip Build(WindowIdentity target, Point point, AlwaysOnTopService pins, Func<bool> enabled,
        Action<PinResult> feedback, Action unpinAll, Action settings, bool frameMenu = false)
    {
        _menu.Close();
        var oldItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var item in oldItems) item.Dispose();
        if (!enabled()) return null;
        var state = pins.Inspect(target.Handle);
        if (state == null || state.Value.Identity != target) { feedback(new(PinStatus.Failed, target, "The original window has closed or changed.")); return null; }
        if (frameMenu)
        {
            foreach (var command in WindowFrameApi.ReadCommands(target.Handle))
                _menu.Items.Add(CreateWindowCommand(command, target, pins, feedback, enabled));
            if (_menu.Items.Count > 0) _menu.Items.Add(new ToolStripSeparator());
        }
        bool owned = pins.Owns(target);
        var toggle = new ToolStripMenuItem(frameMenu || !owned ? "Always on top" : "Unpin this window") { Checked = owned,
            Enabled = enabled() && (owned || !state.Value.Topmost) };
        toggle.Click += (_, _) => { if (enabled()) feedback(pins.Toggle(target)); };
        _menu.Items.Add(toggle);
        if (state.Value.Topmost && !owned) _menu.Items.Add(new ToolStripMenuItem("Already on top outside HzSwitcher") { Enabled = false });
        _menu.Items.Add("Unpin all", null, (_, _) => { if (enabled()) unpinAll(); }).Enabled = pins.Count > 0;
        _menu.Items.Add(new ToolStripSeparator());
        if (frameMenu)
            _menu.Items.Add("Original window menu", null, (_, _) =>
            {
                _menu.Close();
                // Defer until ToolStrip finishes releasing capture and restoring focus.
                _menu.BeginInvoke(() =>
                {
                    if (!_menu.IsDisposed) RunWindowAction(target, pins, feedback, enabled,
                        () => WindowFrameApi.ShowOriginalMenu(target.Handle));
                });
            });
        _menu.Items.Add("Always on top settings", null, (_, _) => { if (enabled()) settings(); });
        return enabled() ? _menu : null;
    }
    private static ToolStripItem CreateWindowCommand(WindowMenuCommand command, WindowIdentity target, AlwaysOnTopService pins, Action<PinResult> feedback, Func<bool> enabled)
    {
        if (command.Separator) return new ToolStripSeparator();
        var item = new ToolStripMenuItem(command.Text) { Enabled = command.Enabled && (command.Id != uint.MaxValue || command.Children.Length > 0), Checked = command.Checked };
        if (command.Children.Length > 0)
            foreach (var child in command.Children) item.DropDownItems.Add(CreateWindowCommand(child, target, pins, feedback, enabled));
        else
            item.Click += (_, _) => RunWindowAction(target, pins, feedback, enabled, () => WindowFrameApi.Execute(target.Handle, command.Id));
        return item;
    }
    private static void RunWindowAction(WindowIdentity target, AlwaysOnTopService pins, Action<PinResult> feedback, Func<bool> enabled, Func<bool> action)
    {
        if (!enabled()) return;
        if (pins.Inspect(target.Handle)?.Identity != target)
            feedback(new(PinStatus.Failed, target, "The original window has closed or changed."));
        else if (!action())
            feedback(new(PinStatus.Failed, target, "Windows could not send the command. The app may be protected or elevated."));
    }
    public void Close() => _menu.Close();
    public void Dispose() => _menu.Dispose();
}
