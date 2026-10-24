using System.Text.RegularExpressions;

namespace HzSwitcher.Services;

internal static class Match
{
    /// <summary>Case-insensitive match with * wildcards; a trailing ".exe" on the pattern is ignored.</summary>
    public static bool Wildcard(string pattern, string text)
    {
        pattern = Normalize(pattern);
        return Regex.IsMatch(text, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
    }

    public static string Normalize(string process) =>
        process.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process.Trim()[..^4] : process.Trim();
}
