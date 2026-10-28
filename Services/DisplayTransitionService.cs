using System.Text.Json;

namespace HzSwitcher.Services;

public class PendingTransition
{
    public string Target { get; set; }
    public int BeforeHz { get; set; }
    public int RequestedHz { get; set; }
    public string Reason { get; set; }
}

/// <summary>One target-bound, durable rate transition at a time. No independent timer or hidden retries.</summary>
public sealed class DisplayTransitionService
{
    private readonly IDisplay _display;
    private readonly Func<string> _target;
    private readonly Func<double> _elapsed;
    private readonly string _path;
    private readonly TransitionHistoryStore _history;
    private double _verifyAt;
    private bool _verifying;
    private bool _journalUnreadable;
    public PendingTransition Pending { get; private set; }
    public bool Busy => _verifying;
    public bool NeedsRecovery => _journalUnreadable || (Pending != null && !_verifying);
    public string Error { get; private set; }

    public DisplayTransitionService(IDisplay display, Func<string> target, Func<double> elapsed,
        string path, TransitionHistoryStore history)
    {
        _display = display; _target = target; _elapsed = elapsed; _path = path; _history = history;
        try
        {
            if (File.Exists(path))
            {
                Pending = JsonSerializer.Deserialize<PendingTransition>(File.ReadAllText(path));
                if (Pending == null || string.IsNullOrEmpty(Pending.Target) || Pending.BeforeHz <= 0 || Pending.RequestedHz <= 0)
                    throw new IOException("Invalid transition journal.");
            }
        }
        catch (Exception e) { _journalUnreadable = true; Error = "Recovery journal needs review: " + e.Message; }
    }

    public bool Apply(int hz, string reason)
    {
        if (Pending != null || _journalUnreadable) { Error = "Finish or review the pending transition first."; return false; }
        string target = _target();
        int before = _display.GetCurrentHz();
        if (string.IsNullOrEmpty(target) || before <= 0 || !_display.GetSupportedHz().Contains(hz))
        { Error = "The target monitor or requested rate is unavailable."; return false; }
        if (before == hz) { Error = null; return true; }
        Pending = new() { Target = target, BeforeHz = before, RequestedHz = hz, Reason = reason };
        try { AtomicJsonFile.Write(_path, Pending); }
        catch (Exception e) { Pending = null; Error = "Switch cancelled: recovery data could not be saved. " + e.Message; return false; }
        if (_target() != target) { Error = "Monitor changed before application; recovery record retained."; return false; }
        bool applied;
        try { applied = _display.TryApply(hz); }
        catch (Exception e) { applied = false; Error = e.Message; }
        int observed = _display.GetCurrentHz();
        if (_target() != target) { Error = "Monitor changed during application; review recovery."; return false; }
        if (!applied || observed != hz)
        {
            FinishFailure(observed, "Application failed");
            return false;
        }
        _verifying = true; _verifyAt = _elapsed() + 3;
        Error = null;
        return true;
    }

    public void Tick()
    {
        if (!_verifying || _elapsed() < _verifyAt) return;
        _verifying = false;
        if (_target() != Pending.Target)
        { Error = "Monitor identity changed during verification. Recovery is pending."; return; }
        int current = _display.GetCurrentHz();
        if (current == Pending.RequestedHz)
        {
            _history.Add(Pending.Target, Pending.BeforeHz, Pending.RequestedHz, current, Pending.Reason, "Verified by readback");
            Clear();
        }
        else FinishFailure(current, "Verification failed");
    }

    private void FinishFailure(int current, string outcome)
    {
        // Yield to external changes. A delayed verification failure does not repeatedly overwrite another utility.
        _verifying = false;
        _history.Add(Pending.Target, Pending.BeforeHz, Pending.RequestedHz, current, Pending.Reason, outcome);
        if (current == Pending.BeforeHz) { Clear(); Error = outcome + "; previous rate is unchanged."; }
        else Error = outcome + ". Review and restore the previous rate explicitly.";
    }

    public bool Recover()
    {
        if (Pending == null || _verifying || _journalUnreadable || _target() != Pending.Target) return false;
        var pending = Pending;
        if (!_display.GetSupportedHz().Contains(pending.BeforeHz))
        { Error = "Previous mode is unavailable. Keep the current rate to dismiss recovery."; return false; }
        bool ok = _display.GetCurrentHz() == pending.BeforeHz || _display.TryApply(pending.BeforeHz);
        int observed = _display.GetCurrentHz();
        if (_target() != pending.Target || !ok || observed != pending.BeforeHz)
        { Error = "Recovery could not be confirmed; journal retained."; return false; }
        _history.Add(pending.Target, pending.RequestedHz, pending.BeforeHz, observed, "Explicit recovery", "Restored by readback");
        return Clear();
    }

    public bool KeepCurrent()
    {
        if (_journalUnreadable && !string.IsNullOrEmpty(_target()) && _display.GetCurrentHz() > 0)
        {
            try
            {
                File.Move(_path, _path + ".unreadable-" + Guid.NewGuid().ToString("N"));
                _journalUnreadable = false; Pending = null; Error = null;
                return true;
            }
            catch (Exception e) { Error = "Could not preserve the unreadable journal: " + e.Message; return false; }
        }
        if (_verifying || Pending == null || _target() != Pending.Target || _display.GetCurrentHz() <= 0) return false;
        _history.Add(Pending.Target, Pending.BeforeHz, Pending.RequestedHz, _display.GetCurrentHz(), "User kept current rate", "Recovery dismissed");
        return Clear();
    }

    public bool ArchiveUnavailable()
    {
        if (_verifying || Pending == null || _target() == Pending.Target) return false;
        try
        {
            File.Move(_path, _path + ".unavailable-" + Guid.NewGuid().ToString("N"));
            _history.Add(Pending.Target, Pending.BeforeHz, Pending.RequestedHz, 0,
                "Explicitly archived unavailable target", "Original monitor was not restored");
            Pending = null; Error = null; return true;
        }
        catch (Exception e) { Error = "Could not archive unavailable target: " + e.Message; return false; }
    }

    private bool Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); Pending = null; Error = null; return true; }
        catch (Exception e) { Error = "Could not complete recovery record: " + e.Message; return false; }
    }
}
