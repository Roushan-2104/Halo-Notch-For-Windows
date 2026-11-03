using System.Globalization;
using Shammy.Core.Calendar;
using Shammy.Core.Cards;
using Shammy.Core.Productivity;

namespace Shammy.Core.Tests;

public class ClockCultureTests
{
    static readonly DateTime Afternoon = new(2026, 11, 4, 14, 5, 0);

    [Fact]
    public void Clock_follows_the_culture()
    {
        Assert.True(ClockFormat.Uses24Hour(CultureInfo.GetCultureInfo("de-DE")));
        Assert.True(ClockFormat.Uses24Hour(CultureInfo.GetCultureInfo("en-GB")));
        Assert.False(ClockFormat.Uses24Hour(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("14:05", ClockFormat.Time(Afternoon, true));
        Assert.Equal("2:05 PM", ClockFormat.Time(Afternoon, false));
        Assert.Equal("09:05", ClockFormat.Short(Afternoon.AddHours(-5), true));
        Assert.Equal("9:05", ClockFormat.Short(Afternoon.AddHours(-5), false));
    }

    [Fact]
    public void Calendar_texts_drop_am_pm_on_a_24_hour_clock()
    {
        var start = new DateTime(2026, 11, 4, 14, 0, 0);
        Assert.Equal("14:00", CalendarText.Clock(start, h24: true));
        Assert.Equal("", CalendarText.AmPm(start, h24: true));
        Assert.Equal("14:00", CalendarText.ClockAmPm(start, h24: true));
        Assert.Equal("14:00 – 15:30", CalendarText.Range(start, start.AddMinutes(90), h24: true));
        Assert.Equal("Standup 14:00", CalendarText.Glance("Standup", start, h24: true));
        Assert.Equal("Meeting 14:00", Shammy.Core.Connect.PrivacyText.CalendarGlance("Standup", start, presenting: true, h24: true));
        // ClockFormat and ClockPreference read a culture the same way ("H:mm" is 24-hour, "h:mm tt" isn't).
        foreach (var name in new[] { "de-DE", "en-GB", "en-US", "en-IN", "ja-JP" })
        {
            var c = CultureInfo.GetCultureInfo(name);
            Assert.Equal(ClockPreference.CultureUses24Hour(c), ClockFormat.Uses24Hour(c));
        }
        // The 12-hour forms are unchanged.
        Assert.Equal("2:00 PM", CalendarText.ClockAmPm(start));
        Assert.Equal("2 – 3 PM", CalendarText.Range(start, start.AddHours(1)));
    }

    [Theory]
    [InlineData(8, false, "8 AM")]
    [InlineData(12, false, "12 PM")]
    [InlineData(0, false, "12 AM")]
    [InlineData(24, false, "12 AM")]
    [InlineData(18, false, "6 PM")]
    [InlineData(8, true, "08:00")]
    [InlineData(20, true, "20:00")]
    public void Timeline_hour_labels(int hour, bool h24, string expected) => Assert.Equal(expected, CalendarText.HourLabel(hour, h24));

    [Fact]
    public void Reminders_and_timers_follow_the_clock_too()
    {
        var now = new DateTime(2026, 11, 4, 9, 0, 0);
        Assert.Equal("Tomorrow 18:30", TimeFormat.When(new DateTime(2026, 11, 5, 18, 30, 0), now, twentyFourHour: true));
        Assert.Equal("Tomorrow 6:30 PM", TimeFormat.When(new DateTime(2026, 11, 5, 18, 30, 0), now));
        Assert.Equal("Ends at 14:05", TimerCardText.Ends(Afternoon, false, "5:00", twentyFourHour: true));
        Assert.Equal("Ends at 2:05 PM", TimerCardText.Ends(Afternoon, false, "5:00"));
    }
}

public class CalendarRefreshTests
{
    static readonly DateTime Now = new(2026, 11, 4, 11, 0, 0);

    [Fact]
    public void A_failed_refresh_says_when_it_last_worked()
    {
        Assert.Null(CalendarText.SyncProblem(null, Now.AddMinutes(-40), Now));
        Assert.Equal("Couldn't refresh · last updated 10:20 AM", CalendarText.SyncProblem("Couldn't reach Google Calendar", Now.AddMinutes(-40), Now));
        Assert.Equal("Couldn't refresh · last updated 10:20", CalendarText.SyncProblem("x", Now.AddMinutes(-40), Now, h24: true));
        Assert.Equal("Couldn't refresh · last updated Tue 10:20 AM", CalendarText.SyncProblem("x", Now.AddDays(-1).AddMinutes(-40), Now));
        Assert.Equal("Couldn't refresh", CalendarText.SyncProblem("x", null, Now));
    }

    static CalendarOccurrence At(int startHour, int startMinute, int minutes, bool allDay = false, int dayOffset = 0)
    {
        var start = Now.Date.AddDays(dayOffset).AddHours(startHour).AddMinutes(startMinute);
        return new CalendarOccurrence("u", "Event", "", start, start.AddMinutes(minutes), allDay, null, null);
    }

    [Fact]
    public void Timeline_is_8_to_8_unless_the_day_needs_more()
    {
        Assert.Equal((8, 20), CalendarAgenda.TimelineHours(Array.Empty<CalendarOccurrence>(), Now));
        Assert.Equal((8, 20), CalendarAgenda.TimelineHours(new[] { At(9, 0, 30), At(19, 0, 60) }, Now));
        // 7:15 AM start → from 6; 9:30 PM end → to 22.
        Assert.Equal((6, 22), CalendarAgenda.TimelineHours(new[] { At(7, 15, 30), At(20, 45, 45) }, Now));
        // Ends exactly at 21:00 → 22 keeps the 2-hour grid.
        Assert.Equal((8, 22), CalendarAgenda.TimelineHours(new[] { At(20, 0, 60) }, Now));
    }

    [Fact]
    public void Timeline_ignores_all_day_events_and_other_days()
    {
        Assert.Equal((8, 20), CalendarAgenda.TimelineHours(new[] { At(0, 0, 24 * 60, allDay: true), At(6, 0, 30, dayOffset: 1) }, Now));
    }

    [Fact]
    public void An_overnight_event_stretches_to_the_edges_of_the_day()
    {
        Assert.Equal((0, 24), CalendarAgenda.TimelineHours(new[] { At(23, 0, 120, dayOffset: -1), At(22, 30, 180) }, Now));
    }
}
