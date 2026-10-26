using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Edit global hotkeys: click the key box and press the combination you want.</summary>
internal sealed class HotkeysForm : DialogShell
{
    private static readonly (string Code, string Label)[] Actions =
    {
        ("Preset", "Apply preset"),
        ("SetHz", "Set refresh rate"),
        ("SetMax", "Highest refresh rate"),
        ("Cycle", "Next preset"),
        ("ToggleAuto", "Toggle auto power-saver"),
        ("ToggleAlwaysOnTop", "Toggle active window always on top"),
    };

    private readonly Settings _s;
    private readonly List<int> _supported;
    private readonly FlowLayoutPanel _list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
    private readonly List<Row> _rows = new();

    private sealed class Row
    {
        public FlowLayoutPanel Panel;
        public TextBox Keys;
        public ComboBox Action, Param;
    }

    public HotkeysForm(Settings s, List<int> supported)
        : base("Edit hotkeys", new Size(720, 440),
               "Click a key box, then press the combination (needs Ctrl, Alt or Shift). Backspace clears it.")
    {
        _s = s;
        _supported = supported;

        var add = new PillButton { Text = "Add hotkey" };
        add.Click += (_, _) => AddRow(new HotkeyBinding { Action = "Preset", Preset = _s.Presets.FirstOrDefault()?.Name ?? "" });
        LeftActions.Controls.Add(add);

        Body.Controls.Add(_list);
        Finish();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);   // scaling happens here; rows are built afterwards so their sizes are not scaled twice
        foreach (var b in _s.Hotkeys) AddRow(b);
    }

    private void AddRow(HotkeyBinding b)
    {
        var row = new Row { Panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 6) } };
        row.Keys = new TextBox { Width = Theme.Dp(this, 150), ReadOnly = true, Text = b.Keys, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 8, 0) };
        row.Keys.KeyDown += CaptureKey;
        row.Action = new ComboBox { Width = Theme.Dp(this, 220), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 8, 0) };
        foreach (var a in Actions) row.Action.Items.Add(a.Label);
        row.Action.SelectedIndex = Math.Max(0, Array.FindIndex(Actions, a => a.Code == b.Action));
        row.Param = new ComboBox { Width = Theme.Dp(this, 140), DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 8, 0) };
        var remove = new PillButton { Text = "Remove", Margin = new Padding(0) };

        void FillParam(string code, HotkeyBinding src)
        {
            row.Param.Items.Clear();
            row.Param.Enabled = code is "Preset" or "SetHz";
            if (code == "Preset")
            {
                foreach (var p in _s.Presets) row.Param.Items.Add(p.Name);
                row.Param.SelectedItem = src.Preset;
                if (row.Param.SelectedIndex < 0 && row.Param.Items.Count > 0) row.Param.SelectedIndex = 0;
            }
            else if (code == "SetHz")
            {
                foreach (int hz in _supported) row.Param.Items.Add($"{hz} Hz");
                row.Param.SelectedItem = $"{src.Hz} Hz";
                if (row.Param.SelectedIndex < 0 && row.Param.Items.Count > 0) row.Param.SelectedIndex = row.Param.Items.Count - 1;
            }
        }
        FillParam(b.Action, b);
        row.Action.SelectedIndexChanged += (_, _) => FillParam(Actions[row.Action.SelectedIndex].Code, new HotkeyBinding());
        remove.Click += (_, _) => { _rows.Remove(row); _list.Controls.Remove(row.Panel); row.Panel.Dispose(); };

        row.Panel.Controls.AddRange(new Control[] { row.Keys, row.Action, row.Param, remove });
        _rows.Add(row);
        _list.Controls.Add(row.Panel);
        Theme.Style(row.Panel);
    }

    private static void CaptureKey(object sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        var box = (TextBox)sender;
        var k = e.KeyCode;
        if (k is Keys.Back or Keys.Delete) { box.Text = ""; return; }
        if (k is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None) return;
        if (!(e.Control || e.Alt || e.Shift)) return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(k >= Keys.D0 && k <= Keys.D9 ? ((int)k - (int)Keys.D0).ToString() : k.ToString());
        box.Text = string.Join("+", parts);
    }

    protected override bool OnSave()
    {
        var list = new List<HotkeyBinding>();
        foreach (var r in _rows)
        {
            if (r.Keys.Text.Length == 0) continue;
            string code = Actions[Math.Max(0, r.Action.SelectedIndex)].Code;
            var b = new HotkeyBinding { Keys = r.Keys.Text, Action = code };
            if (code == "Preset") b.Preset = r.Param.SelectedItem as string ?? "";
            if (code == "SetHz" && r.Param.SelectedItem is string t) b.Hz = int.Parse(t.Split(' ')[0]);
            if (!HotkeyWindow.TryParse(b.Keys, out uint mods, out uint key))
            {
                MessageBox.Show(this, $"Invalid shortcut: {b.Keys}", "Hotkeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (list.Any(x => HotkeyWindow.TryParse(x.Keys, out uint m, out uint k) && m == mods && k == key))
            {
                MessageBox.Show(this, $"{b.Keys} is used twice.", "Hotkeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            list.Add(b);
        }
        _s.Hotkeys = list;
        SettingsStore.Save(_s);
        return true;
    }
}
