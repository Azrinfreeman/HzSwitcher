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
    private readonly LayoutStack _list = new() { Dock = DockStyle.Fill, AutoSize = false, AutoScroll = true };
    private readonly List<Row> _rows = new();
    private readonly Label _empty = new() { Text = "No app profiles. Choose Add app to create one.", Tag = "muted", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 8, 0, 0) };
    private readonly Toggle _enabled = new() { Text = "Enable app profiles" };
    private readonly NumberBox _leave = new() { Minimum = 1, Maximum = 120 };

    private sealed class Row
    {
        public TableLayoutPanel Panel;
        public Toggle On;
        public ComboBox Process, Rate;
    }

    public ProfilesForm(Settings s, List<int> supported)
        : base("App profiles", new Size(680, 420),
               "Choose an app or enter its process name. No .exe is needed; * matches any text. A manual rate change overrides the active profile.")
    {
        _s = s;
        _supported = supported;
        _running = GetRunningApps();

        _enabled.Checked = s.ProfilesEnabled;
        _leave.Value = Math.Clamp(s.ProfileLeaveSeconds, 1, 120);
        _enabled.AccessibleName = "Enable app profiles";
        _leave.AccessibleName = "Profile restore delay in seconds";
        var top = new LayoutStack { Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 16) };
        top.Controls.Add(_enabled);
        top.Controls.Add(MainForm.Setting("Restore delay (seconds)", _leave, "After the app leaves the foreground."));

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
        var row = new Row { Panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 0, 0, 8) } };
        row.Panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        row.Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        row.Panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.On = new Toggle { Text = "", Checked = p.Enabled, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        row.Process = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = "Application process", Margin = new Padding(0, 4, 8, 0) };
        row.Process.Items.AddRange(_running.Cast<object>().ToArray());
        row.Process.Text = p.Process;
        row.Rate = new ComboBox { Dock = DockStyle.Fill, AccessibleName = "Profile refresh rate", Margin = new Padding(0, 4, 8, 0) };
        RateChoice.Fill(row.Rate, _supported, p.Hz);
        var remove = new PillButton { Text = "Remove", Margin = new Padding(0) };
        remove.AccessibleName = "Remove profile for " + p.Process;
        row.On.AccessibleName = "Enable profile for " + p.Process;
        row.Process.TextChanged += (_, _) =>
        {
            row.On.AccessibleName = "Enable profile for " + row.Process.Text;
            remove.AccessibleName = "Remove profile for " + row.Process.Text;
        };
        remove.Click += (_, _) =>
        {
            _rows.Remove(row); _list.Controls.Remove(row.Panel); row.Panel.Dispose();
            if (_rows.Count == 0) _list.Controls.Add(_empty);
        };
        row.Panel.Controls.AddRange(new Control[] { row.On, row.Process, row.Rate, remove });
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
            .Select(r => new AppProfile { Process = Match.Normalize(r.Process.Text), Hz = RateChoice.Value(r.Rate), Enabled = r.On.Checked })
            .ToList();
        SettingsStore.Save(_s);
        return true;
    }
}
