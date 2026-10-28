using HzSwitcher.Services;
using HzSwitcher.UI.Controls;

namespace HzSwitcher.UI;

internal sealed class DiagnosticsForm : DialogShell
{
    private readonly Func<DiagnosticsSnapshot> _capture;
    private readonly Func<Task> _refresh;
    private readonly Label _local = MainForm.TextLabel("", muted: true, size: 9.5f);
    private readonly TextBox _report = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        AccessibleName = "Diagnostics report to copy", WordWrap = true,
    };
    private readonly PillButton _refreshButton = new() { Text = "Refresh" };

    public DiagnosticsForm(Func<DiagnosticsSnapshot> capture, Func<Task> refresh)
        : base("Diagnostics", new Size(660, 600), readOnly: true)
    {
        _capture = capture;
        _refresh = refresh;
        _local.UseMnemonic = false;
        _report.TabIndex = 0;
        _local.Dock = DockStyle.Top;
        _local.Padding = new Padding(0, 0, 0, 16);
        Body.SizeChanged += (_, _) => _local.MaximumSize = new Size(Math.Max(1, Body.ClientSize.Width - Body.Padding.Horizontal), 0);
        Body.Controls.Add(_report);
        Body.Controls.Add(_local);
        var copy = new PillButton { Text = "Copy diagnostics" };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_report.Text); ClearError(); copy.Text = "Copied"; }
            catch (System.Runtime.InteropServices.ExternalException)
            { ShowError(_report, "Clipboard is busy. Try copying again."); }
        };
        _refreshButton.Click += async (_, _) =>
        {
            _refreshButton.Enabled = false;
            copy.Text = "Copy diagnostics";
            try { await _refresh(); if (!IsDisposed) { ClearError(); RefreshReport(); } }
            catch (Exception e)
            {
                Log.Write("Diagnostics refresh failed: " + e.Message);
                if (!IsDisposed) ShowError(_report, "Could not refresh diagnostics. Try again.");
            }
            finally { if (!IsDisposed) _refreshButton.Enabled = true; }
        };
        LeftActions.Controls.Add(_refreshButton);
        LeftActions.Controls.Add(copy);
        RefreshReport();
        Finish();
    }

    private void RefreshReport()
    {
        var snapshot = _capture();
        _local.Text = "Local details (not copied)\n" + snapshot.LocalMonitor
            + (string.IsNullOrEmpty(snapshot.LocalProfile) ? "" : "\nActive profile: " + snapshot.LocalProfile);
        _report.Text = snapshot.ToReport();
        _report.SelectionStart = 0;
    }

    protected override bool OnSave() => false;
}
