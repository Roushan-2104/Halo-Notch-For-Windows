using Shammy.Core.Calendar;

namespace Shammy.Core.Connect;

/// <summary>Today's calendar on the Notes card and today's reminders on the calendar card.</summary>
public static class DayLinks
{
    /// <summary>Timed events still to come (or going on) today, soonest first, at most <paramref name="max"/>.</summary>
    public static List<CalendarOccurrence> UpcomingToday(IEnumerable<CalendarOccurrence> items, DateTime now, int max = 3) =>
        items.Where(o => !o.AllDay && o.End > now && o.Start.Date == now.Date)
            .OrderBy(o => o.Start)
            .Take(Math.Max(0, max))
            .ToList();

    /// <summary>
    /// Today's reminders that aren't ticked off, by time (ones already due first), at most <paramref name="max"/>.
    /// Generic over the app's reminder type so the rule stays testable here.
    /// </summary>
    public static List<T> RemindersToday<T>(IEnumerable<T> reminders, Func<T, DateTime> due, Func<T, bool> done, DateTime now, int max = 3) =>
        reminders.Where(r => !done(r) && due(r).Date == now.Date)
            .OrderBy(due)
            .Take(Math.Max(0, max))
            .ToList();

    /// <summary>"10:30 AM · Google Meet" / "now · Zoom" for an event row.</summary>
    public static string EventMeta(CalendarOccurrence o, DateTime now)
    {
        var when = o.Start <= now ? "now" : CalendarText.ClockAmPm(o.Start);
        return o.Provider is { Length: > 0 } p ? $"{when} · {p}" : when;
    }
}
