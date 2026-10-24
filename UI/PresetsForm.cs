using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Edit up to six named presets. A blank name removes the row.</summary>
internal sealed class PresetsForm : DialogShell
{
    private const int Rows = 6;
    private readonly Settings _s;
    private readonly TextBox[] _names = new TextBox[Rows];
    private readonly ComboBox[] _rates = new ComboBox[Rows];

    public PresetsForm(Settings s, List<int> supported)
        : base("Edit presets", new Size(480, 420),
               "Presets show as one-click buttons and can be used by hotkeys. If a monitor doesn't support a rate, the nearest one is used.")
    {
        _s = s;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.Controls.Add(new Label { Text = "Name", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 0, 0, 4) });
        grid.Controls.Add(new Label { Text = "Refresh rate", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(8, 0, 0, 4) });
        for (int i = 0; i < Rows; i++)
        {
            var preset = i < s.Presets.Count ? s.Presets[i] : null;
            _names[i] = new TextBox { Dock = DockStyle.Fill, Text = preset?.Name ?? "", MaxLength = 24, Margin = new Padding(0, 3, 4, 3) };
            _rates[i] = new ComboBox { Dock = DockStyle.Fill, Margin = new Padding(4, 3, 0, 3) };
            RateChoice.Fill(_rates[i], supported, preset?.Hz ?? 0);
            grid.Controls.Add(_names[i]);
            grid.Controls.Add(_rates[i]);
        }
        Body.Controls.Add(grid);
        Finish();
    }

    protected override bool OnSave()
    {
        var list = new List<Preset>();
        for (int i = 0; i < Rows; i++)
        {
            string name = _names[i].Text.Trim();
            if (name.Length == 0 || list.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new Preset { Name = name, Hz = RateChoice.Value(_rates[i]) });
        }
        _s.Presets = list;
        SettingsStore.Save(_s);
        return true;
    }
}
