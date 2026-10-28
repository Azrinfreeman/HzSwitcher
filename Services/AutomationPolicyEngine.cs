namespace HzSwitcher.Services;

public enum AutomationReason { Baseline, Profile, Saver, Game, Restoring }

public record AutomationDecision(int TargetHz, AutomationReason Reason, string Explanation,
    bool Saver = false, string Process = null);

public record AutomationContext(int CurrentHz, int BaselineHz, int RestoreHz, bool SaverActive,
    bool Game, bool SaverDue, bool RestoreReady, AppProfile Profile, string Process, IReadOnlyList<int> Rates);

/// <summary>Pure selection. Execution gates (pause, preview, cooldown, identity) belong to the coordinator.</summary>
public static class AutomationPolicyEngine
{
    public static AutomationDecision Evaluate(Settings s, AutomationContext c)
    {
        if (c.Game)
            return new(Math.Max(c.BaselineHz, c.RestoreHz), AutomationReason.Game, "Game protection: saver lowering is blocked.");
        if (c.Profile is { AllowSaver: false } p)
            return new(ModeMath.Resolve(c.Rates, p.Hz), AutomationReason.Profile,
                $"App profile: {c.Process}.", Process: c.Process);
        if (s.AutoEnabled && c.SaverDue && (!c.SaverActive || !c.RestoreReady))
        {
            int rate = c.Rates.Contains(s.SaverHz) ? Math.Min(c.CurrentHz, s.SaverHz) : c.CurrentHz;
            return new(rate, AutomationReason.Saver,
                c.Rates.Contains(s.SaverHz) ? "Idle / media saver threshold reached." : "Saver rate is unavailable on this monitor.", true);
        }
        if (c.SaverActive && s.AutoEnabled && !c.RestoreReady)
            return new(c.CurrentHz, AutomationReason.Saver, $"Saver active; waiting for {s.RestoreSeconds} seconds of activity.", true);
        if (c.Profile != null)
            return new(ModeMath.Resolve(c.Rates, c.Profile.Hz), AutomationReason.Profile,
                $"App profile: {c.Process}.", Process: c.Process);
        return new(c.BaselineHz, c.SaverActive ? AutomationReason.Restoring : AutomationReason.Baseline,
            c.SaverActive ? "Restoring your previous refresh rate." : "Your baseline refresh rate.");
    }
}
