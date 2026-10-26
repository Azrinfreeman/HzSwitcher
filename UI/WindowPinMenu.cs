using HzSwitcher.Services;

namespace HzSwitcher.UI;

internal sealed class WindowPinMenu : IDisposable
{
    private readonly ContextMenuStrip _menu = new() { BackColor = Theme.Surface, ForeColor = Theme.Text, Renderer = new ThemedMenuRenderer() };
    public bool IsOpen => _menu.Visible;
    public void Show(WindowIdentity target, Point point, AlwaysOnTopService pins, Func<bool> enabled,
        Action<PinResult> feedback, Action unpinAll, Action settings)
    {
        _menu.Close();
        var oldItems = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var item in oldItems) item.Dispose();
        var state = pins.Inspect(target.Handle);
        if (state == null || state.Value.Identity != target) { feedback(new(PinStatus.Failed, target, "The original window has closed or changed.")); return; }
        bool owned = pins.Owns(target);
        var toggle = new ToolStripMenuItem(owned ? "Unpin this window" : "Always on top") { Checked = owned,
            Enabled = enabled() && (owned || !state.Value.Topmost) };
        toggle.Click += (_, _) => { if (enabled()) feedback(pins.Toggle(target)); };
        _menu.Items.Add(toggle);
        if (state.Value.Topmost && !owned) _menu.Items.Add(new ToolStripMenuItem("Already on top outside HzSwitcher") { Enabled = false });
        _menu.Items.Add("Unpin all", null, (_, _) => unpinAll()).Enabled = pins.Count > 0;
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Always on top settings", null, (_, _) => settings());
        _menu.Show(point);
    }
    public void Close() => _menu.Close();
    public void Dispose() => _menu.Dispose();
}
