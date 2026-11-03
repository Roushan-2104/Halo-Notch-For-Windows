using System.Globalization;
using System.Text.RegularExpressions;

namespace Shammy.Core.MeetingSystem;

public enum MicAction { Mute, Unmute, Toggle }

/// <summary>The exact phrases this feature answers; anything looser stays a web search.</summary>
public static class Phrases
{
    static readonly Regex Mic = new(@"^(?<verb>mute|unmute|toggle)\s+(?:my\s+|the\s+)?(?:mic|microphone)$", RegexOptions.IgnoreCase);

    static readonly HashSet<string> SystemQueries = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "system status", "system monitor", "cpu", "cpu usage", "battery status", "memory usage", "ram usage",
    };

    public static MicAction? ParseMic(string text)
    {
        var m = Mic.Match(Normalize(text));
        if (!m.Success) return null;
        return m.Groups["verb"].Value.ToLowerInvariant() switch
        {
            "mute" => MicAction.Mute,
            "unmute" => MicAction.Unmute,
            _ => MicAction.Toggle,
        };
    }

    public static bool IsSystemQuery(string text) => SystemQueries.Contains(Normalize(text));

    static string Normalize(string text) => Regex.Replace(text.Trim().TrimEnd('.', '!', '?'), @"\s+", " ");
}

public static class SystemFormat
{
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.FromMinutes(1)) return "<1m";
        int hours = (int)t.TotalHours;
        return hours > 0 ? $"{hours}h {t.Minutes}m" : $"{t.Minutes}m";
    }

    public static string Gigabytes(ulong bytes) => (bytes / (double)(1UL << 30)).ToString("0.0", CultureInfo.InvariantCulture);

    public static string Percent(double value) => value.ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Rate(double percentPerHour) => percentPerHour.ToString("0.0", CultureInfo.InvariantCulture) + "%/h";
}
