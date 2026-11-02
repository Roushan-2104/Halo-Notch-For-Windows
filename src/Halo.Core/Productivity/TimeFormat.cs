using System.Globalization;

namespace Halo.Core.Productivity;

/// <summary>How times read in the notch: "24:59", "1:05:09", "25 min", "Tomorrow 9:00 AM".</summary>
public static class TimeFormat
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>A countdown shows whole seconds rounded up, so it reads 0:01 until the very end, never 0:00 early.</summary>
    public static string Countdown(TimeSpan remaining) => Clock((long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds - 0.001)));

    /// <summary>Elapsed time rounds down, like a stopwatch.</summary>
    public static string Elapsed(TimeSpan elapsed) => Clock((long)Math.Floor(Math.Max(0, elapsed.TotalSeconds)));

    static string Clock(long seconds)
    {
        long h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>"25 min", "1 h 30 min", "1 min 30 s", "45 s", "2 days".</summary>
    public static string Describe(TimeSpan d)
    {
        long total = (long)Math.Round(d.TotalSeconds);
        long days = total / 86400, h = total / 3600 % 24, m = total / 60 % 60, s = total % 60;
        var parts = new List<string>();
        if (days > 0) parts.Add(days == 1 ? "1 day" : $"{days} days");
        if (h > 0) parts.Add($"{h} h");
        if (m > 0) parts.Add($"{m} min");
        if (s > 0 && days == 0 && h == 0) parts.Add($"{s} s");
        return parts.Count == 0 ? "0 s" : string.Join(" ", parts);
    }

    /// <summary>"6:00 PM", "Tomorrow 9:00 AM", "Fri 9:00 AM", "12 Nov 9:00 AM", relative to <paramref name="now"/>.</summary>
    public static string When(DateTime at, DateTime now)
    {
        var time = at.ToString("h:mm tt", Invariant);
        int days = (at.Date - now.Date).Days;
        return days switch
        {
            0 => $"Today {time}",
            1 => $"Tomorrow {time}",
            -1 => $"Yesterday {time}",
            > 1 and < 7 => $"{at.ToString("ddd", Invariant)} {time}",
            _ => $"{at.ToString("d MMM", Invariant)} {time}",
        };
    }
}
