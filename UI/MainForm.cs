using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

/// <summary>The display control panel and its configuration pages.</summary>
internal sealed class MainForm : Form
{
    private readonly TrayApp _app;
    private readonly ToolTip _tips = new() { InitialDelay = 400, ReshowDelay = 200 };
    private readonly NavRail _rail = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly List<LayoutStack> _pages = new();
    private readonly Label _big = TextLabel("", size: 22, bold: true);
    private readonly Label _mode = TextLabel("", muted: true);
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label", DropDownWidth = 600 };
    private readonly Label _monitorName = TextLabel("");
    private readonly FlowLayoutPanel _presets = new() { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
    private readonly ChipGroup _rates = new() { AccessibleName = "Refresh rate", AccessibleRole = AccessibleRole.Grouping };
    private readonly CheckBox _showAll = new() { Text = "Show all rates", AutoSize = true };
    private readonly Label _displayStatus = TextLabel("", muted: true);
    private readonly ThemedSlider _bri = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, Dock = DockStyle.Fill, Height = 44, AccessibleName = "Brightness" };
    private readonly Label _briValue = TextLabel("", size: 14);
    private readonly Label _briInfo = TextLabel("", muted: true);
    private readonly Button _briDown, _briUp;
    private readonly Toggle _auto = new() { Text = "", AccessibleName = "Enable auto power-saver" };
    private readonly Label _status = TextLabel("", muted: true);
    private readonly SaverBar _bar = new();
    private readonly NumberBox _idle = Num(1, 240);
    private readonly NumberBox _restore = Num(1, 60);
    private readonly ComboBox _saverHz = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132 };
    private readonly ComboBox _saverBri = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132 };
    private TableLayoutPanel _saverFields;
    private readonly Toggle _profilesOn = new() { Text = "", AccessibleName = "Enable app profiles" };
    private readonly Label _profilesInfo = TextLabel("", muted: true);
    private PillButton _manageProfiles;
    private readonly LayoutStack _hotkeys = new();
    private string _hotkeySignature, _rateError;
    private readonly Toggle _alwaysOnTop = new() { Text = "", AccessibleName = "Enable always on top" };
    private readonly Label _alwaysOnTopStatus = TextLabel("", muted: true);
    private PillButton _unpinAll;
    private AlwaysOnTopSettingsForm _pinEditor;
    private readonly Toggle _notify = new() { Text = "", AccessibleName = "Notify on automatic switches" };
    private readonly Toggle _startup = new() { Text = "", AccessibleName = "Start with Windows minimized to tray" };
    private readonly Toggle _briRestore = new() { Text = "", AccessibleName = "Restore each monitor's brightness at startup" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Label _pauseStatus = TextLabel("", muted: true);
    private readonly PillButton _pauseAction = new() { Text = "Pause…" };
    private readonly PillButton _retryRestore = new() { Text = "Retry restore" };
    private bool _loading, _hintShown, _positionRestored;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public MainForm(TrayApp app)
    {
        _app = app;
        Text = "HzSwitcher";
        Font = Theme.Font();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(780, 560);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _briDown = ActionButton("−", (_, _) => StepBrightness(-5));
        _briUp = ActionButton("+", (_, _) => StepBrightness(5));
        foreach (var button in new[] { _briDown, _briUp })
        { button.AutoSize = false; button.Size = new Size(44, 44); button.Margin = Padding.Empty; }

        BuildDisplayPage();
        BuildAutomationPage();
        BuildShortcutsPage();
        BuildGeneralPage();
        foreach (var page in _pages) _content.Controls.Add(page);
        Controls.Add(_content);
        _rail.Footer = "v" + Application.ProductVersion.Split('+')[0];
        _rail.Add("Display", "\uE7F4");
        _rail.Add("Automation", "\uE945");
        _rail.Add("Shortcuts", "\uE765");
        _rail.Add("General", "\uE713");
        _rail.SelectedIndexChanged += (_, _) => ShowPage(_rail.SelectedIndex);
        Controls.Add(_rail);
        ShowPage(0);
        _rail.TabIndex = 0;
        _content.TabIndex = 1;
        _monitor.AccessibleName = "Monitor";
        _idle.AccessibleName = "Idle or video timeout in minutes";
        _restore.AccessibleName = "Continuous activity before restoring in seconds";
        _saverHz.AccessibleName = "Saver refresh rate";
        _saverBri.AccessibleName = "Saver brightness";
        _briDown.AccessibleName = "Decrease brightness";
        _briUp.AccessibleName = "Increase brightness";
        WireEvents();
        _timer.Tick += (_, _) => UpdateStatus();
        VisibleChanged += OnVisibleChanged;
        Shown += (_, _) => Theme.FitWorkingArea(this);
        DpiChanged += (_, _) => BeginInvoke(() => Theme.FitWorkingArea(this));
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Theme.RoundCorners(this);
    }

    private void BuildDisplayPage()
    {
        var page = NewPage("Display");
        _monitor.Margin = new Padding(0, 0, 0, 4);
        _monitorName.Margin = new Padding(0, 0, 0, 4);
        page.Controls.Add(_monitor);
        page.Controls.Add(_monitorName);
        _mode.Margin = new Padding(0, 0, 0, 20);
        page.Controls.Add(_mode);
        var surface = new Card { Margin = Padding.Empty };
        surface.Controls.Add(Heading("Refresh rate", _big));
        _rates.Margin = new Padding(0, 8, 0, 0);
        surface.Controls.Add(_rates);
        var more = Heading("", _showAll);
        more.Margin = new Padding(0, 0, 0, 16);
        surface.Controls.Add(more);
        surface.Controls.Add(Heading("Presets", ActionButton("Edit…", (_, _) => _app.EditPresets(), quiet: true)));
        _presets.Margin = new Padding(0, 4, 0, 20);
        surface.Controls.Add(_presets);
        surface.Controls.Add(Heading("Brightness", _briValue));
        var brightness = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 8, 0, 0) };
        brightness.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brightness.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brightness.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bri.Margin = new Padding(8, 0, 8, 0);
        brightness.Controls.Add(_briDown);
        brightness.Controls.Add(_bri);
        brightness.Controls.Add(_briUp);
        surface.Controls.Add(brightness);
        _briInfo.Margin = new Padding(0, 8, 0, 0);
        surface.Controls.Add(_briInfo);
        page.Controls.Add(surface);
        _displayStatus.AutoSize = false;
        _displayStatus.Height = 48;
        _displayStatus.Margin = new Padding(0, 12, 0, 0);
        page.Controls.Add(_displayStatus);
        _tips.SetToolTip(_briInfo, "Software dimming only darkens the picture and may conflict with Night light or HDR. Enable DDC/CI in the monitor menu for backlight control.");
    }

    private void BuildAutomationPage()
    {
        var page = NewPage("Automation");
        var pauseActions = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        pauseActions.Controls.Add(_pauseAction);
        pauseActions.Controls.Add(_retryRestore);
        _pauseAction.Click += (_, _) =>
        {
            if (_app.Pause.IsPaused) _app.ResumeAutomation();
            else
            {
                var menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Font() };
                _app.AddPauseChoices(menu.Items);
                menu.Closed += (_, _) => menu.Dispose();
                menu.Show(_pauseAction, new Point(0, _pauseAction.Height));
            }
        };
        _retryRestore.Click += (_, _) => _app.RetryAutomationRestore();
        var pauseRow = Setting("Automatic changes", pauseActions);
        page.Controls.Add(pauseRow);
        _pauseStatus.Margin = new Padding(0, 0, 0, 16);
        page.Controls.Add(_pauseStatus);
        var saver = Section("Auto power-saver", _auto);
        _status.Margin = new Padding(0, 8, 0, 8);
        saver.Controls.Add(_status);
        _bar.Margin = new Padding(0, 0, 0, 8);
        saver.Controls.Add(_bar);
        _saverFields = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        _saverFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _saverFields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(_saverFields, "Idle or video timeout", WithUnit(_idle, "min"));
        AddRow(_saverFields, "Restore after activity", WithUnit(_restore, "sec"));
        AddRow(_saverFields, "Saver refresh rate", _saverHz);
        AddRow(_saverFields, "Saver brightness", _saverBri);
        saver.Controls.Add(_saverFields);
        var detection = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        detection.Controls.Add(ActionButton("Detection settings…", (_, _) => _app.ShowAdvancedSettings(), quiet: true));
        saver.Controls.Add(detection);
        page.Controls.Add(saver);
        var profiles = Section("App profiles", _profilesOn);
        _profilesInfo.Margin = new Padding(0, 8, 0, 12);
        profiles.Controls.Add(_profilesInfo);
        _manageProfiles = ActionButton("Manage profiles…", (_, _) => _app.EditProfiles());
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        actions.Controls.Add(_manageProfiles);
        profiles.Controls.Add(actions);
        page.Controls.Add(profiles);
    }

    private void BuildShortcutsPage()
    {
        var page = NewPage("Shortcuts");
        page.Controls.Add(_hotkeys);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 20, 0, 0) };
        actions.Controls.Add(ActionButton("Edit shortcuts…", (_, _) => _app.EditHotkeys()));
        page.Controls.Add(actions);
    }

    private void BuildGeneralPage()
    {
        var page = NewPage("General");
        var behavior = Section("Behavior");
        behavior.Controls.Add(Setting("Notify on automatic switches", _notify));
        behavior.Controls.Add(Setting("Start with Windows in tray", _startup));
        behavior.Controls.Add(Setting("Restore brightness at startup", _briRestore));
        page.Controls.Add(behavior);
        var pin = Section("Always on top", _alwaysOnTop);
        _alwaysOnTopStatus.Margin = new Padding(0, 8, 0, 12);
        pin.Controls.Add(_alwaysOnTopStatus);
        var pinActions = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        pinActions.Controls.Add(ActionButton("Configure…", (_, _) => ShowAlwaysOnTopSettings()));
        _unpinAll = ActionButton("Unpin all", (_, _) => _app.UnpinAll());
        pinActions.Controls.Add(_unpinAll);
        pin.Controls.Add(pinActions);
        page.Controls.Add(pin);
        var maintenance = Section("Maintenance");
        var tools = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        tools.Controls.Add(ActionButton("Diagnostics…", (_, _) => _app.ShowDiagnostics()));
        tools.Controls.Add(ActionButton("Open log", (_, _) => _app.OpenLog()));
        tools.Controls.Add(ActionButton("Open settings file", (_, _) => _app.OpenSettingsFile()));
        maintenance.Controls.Add(tools);
        page.Controls.Add(maintenance);
        var footer = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        footer.Controls.Add(ActionButton("Hide to tray", (_, _) => Hide(), quiet: true));
        footer.Controls.Add(ActionButton("Exit app", (_, _) => _app.ExitApp(), quiet: true));
        page.Controls.Add(footer);
    }

    private LayoutStack NewPage(string title)
    {
        var page = new LayoutStack { Dock = DockStyle.Fill, AutoSize = false, AutoScroll = true,
            Padding = new Padding(24, 20, 24, 12), Visible = false };
        var heading = TextLabel(title, size: 18, bold: true);
        heading.Margin = new Padding(0, 0, 0, 20);
        page.Controls.Add(heading);
        _pages.Add(page);
        return page;
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;
    }

    private void WireEvents()
    {
        _tips.SetToolTip(_monitor, "Monitor controlled by HzSwitcher");
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
        _auto.CheckedChanged += (_, _) => { if (!_loading) { _app.Settings.AutoEnabled = _auto.Checked; _app.SaveSettings(); if (!_auto.Checked) _app.ExitSaverNow(); RefreshAll(); } };
        _idle.ValueChanged += (_, _) => { if (!_loading) { _app.Settings.IdleMinutes = _idle.Value; _app.SaveSettings(); } };
        _restore.ValueChanged += (_, _) => { if (!_loading) { _app.Settings.RestoreSeconds = _restore.Value; _app.SaveSettings(); } };
        _saverHz.SelectedIndexChanged += (_, _) => { if (!_loading && _saverHz.SelectedItem is int hz) { _app.Settings.SaverHz = hz; _app.SaveSettings(); } };
        _bri.Scroll += (_, _) => { if (!_loading) { _app.SetBrightness(_bri.Value); _briValue.Text = _bri.Value + "%"; } };
        _bri.InteractionCompleted += (_, _) => _app.CommitBrightness();
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
        _startup.CheckedChanged += (_, _) => { if (!_loading) _app.SetStartup(_startup.Checked); };
    }


    private void OnVisibleChanged(object sender, EventArgs e)
    {
        if (Visible)
        {
            if (!_positionRestored)
            {
                _positionRestored = true;
                var s = _app.Settings;
                if (s.WindowWidth > 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(new Point(s.WindowLeft, s.WindowTop))))
                { StartPosition = FormStartPosition.Manual; Location = new Point(s.WindowLeft, s.WindowTop); }
                Theme.FitWorkingArea(this);
                ActiveControl = _rail;
            }
            RefreshAll();
            _timer.Start();
        }
        else { _timer.Stop(); SaveWindowPosition(); }
    }

    private void SaveWindowPosition()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
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

    public void RefreshAll()
    {
        if (!Visible) return;
        _loading = true;
        SuspendLayout();
        try
        {
            var s = _app.Settings;
            int cur = _app.Display.GetCurrentHz();
            _big.Text = cur + " Hz";
            _mode.Text = _app.Display.CurrentModeText().Split(" @ ")[0];
            var monitors = _app.Display.GetMonitors();
            if (!_monitor.Items.Cast<MonitorInfo>().SequenceEqual(monitors))
            {
                _monitor.Items.Clear();
                foreach (var m in monitors) _monitor.Items.Add(m);
            }
            var selectedMonitor = monitors.FirstOrDefault(m => m.Device == _app.Display.Device);
            _monitor.SelectedItem = selectedMonitor;
            _monitor.Visible = monitors.Count > 1;
            _monitorName.Visible = monitors.Count <= 1;
            _monitorName.Text = selectedMonitor?.Label ?? "No monitor detected";
            _monitorName.AccessibleName = _monitorName.Text;
            _monitor.AccessibleDescription = selectedMonitor?.Label;
            _tips.SetToolTip(_monitor, selectedMonitor?.Label);
            _tips.SetToolTip(_monitorName, _monitorName.Text);

            var visibleRates = _app.VisibleRates();
            if (!_rates.Controls.Cast<PillButton>().Select(b => int.Parse(b.Name)).SequenceEqual(visibleRates))
            {
                var oldFocused = _rates.Controls.Cast<PillButton>().FirstOrDefault(b => b.Focused)?.Name;
                ClearAndDispose(_rates);
                foreach (int hz in visibleRates)
                {
                    int target = hz;
                    var button = ActionButton(hz + " Hz", (_, _) => ApplyRate(target));
                    button.Name = hz.ToString();
                    button.AccessibleName = hz + " hertz";
                    button.AutoSize = false;
                    button.Size = new Size(Theme.Dp(this, 86), Theme.Dp(this, 40));
                    _rates.Controls.Add(button);
                }
                if (oldFocused != null) (_rates.Controls.Find(oldFocused, false).FirstOrDefault() ?? _rates.Controls.Cast<Control>().FirstOrDefault())?.Focus();
            }
            foreach (PillButton button in _rates.Controls) button.Selected = button.Name == cur.ToString();
            if (_presets.Controls.Count != Math.Max(1, s.Presets.Count) || (s.Presets.Count == 0) != (_presets.Controls[0] is Label))
            {
                ClearAndDispose(_presets);
                if (s.Presets.Count == 0) _presets.Controls.Add(TextLabel("No presets", muted: true));
                else for (int i = 0; i < s.Presets.Count; i++)
                {
                    int index = i;
                    _presets.Controls.Add(ActionButton("", (_, _) => ApplyRate(_app.ResolvePreset(_app.Settings.Presets[index]))));
                }
            }
            for (int i = 0; i < s.Presets.Count; i++)
            {
                var p = s.Presets[i];
                var button = (PillButton)_presets.Controls[i];
                button.Text = p.Name + "  " + _app.ResolvePreset(p) + " Hz";
                button.MaximumSize = new Size(Math.Max(1, _presets.Width), 0);
                _tips.SetToolTip(button, button.Text);
            }
            _showAll.Checked = s.ShowAllRates;
            _auto.Checked = s.AutoEnabled;
            _saverFields.Enabled = s.AutoEnabled;
            _idle.Value = Math.Clamp(s.IdleMinutes, 1, 240);
            _restore.Value = Math.Clamp(s.RestoreSeconds, 1, 60);
            var supported = _app.Display.GetSupportedHz();
            if (!_saverHz.Items.Cast<int>().SequenceEqual(supported))
            { _saverHz.Items.Clear(); foreach (int hz in supported) _saverHz.Items.Add(hz); }
            _saverHz.SelectedItem = s.SaverHz;
            _saverBri.SelectedIndex = s.SaverBrightness < 0 ? 0 : Math.Clamp((int)Math.Round(s.SaverBrightness / 10.0), 1, 9);
            _briRestore.Checked = s.RestoreBrightnessOnStart;
            _profilesOn.Checked = s.ProfilesEnabled;
            var active = s.Profiles.Where(p => p.Enabled).ToList();
            _profilesInfo.Text = s.Profiles.Count == 0 ? "No app profiles" : active.Count + " enabled" +
                (active.Count == 0 ? "" : "\n" + string.Join("\n", active.Take(3).Select(p => Match.Normalize(p.Process) + "    " + (p.Hz <= 0 ? "Highest" : p.Hz + " Hz")))) +
                (active.Count > 3 ? "\n" + (active.Count - 3) + " more" : "");
            _manageProfiles.Text = s.Profiles.Count == 0 ? "Add app…" : "Manage profiles…";
            RefreshShortcuts();
            _alwaysOnTop.Checked = s.AlwaysOnTopEnabled;
            RefreshAlwaysOnTop();
            _notify.Checked = s.NotifyOnAutoSwitch;
            _startup.Checked = _app.IsStartupEnabled();
        }
        finally { _loading = false; ResumeLayout(true); }
        RefreshBrightness();
        UpdateStatus();
        Invalidate(true);
    }

    private void ApplyRate(int hz)
    {
        _rateError = null;
        _displayStatus.Text = "Applying " + hz + " Hz…";
        _displayStatus.Refresh();
        _app.SetHz(hz);
        int actual = _app.Display.GetCurrentHz();
        if (actual != hz) _rateError = "Could not apply " + hz + " Hz. Current rate: " + actual + " Hz.";
        UpdateStatus();
    }

    private void RefreshShortcuts()
    {
        var bindings = _app.Settings.Hotkeys;
        string signature = string.Join("\n", bindings.Select(b => b.Keys + "|" + b.Action + "|" + b.Hz + "|" + b.Preset));
        if (signature == _hotkeySignature) return;
        _hotkeySignature = signature;
        ClearAndDispose(_hotkeys);
        if (bindings.Count == 0) _hotkeys.Controls.Add(TextLabel("No shortcuts configured", muted: true));
        foreach (var binding in bindings)
        {
            string action = binding.Action switch
            {
                "SetHz" => "Set " + binding.Hz + " Hz",
                "SetMax" => "Highest refresh rate",
                "Preset" => "Apply " + binding.Preset,
                "Cycle" => "Next preset",
                "ToggleAlwaysOnTop" => "Pin or unpin active window",
                "ToggleAuto" => "Toggle auto power-saver",
                _ => binding.Action,
            };
            var keys = TextLabel(binding.Keys);
            keys.BackColor = Theme.SurfaceRaised;
            keys.Padding = new Padding(10, 6, 10, 6);
            keys.AccessibleName = action + ": " + binding.Keys;
            keys.Tag = "keycap";
            var row = Heading(action, keys);
            row.Controls[0].Font = Theme.Font();
            row.Margin = new Padding(0, 0, 0, 12);
            _hotkeys.Controls.Add(row);
        }
        Theme.Style(_hotkeys);
    }

    private void RefreshBrightness()
    {
        if (!Visible || _bri.Capture) return;
        var brightness = _app.Brightness;
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _bri.Minimum = brightness.Mode == BrightnessMode.Software ? BrightnessService.SoftwareFloor : 0;
            _bri.Value = Math.Clamp(brightness.Get(), _bri.Minimum, 100);
            _briValue.Text = brightness.IsSupported ? _bri.Value + "%" : "Unavailable";
        }
        finally { _loading = wasLoading; }
        _bri.Enabled = _briDown.Enabled = _briUp.Enabled = brightness.IsSupported;
        _briRestore.Enabled = brightness.IsSupported;
        _saverBri.Enabled = brightness.IsSupported && _app.Settings.AutoEnabled;
        _briInfo.Text = !brightness.IsSupported ? "This monitor does not allow brightness control."
            : brightness.Mode == BrightnessMode.Software ? "Software dimming: does not change the backlight.\nEnable DDC/CI in the monitor menu for backlight control. May conflict with Night light or HDR." : "";
        _briInfo.Visible = _briInfo.Text.Length > 0;
    }

    private void StepBrightness(int delta)
    {
        _app.SetBrightness(Math.Clamp(_app.Brightness.Get() + delta, _bri.Minimum, 100));
        _app.CommitBrightness();
        RefreshBrightness();
    }

    internal void ShowAlwaysOnTopSettings()
    {
        _rail.SelectedIndex = 3;
        if (_pinEditor != null) { _pinEditor.Activate(); return; }
        using var editor = new AlwaysOnTopSettingsForm(_app);
        _pinEditor = editor;
        try { editor.ShowDialog(this); }
        finally { _pinEditor = null; RefreshAll(); }
    }

    internal void RefreshAlwaysOnTop()
    {
        _alwaysOnTopStatus.Text = _app.AlwaysOnTopStatus;
        if (_unpinAll != null) _unpinAll.Enabled = _app.HasPinnedWindows;
        _pinEditor?.RefreshStatus();
    }

    private void UpdateStatus()
    {
        _pauseAction.Text = _app.Pause.IsPaused ? "Resume" : "Pause…";
        _pauseStatus.Text = _app.Pause.IsPaused ? _app.Pause.Description : "";
        _pauseStatus.Visible = _app.Pause.IsPaused;
        _retryRestore.Visible = _app.Pause.RestorationPending;
        _status.Text = _app.Auto.Status;
        _status.Visible = !_app.Pause.IsPaused;
        _bar.Visible = !_app.Pause.IsPaused && _app.Settings.AutoEnabled && _app.Auto.Progress > 0;
        _bar.Set(_app.Auto.Progress, _app.Auto.State == SaverState.Saver ? Theme.Saver : Theme.Accent);
        bool saver = _app.Auto.State == SaverState.Saver;
        _displayStatus.Text = _rateError ?? (_app.Pause.IsPaused ? _app.Pause.Description : saver ? "Saver active: " + _app.Display.GetCurrentHz() + " Hz; " + _app.Auto.RestoreHz + " Hz returns on activity."
            : _app.Profiles.ActiveProcess != null ? "App profile: " + _app.Profiles.ActiveProcess : "");
        _displayStatus.Tag = _rateError != null ? "error" : "muted";
        _displayStatus.ForeColor = _rateError != null ? Theme.DangerText : Theme.TextMuted;
    }

    internal static Label TextLabel(string text, bool muted = false, float size = 10.5f, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Margin = Padding.Empty, Font = Theme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = muted ? Theme.TextMuted : Theme.Text, Tag = muted ? "muted" : null,
    };

    private static NumberBox Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 112, Height = 36 };

    internal static TableLayoutPanel Heading(string title, Control action = null)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = TextLabel(title, size: 12, bold: true);
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 6, 8, 6);
        row.Controls.Add(label);
        if (action != null) { action.Anchor = AnchorStyles.Right; action.Margin = Padding.Empty; row.Controls.Add(action); }
        row.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(1, row.Width - (action?.Width ?? 0) - 16), 0);
        return row;
    }

    private static LayoutStack Section(string title, Control action = null)
    {
        var section = new LayoutStack { Margin = new Padding(0, 0, 0, 24) };
        section.Controls.Add(Heading(title, action));
        return section;
    }

    internal static TableLayoutPanel Setting(string title, Control input, string hint = null)
    {
        var labels = new LayoutStack { Margin = new Padding(0, 8, 16, 8), Dock = DockStyle.Fill };
        labels.Controls.Add(TextLabel(title));
        if (hint != null) { var text = TextLabel(hint, muted: true, size: 9.5f); text.Margin = new Padding(0, 4, 0, 0); labels.Controls.Add(text); }
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.Anchor = AnchorStyles.Right;
        input.Margin = new Padding(0, 8, 0, 8);
        row.Controls.Add(labels);
        row.Controls.Add(input);
        return row;
    }

    private static FlowLayoutPanel WithUnit(Control input, string unit)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        input.Margin = new Padding(0, 0, 8, 0);
        var label = TextLabel(unit, muted: true);
        label.Margin = new Padding(0, 8, 0, 0);
        row.Controls.Add(input);
        row.Controls.Add(label);
        return row;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var text = TextLabel(label);
        text.Anchor = AnchorStyles.Left;
        text.Margin = new Padding(0, 8, 12, 8);
        input.Anchor = AnchorStyles.Right;
        input.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(text);
        table.Controls.Add(input);
        table.SizeChanged += (_, _) => text.MaximumSize = new Size(Math.Max(1, table.Width - input.Width - 12), 0);
    }

    private static PillButton ActionButton(string text, EventHandler action, bool quiet = false)
    {
        var button = new PillButton { Text = text, Style = quiet ? PillStyle.Quiet : PillStyle.Neutral };
        button.Click += action;
        return button;
    }

    private static void ClearAndDispose(Control parent)
    {
        var controls = parent.Controls.Cast<Control>().ToArray();
        parent.Controls.Clear();
        foreach (var control in controls) control.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tips.Dispose();
            _app.Brightness.Changed -= RefreshBrightness;
        }
        base.Dispose(disposing);
    }
}
