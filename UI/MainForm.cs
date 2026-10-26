using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>Main window: a navigation rail and four pages (Display, Automation, Shortcuts, General).</summary>
internal sealed class MainForm : Form
{
    // Design units at 96 dpi; the form scales them for the display.
    private const int WindowWidth = 780;
    private const int WindowHeight = 560;
    private const int RailWidth = 180;
    private const int CardWidth = 560;
    private const int BodyWidth = 530;

    private readonly TrayApp _app;
    private readonly ToolTip _tips = new() { InitialDelay = 400, ReshowDelay = 200 };

    private readonly NavRail _rail = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly List<FlowLayoutPanel> _pages = new();

    // Display page
    private readonly Label _big = new() { AutoSize = true, Font = new Font("Segoe UI", 34f, FontStyle.Bold) };
    private readonly Label _mode = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = new Font("Segoe UI", 10f) };
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DropDownWidth = 440, DisplayMember = "Label" };
    private readonly FlowLayoutPanel _chips = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(260, 0), Margin = new Padding(0, 0, 0, 6) };
    private readonly FlowLayoutPanel _presets = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(BodyWidth, 0), MinimumSize = new Size(BodyWidth, 0) };
    private readonly ChipGroup _rates = new() { MaximumSize = new Size(BodyWidth, 0), MinimumSize = new Size(BodyWidth, 0), AccessibleName = "Refresh rate" };
    private readonly Toggle _showAll = new() { Text = "Show all rates" };
    private readonly ThemedSlider _bri = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Width = BodyWidth - 100, Height = 32, AccessibleName = "Brightness" };
    private readonly Label _briValue = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
    private readonly Label _briInfo = new() { AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(BodyWidth, 0) };
    private readonly Button _briDown;
    private readonly Button _briUp;

    // Automation page
    private readonly Toggle _auto = new() { Text = "Enabled", Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly Label _status = new() { AutoSize = false, Height = 36, Width = BodyWidth, ForeColor = Theme.TextMuted };
    private readonly SaverBar _bar = new() { Width = BodyWidth };
    private readonly NumberBox _idle = Num(1, 240);
    private readonly NumberBox _restore = Num(1, 60);
    private readonly ComboBox _saverHz = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ComboBox _saverBri = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly Toggle _profilesOn = new() { Text = "Use per-app refresh rates" };
    private readonly Label _profilesInfo = new() { AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(BodyWidth, 0) };

    // Shortcuts page
    private readonly Label _hotkeys = new() { AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(BodyWidth, 0) };

    // General page
    private readonly Toggle _pinOutline = new() { Text = "Show outline around pinned windows" };
    private readonly Toggle _pinSound = new() { Text = "Play sound when pinning or unpinning" };
    private readonly Toggle _pinMouse = new() { Text = "Enable Shift+right-click menu" };
    private readonly NumberBox _pinThickness = new() { Minimum = 1, Maximum = 8, Value = 3, AccessibleName = "Outline thickness in logical pixels" };
    private readonly Toggle _alwaysOnTop = new() { Text = "Enable always on top" };
    private readonly Label _alwaysOnTopStatus = new() { AutoSize = true, MaximumSize = new Size(BodyWidth, 0), ForeColor = Theme.TextMuted };
    private Button _unpinAll;
    private readonly Toggle _notify = new() { Text = "Notify me when it switches automatically" };
    private readonly Toggle _startup = new() { Text = "Start with Windows (minimized to tray)" };
    private readonly Toggle _briRestore = new() { Text = "Restore each monitor's brightness at startup" };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private bool _loading;
    private bool _hintShown;
    private bool _positionRestored;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public MainForm(TrayApp app)
    {
        _app = app;
        Text = "HzSwitcher " + Application.ProductVersion.Split('+')[0];
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(WindowWidth, WindowHeight);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _briDown = FlatButton("−", (_, _) => StepBrightness(-5), "Darker");
        _briUp = FlatButton("+", (_, _) => StepBrightness(5), "Brighter");

        BuildDisplayPage();
        BuildAutomationPage();
        BuildShortcutsPage();
        BuildGeneralPage();

        foreach (var page in _pages) _content.Controls.Add(page);
        Controls.Add(_content);   // fill first, edge-docked rail after
        _rail.Width = RailWidth;
        _rail.Footer = "Version " + Application.ProductVersion.Split('+')[0];
        _rail.Add("Display", "");
        _rail.Add("Automation", "");
        _rail.Add("Shortcuts", "");
        _rail.Add("General", "");
        _rail.SelectedIndexChanged += (_, _) => ShowPage(_rail.SelectedIndex);
        Controls.Add(_rail);
        ShowPage(0);

        // Keyboard and screen-reader basics: the rail comes first in tab order, unlabeled inputs get names.
        _rail.TabIndex = 0;
        _content.TabIndex = 1;
        _monitor.AccessibleName = "Monitor";
        _idle.AccessibleName = "Minutes of idle or video before dropping";
        _restore.AccessibleName = "Seconds of activity before restoring";
        _saverHz.AccessibleName = "Saver refresh rate";
        _saverBri.AccessibleName = "Saver brightness";
        _briDown.AccessibleName = "Decrease brightness";
        _briUp.AccessibleName = "Increase brightness";

        WireEvents();

        _timer.Tick += (_, _) => UpdateStatus();
        VisibleChanged += OnVisibleChanged;

        // Set last, as the designer does: everything above is laid out in 96-dpi units and scaled for the display on load.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Apply(this);
        Theme.RoundCorners(this);
        BackColor = Theme.Bg;
        _briInfo.ForeColor = Theme.TextMuted;
        _big.ForeColor = Theme.Text;
        _mode.ForeColor = Theme.TextMuted;
        _status.ForeColor = Theme.TextMuted;
        _profilesInfo.ForeColor = Theme.TextMuted;
    }

    // ---------- pages ----------

    private void BuildDisplayPage()
    {
        var page = NewPage();

        // hero: current rate on the left, state chips and monitor picker on the right
        var left = Stack();
        _big.Margin = new Padding(0);
        _mode.Margin = new Padding(4, 0, 0, 0);
        left.Controls.Add(_big);
        left.Controls.Add(_mode);
        var right = Stack();
        right.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        right.Controls.Add(_chips);
        right.Controls.Add(_monitor);
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0), MinimumSize = new Size(BodyWidth, 0), MaximumSize = new Size(BodyWidth, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        left.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        grid.Controls.Add(left);
        grid.Controls.Add(right);
        var hero = new Card { Padding = new Padding(15), MinimumSize = new Size(CardWidth, 0), MaximumSize = new Size(CardWidth, 0), Margin = new Padding(0, 0, 0, 12) };
        hero.Controls.Add(grid);
        page.Controls.Add(hero);

        var edit = FlatButton("Edit presets…", (_, _) => _app.EditPresets(), "Rename presets or change their refresh rates");
        page.Controls.Add(Section("Presets", _presets, null, edit));

        page.Controls.Add(Section("Refresh rate", _rates,
            $"Switches instantly. A display problem resets it to {_app.Settings.FallbackHz} Hz.", _showAll));

        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        _bri.Margin = new Padding(0, 0, 8, 0);
        _briDown.Margin = new Padding(0, 0, 8, 0);
        _briUp.Margin = new Padding(0, 0, 0, 0);
        row.Controls.Add(_briDown);
        row.Controls.Add(_bri);
        row.Controls.Add(_briUp);
        var briBody = Stack();
        briBody.Controls.Add(row);
        briBody.Controls.Add(_briInfo);
        page.Controls.Add(Section("Brightness", briBody, null, _briValue));
    }

    private void BuildAutomationPage()
    {
        var page = NewPage();

        var autoBody = Stack();
        _status.Margin = new Padding(0, 4, 0, 4);
        _bar.Margin = new Padding(0, 0, 0, 12);
        var fields = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, Margin = new Padding(0),
            MinimumSize = new Size(BodyWidth, 0), MaximumSize = new Size(BodyWidth, 0),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(fields, "Drop after idle / video (minutes)", _idle);
        AddRow(fields, "Restore after activity for (seconds)", _restore);
        AddRow(fields, "Saver refresh rate (Hz)", _saverHz);
        AddRow(fields, "Saver brightness", _saverBri);
        autoBody.Controls.Add(_auto);
        autoBody.Controls.Add(_status);
        autoBody.Controls.Add(_bar);
        autoBody.Controls.Add(fields);
        page.Controls.Add(Section("Auto power-saver", autoBody));

        var profileBody = Stack();
        profileBody.Controls.Add(_profilesOn);
        profileBody.Controls.Add(_profilesInfo);
        var manage = FlatButton("Manage…", (_, _) => _app.EditProfiles(), "Choose a refresh rate for specific apps");
        page.Controls.Add(Section("App profiles", profileBody, "Example: 240 Hz while a game is in front, 60 Hz while a video player is.", manage));
    }

    private void BuildShortcutsPage()
    {
        var page = NewPage();
        var edit = FlatButton("Edit hotkeys…", (_, _) => _app.EditHotkeys(), "Record new global hotkeys");
        page.Controls.Add(Section("Hotkeys", _hotkeys, "Hotkeys work everywhere while HzSwitcher is running.", edit));
    }

    private void BuildGeneralPage()
    {
        var page = NewPage();

        var behavior = Stack();
        foreach (var t in new[] { _notify, _startup, _briRestore }) { t.Margin = new Padding(0, 0, 0, 8); behavior.Controls.Add(t); }
        page.Controls.Add(Section("Behavior", behavior));

        var pin = Stack();
        pin.Controls.Add(_alwaysOnTop);
        pin.Controls.Add(_alwaysOnTopStatus);
        foreach (var toggle in new[] { _pinOutline, _pinSound, _pinMouse })
        { toggle.Margin = new Padding(0, 6, 0, 6); pin.Controls.Add(toggle); }
        var thicknessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        thicknessRow.Controls.Add(new Label { Text = "Outline thickness (1-8):", AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
        thicknessRow.Controls.Add(_pinThickness);
        pin.Controls.Add(thicknessRow);
        var pinActions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(BodyWidth, 0) };
        pinActions.Controls.Add(FlatButton("Edit hotkeys...", (_, _) => _app.EditHotkeys(), "Choose the pin/unpin shortcut"));
        _unpinAll = FlatButton("Unpin all", (_, _) => _app.UnpinAll(), "Restore windows pinned by HzSwitcher");
        pinActions.Controls.Add(_unpinAll);
        pin.Controls.Add(pinActions);
        page.Controls.Add(Section("Always on top", pin, "Use the shortcut or Shift+right-click a window to pin it. Right-click its outline to unpin. The outline follows the Windows accent color. HzSwitcher must stay running."));

        var tools = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(BodyWidth, 0) };
        tools.Controls.Add(FlatButton("Advanced settings…", (_, _) => _app.ShowAdvancedSettings(), "Video/game detection lists"));
        tools.Controls.Add(FlatButton("Open log", (_, _) => _app.OpenLog(), null));
        tools.Controls.Add(FlatButton("Hide to tray", (_, _) => Hide(), "Keep running in the tray"));
        page.Controls.Add(Section("Tools", tools));

        var exit = FlatButton("Exit app", (_, _) => _app.ExitApp(), "Quit HzSwitcher completely");
        exit.Style = PillStyle.Danger;
        page.Controls.Add(Section("Quit", exit, "Closing the window keeps HzSwitcher running in the tray; Exit app stops it completely."));
    }

    private FlowLayoutPanel NewPage()
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(20, 16, 20, 8), Visible = false, BackColor = Theme.Bg,
        };
        _pages.Add(page);
        return page;
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;
    }

    private void WireEvents()
    {
        _tips.SetToolTip(_monitor, "Which monitor HzSwitcher controls");
        _tips.SetToolTip(_showAll, "Include rates below 60 Hz and near-duplicate rates like 59 or 119");
        _tips.SetToolTip(_auto, "Drop to the saver rate when idle or watching video; restore when you're back");
        _tips.SetToolTip(_idle, "Minutes of no input, or of video playing with no input, before dropping");
        _tips.SetToolTip(_restore, "Seconds of continuous activity needed before your rate returns");
        _tips.SetToolTip(_saverHz, "The rate used while the saver is active");
        _tips.SetToolTip(_bri, "Brightness of the selected monitor");
        _tips.SetToolTip(_saverBri, "Brightness while the saver is active; your level returns afterwards");
        _tips.SetToolTip(_profilesOn, "Switch rates automatically based on which app is in front");
        _tips.SetToolTip(_notify, "Show a Windows notification on automatic changes");
        _tips.SetToolTip(_briRestore, "Re-apply the last brightness you set when HzSwitcher starts");

        _monitor.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _monitor.SelectedItem is MonitorInfo m && m.Device != _app.Display.Device) _app.SelectMonitor(m.Device);
        };
        _showAll.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.ShowAllRates = _showAll.Checked; _app.SaveSettings(); RefreshAll(); } };
        _auto.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.AutoEnabled = _auto.Checked; _app.SaveSettings(); if (!_auto.Checked) _app.ExitSaverNow(); } };
        _idle.ValueChanged += (_, _) => { if (!_loading) { _app.Settings.IdleMinutes = _idle.Value; _app.SaveSettings(); } };
        _restore.ValueChanged += (_, _) => { if (!_loading) { _app.Settings.RestoreSeconds = _restore.Value; _app.SaveSettings(); } };
        _saverHz.SelectedIndexChanged += (_, _) => { if (!_loading && _saverHz.SelectedItem is int hz) { _app.Settings.SaverHz = hz; _app.SaveSettings(); } };
        _bri.Scroll += (_, _) => { if (!_loading) { _app.SetBrightness(_bri.Value); _briValue.Text = _bri.Value + "%"; } };
        _bri.MouseUp += (_, _) => _app.CommitBrightness();
        _bri.KeyUp += (_, _) => _app.CommitBrightness();
        _briRestore.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.RestoreBrightnessOnStart = _briRestore.Checked; _app.SaveSettings(); } };
        _app.Brightness.Changed += RefreshBrightness;
        _saverBri.Items.Add("Unchanged");
        for (int p = 10; p <= 90; p += 10) _saverBri.Items.Add(p + "%");
        _saverBri.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _app.Settings.SaverBrightness = _saverBri.SelectedIndex <= 0 ? -1 : _saverBri.SelectedIndex * 10;
            _app.SaveSettings();
        };
        _profilesOn.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.ProfilesEnabled = _profilesOn.Checked; _app.SaveSettings(); } };
        _notify.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.NotifyOnAutoSwitch = _notify.Checked; _app.SaveSettings(); } };
        _alwaysOnTop.CheckedChanged += (_, _) => { if (!_loading) _app.SetAlwaysOnTop(_alwaysOnTop.Checked); };
        _pinOutline.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.AlwaysOnTopOutline = _pinOutline.Checked; _app.UpdatePinPreferences(); } };
        _pinSound.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.AlwaysOnTopSound = _pinSound.Checked; _app.UpdatePinPreferences(); } };
        _pinMouse.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.AlwaysOnTopMouseMenu = _pinMouse.Checked; _app.UpdatePinPreferences(); } };
        _pinThickness.ValueChanged += (_, _) => { if (!_loading) { _app.Settings.AlwaysOnTopOutlineThickness = _pinThickness.Value; _app.UpdatePinPreferences(); } };
        _startup.CheckedChanged += (_, _) => { if (!_loading) _app.SetStartup(_startup.Checked); };
    }

    private void OnVisibleChanged(object sender, EventArgs e)
    {
        if (Visible)
        {
            if (!_positionRestored) { _positionRestored = true; RestoreWindowPosition(); ActiveControl = _rail; }
            RefreshAll();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            SaveWindowPosition();
        }
    }

    // ---------- remember position (the size is fixed) ----------

    private void RestoreWindowPosition()
    {
        var s = _app.Settings;
        if (s.WindowWidth <= 0) return;
        var centre = new Point(s.WindowLeft + Width / 2, s.WindowTop + 40);
        if (!Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(centre))) return;   // monitor is gone: keep default placement
        StartPosition = FormStartPosition.Manual;
        Location = new Point(s.WindowLeft, s.WindowTop);
    }

    private void SaveWindowPosition()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : base.RestoreBounds;
        var s = _app.Settings;
        (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight) = (b.Left, b.Top, b.Width, b.Height);
        _app.SaveSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_hintShown) { _hintShown = true; _app.Hint("HzSwitcher is still running", "Use the tray icon to reopen it, or Exit app to quit."); }
            return;
        }
        SaveWindowPosition();
        base.OnFormClosing(e);
    }

    // ---------- refresh ----------

    /// <summary>Re-read everything from the app (rates, settings, state).</summary>
    public void RefreshAll()
    {
        if (!Visible) return;
        _loading = true;
        try
        {
            var s = _app.Settings;
            int cur = _app.Display.GetCurrentHz();
            bool saver = _app.Auto.State == SaverState.Saver;
            _big.Text = $"{cur} Hz";
            _big.ForeColor = saver ? Theme.Saver : Theme.Text;
            _mode.Text = _app.Display.CurrentModeText();
            ClearAndDispose(_chips);
            if (saver) _chips.Controls.Add(new StatusChip($"Saver active · {_app.Auto.RestoreHz} Hz returns on activity", Theme.Saver));
            if (_app.Profiles.ActiveProcess != null) _chips.Controls.Add(new StatusChip($"Profile: {_app.Profiles.ActiveProcess}", Theme.Accent));
            _chips.Visible = _chips.Controls.Count > 0;

            var monitors = _app.Display.GetMonitors();
            _monitor.Visible = monitors.Count > 1;
            _monitor.Items.Clear();
            foreach (var m in monitors) _monitor.Items.Add(m);
            _monitor.SelectedItem = monitors.FirstOrDefault(m => m.Device == _app.Display.Device);

            ClearAndDispose(_presets);
            foreach (var preset in s.Presets)
            {
                var p = preset;
                int hz = _app.ResolvePreset(p);
                _presets.Controls.Add(FlatButton($"{p.Name}  ·  {hz} Hz", (_, _) => _app.ApplyPreset(p), $"Switch to {hz} Hz", hz == cur));
            }
            if (s.Presets.Count == 0) _presets.Controls.Add(new Label { Text = "No presets yet.", AutoSize = true, ForeColor = Theme.TextMuted });

            ClearAndDispose(_rates);
            var supported = _app.Display.GetSupportedHz();
            foreach (int hz in _app.VisibleRates())
            {
                int target = hz;
                var b = FlatButton($"{hz}", (_, _) => _app.SetHz(target), $"Switch to {hz} Hz", hz == cur);
                b.AutoSize = false; b.Width = 66; b.Height = 40;
                _rates.Controls.Add(b);
            }
            _showAll.Checked = s.ShowAllRates;

            _auto.Checked = s.AutoEnabled;
            _idle.Value = Math.Clamp(s.IdleMinutes, 1, 240);
            _restore.Value = Math.Clamp(s.RestoreSeconds, 1, 60);
            _saverHz.Items.Clear();
            foreach (int hz in supported) _saverHz.Items.Add(hz);
            _saverHz.SelectedItem = s.SaverHz;
            _saverBri.SelectedIndex = s.SaverBrightness < 0 ? 0 : Math.Clamp((int)Math.Round(s.SaverBrightness / 10.0), 1, 9);
            _briRestore.Checked = s.RestoreBrightnessOnStart;

            _profilesOn.Checked = s.ProfilesEnabled;
            int active = s.Profiles.Count(p => p.Enabled);
            _profilesInfo.Text = s.Profiles.Count == 0
                ? "No apps added yet."
                : string.Join(", ", s.Profiles.Where(p => p.Enabled).Take(6).Select(p => $"{Match.Normalize(p.Process)} → {(p.Hz <= 0 ? "highest" : p.Hz + " Hz")}"))
                  + (active > 6 ? $" (+{active - 6} more)" : "");
            _hotkeys.Text = s.Hotkeys.Count == 0 ? "No hotkeys set." : string.Join("\n", s.Hotkeys.Select(HotkeyText));
            _alwaysOnTop.Checked = s.AlwaysOnTopEnabled;
            _pinOutline.Checked = s.AlwaysOnTopOutline;
            _pinSound.Checked = s.AlwaysOnTopSound;
            _pinMouse.Checked = s.AlwaysOnTopMouseMenu;
            _pinThickness.Value = s.AlwaysOnTopOutlineThickness;
            RefreshAlwaysOnTop();
            _notify.Checked = s.NotifyOnAutoSwitch;
            _startup.Checked = _app.IsStartupEnabled();
        }
        finally { _loading = false; }
        RefreshBrightness();
        UpdateStatus();
        Invalidate(true);   // accent color may have changed
    }

    /// <summary>Updates the brightness card from the service; leaves the slider alone while it is being dragged.</summary>
    private void RefreshBrightness()
    {
        if (!Visible || _bri.Capture) return;
        var b = _app.Brightness;
        bool ok = b.IsSupported;
        bool soft = b.Mode == BrightnessMode.Software;
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _bri.Minimum = soft ? BrightnessService.SoftwareFloor : 0;
            _bri.Value = Math.Clamp(b.Get(), _bri.Minimum, _bri.Maximum);
            _briValue.Text = ok ? _bri.Value + "%" : "n/a";
        }
        finally { _loading = wasLoading; }
        _bri.Enabled = _briDown.Enabled = _briUp.Enabled = _briRestore.Enabled = _saverBri.Enabled = ok;
        _briInfo.Text = !ok ? "This monitor does not allow brightness control."
            : soft ? "Software dimming: only darkens the picture. Turn on DDC/CI in the monitor's own menu for real backlight control."
            : "";
        _briInfo.Visible = _briInfo.Text.Length > 0;
    }

    private void StepBrightness(int delta)
    {
        _app.SetBrightness(Math.Clamp(_app.Brightness.Get() + delta, 0, 100));
        _app.CommitBrightness();
        RefreshBrightness();
    }

    internal void ShowAlwaysOnTopSettings() => _rail.SelectedIndex = 3;

    internal void RefreshAlwaysOnTop()
    {
        _alwaysOnTopStatus.Text = _app.AlwaysOnTopStatus;
        if (_unpinAll != null) _unpinAll.Enabled = _app.HasPinnedWindows;
    }

    private void UpdateStatus()
    {
        _status.Text = _app.Auto.Status;
        bool saver = _app.Auto.State == SaverState.Saver;
        _bar.Set(_app.Auto.Progress, saver ? Theme.Saver : Theme.Accent);
    }

    // ---------- small UI helpers ----------

    private static NumberBox Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 104 };

    private static FlowLayoutPanel Stack() =>
        new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

    private static string HotkeyText(HotkeyBinding b) => b.Action switch
    {
        "SetHz" => $"{b.Keys}   →   {b.Hz} Hz",
        "SetMax" => $"{b.Keys}   →   highest rate",
        "Preset" => $"{b.Keys}   →   preset “{b.Preset}”",
        "Cycle" => $"{b.Keys}   →   next preset",
        "ToggleAlwaysOnTop" => $"{b.Keys}   ->   toggle active window always on top",
        "ToggleAuto" => $"{b.Keys}   →   toggle auto power-saver",
        _ => b.Keys,
    };

    private PillButton FlatButton(string text, EventHandler onClick, string tip, bool selected = false)
    {
        var b = new PillButton { Text = text, Selected = selected };
        b.Click += onClick;
        if (tip != null) _tips.SetToolTip(b, tip);
        return b;
    }

    private static void ClearAndDispose(Control parent)
    {
        var old = parent.Controls.Cast<Control>().ToList();
        parent.Controls.Clear();
        foreach (var c in old) c.Dispose();
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        input.Anchor = AnchorStyles.Right;
        input.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(input);
    }

    /// <summary>A titled card; an optional control sits at the right of the title row.</summary>
    private Card Section(string title, Control body, string hint = null, Control action = null)
    {
        var card = new Card
        {
            Padding = new Padding(15),
            MinimumSize = new Size(CardWidth, 0), MaximumSize = new Size(CardWidth, 0), Margin = new Padding(0, 0, 0, 12),
        };
        var head = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6),
            MinimumSize = new Size(BodyWidth, 0), MaximumSize = new Size(BodyWidth, 0),
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        head.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = new Padding(0) });
        if (action != null)
        {
            action.Anchor = AnchorStyles.Right;
            action.Margin = new Padding(0);
            head.Controls.Add(action);
        }
        card.Controls.Add(head);
        body.Margin = new Padding(0, 0, 0, 4);
        card.Controls.Add(body);
        if (hint != null)
            card.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(BodyWidth, 0), ForeColor = Theme.TextMuted });
        return card;
    }
}
