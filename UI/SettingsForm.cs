using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Specialist saver timings and video/game detection lists.</summary>
internal sealed class SettingsForm : DialogShell
{
    private readonly Settings _settings;
    private readonly NumberBox _quiet = new() { Minimum = 5, Maximum = 600, AccessibleName = "Watching rule quiet time in seconds" };
    private readonly NumberBox _cooldown = new() { Minimum = 0, Maximum = 300, AccessibleName = "Automatic switch cooldown in seconds" };
    private readonly TextBox _titles = Multi("Video title keywords");
    private readonly TextBox _players = Multi("Player processes");
    private readonly TextBox _games = Multi("Never throttle these games");

    public SettingsForm(Settings settings, List<int> supportedHz)
        : base("Detection settings", new Size(600, 560), "One entry per line in the detection lists. Process names may include * wildcards.")
    {
        _settings = settings;
        _quiet.Value = Math.Clamp(settings.WatchingQuietSeconds, 5, 600);
        _cooldown.Value = Math.Clamp(settings.MinSwitchSeconds, 0, 300);
        _titles.Text = string.Join(Environment.NewLine, settings.WatchTitles);
        _players.Text = string.Join(Environment.NewLine, settings.WatchPlayers);
        _games.Text = string.Join(Environment.NewLine, settings.GameProcesses);
        var stack = new LayoutStack { Dock = DockStyle.Top };
        stack.Controls.Add(MainForm.Setting("Watching quiet time", _quiet, "Seconds with no input before video counts as watching."));
        stack.Controls.Add(MainForm.Setting("Automatic switch cooldown", _cooldown, "Seconds between automatic rate changes."));
        AddList(stack, "Video title keywords", _titles);
        AddList(stack, "Player processes", _players);
        AddList(stack, "Never throttle these games", _games);
        Body.Controls.Add(stack);
        Finish();
    }

    private static TextBox Multi(string name) => new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 72, AccessibleName = name };

    private static void AddList(LayoutStack stack, string title, TextBox input)
    {
        var label = MainForm.TextLabel(title);
        label.Margin = new Padding(0, 16, 0, 6);
        input.Margin = Padding.Empty;
        stack.Controls.Add(label);
        stack.Controls.Add(input);
    }

    private static List<string> Lines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    protected override bool OnSave()
    {
        _settings.WatchingQuietSeconds = _quiet.Value;
        _settings.MinSwitchSeconds = _cooldown.Value;
        _settings.WatchTitles = Lines(_titles.Text);
        _settings.WatchPlayers = Lines(_players.Text);
        _settings.GameProcesses = Lines(_games.Text);
        SettingsStore.Save(_settings);
        return true;
    }
}
