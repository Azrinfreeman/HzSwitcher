using HzSwitcher.Services;
using HzSwitcher.UI;
using HzSwitcher.UI.Controls;
using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HzSwitcher;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "HzSwitcher";

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly Settings _settings = SettingsStore.Load();
    private readonly DisplayService _display;
    private readonly BrightnessService _brightness;
    private readonly MediaDetector _media;
    private readonly AutomationCoordinator _automation;
    private AlwaysOnTopService _alwaysOnTop;
    private string _alwaysOnTopFailure;
    private AlwaysOnTopBorderService _pinBorders;
    private AlwaysOnTopSoundService _pinSounds;
    private HzSwitcher.Native.WindowMouseHook _pinMouse;
    private WindowSystemMenuService _systemMenus;
    private HzSwitcher.Native.WindowMenuEventObserver _menuEvents;
    private System.Windows.Forms.Timer _menuPump;
    private string _systemMenuFailure;
    private readonly WindowPinMenu _pinMenu = new();
    private readonly HashSet<int> _registeredHotkeys = new();
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Control _ui = new();
    private readonly NotifyIcon _tray = new() { Visible = false };
    private readonly MainForm _main;
    private readonly EventWaitHandle _showSignal = new(false, EventResetMode.AutoReset, Program.ShowEventName);
    private RegisteredWaitHandle _showWait;
    private IntPtr _iconHandle;
    private int _preSaverBrightness = -1;
    private string _saverBrightnessTarget;
    private int _saverBrightnessLevel;
    private bool _restoringBrightness;
    private bool _brightnessDirty;
    private ThemedSlider _trayBar;
    private Label _trayBarLabel;

    internal Settings Settings => _settings;
    internal DisplayService Display => _display;
    internal BrightnessService Brightness => _brightness;
    internal AutomationCoordinator Automation => _automation;

    public TrayApp(bool startHidden)
    {
        _ = _ui.Handle;   // force handle creation so BeginInvoke works from other threads

        _display = new DisplayService(() => _settings.MonitorDevice);
        if (string.IsNullOrEmpty(_settings.MonitorDevice))
        { _settings.MonitorDevice = _display.Device; SettingsStore.Save(_settings); }
        _brightness = new BrightnessService(() => _display.Device,
            key => _settings.MonitorBrightness.TryGetValue(key, out int v) ? v : -1, _display.TargetIdentity);
        _brightness.Changed += OnBrightnessChanged;
        _media = new MediaDetector(() => _settings);
        _automation = new AutomationCoordinator(_settings, _display, _media, _display.TargetIdentity, Log.Dir);
        _automation.Changed += RefreshTray;
        _automation.Notice += OnNotice;
        _automation.CancelDeferredWrites += _brightness.CancelAutomaticPending;
        _automation.SaverChanged += active => { if (active) OnSaverStarted(); else OnSaverEnded(); };
        _main = new MainForm(this);
        if (_settings.AlwaysOnTopEnabled) InitializeAlwaysOnTop();

        var menu = new ContextMenuStrip();
        ToolStripManager.Renderer = new ThemedMenuRenderer();
        menu.BackColor = Theme.Surface;
        menu.ForeColor = Theme.Text;
        menu.Font = new Font("Segoe UI", 10f);
        Theme.RoundCorners(menu);
        menu.Opening += (_, _) => BuildMenu(menu);
        menu.Closed += (_, _) => CommitBrightness();
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        Theme.Watch();
        Theme.AccentChanged += () => _ui.BeginInvoke(RefreshTray);
        _hotkeys.HotkeyPressed += OnHotkey;
        var failed = ApplyHotkeys();
        if (failed.Count > 0) Balloon("Hotkey unavailable", "Could not register: " + string.Join(", ", failed));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // A second launch signals this event; open the window in response.
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => _ui.BeginInvoke(ShowMain), null, Timeout.Infinite, false);

        _tray.Visible = true;
        RestoreBrightnessAtStart();
        RefreshTray();
        if (!startHidden) ShowMain();
        Log.Write($"Started. Monitor {_display.Device}, mode {_display.CurrentModeText()}; supported: {string.Join(", ", _display.GetSupportedHz())}");
    }

    // ---------- rates, presets, monitors ----------

    /// <summary>Rates worth showing: hides sub-60 and NTSC duplicates (59, 119…) unless "show all" is on.</summary>
    internal List<int> VisibleRates()
    {
        var all = _display.GetSupportedHz();
        if (_settings.ShowAllRates) return all;
        var keep = all.Where(h => h >= 60 && !all.Contains(h + 1)).ToHashSet();
        keep.Add(_display.GetCurrentHz());
        keep.Add(_settings.SaverHz);
        return all.Where(keep.Contains).ToList();
    }

    internal int ResolvePreset(Preset p) => _display.Resolve(p.Hz);

    internal void ApplyPreset(Preset p) => SetHz(ResolvePreset(p));

    internal void SelectMonitor(string device)
    {
        if (_automation.Transitions.Pending != null || _settings.PendingBrightness >= 0)
        { Balloon("Finish display change", "Review the pending transition before selecting another monitor."); return; }
        if (_automation.SaverActive)
        {
            Balloon("Temporary mode retained", "The previous monitor keeps its current rate. Select it again to adjust it manually.");
            _preSaverBrightness = -1;
        }
        CommitBrightness();
        _settings.MonitorDevice = device;
        SettingsStore.Save(_settings);
        _brightness.Invalidate();
        _brightness.RefreshAsync();
        _automation.Tick();
        Log.Write($"Monitor set to {_display.Device}");
        RefreshTray();
    }

    internal void SetHz(int hz)
    {
        if (!_display.GetSupportedHz().Contains(hz)) { Balloon("Not supported", $"{hz} Hz is not available on this monitor."); return; }
        if (!_automation.SetManualRate(hz))
        {
            Balloon("Refresh rate", _automation.Status);
        }
        RefreshTray();
    }

    private void CyclePreset()
    {
        var rates = _settings.Presets.Select(ResolvePreset).Distinct().OrderBy(x => x).ToList();
        if (rates.Count == 0) return;
        int cur = _display.GetCurrentHz();
        SetHz(rates.FirstOrDefault(r => r > cur, rates[0]));
    }

    private void ToggleAuto()
    {
        _settings.AutoEnabled = !_settings.AutoEnabled;
        SettingsStore.Save(_settings);
        _automation.Tick();
        Balloon("Auto power-saver", _settings.AutoEnabled ? "On" : "Off");
        RefreshTray();
    }

    // ---------- brightness ----------

    private void RestoreBrightnessAtStart()
    {
        // Legacy pending brightness cannot establish its original target. Review it instead of redirecting recovery.
        if (_settings.PendingBrightness >= 0 || _settings.PreviewMode || _settings.AutomationPaused)
            _brightness.RefreshAsync();
        else if (_settings.RestoreBrightnessOnStart && _settings.MonitorBrightness.TryGetValue(_brightness.Key, out int saved))
            _brightness.RequestSet(saved, automatic: true);
        else
            _brightness.RefreshAsync();
    }

    /// <summary>User-driven change (slider or buttons). Not saved until CommitBrightness.</summary>
    internal void SetBrightness(int percent)
    {
        _brightness.RequestSet(percent);
        _brightnessDirty = true;
        // A change made while the saver is active is the level to return to.
        if (_preSaverBrightness >= 0) _preSaverBrightness = Math.Clamp(percent,
            _brightness.Mode == BrightnessMode.Software ? BrightnessService.SoftwareFloor : 0, 100);
        if (_preSaverBrightness >= 0) _saverBrightnessLevel = _preSaverBrightness;
    }

    /// <summary>Remembers the current level for this monitor.</summary>
    internal void CommitBrightness()
    {
        if (!_brightnessDirty) return;
        if (_brightness.IsBusy || !_brightness.LastWriteSucceeded) return;
        _brightnessDirty = false;
        if (!_brightness.IsSupported) return;
        int level = _preSaverBrightness >= 0 ? _preSaverBrightness : _brightness.Get();
        _settings.MonitorBrightness[_brightness.Key] = level;
        if (_preSaverBrightness >= 0) _settings.PendingBrightness = level;
        SettingsStore.Save(_settings);
    }

    private void OnSaverStarted()
    {
        if (_settings.PreviewMode) return;
        int target = _settings.SaverBrightness;
        if (target < 0 || !_brightness.IsSupported) return;
        _preSaverBrightness = _brightness.Get();
        _settings.PendingBrightness = _preSaverBrightness;
        _settings.PendingBrightnessTarget = _saverBrightnessTarget = _display.TargetIdentity();
        _saverBrightnessLevel = target;
        if (!SettingsStore.TrySave(_settings)) { _preSaverBrightness = -1; return; }
        _brightness.RequestSet(target, automatic: true);
    }

    private void OnSaverEnded()
    {
        if (_settings.PreviewMode) return;
        if (_preSaverBrightness < 0) return;
        if (_display.TargetIdentity() != _saverBrightnessTarget) return;
        int restore = _preSaverBrightness;
        string target = _saverBrightnessTarget;
        _brightness.RefreshAsync(observed =>
        {
            if (!_automation.CanWriteAutomation || _display.TargetIdentity() != target || _preSaverBrightness != restore) return;
            if (Math.Abs(observed - _saverBrightnessLevel) > 2)
            {
                // Another control has superseded the automatic level. Yield instead of restoring over it.
                _preSaverBrightness = -1;
                _settings.PendingBrightness = -1; _settings.PendingBrightnessTarget = "";
                SettingsStore.Save(_settings);
                return;
            }
            _restoringBrightness = true;
            _brightness.RequestSet(restore, automatic: true);
            _preSaverBrightness = -1;
        });
    }

    private void OnBrightnessChanged()
    {
        if (_brightnessDirty) CommitBrightness();
        if (_restoringBrightness && _display.TargetIdentity() == _settings.PendingBrightnessTarget &&
            _brightness.LastWriteSucceeded && Math.Abs(_brightness.Get() - _settings.PendingBrightness) <= 2)
        {
            _restoringBrightness = false;
            _settings.PendingBrightness = -1; _settings.PendingBrightnessTarget = "";
            if (SettingsStore.TrySave(_settings)) _automation.BrightnessRecoveryCompleted();
        }
        if (_trayBar == null || _trayBar.IsDisposed || _trayBar.Capture) return;
        int v = Math.Clamp(_brightness.Get(), _trayBar.Minimum, _trayBar.Maximum);
        _trayBar.Value = v;
        _trayBarLabel.Text = BrightnessLabel(v);
    }

    private string BrightnessLabel(int v) =>
        (_brightness.Mode == BrightnessMode.Software ? "Brightness (software dimming)  " : "Brightness  ") + v + "%";

    private void AddBrightnessItem(ContextMenuStrip menu)
    {
        _trayBar = null;
        if (!_brightness.IsSupported)
        {
            menu.Items.Add(new ToolStripMenuItem("Brightness (not supported on this monitor)") { Enabled = false });
            return;
        }
        _brightness.RefreshAsync();   // pick up changes made with the monitor's own buttons

        int level = _brightness.Get();
        int W = Theme.Dp(_ui, 250), H = Theme.Dp(_ui, 62);
        var panel = new Panel { Size = new Size(W, H), BackColor = Theme.Surface };
        var label = new Label
        {
            AutoSize = false, Location = new Point(Theme.Dp(_ui, 14), Theme.Dp(_ui, 6)), Size = new Size(W - Theme.Dp(_ui, 28), Theme.Dp(_ui, 22)),
            Text = BrightnessLabel(level), ForeColor = Theme.Text, BackColor = Theme.Surface,
        };
        var bar = new ThemedSlider
        {
            Minimum = _brightness.Mode == BrightnessMode.Software ? BrightnessService.SoftwareFloor : 0, Maximum = 100,
            SmallChange = 5, LargeChange = 10, AccessibleName = "Brightness",
            Location = new Point(Theme.Dp(_ui, 6), Theme.Dp(_ui, 28)), Size = new Size(W - Theme.Dp(_ui, 12), Theme.Dp(_ui, 30)),
        };
        bar.Value = Math.Clamp(level, bar.Minimum, bar.Maximum);
        bar.Scroll += (_, _) => { SetBrightness(bar.Value); label.Text = BrightnessLabel(bar.Value); };
        bar.MouseUp += (_, _) => CommitBrightness();
        bar.KeyUp += (_, _) => CommitBrightness();
        panel.Controls.Add(label);
        panel.Controls.Add(bar);
        _trayBar = bar;
        _trayBarLabel = label;

        menu.Items.Add(new ToolStripControlHost(panel) { AutoSize = false, Size = panel.Size, Margin = Padding.Empty });
    }

    // ---------- hotkeys ----------

    /// <summary>(Re)registers all hotkeys from settings; returns the ones that could not be registered.</summary>
    internal List<string> ApplyHotkeys()
    {
        _hotkeys.UnregisterAll();
        _registeredHotkeys.Clear();
        var failed = new List<string>();
        for (int i = 0; i < _settings.Hotkeys.Count; i++)
        {
            if (_settings.Hotkeys[i].Action == "ToggleAlwaysOnTop" && (!_settings.AlwaysOnTopEnabled || _alwaysOnTop == null)) continue;
            if (_hotkeys.Register(i, _settings.Hotkeys[i].Keys)) _registeredHotkeys.Add(i);
            else failed.Add(_settings.Hotkeys[i].Keys);
        }
        return failed;
    }

    private void OnHotkey(int id)
    {
        if (id < 0 || id >= _settings.Hotkeys.Count || !_registeredHotkeys.Contains(id)) return;
        var b = _settings.Hotkeys[id];
        switch (b.Action)
        {
            case "SetHz": SetHz(b.Hz); break;
            case "SetMax": { var r = _display.GetSupportedHz(); if (r.Count > 0) SetHz(r.Max()); break; }
            case "Preset":
                {
                    var p = _settings.Presets.FirstOrDefault(x => x.Name.Equals(b.Preset, StringComparison.OrdinalIgnoreCase));
                    if (p != null) ApplyPreset(p); else Balloon("Hotkey", $"Preset \"{b.Preset}\" no longer exists.");
                    break;
                }
            case "Cycle": CyclePreset(); break;
            case "ToggleAuto": ToggleAuto(); break;
            case "ToggleAlwaysOnTop":
                if (_settings.AlwaysOnTopEnabled && _alwaysOnTop != null)
                    AlwaysOnTopNotice(_alwaysOnTop.ToggleForeground(), true);
                break;
        }
    }

    private void InitializeAlwaysOnTop()
    {
        if (_alwaysOnTop != null) return;
        try
        {
            _alwaysOnTop = new AlwaysOnTopService(new HzSwitcher.Native.WindowApi());
            _alwaysOnTop.Changed += () => _main.RefreshAlwaysOnTop();
            _pinBorders = new AlwaysOnTopBorderService(_alwaysOnTop, _settings, ShowPinMenu);
            _pinSounds = new AlwaysOnTopSoundService();
            _pinMouse = new HzSwitcher.Native.WindowMouseHook(_ui, (handle, point, frameClick) =>
            {
                if (_pinMenu.IsOpen) return;
                var overlayTarget = _pinBorders.TargetForOverlay(handle);
                if (overlayTarget.HasValue) { ShowPinMenu(overlayTarget.Value, point); return; }
                var state = _alwaysOnTop.Inspect(handle);
                if (state == null) { AlwaysOnTopNotice(new(PinStatus.Failed, null, "This window cannot be pinned.")); return; }
                var target = state.Value.Identity;
                if (frameClick)
                {
                    int? hit = HzSwitcher.Native.WindowFrameApi.HitTest(handle, point);
                    if (_alwaysOnTop.Inspect(handle)?.Identity != target) return;
                    if (!hit.HasValue || !HzSwitcher.Native.WindowFrameApi.IsFrameHit(hit.Value))
                    {
                        HzSwitcher.Native.WindowFrameApi.ReplayFrameClick(handle, point, hit);
                        return;
                    }
                }
                ShowPinMenu(target, point, frameClick);
            });
            _pinMouse.SetEnabled(_settings.AlwaysOnTopEnabled && _settings.AlwaysOnTopMouseMenu);
            _systemMenus = new WindowSystemMenuService(new HzSwitcher.Native.WindowSystemMenuApi(), _alwaysOnTop, result => AlwaysOnTopNotice(result, true));
            _menuPump = new System.Windows.Forms.Timer { Interval = 100 };
            _menuPump.Tick += (_, _) => _systemMenus.Pump();
            ApplySystemMenus();
            _alwaysOnTopFailure = null;
        }
        catch (Exception e) { _alwaysOnTopFailure = "Unavailable: window tracking failed."; Log.Write(e.ToString()); }
    }

    internal void SetAlwaysOnTop(bool enabled)
    {
        _settings.AlwaysOnTopEnabled = enabled;
        if (enabled)
        {
            InitializeAlwaysOnTop();
            if (!_settings.AlwaysOnTopShortcutInitialized)
            {
                if (!_settings.Hotkeys.Any(b => b.Action == "ToggleAlwaysOnTop"))
                    _settings.Hotkeys.Add(new HotkeyBinding { Keys = "Ctrl+Alt+T", Action = "ToggleAlwaysOnTop" });
                _settings.AlwaysOnTopShortcutInitialized = true;
            }
        }
        else if (_alwaysOnTop != null) AlwaysOnTopNotice(_alwaysOnTop.RestoreAll());
        UpdatePinPreferences();
        var failed = ApplyHotkeys();
        if (failed.Count > 0) Balloon("Hotkey unavailable", "Could not register: " + string.Join(", ", failed));
        _main.RefreshAll();
    }

    internal void UpdatePinPreferences()
    {
        _pinMenu.Close();
        _pinMouse?.SetEnabled(_settings.AlwaysOnTopEnabled && _settings.AlwaysOnTopMouseMenu);
        ApplySystemMenus();
        _pinBorders?.Refresh();
        SaveSettings();
        _main.RefreshAlwaysOnTop();
    }

    private void ApplySystemMenus()
    {
        if (_systemMenus == null) return;
        bool want = _settings.AlwaysOnTopEnabled && _settings.AlwaysOnTopSystemMenu;
        if (want && _menuEvents == null)
        {
            try
            {
                _menuEvents = new HzSwitcher.Native.WindowMenuEventObserver(_ui, _systemMenus.Handle, _systemMenus.Rescan);
                _systemMenuFailure = null;
            }
            catch (Exception e) { _systemMenuFailure = "System-menu events unavailable"; Log.Write(e.ToString()); }
        }
        if (!want || _menuEvents == null)
        {
            // Stop new event work before removing owned entries.
            _menuEvents?.Dispose(); _menuEvents = null;
            _menuPump.Stop();
            _systemMenus.SetEnabled(false);
        }
        else { _systemMenus.SetEnabled(true); _menuPump.Start(); }
        // Native menus replace the legacy frame interception; Shift+right-click stays as the fallback.
        if (_pinMouse != null) _pinMouse.FrameMenus = _menuEvents == null;
    }

    private void ShowPinMenu(WindowIdentity target, Point point) => ShowPinMenu(target, point, false);

    private void ShowPinMenu(WindowIdentity target, Point point, bool frameMenu)
    {
        if (!_settings.AlwaysOnTopEnabled || _alwaysOnTop == null) return;
        _pinMenu.Show(target, point, _alwaysOnTop, () => _settings.AlwaysOnTopEnabled,
            result => AlwaysOnTopNotice(result, true), UnpinAll, () => { ShowMain(); _main.ShowAlwaysOnTopSettings(); }, frameMenu);
    }

    internal string AlwaysOnTopStatus
    {
        get
        {
            var bindings = _settings.Hotkeys.Select((b, i) => (b, i)).Where(x => x.b.Action == "ToggleAlwaysOnTop").ToArray();
            string status = !_settings.AlwaysOnTopEnabled ? "Disabled" : _alwaysOnTopFailure ??
                (bindings.Length == 0 ? "No shortcut assigned" : bindings.All(x => _registeredHotkeys.Contains(x.i)) ? "Ready" : "Shortcut unavailable");
            return status + (_settings.AlwaysOnTopEnabled && _settings.AlwaysOnTopMouseMenu && _pinMouse?.Available != true ? " / Mouse menu unavailable" : "") +
                (_settings.AlwaysOnTopEnabled && _settings.AlwaysOnTopSystemMenu ? " / " + (_systemMenuFailure ?? _systemMenus?.Status ?? "System menus unavailable") : "") +
                (_pinBorders?.Available == false ? " / Outline unavailable" : "") + " - " + (_alwaysOnTop?.Count ?? 0) + " pinned" +
                (bindings.Length == 0 ? "" : "\nShortcut: " + string.Join(", ", bindings.Select(x => x.b.Keys)));
        }
    }

    internal bool HasPinnedWindows => (_alwaysOnTop?.Count ?? 0) > 0;
    internal void UnpinAll() { if (_alwaysOnTop != null) AlwaysOnTopNotice(_alwaysOnTop.RestoreAll(), true); }
    private void AlwaysOnTopNotice(PinResult result, bool userAction = false)
    {
        _pinSounds?.Play(result, userAction && _settings.AlwaysOnTopSound);
        string message = result.Message;
        Log.Write("Always on top: " + message);
        if (result.ShowNotification) Balloon("Always on top", message);
        _main.RefreshAlwaysOnTop();
    }

    // ---------- tray UI ----------

    private void OnNotice(string message)
    {
        if (_settings.NotifyOnAutoSwitch) Balloon("HzSwitcher", message);
    }

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        int cur = _display.GetCurrentHz();
        menu.Items.Add("Open HzSwitcher", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Info($"Current: {_display.CurrentModeText()}", menu, bold: true));
        menu.Items.Add(Info(_automation.Status, menu));
        menu.Items.Add("Why this rate? / History", null, (_, _) => ShowAutomationDetails());
        var pause = new ToolStripMenuItem("Pause automation");
        pause.DropDownItems.Add("15 minutes", null, (_, _) => _automation.Pause(15));
        pause.DropDownItems.Add("1 hour", null, (_, _) => _automation.Pause(60));
        pause.DropDownItems.Add("Until resumed", null, (_, _) => _automation.Pause(null));
        menu.Items.Add(pause);
        menu.Items.Add("Resume automation", null, (_, _) => _automation.Resume());
        var preview = new ToolStripMenuItem("Preview automation") { Checked = _settings.PreviewMode };
        preview.Click += (_, _) => SetPreview(!_settings.PreviewMode);
        menu.Items.Add(preview);
        var recording = new ToolStripMenuItem("Recording / presentation session") { Checked = _automation.RecordingSession };
        recording.Click += (_, _) => _automation.SetRecording(!_automation.RecordingSession);
        menu.Items.Add(recording);
        menu.Items.Add(new ToolStripSeparator());

        foreach (var preset in _settings.Presets)
        {
            var p = preset;
            int hz = ResolvePreset(p);
            var item = new ToolStripMenuItem($"{p.Name} ({hz} Hz)") { Checked = hz == cur };
            item.Click += (_, _) => ApplyPreset(p);
            menu.Items.Add(item);
        }
        if (_settings.Presets.Count > 0) menu.Items.Add(new ToolStripSeparator());

        var rates = new ToolStripMenuItem("All refresh rates");
        foreach (int hz in VisibleRates())
        {
            int target = hz;
            var item = new ToolStripMenuItem($"{hz} Hz") { Checked = hz == cur };
            item.Click += (_, _) => SetHz(target);
            rates.DropDownItems.Add(item);
        }
        menu.Items.Add(rates);

        AddBrightnessItem(menu);

        var monitors = _display.GetMonitors();
        if (monitors.Count > 1)
        {
            var sub = new ToolStripMenuItem("Monitor");
            foreach (var m in monitors)
            {
                var mon = m;
                var item = new ToolStripMenuItem(mon.Label) { Checked = mon.Device == _display.Device };
                item.Click += (_, _) => SelectMonitor(mon.Device);
                sub.DropDownItems.Add(item);
            }
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        var auto = new ToolStripMenuItem("Auto power-saver") { Checked = _settings.AutoEnabled };
        auto.Click += (_, _) => ToggleAuto();
        menu.Items.Add(auto);

        var profiles = new ToolStripMenuItem("App profiles") { Checked = _settings.ProfilesEnabled };
        profiles.Click += (_, _) => { _settings.ProfilesEnabled = !_settings.ProfilesEnabled; SettingsStore.Save(_settings); RefreshTray(); };
        menu.Items.Add(profiles);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) => SetStartup(!IsStartupEnabled());
        menu.Items.Add(startup);

        menu.Items.Add("Advanced settings…", null, (_, _) => ShowAdvancedSettings());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        Polish(menu.Items);
    }

    /// <summary>A non-clickable line of information in the menu.</summary>
    private static ToolStripMenuItem Info(string text, ContextMenuStrip menu, bool bold = false) => new(text)
    {
        Enabled = false, Tag = "info",
        ForeColor = bold ? Theme.Text : Theme.TextMuted,
        Font = bold ? new Font(menu.Font, FontStyle.Bold) : menu.Font,
    };

    /// <summary>Roomier rows and rounded corners for every menu level.</summary>
    private static void Polish(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is not ToolStripMenuItem mi) continue;
            mi.Padding = new Padding(2, 5, 2, 5);
            if (mi.DropDownItems.Count > 0)
            {
                Theme.RoundCorners(mi.DropDown);
                Polish(mi.DropDownItems);
            }
        }
    }

    internal void ShowMain()
    {
        _brightness.RefreshAsync();
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.RefreshAll();
    }

    internal void SaveSettings() => SettingsStore.Save(_settings);
    internal void ExitSaverNow() { _automation.Tick(); RefreshTray(); }
    internal void ShowAutomationDetails()
    {
        using var form = new AutomationDetailsForm(_automation, _settings, SetPreview, RecoverBrightness);
        form.ShowDialog(_main.Visible ? _main : null);
        RefreshTray();
    }

    private bool RecoverBrightness(bool restore)
    {
        if (_settings.PendingBrightness < 0) return true;
        if (restore)
        {
            if (string.IsNullOrEmpty(_settings.PendingBrightnessTarget) || _display.TargetIdentity() != _settings.PendingBrightnessTarget)
            { Balloon("Brightness recovery", "The original monitor cannot be confirmed. Keep the current brightness and adjust it manually."); return false; }
            _restoringBrightness = true;
            _brightness.RequestSet(_settings.PendingBrightness);
            return false; // asynchronous completion clears the durable record, never the request itself
        }
        _brightness.CancelPending(); _preSaverBrightness = -1; _restoringBrightness = false;
        _settings.PendingBrightness = -1; _settings.PendingBrightnessTarget = "";
        return SettingsStore.TrySave(_settings);
    }
    internal void SetPreview(bool preview)
    {
        if (preview && _automation.Transitions.Busy)
        { Balloon("Verification in progress", "Wait for the current display change to finish before entering preview."); return; }
        if (preview && _automation.SaverActive)
        {
            var choice = MessageBox.Show("Keep the current temporary rate and brightness while previewing?\n\nChoose No to stay in active mode and restore or choose a rate first.",
                "Preview automation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;
        }
        if (preview)
        {
            _brightness.CancelAutomaticPending();
            _brightness.Flush();
            if (_brightness.IsBusy)
            { Balloon("Brightness in progress", "Wait for the current hardware request to finish, then try again."); return; }
        }
        _automation.SetPreview(preview);
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs e) => _ui.BeginInvoke(() =>
    { _display.RefreshCatalog(); _brightness.Invalidate(); _brightness.RefreshAsync(); _automation.Tick(); RefreshTray(); });
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => _ui.BeginInvoke(() =>
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _automation.SetLocked(true);
        if (e.Reason == SessionSwitchReason.SessionUnlock) _automation.SetLocked(false);
    });
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Suspend) _automation.SetSuspended(true);
        if (e.Mode == PowerModes.Resume) { _display.RefreshCatalog(); _brightness.Invalidate(); _automation.SetSuspended(false); }
    });
    internal void OpenLog() => OpenPath(Log.FilePath);
    internal void Hint(string title, string text) => Balloon(title, text);

    internal void ShowAdvancedSettings()
    {
        using var form = new SettingsForm(_settings, _display.GetSupportedHz());
        form.ShowDialog(_main.Visible ? _main : null);
        RefreshTray();
    }

    internal void EditPresets()
    {
        using var form = new PresetsForm(_settings, _display.GetSupportedHz());
        form.ShowDialog(_main.Visible ? _main : null);
        RefreshTray();
    }

    internal void EditHotkeys()
    {
        using var form = new HotkeysForm(_settings, _display.GetSupportedHz());
        if (form.ShowDialog(_main.Visible ? _main : null) == DialogResult.OK)
        {
            var failed = ApplyHotkeys();
            if (failed.Count > 0) Balloon("Hotkey unavailable", "Could not register: " + string.Join(", ", failed));
        }
        RefreshTray();
    }

    internal void EditProfiles()
    {
        using var form = new ProfilesForm(_settings, _display.GetSupportedHz());
        form.ShowDialog(_main.Visible ? _main : null);
        RefreshTray();
    }

    internal void RefreshTray()
    {
        _main?.RefreshAll();
        int hz = _display.GetCurrentHz();
        string text = $"HzSwitcher: {hz} Hz - {_automation.Status}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        SetIcon(hz.ToString(), _automation.SaverActive ? Theme.Saver : _automation.ActiveProcess != null ? Theme.Warn : null);
    }

    /// <summary>Accent badge with the rate; a corner dot marks the saver (green) or an active profile (amber).</summary>
    private void SetIcon(string text, Color? dot)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);   // draw at the real size: no blurry downscale
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Gfx.FillRound(g, new RectangleF(0, 0, size, size), size * 0.28f, Theme.Accent);
            // Largest bold size at which the whole number fits the badge.
            float px = size * 0.74f;
            Font font;
            while (true)
            {
                font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
                if (px <= 6f || g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width <= size * 0.92f) break;
                font.Dispose();
                px -= 0.5f;
            }
            using var fontScope = font;
            using var fore = new SolidBrush(Theme.AccentText);
            using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, fore, new RectangleF(0, 0, size, size), fmt);
            if (dot.HasValue)
            {
                float d = size * 0.36f;
                var r = new RectangleF(size - d, size - d, d, d);
                using var ring = new SolidBrush(Theme.Bg);
                using var fill = new SolidBrush(dot.Value);
                g.FillEllipse(ring, RectangleF.Inflate(r, size * 0.06f, size * 0.06f));
                g.FillEllipse(fill, r);
            }
        }
        IntPtr handle = bmp.GetHicon();
        _tray.Icon = Icon.FromHandle(handle);
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _iconHandle = handle;
    }

    private void Balloon(string title, string text)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(3000);
    }

    // ---------- startup / misc ----------

    internal bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) != null;
    }

    internal void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enable) key.SetValue(RunName, $"\"{Application.ExecutablePath}\" --minimized");
        else key.DeleteValue(RunName, false);
    }

    private static void OpenPath(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("Open failed: " + e.Message); }
    }

    internal void ExitApp()
    {
        _pinMouse?.Dispose();
        _menuEvents?.Dispose();
        _menuPump?.Dispose();
        _systemMenus?.Dispose();
        _pinMenu.Dispose();
        _pinBorders?.Dispose();
        _pinSounds?.Dispose();
        if (_alwaysOnTop != null)
        {
            Log.Write("Always on top exit: " + _alwaysOnTop.RestoreAll().Message);
            _alwaysOnTop.Dispose();
        }
        _main.AllowClose = true;
        _main.Close();
        _automation.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        CommitBrightness();
        _brightness.Flush();       // let a restored brightness finish before the process ends
        _showWait?.Unregister(null);
        _showSignal.Dispose();
        _tray.Visible = false;
        _hotkeys.Dispose();
        _brightness.Dispose();
        _media.Dispose();
        _tray.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        ExitThread();
    }
}
