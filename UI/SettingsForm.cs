using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Advanced settings: timings and the video / game detection lists.</summary>
internal sealed class SettingsForm : DialogShell
{
    private readonly Settings _s;
    private readonly NumberBox _idle = new() { Minimum = 1, Maximum = 240 };
    private readonly NumberBox _restore = new() { Minimum = 1, Maximum = 60 };
    private readonly NumberBox _quiet = new() { Minimum = 5, Maximum = 600 };
    private readonly NumberBox _cooldown = new() { Minimum = 0, Maximum = 300 };
    private readonly ComboBox _saverHz = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Toggle _auto = new() { Text = "Enable auto power-saver" };
    private readonly TextBox _titles = Multi();
    private readonly TextBox _players = Multi();
    private readonly TextBox _games = Multi();

    public SettingsForm(Settings s, List<int> supportedHz)
        : base("Advanced settings", new Size(600, 620), "Timings and detection lists for the auto power-saver. One entry per line in the lists.")
    {
        _s = s;

        foreach (int hz in supportedHz) _saverHz.Items.Add(hz);
        _saverHz.SelectedItem = s.SaverHz;
        if (_saverHz.SelectedItem == null && _saverHz.Items.Count > 0) _saverHz.SelectedIndex = 0;
        _idle.Value = Math.Clamp(s.IdleMinutes, 1, 240);
        _restore.Value = Math.Clamp(s.RestoreSeconds, 1, 60);
        _quiet.Value = Math.Clamp(s.WatchingQuietSeconds, 5, 600);
        _cooldown.Value = Math.Clamp(s.MinSwitchSeconds, 0, 300);
        _auto.Checked = s.AutoEnabled;
        _titles.Text = string.Join(Environment.NewLine, s.WatchTitles);
        _players.Text = string.Join(Environment.NewLine, s.WatchPlayers);
        _games.Text = string.Join(Environment.NewLine, s.GameProcesses);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _auto.Margin = new Padding(0, 0, 0, 8);
        grid.Controls.Add(_auto); grid.SetColumnSpan(_auto, 2);
        AddRow(grid, "Idle / watching timeout (minutes)", _idle);
        AddRow(grid, "Restore after continuous activity (seconds)", _restore);
        AddRow(grid, "Watching rule: no input for (seconds)", _quiet);
        AddRow(grid, "Automatic switch cooldown (seconds)", _cooldown);
        AddRow(grid, "Saver refresh rate (Hz)", _saverHz);
        AddRow(grid, "Video title keywords", _titles, 64);
        AddRow(grid, "Player processes", _players, 64);
        AddRow(grid, "Never throttle these games (* allowed)", _games, 64);
        Body.Controls.Add(grid);

        var open = new PillButton { Text = "Open settings file" };
        open.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsStore.Path_) { UseShellExecute = true });
        LeftActions.Controls.Add(open);
        Finish();
    }

    private static TextBox Multi() => new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    private static void AddRow(TableLayoutPanel grid, string label, Control input, int height = 0)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | (height > 0 ? AnchorStyles.Top : AnchorStyles.None), Margin = new Padding(0, 8, 8, 8) };
        input.Anchor = input is NumberBox ? AnchorStyles.Left : AnchorStyles.Left | AnchorStyles.Right | (height > 0 ? AnchorStyles.Top : AnchorStyles.None);
        input.Margin = new Padding(0, 4, 0, 4);
        if (height > 0) input.Height = height;
        if (input is NumberBox) input.Width = 104;
        grid.Controls.Add(l);
        grid.Controls.Add(input);
    }

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    protected override bool OnSave()
    {
        _s.AutoEnabled = _auto.Checked;
        _s.IdleMinutes = _idle.Value;
        _s.RestoreSeconds = _restore.Value;
        _s.WatchingQuietSeconds = _quiet.Value;
        _s.MinSwitchSeconds = _cooldown.Value;
        if (_saverHz.SelectedItem is int hz) _s.SaverHz = hz;
        _s.WatchTitles = Lines(_titles.Text);
        _s.WatchPlayers = Lines(_players.Text);
        _s.GameProcesses = Lines(_games.Text);
        SettingsStore.Save(_s);
        return true;
    }
}
