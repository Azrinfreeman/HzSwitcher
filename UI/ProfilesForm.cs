using HzSwitcher.Services;
using HzSwitcher.UI.Controls;
using System.Diagnostics;

namespace HzSwitcher.UI;

/// <summary>Per-app refresh-rate profiles.</summary>
internal sealed class ProfilesForm : DialogShell
{
    private readonly Settings _s;
    private readonly List<int> _supported;
    private readonly List<string> _running;
    private readonly FlowLayoutPanel _list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
    private readonly List<Row> _rows = new();
    private readonly Label _empty = new() { Text = "No apps yet. Use Add app to choose one.", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 8, 0, 0) };
    private readonly Toggle _enabled = new() { Text = "Enable app profiles" };
    private readonly NumberBox _leave = new() { Minimum = 1, Maximum = 120, Width = 104 };

    private sealed class Row
    {
        public FlowLayoutPanel Panel;
        public Toggle On;
        public CheckBox AllowSaver;
        public ComboBox Process, Rate;
    }

    public ProfilesForm(Settings s, List<int> supported)
        : base("App profiles", new Size(680, 420),
               "Pick a running app or type its process name (no .exe, * allowed). Changing the rate yourself while the app is in front overrides the profile.")
    {
        _s = s;
        _supported = supported;
        _running = GetRunningApps();

        _enabled.Checked = s.ProfilesEnabled;
        _leave.Value = Math.Clamp(s.ProfileLeaveSeconds, 1, 120);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        top.Controls.Add(_enabled);
        top.SetFlowBreak(_enabled, true);
        top.Controls.Add(new Label { Text = "Switch back after the app is out of focus (seconds):", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        top.Controls.Add(_leave);

        var add = new PillButton { Text = "Add app" };
        add.Click += (_, _) => AddRow(new AppProfile { Process = "", Hz = 0 });
        LeftActions.Controls.Add(add);

        Body.Controls.Add(_list);
        Body.Controls.Add(top);
        Finish();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);   // scaling happens here; rows are built afterwards so their sizes are not scaled twice
        foreach (var p in _s.Profiles) AddRow(p);
        if (_rows.Count == 0) _list.Controls.Add(_empty);
    }

    private static List<string> GetRunningApps()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0; } catch { return false; } })
                .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new List<string>(); }
    }

    private void AddRow(AppProfile p)
    {
        _list.Controls.Remove(_empty);
        var row = new Row { Panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 6) } };
        row.On = new Toggle { Checked = p.Enabled, Margin = new Padding(0, 3, 10, 0) };
        row.Process = new ComboBox { Width = Theme.Dp(this, 170), DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 3, 8, 0) };
        row.Process.Items.AddRange(_running.Cast<object>().ToArray());
        row.Process.Text = p.Process;
        row.Rate = new ComboBox { Width = Theme.Dp(this, 110), Margin = new Padding(0, 3, 8, 0) };
        row.AllowSaver = new CheckBox { Text = "Allow saver", AutoSize = true, Checked = p.AllowSaver, AccessibleName = "Allow saver while this app is open" };
        RateChoice.Fill(row.Rate, _supported, p.Hz);
        var remove = new PillButton { Text = "Remove", Margin = new Padding(0) };
        remove.Click += (_, _) => { _rows.Remove(row); _list.Controls.Remove(row.Panel); row.Panel.Dispose(); };
        row.Panel.Controls.AddRange(new Control[] { row.On, row.Process, row.Rate, row.AllowSaver, remove });
        _rows.Add(row);
        _list.Controls.Add(row.Panel);
        Theme.Style(row.Panel);
    }

    protected override bool OnSave()
    {
        _s.ProfilesEnabled = _enabled.Checked;
        _s.ProfileLeaveSeconds = _leave.Value;
        _s.Profiles = _rows
            .Where(r => r.Process.Text.Trim().Length > 0)
            .Select(r => new AppProfile { Process = Match.Normalize(r.Process.Text), Hz = RateChoice.Value(r.Rate), Enabled = r.On.Checked, AllowSaver = r.AllowSaver.Checked })
            .ToList();
        SettingsStore.Save(_s);
        return true;
    }
}
