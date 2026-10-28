using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

internal sealed class AutomationDetailsForm : Form
{
    private readonly AutomationCoordinator _automation;
    private readonly Settings _settings;
    private readonly Func<bool, bool> _brightnessRecovery;
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 0, 0, 12) };
    private readonly DataGridView _history = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, AccessibleName = "Recent display transitions" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public AutomationDetailsForm(AutomationCoordinator automation, Settings settings, Action<bool> preview, Func<bool, bool> brightnessRecovery = null)
    {
        _automation = automation; _settings = settings;
        _brightnessRecovery = brightnessRecovery;
        Text = "Why this rate? / Transition history";
        ClientSize = new Size(780, 620); MinimumSize = new Size(780, 620);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16), MaximumSize = new Size(760, 0) };
        head.Controls.Add(_status);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0) };
        Add(actions, "Active", () => { preview(false); _automation.Resume(); });
        Add(actions, "Preview", () => preview(true));
        Add(actions, "Pause 15 min", () => _automation.Pause(15));
        Add(actions, "Pause 1 hour", () => _automation.Pause(60));
        Add(actions, "Pause until resumed", () => _automation.Pause(null));
        Add(actions, "Resume", _automation.Resume);
        Add(actions, "Recording session", () => _automation.SetRecording(!_automation.RecordingSession));
        Add(actions, "Restore previous rate", () => ReviewRecovery(true));
        Add(actions, "Restore owned mode", () =>
        {
            if (!_automation.RestoreOwnedMode()) MessageBox.Show(this, "No owned temporary mode can be restored now. Finish verification or review recovery first. In preview, choose a rate manually.");
        });
        Add(actions, "Keep current rate", () => ReviewRecovery(false));
        Add(actions, "Archive unavailable recovery", () =>
        {
            if (MessageBox.Show(this, "Archive the recovery record for a monitor whose connection identity no longer matches?\n\nThis does not restore that monitor. Its original record is preserved locally for inspection.",
                "Unavailable monitor", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (_automation.Transitions.ArchiveUnavailable()) _automation.RecoveryFinished();
            else MessageBox.Show(this, _automation.Transitions.Error ?? "No unavailable target can be archived.");
            RefreshHistory();
        });
        head.Controls.Add(actions);
        var hold = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        hold.Controls.Add(new Label { Text = "Hold manual choices for (minutes):", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        var duration = new NumberBox { Minimum = 1, Maximum = 1440, Value = settings.ManualHoldMinutes,
            AccessibleName = "Manual hold duration in minutes" };
        duration.ValueChanged += (_, _) => { settings.ManualHoldMinutes = duration.Value; SettingsStore.Save(settings); };
        hold.Controls.Add(duration); head.Controls.Add(hold);
        head.Controls.Add(new Label { Text = "Readback verifies the reported rate, not image quality. History stays local (500 events / 30 days).",
            AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Theme.TextMuted });
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), WrapContents = true };
        Add(foot, "Looked fine", () => Feedback("Looked fine", null));
        Add(foot, "Report flicker / block", () => Feedback("Visible flicker", true));
        Add(foot, "Report blackout / block", () => Feedback("Black screen", true));
        Add(foot, "Allow automatic retry", () => Feedback("Retry explicitly allowed", false));
        Add(foot, "Export redacted...", Export);
        Add(foot, "Delete history", () =>
        {
            if (MessageBox.Show(this, "Delete local history and its transition blocks?", "Delete history", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { automation.History.Clear(); RefreshHistory(); }
        });
        Add(foot, "Refresh", RefreshHistory);
        var centre = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0) };
        centre.Controls.Add(_history);
        Controls.Add(centre); Controls.Add(head); Controls.Add(foot);
        _timer.Tick += (_, _) => UpdateStatus();
        Shown += (_, _) => { RefreshHistory(); UpdateStatus(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Dispose();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this); BackColor = Theme.Bg;
        _history.BackgroundColor = Theme.Surface; _history.DefaultCellStyle.BackColor = Theme.Surface;
        _history.DefaultCellStyle.ForeColor = Theme.Text;
        _history.EnableHeadersVisualStyles = false;
        _history.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        _history.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
    }

    private void UpdateStatus()
    {
        _status.Text = (_settings.PreviewMode ? "PREVIEW\n" : "ACTIVE MODE\n") + _automation.Status +
            (_automation.Decision == null ? "" : $"\nWinning rule: {_automation.Decision.Reason}; proposed {_automation.Decision.TargetHz} Hz.") +
            (string.IsNullOrEmpty(_automation.History.Error) ? "" : "\n" + _automation.History.Error) +
            (string.IsNullOrEmpty(SettingsStore.LastError) ? "" : "\nSettings: " + SettingsStore.LastError);
    }

    private static void Add(FlowLayoutPanel parent, string text, Action action)
    {
        var button = new PillButton { Text = text, AccessibleName = text };
        button.Click += (_, _) => action(); parent.Controls.Add(button);
    }

    private void ReviewRecovery(bool restore)
    {
        if (!restore && MessageBox.Show(this, "Keep current settings and dismiss the pending recovery? An unreadable journal will be archived for inspection.",
            "Keep current settings", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        if (_settings.PendingBrightness >= 0 && !string.IsNullOrEmpty(_settings.PendingBrightnessTarget))
        {
            if (_brightnessRecovery?.Invoke(restore) == true) _automation.RecoveryFinished();
            else MessageBox.Show(this, restore ? "Brightness restoration requested if the monitor matches. Wait for completion, then press Keep current rate to finish review." : "Brightness recovery could not be dismissed.");
            return;
        }
        if (_settings.PendingChange || _settings.PendingBrightness >= 0)
        {
            if (restore) { MessageBox.Show(this, "The old recovery record has no reliable monitor identity. Choose a rate/brightness manually after keeping the current settings."); return; }
            if (MessageBox.Show(this, "Keep current settings and dismiss the ambiguous legacy recovery record?", "Legacy recovery", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _settings.PendingChange = false; _settings.PendingBrightness = -1;
            SettingsStore.Save(_settings); _automation.RecoveryFinished(); return;
        }
        bool ok = restore ? _automation.Transitions.Recover() : _automation.Transitions.KeepCurrent();
        if (ok) _automation.RecoveryFinished();
        else MessageBox.Show(this, _automation.Transitions.Error ?? "No matching recoverable transition is available.");
        RefreshHistory(); UpdateStatus();
    }

    private void RefreshHistory()
    {
        _history.DataSource = _automation.History.Records.Select(r => new HistoryRow(r)).ToList();
        if (_history.Columns.Contains("Id")) _history.Columns["Id"].Visible = false;
    }

    private void Feedback(string feedback, bool? block)
    {
        if (_history.CurrentRow?.DataBoundItem is not HistoryRow row) return;
        if (block == true && MessageBox.Show(this, "Disable this directed transition for automation on the recorded monitor context? Manual changes remain available.",
            "Disable automatic transition", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        if (!_automation.History.Feedback(row.Id, feedback, block)) MessageBox.Show(this, _automation.History.Error ?? "Feedback could not be saved.");
        RefreshHistory();
    }

    private void Export()
    {
        string json = _automation.History.ExportRedacted();
        using var preview = new Form { Text = "Redacted export preview", ClientSize = new Size(640, 440), StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Text = json };
        var save = new Button { Text = "Save JSON...", Dock = DockStyle.Bottom, Height = 36 };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "JSON files|*.json", FileName = "HzSwitcher-history-redacted.json" };
            if (dialog.ShowDialog(preview) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, json); preview.Close(); }
            catch (Exception e) { MessageBox.Show(preview, e.Message); }
        };
        preview.Controls.Add(text); preview.Controls.Add(save); Theme.Apply(preview); preview.ShowDialog(this);
    }

    private sealed class HistoryRow
    {
        public Guid Id { get; }
        public string Time { get; }
        public string Change { get; }
        public string Outcome { get; }
        public string Feedback { get; }
        public bool Blocked { get; }
        public HistoryRow(TransitionRecord r)
        { Id = r.Id; Time = r.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss"); Change = $"{r.BeforeHz} → {r.RequestedHz} (read {r.ObservedHz})";
            Outcome = r.Outcome; Feedback = r.Feedback; Blocked = r.DisabledForAutomation; }
    }
}
