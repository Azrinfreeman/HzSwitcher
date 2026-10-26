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
        var state = pins.Inspect(target.Handle);
        if (state == null || state.Value.Identity != target) { feedback(new(PinStatus.Failed, target, "The original window has closed or changed.")); return null; }
        if (frameMenu)
        {
            foreach (var command in WindowFrameApi.ReadCommands(target.Handle))
                _menu.Items.Add(CreateWindowCommand(command, target, pins, feedback));
            if (_menu.Items.Count > 0) _menu.Items.Add(new ToolStripSeparator());
        }
        bool owned = pins.Owns(target);
        var toggle = new ToolStripMenuItem(frameMenu || !owned ? "Always on top" : "Unpin this window") { Checked = owned,
            Enabled = enabled() && (owned || !state.Value.Topmost) };
        toggle.Click += (_, _) => { if (enabled()) feedback(pins.Toggle(target)); };
        _menu.Items.Add(toggle);
        if (state.Value.Topmost && !owned) _menu.Items.Add(new ToolStripMenuItem("Already on top outside HzSwitcher") { Enabled = false });
        _menu.Items.Add("Unpin all", null, (_, _) => unpinAll()).Enabled = pins.Count > 0;
        _menu.Items.Add(new ToolStripSeparator());
        if (frameMenu)
            _menu.Items.Add("Original window menu", null, (_, _) => RunWindowAction(target, pins, feedback,
                () => WindowFrameApi.ShowOriginalMenu(target.Handle, point)));
        _menu.Items.Add("Always on top settings", null, (_, _) => settings());
        return _menu;
    }
    private static ToolStripItem CreateWindowCommand(WindowMenuCommand command, WindowIdentity target, AlwaysOnTopService pins, Action<PinResult> feedback)
    {
        if (command.Separator) return new ToolStripSeparator();
        var item = new ToolStripMenuItem(command.Text) { Enabled = command.Enabled && (command.Id != uint.MaxValue || command.Children.Length > 0), Checked = command.Checked };
        if (command.Children.Length > 0)
            foreach (var child in command.Children) item.DropDownItems.Add(CreateWindowCommand(child, target, pins, feedback));
        else
            item.Click += (_, _) => RunWindowAction(target, pins, feedback, () => WindowFrameApi.Execute(target.Handle, command.Id));
        return item;
    }
    private static void RunWindowAction(WindowIdentity target, AlwaysOnTopService pins, Action<PinResult> feedback, Func<bool> action)
    {
        if (pins.Inspect(target.Handle)?.Identity != target)
            feedback(new(PinStatus.Failed, target, "The original window has closed or changed."));
        else if (!action())
            feedback(new(PinStatus.Failed, target, "Windows could not send the command. The app may be protected or elevated."));
    }
    public void Close() => _menu.Close();
    public void Dispose() => _menu.Dispose();
}
