using System.Globalization;

namespace Shammy.Core.Productivity;

/// <summary>How alarms read in the notch: "7:00 AM", "Weekdays", "Mon, Wed", "Tomorrow", "in 8 h 20 min".</summary>
public static class AlarmText
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static readonly (AlarmDays Day, string Name)[] Order =
    {
        (AlarmDays.Monday, "Mon"), (AlarmDays.Tuesday, "Tue"), (AlarmDays.Wednesday, "Wed"), (AlarmDays.Thursday, "Thu"),
        (AlarmDays.Friday, "Fri"), (AlarmDays.Saturday, "Sat"), (AlarmDays.Sunday, "Sun"),
    };

    public static string Time(Alarm a, bool? use24Hour = null) => ClockPreference.Format(a.Hour, a.Minute, use24Hour);

    /// <summary>"Every day", "Weekdays", "Weekends", "Mon, Wed, Fri"; for a one-off, its day ("Today", "Tomorrow", "Sat 14 Nov").</summary>
    public static string Repeat(AlarmDays days) => days switch
    {
        AlarmDays.EveryDay => "Every day",
        AlarmDays.Weekdays => "Weekdays",
        AlarmDays.Weekends => "Weekends",
        AlarmDays.None => "Once",
        _ => string.Join(", ", Order.Where(o => (days & o.Day) != 0).Select(o => o.Name)),
    };

    public static string Repeat(Alarm a, DateTime nowLocal, DateTime? upcomingLocal)
    {
        if (a.Repeats) return Repeat(a.Days);
        var date = a.Date?.Date ?? upcomingLocal?.Date;
        if (date is not { } d) return "Once";
        int days = (d - nowLocal.Date).Days;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            > 1 and < 7 => d.ToString("dddd", Invariant),
            _ => d.ToString("ddd d MMM", Invariant),
        };
    }

    /// <summary>"in 45 min", "in 8 h 20 min", "in 2 days"; rounded up to the minute so it never says "in 0 min".</summary>
    public static string Until(TimeSpan until)
    {
        long minutes = (long)Math.Ceiling(Math.Max(0, until.TotalMinutes));
        if (minutes < 1) return "now";
        if (minutes < 60) return $"in {minutes} min";
        if (minutes < 48 * 60)
        {
            long h = minutes / 60, m = minutes % 60;
            return m == 0 ? $"in {h} h" : $"in {h} h {m} min";
        }
        return $"in {minutes / (24 * 60)} days";
    }

    /// <summary>"Alarm · 7:00 AM tomorrow", "Alarm · 6:30 AM · Weekdays · Gym" (the palette row and the confirmation).</summary>
    public static string Describe(AlarmSpec spec, DateTime nowLocal)
    {
        var time = ClockPreference.Format(spec.Hour, spec.Minute);
        string when = spec.Days != AlarmDays.None ? " · " + Repeat(spec.Days)
            : spec.Date is { } d ? (d.Date - nowLocal.Date).Days switch
            {
                0 => " today",
                1 => " tomorrow",
                _ => " " + d.ToString("ddd d MMM", Invariant),
            }
            : "";
        var label = spec.Label.Length > 0 ? $" · {spec.Label}" : "";
        return $"Alarm · {time}{when}{label}";
    }

    /// <summary>"Missed alarm 7:00 AM".</summary>
    public static string Missed(Alarm a) => $"Missed alarm {Time(a)}";
}
