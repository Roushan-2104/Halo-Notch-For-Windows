using System.Globalization;

namespace Shammy.Core.Productivity;

/// <summary>
/// 12- or 24-hour clock times. The user's choice (settings: Use24Hour) wins; null follows the Windows region
/// format (its short time pattern, user overrides included).
/// </summary>
public static class ClockPreference
{
    /// <summary>True/false from settings; null follows Windows.</summary>
    public static bool? Use24Hour { get; set; }

    public static bool Is24Hour => Use24Hour ?? CultureUses24Hour(CultureInfo.CurrentCulture);

    /// <summary>"HH:mm" and "H:mm" are 24-hour; "h:mm tt" is 12-hour.</summary>
    public static bool CultureUses24Hour(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortTimePattern;
        return pattern.Contains('H') && !pattern.Contains('h');
    }

    /// <summary>"7:00 AM" or "07:00".</summary>
    public static string Format(int hour, int minute, bool? use24Hour = null)
    {
        var (time, suffix) = Parts(hour, minute, use24Hour);
        return suffix.Length > 0 ? $"{time} {suffix}" : time;
    }

    public static string Format(DateTime t, bool? use24Hour = null) => Format(t.Hour, t.Minute, use24Hour);

    /// <summary>The digits and the AM/PM apart, for a big number with a small suffix: ("7:00", "AM") or ("07:00", "").</summary>
    public static (string Time, string Suffix) Parts(int hour, int minute, bool? use24Hour = null)
    {
        if (use24Hour ?? Is24Hour) return ($"{hour:00}:{minute:00}", "");
        int h = hour % 12 == 0 ? 12 : hour % 12;
        return ($"{h}:{minute:00}", hour < 12 ? "AM" : "PM");
    }
}
