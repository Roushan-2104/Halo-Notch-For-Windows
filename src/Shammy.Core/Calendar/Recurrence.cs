using System.Globalization;

namespace Shammy.Core.Calendar;

public enum RecurrenceFrequency { Daily, Weekly, Monthly, Yearly }

/// <summary>
/// An RRULE (RFC 5545 §3.3.10), the parts normal meetings use: FREQ, INTERVAL, COUNT, UNTIL, BYDAY (with
/// ordinals for monthly "2nd Tuesday"), BYMONTHDAY and WKST. Anything else makes the rule unsupported, and
/// the event then shows only its first occurrence rather than wrong ones.
/// </summary>
public sealed record RecurrenceRule(RecurrenceFrequency Frequency)
{
    public int Interval { get; init; } = 1;
    public int? Count { get; init; }
    public IcsTime? Until { get; init; }
    /// <summary>Weekdays, each with an ordinal for MONTHLY ("2TU" = 2, "-1FR" = -1; 0 = every).</summary>
    public IReadOnlyList<(DayOfWeek Day, int Ordinal)> ByDay { get; init; } = Array.Empty<(DayOfWeek, int)>();
    public IReadOnlyList<int> ByMonthDay { get; init; } = Array.Empty<int>();
    public DayOfWeek WeekStart { get; init; } = DayOfWeek.Monday;

    public static RecurrenceRule? Parse(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule)) return null;
        RecurrenceFrequency? freq = null;
        int interval = 1;
        int? count = null;
        IcsTime? until = null;
        var byDay = new List<(DayOfWeek, int)>();
        var byMonthDay = new List<int>();
        var wkst = DayOfWeek.Monday;
        foreach (var part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return null;
            var key = part[..eq].ToUpperInvariant();
            var value = part[(eq + 1)..].ToUpperInvariant();
            switch (key)
            {
                case "FREQ":
                    freq = value switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };
                    if (freq is null) return null;
                    break;
                case "INTERVAL":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out interval) || interval < 1) return null;
                    break;
                case "COUNT":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var c) || c < 1) return null;
                    count = c;
                    break;
                case "UNTIL":
                    until = IcsParser.ParseTime(value, new Dictionary<string, string>());
                    if (until is null) return null;
                    break;
                case "BYDAY":
                    foreach (var d in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (d.Length < 2 || Day(d[^2..]) is not { } day) return null;
                        int ordinal = 0;
                        if (d.Length > 2 && !int.TryParse(d[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal)) return null;
                        byDay.Add((day, ordinal));
                    }
                    break;
                case "BYMONTHDAY":
                    foreach (var d in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!int.TryParse(d, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var md) || md == 0 || Math.Abs(md) > 31) return null;
                        byMonthDay.Add(md);
                    }
                    break;
                case "WKST":
                    if (Day(value) is not { } w) return null;
                    wkst = w;
                    break;
                case "BYMONTH" or "BYSETPOS" or "BYYEARDAY" or "BYWEEKNO" or "BYHOUR" or "BYMINUTE" or "BYSECOND":
                    // Rare for meetings; better to show one occurrence than invent wrong ones.
                    return null;
            }
        }
        if (freq is null) return null;
        return new RecurrenceRule(freq.Value)
        {
            Interval = interval,
            Count = count,
            Until = until,
            ByDay = byDay,
            ByMonthDay = byMonthDay,
            WeekStart = wkst,
        };
    }

    static DayOfWeek? Day(string two) => two switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null,
    };

    /// <summary>
    /// Start times (wall clock, in the event's zone) in ascending order, beginning with <paramref name="start"/>
    /// itself. Endless: the caller stops at COUNT, UNTIL or the end of the window it wants.
    /// </summary>
    public IEnumerable<DateTime> Starts(DateTime start)
    {
        yield return start;
        const int MaxPeriods = 20_000; // a daily meeting for 50 years; protects against a hostile feed
        for (int period = 0; period < MaxPeriods; period++)
        {
            foreach (var t in PeriodStarts(start, period).Where(t => t > start).OrderBy(t => t))
                yield return t;
        }
    }

    IEnumerable<DateTime> PeriodStarts(DateTime start, int period)
    {
        var time = start.TimeOfDay;
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
            {
                var day = start.Date.AddDays((long)period * Interval);
                if (ByDay.Count == 0 || ByDay.Any(b => b.Day == day.DayOfWeek)) yield return day + time;
                break;
            }
            case RecurrenceFrequency.Weekly:
            {
                int back = ((int)start.DayOfWeek - (int)WeekStart + 7) % 7;
                var weekStart = start.Date.AddDays(-back).AddDays(7L * period * Interval);
                var days = ByDay.Count > 0 ? ByDay.Select(b => b.Day).Distinct() : new[] { start.DayOfWeek };
                foreach (var d in days)
                    yield return weekStart.AddDays(((int)d - (int)WeekStart + 7) % 7) + time;
                break;
            }
            case RecurrenceFrequency.Monthly:
            {
                var month = new DateTime(start.Year, start.Month, 1).AddMonths(period * Interval);
                int length = DateTime.DaysInMonth(month.Year, month.Month);
                if (ByDay.Count > 0)
                {
                    foreach (var (dow, ordinal) in ByDay)
                        foreach (var d in WeekdaysInMonth(month, dow, ordinal))
                            if (ByMonthDay.Count == 0 || ByMonthDay.Any(md => MonthDay(md, length) == d.Day))
                                yield return d + time;
                }
                else
                {
                    var monthDays = ByMonthDay.Count > 0 ? ByMonthDay : new[] { start.Day };
                    foreach (var md in monthDays)
                    {
                        int day = MonthDay(md, length);
                        // The 31st doesn't exist every month; RFC 5545 skips those months.
                        if (day >= 1 && day <= length && (md < 0 || md <= length)) yield return month.AddDays(day - 1) + time;
                    }
                }
                break;
            }
            case RecurrenceFrequency.Yearly:
            {
                int year = start.Year + period * Interval;
                if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(year)) break;
                yield return new DateTime(year, start.Month, start.Day) + time;
                break;
            }
        }
    }

    static int MonthDay(int md, int length) => md > 0 ? md : length + md + 1;

    static IEnumerable<DateTime> WeekdaysInMonth(DateTime month, DayOfWeek dow, int ordinal)
    {
        int length = DateTime.DaysInMonth(month.Year, month.Month);
        var all = Enumerable.Range(0, length).Select(i => month.AddDays(i)).Where(d => d.DayOfWeek == dow).ToList();
        if (ordinal == 0) return all;
        int index = ordinal > 0 ? ordinal - 1 : all.Count + ordinal;
        return index >= 0 && index < all.Count ? new[] { all[index] } : Array.Empty<DateTime>();
    }
}
