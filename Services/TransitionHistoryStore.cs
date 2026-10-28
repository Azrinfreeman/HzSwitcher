using System.Text.Json;

namespace HzSwitcher.Services;

public class TransitionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Utc { get; set; }
    public string Target { get; set; }
    public int BeforeHz { get; set; }
    public int RequestedHz { get; set; }
    public int ObservedHz { get; set; }
    public string Reason { get; set; }
    public string Outcome { get; set; }
    public string Feedback { get; set; }
    public bool DisabledForAutomation { get; set; }
}

public sealed class TransitionHistoryStore
{
    private readonly string _path;
    private readonly IClock _clock;
    private List<TransitionRecord> _records = new();
    public string Error { get; private set; }
    public IReadOnlyList<TransitionRecord> Records { get { Prune(); return _records.AsReadOnly(); } }

    public TransitionHistoryStore(string path, IClock clock)
    {
        _path = path; _clock = clock;
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("History file exceeds its size limit.");
                _records = JsonSerializer.Deserialize<List<TransitionRecord>>(File.ReadAllText(path)) ?? new();
            }
            Prune();
        }
        catch (Exception e) { Error = "History could not be loaded: " + e.Message; }
    }

    private void Prune() => _records = _records.Where(r => r != null && r.Utc >= _clock.UtcNow.AddDays(-30))
        .OrderByDescending(r => r.Utc).Take(500).ToList();

    public TransitionRecord Add(string target, int before, int requested, int observed, string reason, string outcome)
    {
        var record = new TransitionRecord { Utc = _clock.UtcNow, Target = target, BeforeHz = before,
            RequestedHz = requested, ObservedHz = observed, Reason = reason, Outcome = outcome };
        _records.Insert(0, record); Save(); return record;
    }

    public bool Save()
    {
        try { Prune(); AtomicJsonFile.Write(_path, _records, backup: false); Error = null; return true; }
        catch (Exception e) { Error = "History could not be saved: " + e.Message; return false; }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
            if (File.Exists(_path + ".bak")) File.Delete(_path + ".bak");
            _records.Clear(); Error = null;
        }
        catch (Exception e) { Error = "History could not be deleted: " + e.Message; }
    }
    public bool IsBlocked(string target, int from, int to) => Records.Any(r => r.Target == target &&
        r.BeforeHz == from && r.RequestedHz == to && r.DisabledForAutomation);

    public bool Feedback(Guid id, string feedback, bool? disable)
    {
        var record = _records.FirstOrDefault(r => r.Id == id);
        if (record == null) return false;
        record.Feedback = feedback;
        if (disable.HasValue)
            foreach (var matching in _records.Where(r => r.Target == record.Target && r.BeforeHz == record.BeforeHz && r.RequestedHz == record.RequestedHz))
                matching.DisabledForAutomation = disable.Value;
        return Save();
    }

    public string ExportRedacted()
    {
        // Deliberately omit device paths, app names and user-authored text.
        var targets = new Dictionary<string, string>();
        return JsonSerializer.Serialize(Records.Select(r =>
        {
            string key = r.Target ?? "unknown";
            if (!targets.ContainsKey(key)) targets[key] = "Monitor " + (targets.Count + 1);
            return new { r.Utc, Monitor = targets[key], r.BeforeHz, r.RequestedHz, r.ObservedHz,
                r.Outcome, r.Feedback, r.DisabledForAutomation };
        }), new JsonSerializerOptions { WriteIndented = true });
    }
}
