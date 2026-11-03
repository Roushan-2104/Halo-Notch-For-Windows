using Shammy.Core.Calendar;

namespace Shammy.Core.Tests;

public class CalendarTests
{
    static readonly TimeZoneInfo India = IcsZones.Find("Asia/Kolkata")!;
    static readonly TimeZoneInfo NewYork = IcsZones.Find("America/New_York")!;

    static string Cal(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Google Inc//Google Calendar 70.9054//EN\r\n" +
        string.Join("", events) + "END:VCALENDAR\r\n";

    static string Ev(params string[] lines) => "BEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\n";

    static List<CalendarOccurrence> Expand(string ics, DateTime fromLocal, DateTime toLocal, TimeZoneInfo local) =>
        CalendarExpander.Expand(IcsParser.Parse(ics), TimeZoneInfo.ConvertTimeToUtc(fromLocal, local),
            TimeZoneInfo.ConvertTimeToUtc(toLocal, local), local);

    // ---- Parsing ------------------------------------------------------------------------------------

    [Fact]
    public void Unfolds_continuation_lines()
    {
        var lines = IcsParser.Unfold("SUMMARY:Weekly \r\n sync with\r\n\tthe team\r\nUID:1\r\n").ToList();
        Assert.Equal(new[] { "SUMMARY:Weekly sync withthe team", "UID:1" }, lines);
    }

    [Fact]
    public void Unescapes_text()
    {
        Assert.Equal("a, b; c\\d\nnext", IcsParser.Unescape(@"a\, b\; c\\d\nnext"));
    }

    [Fact]
    public void Splits_quoted_parameters_containing_colons()
    {
        Assert.True(IcsParser.TrySplit("DTSTART;TZID=\"Asia/Kolkata\":20261103T103000", out var name, out var p, out var value));
        Assert.Equal("DTSTART", name);
        Assert.Equal("Asia/Kolkata", p["TZID"]);
        Assert.Equal("20261103T103000", value);
        Assert.True(IcsParser.TrySplit("ATTENDEE;CN=\"Riya: PM\":mailto:r@example.com", out _, out var p2, out var v2));
        Assert.Equal("Riya: PM", p2["CN"]);
        Assert.Equal("mailto:r@example.com", v2);
    }

    [Fact]
    public void Parses_summary_location_and_ignores_alarm_properties()
    {
        var ics = Cal(Ev("UID:a1", "DTSTART:20261103T050000Z", "DTEND:20261103T051500Z", "SUMMARY:Standup",
            "LOCATION:Koregaon Park\\, Pune", "BEGIN:VALARM", "ACTION:DISPLAY", "DESCRIPTION:Reminder", "END:VALARM",
            "DESCRIPTION:Agenda"));
        var e = Assert.Single(IcsParser.Parse(ics));
        Assert.Equal("Standup", e.Summary);
        Assert.Equal("Koregaon Park, Pune", e.Location);
        Assert.Equal("Agenda", e.Description);
    }

    [Fact]
    public void Parses_durations()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), IcsParser.ParseDuration("PT1H30M"));
        Assert.Equal(TimeSpan.FromDays(1), IcsParser.ParseDuration("P1D"));
        Assert.Equal(TimeSpan.FromDays(14), IcsParser.ParseDuration("P2W"));
        Assert.Equal(TimeSpan.FromMinutes(-15), IcsParser.ParseDuration("-PT15M"));
        Assert.Null(IcsParser.ParseDuration("PT"));
        Assert.Null(IcsParser.ParseDuration("garbage"));
    }

    [Fact]
    public void Lf_only_files_parse_too()
    {
        var ics = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:x\nDTSTART:20261103T050000Z\nSUMMARY:Lunch\nEND:VEVENT\nEND:VCALENDAR\n";
        Assert.Equal("Lunch", Assert.Single(IcsParser.Parse(ics)).Summary);
    }

    // ---- Time zones ---------------------------------------------------------------------------------

    [Fact]
    public void Utc_times_convert_to_india()
    {
        var ics = Cal(Ev("UID:1", "DTSTART:20261103T050000Z", "DTEND:20261103T051500Z", "SUMMARY:Standup"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), India));
        Assert.Equal(new DateTime(2026, 11, 3, 10, 30, 0), o.Start);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 45, 0), o.End);
        Assert.False(o.AllDay);
    }

    [Fact]
    public void Tzid_asia_kolkata_is_read_in_its_zone()
    {
        var ics = Cal(Ev("UID:1", "DTSTART;TZID=Asia/Kolkata:20261103T103000", "DTEND;TZID=Asia/Kolkata:20261103T104500", "SUMMARY:Standup"));
        // Viewed from New York (EST, UTC-5 after 1 Nov): 10:30 IST = 05:00 UTC = 00:00 EST.
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 2), new DateTime(2026, 11, 4), NewYork));
        Assert.Equal(new DateTime(2026, 11, 3, 0, 0, 0), o.Start);
        var mine = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), India));
        Assert.Equal(new DateTime(2026, 11, 3, 10, 30, 0), mine.Start);
    }

    [Fact]
    public void Windows_zone_names_work_as_tzid()
    {
        var ics = Cal(Ev("UID:1", "DTSTART;TZID=India Standard Time:20261103T103000", "DURATION:PT30M", "SUMMARY:Sync"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), India));
        Assert.Equal(new DateTime(2026, 11, 3, 10, 30, 0), o.Start);
        Assert.Equal(new DateTime(2026, 11, 3, 11, 0, 0), o.End);
    }

    [Fact]
    public void Unknown_tzid_falls_back_to_the_viewers_zone()
    {
        var ics = Cal(Ev("UID:1", "DTSTART;TZID=Mars/Olympus:20261103T103000", "SUMMARY:Odd"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), India));
        Assert.Equal(new DateTime(2026, 11, 3, 10, 30, 0), o.Start);
    }

    [Fact]
    public void Floating_times_are_local()
    {
        var ics = Cal(Ev("UID:1", "DTSTART:20261103T090000", "DTEND:20261103T100000", "SUMMARY:Gym"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), NewYork));
        Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0), o.Start);
    }

    [Fact]
    public void Weekly_meeting_keeps_wall_time_across_dst_change()
    {
        // New York leaves daylight time on Sunday 1 Nov 2026. A Friday 9:00 meeting stays 9:00 local,
        // which is 13:00 UTC before and 14:00 UTC after; seen from India it moves from 18:30 to 19:30.
        var ics = Cal(Ev("UID:w", "DTSTART;TZID=America/New_York:20261023T090000", "DTEND;TZID=America/New_York:20261023T093000",
            "RRULE:FREQ=WEEKLY;BYDAY=FR", "SUMMARY:Review"));
        var ny = Expand(ics, new DateTime(2026, 10, 20), new DateTime(2026, 11, 10), NewYork);
        Assert.Equal(3, ny.Count);
        Assert.All(ny, o => Assert.Equal(new TimeSpan(9, 0, 0), o.Start.TimeOfDay));
        var ist = Expand(ics, new DateTime(2026, 10, 20), new DateTime(2026, 11, 10), India);
        Assert.Equal(new TimeSpan(18, 30, 0), ist[0].Start.TimeOfDay);
        Assert.Equal(new TimeSpan(18, 30, 0), ist[1].Start.TimeOfDay);
        Assert.Equal(new TimeSpan(19, 30, 0), ist[2].Start.TimeOfDay);
    }

    [Fact]
    public void Time_inside_spring_forward_gap_moves_forward()
    {
        // 2:30 AM on 8 March 2026 doesn't exist in New York; it becomes 3:30 EDT (07:30 UTC).
        var utc = IcsZones.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), NewYork);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0), utc);
    }

    [Fact]
    public void Repeated_autumn_hour_means_the_first_one()
    {
        // 1:30 AM on 1 Nov 2026 happens twice in New York; the first is EDT (05:30 UTC).
        var utc = IcsZones.ToUtc(new DateTime(2026, 11, 1, 1, 30, 0), NewYork);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0), utc);
    }

    [Fact]
    public void Iana_names_resolve()
    {
        Assert.NotNull(IcsZones.Find("Asia/Calcutta"));
        Assert.NotNull(IcsZones.Find("Europe/London"));
        Assert.NotNull(IcsZones.Find("/mozilla.org/20050126_1/Europe/Berlin"));
        Assert.Null(IcsZones.Find(""));
    }

    // ---- All-day ------------------------------------------------------------------------------------

    [Fact]
    public void All_day_event_spans_local_midnights()
    {
        var ics = Cal(Ev("UID:d", "DTSTART;VALUE=DATE:20261105", "DTEND;VALUE=DATE:20261107", "SUMMARY:Offsite"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), India));
        Assert.True(o.AllDay);
        Assert.Equal(new DateTime(2026, 11, 5), o.Start);
        Assert.Equal(new DateTime(2026, 11, 7), o.End);
        // The same file read from New York is still the 5th and 6th there.
        var ny = Assert.Single(Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), NewYork));
        Assert.Equal(new DateTime(2026, 11, 5), ny.Start);
    }

    [Fact]
    public void All_day_without_end_is_one_day()
    {
        var ics = Cal(Ev("UID:d", "DTSTART;VALUE=DATE:20261105", "SUMMARY:Holiday"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), India));
        Assert.Equal(new DateTime(2026, 11, 6), o.End);
        Assert.Single(CalendarAgenda.OnDay(new[] { o }, new DateTime(2026, 11, 5)));
        Assert.Empty(CalendarAgenda.OnDay(new[] { o }, new DateTime(2026, 11, 6)));
    }

    [Fact]
    public void Yearly_all_day_birthday()
    {
        var ics = Cal(Ev("UID:b", "DTSTART;VALUE=DATE:20200310", "DTEND;VALUE=DATE:20200311", "RRULE:FREQ=YEARLY", "SUMMARY:Mom's birthday"));
        var o = Assert.Single(Expand(ics, new DateTime(2027, 1, 1), new DateTime(2027, 12, 31), India));
        Assert.Equal(new DateTime(2027, 3, 10), o.Start);
    }

    // ---- Recurrence ---------------------------------------------------------------------------------

    [Fact]
    public void Daily_with_count()
    {
        var ics = Cal(Ev("UID:r", "DTSTART;TZID=Asia/Kolkata:20261102T103000", "DURATION:PT15M", "RRULE:FREQ=DAILY;COUNT=3", "SUMMARY:Standup"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), India);
        Assert.Equal(new[] { 2, 3, 4 }, all.Select(o => o.Start.Day));
    }

    [Fact]
    public void Daily_with_interval_and_until()
    {
        var ics = Cal(Ev("UID:r", "DTSTART:20261102T050000Z", "DTEND:20261102T053000Z",
            "RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261108T050000Z", "SUMMARY:Run"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), India);
        Assert.Equal(new[] { 2, 4, 6, 8 }, all.Select(o => o.Start.Day));
    }

    [Fact]
    public void Weekdays_standup_with_exdate()
    {
        var ics = Cal(Ev("UID:s", "DTSTART;TZID=Asia/Kolkata:20261102T103000", "DTEND;TZID=Asia/Kolkata:20261102T104500",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "EXDATE;TZID=Asia/Kolkata:20261104T103000",
            "SUMMARY:Standup"));
        var all = Expand(ics, new DateTime(2026, 11, 2), new DateTime(2026, 11, 9), India);
        Assert.Equal(new[] { 2, 3, 5, 6 }, all.Select(o => o.Start.Day));
        Assert.All(all, o => Assert.Equal(new TimeSpan(10, 30, 0), o.Start.TimeOfDay));
    }

    [Fact]
    public void Exdate_in_utc_matches_a_zoned_series()
    {
        // Google writes EXDATEs in the series' zone, but other tools use UTC; both name the same instant.
        var ics = Cal(Ev("UID:s", "DTSTART;TZID=Asia/Kolkata:20261102T103000", "DURATION:PT15M",
            "RRULE:FREQ=DAILY", "EXDATE:20261103T050000Z,20261105T050000Z", "SUMMARY:Standup"));
        var all = Expand(ics, new DateTime(2026, 11, 2), new DateTime(2026, 11, 7), India);
        Assert.Equal(new[] { 2, 4, 6 }, all.Select(o => o.Start.Day));
    }

    [Fact]
    public void Biweekly_with_interval_and_wkst()
    {
        var ics = Cal(Ev("UID:p", "DTSTART;TZID=Asia/Kolkata:20261104T110000", "DTEND;TZID=Asia/Kolkata:20261104T120000",
            "RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=WE;WKST=SU", "SUMMARY:Sprint planning"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 12, 10), India);
        Assert.Equal(new[] { new DateTime(2026, 11, 4, 11, 0, 0), new DateTime(2026, 11, 18, 11, 0, 0), new DateTime(2026, 12, 2, 11, 0, 0) },
            all.Select(o => o.Start));
    }

    [Fact]
    public void Weekly_until_date_is_inclusive()
    {
        var ics = Cal(Ev("UID:p", "DTSTART;TZID=Asia/Kolkata:20261103T190000", "DURATION:PT1H",
            "RRULE:FREQ=WEEKLY;UNTIL=20261117", "SUMMARY:Gym"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 12, 31), India);
        Assert.Equal(new[] { 3, 10, 17 }, all.Select(o => o.Start.Day));
    }

    [Fact]
    public void Count_includes_instances_before_the_window()
    {
        var ics = Cal(Ev("UID:c", "DTSTART:20261026T050000Z", "DURATION:PT30M", "RRULE:FREQ=WEEKLY;COUNT=3", "SUMMARY:Course"));
        var all = Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 12, 31), India);
        Assert.Equal(new[] { new DateTime(2026, 11, 9, 10, 30, 0) }, all.Select(o => o.Start));
    }

    [Fact]
    public void Monthly_second_tuesday_and_last_friday()
    {
        var ics = Cal(Ev("UID:m", "DTSTART:20261013T050000Z", "DURATION:PT1H", "RRULE:FREQ=MONTHLY;BYDAY=2TU", "SUMMARY:Board"),
            Ev("UID:n", "DTSTART:20261030T050000Z", "DURATION:PT1H", "RRULE:FREQ=MONTHLY;BYDAY=-1FR", "SUMMARY:Retro"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 12, 31), India);
        Assert.Equal(new[] { "10 Board", "27 Retro", "8 Board", "25 Retro" }, all.Select(o => $"{o.Start.Day} {o.Title}"));
    }

    [Fact]
    public void Monthly_on_the_31st_skips_short_months()
    {
        var ics = Cal(Ev("UID:m", "DTSTART:20261031T050000Z", "DURATION:PT1H", "RRULE:FREQ=MONTHLY", "SUMMARY:Rent"));
        var all = Expand(ics, new DateTime(2026, 10, 1), new DateTime(2027, 1, 15), India);
        Assert.Equal(new[] { new DateTime(2026, 10, 31), new DateTime(2026, 12, 31) }, all.Select(o => o.Start.Date));
    }

    [Fact]
    public void Moved_instance_replaces_the_original()
    {
        var ics = Cal(
            Ev("UID:s", "DTSTART;TZID=Asia/Kolkata:20261102T103000", "DURATION:PT15M", "RRULE:FREQ=DAILY;COUNT=5", "SUMMARY:Standup"),
            Ev("UID:s", "RECURRENCE-ID;TZID=Asia/Kolkata:20261104T103000", "DTSTART;TZID=Asia/Kolkata:20261104T120000",
                "DTEND;TZID=Asia/Kolkata:20261104T121500", "SUMMARY:Standup (moved)"));
        var day = Expand(ics, new DateTime(2026, 11, 4), new DateTime(2026, 11, 5), India);
        var o = Assert.Single(day);
        Assert.Equal("Standup (moved)", o.Title);
        Assert.Equal(new TimeSpan(12, 0, 0), o.Start.TimeOfDay);
    }

    [Fact]
    public void Cancelled_instance_and_cancelled_event_are_dropped()
    {
        var ics = Cal(
            Ev("UID:s", "DTSTART;TZID=Asia/Kolkata:20261102T103000", "DURATION:PT15M", "RRULE:FREQ=DAILY;COUNT=3", "SUMMARY:Standup"),
            Ev("UID:s", "RECURRENCE-ID;TZID=Asia/Kolkata:20261103T103000", "DTSTART;TZID=Asia/Kolkata:20261103T103000",
                "STATUS:CANCELLED", "SUMMARY:Standup"),
            Ev("UID:x", "DTSTART:20261103T080000Z", "STATUS:CANCELLED", "SUMMARY:Gone"));
        var all = Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), India);
        Assert.Equal(new[] { 2, 4 }, all.Select(o => o.Start.Day));
    }

    [Fact]
    public void Unsupported_rule_shows_only_the_first_occurrence()
    {
        var ics = Cal(Ev("UID:u", "DTSTART:20261103T050000Z", "RRULE:FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU", "SUMMARY:Odd"));
        Assert.Single(Expand(ics, new DateTime(2026, 11, 1), new DateTime(2027, 6, 1), India));
        Assert.Null(RecurrenceRule.Parse("FREQ=HOURLY"));
    }

    [Fact]
    public void Events_outside_the_window_are_left_out()
    {
        var ics = Cal(Ev("UID:1", "DTSTART:20261001T050000Z", "DTEND:20261001T060000Z", "SUMMARY:Old"),
            Ev("UID:2", "DTSTART:20261103T050000Z", "DTEND:20261103T060000Z", "SUMMARY:Now"));
        Assert.Equal("Now", Assert.Single(Expand(ics, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10), India)).Title);
    }

    // ---- Meeting links ------------------------------------------------------------------------------

    [Fact]
    public void Finds_meet_link_in_google_description()
    {
        var link = MeetingLinks.Find("", "", "Join with Google Meet: https://meet.google.com/abc-defg-hij\nJoin by phone");
        Assert.Equal("https://meet.google.com/abc-defg-hij", link!.Url);
        Assert.Equal("Google Meet", link.Provider);
    }

    [Fact]
    public void Conference_field_wins_and_ics_event_carries_the_link()
    {
        var ics = Cal(Ev("UID:1", "DTSTART:20261103T050000Z", "DTEND:20261103T051500Z", "SUMMARY:Standup",
            "X-GOOGLE-CONFERENCE:https://meet.google.com/xyz-abcd-efg",
            "DESCRIPTION:Notes at https://docs.google.com/doc/1 and https://zoom.us/j/999"));
        var o = Assert.Single(Expand(ics, new DateTime(2026, 11, 3), new DateTime(2026, 11, 4), India));
        Assert.Equal("https://meet.google.com/xyz-abcd-efg", o.Link);
        Assert.Equal("Google Meet", o.Provider);
    }

    [Fact]
    public void Finds_zoom_and_teams_links()
    {
        Assert.Equal("Zoom", MeetingLinks.Find("https://us02web.zoom.us/j/81234567890?pwd=abcDEF123.")!.Provider);
        Assert.Equal("https://us02web.zoom.us/j/81234567890?pwd=abcDEF123", MeetingLinks.Find("(https://us02web.zoom.us/j/81234567890?pwd=abcDEF123).")!.Url);
        var teams = MeetingLinks.Find("Join: <https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%22Tid%22%7d>");
        Assert.Equal("Teams", teams!.Provider);
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc", teams.Url);
        Assert.Equal("Teams", MeetingLinks.Find("https://teams.live.com/meet/9876543210")!.Provider);
    }

    [Fact]
    public void Decodes_html_ampersands_and_ignores_other_links()
    {
        var link = MeetingLinks.Find("<a href=\"https://zoom.us/j/123?pwd=a&amp;uname=b\">Join</a>");
        Assert.Equal("https://zoom.us/j/123?pwd=a&uname=b", link!.Url);
        Assert.Null(MeetingLinks.Find("https://www.google.com/maps/place/Pune", "https://zoom.us/pricing", "https://meet.google.com/"));
    }

    // ---- Address validation -------------------------------------------------------------------------

    [Fact]
    public void Accepts_only_google_ical_addresses()
    {
        const string ok = "https://calendar.google.com/calendar/ical/me%40gmail.com/private-0123456789abcdef/basic.ics";
        Assert.Equal(ok, CalendarUrl.Normalize("  " + ok + "\n"));
        Assert.True(CalendarUrl.IsPrivate(ok));
        Assert.Equal(ok, CalendarUrl.Normalize(ok.Replace("https://", "webcal://")));
        Assert.Null(CalendarUrl.Normalize(ok.Replace("https://", "http://")));
        Assert.Null(CalendarUrl.Normalize("https://calendar.google.com.evil.com/calendar/ical/x/private-1/basic.ics"));
        Assert.Null(CalendarUrl.Normalize("https://calendar.google.com/calendar/embed?src=me"));
        Assert.Null(CalendarUrl.Normalize("https://calendar.google.com/calendar/ical/x/private-1/basic.ics.exe"));
        Assert.Null(CalendarUrl.Normalize("not a url"));
        Assert.Null(CalendarUrl.Normalize(""));
        Assert.False(CalendarUrl.IsPrivate("https://calendar.google.com/calendar/ical/me%40gmail.com/public/basic.ics"));
    }

    // ---- Agenda and texts ---------------------------------------------------------------------------

    static CalendarOccurrence Occ(string title, DateTime start, int minutes, string? link = "https://meet.google.com/a-b-c", bool allDay = false) =>
        new("u", title, "", start, start.AddMinutes(minutes), allDay, link, link is null ? null : "Google Meet");

    [Fact]
    public void Until_texts()
    {
        var now = new DateTime(2026, 11, 3, 10, 20, 0);
        Assert.Equal("in 10 min", CalendarText.Until(now.AddMinutes(10), now.AddMinutes(25), now));
        Assert.Equal("in 1 min", CalendarText.Until(now.AddSeconds(20), now.AddMinutes(25), now));
        Assert.Equal("in 1 h 20 min", CalendarText.Until(now.AddMinutes(80), now.AddMinutes(95), now));
        Assert.Equal("in 3 h", CalendarText.Until(now.AddHours(3), now.AddHours(4), now));
        Assert.Equal("now", CalendarText.Until(now.AddMinutes(-5), now.AddMinutes(10), now));
        Assert.Equal("tomorrow", CalendarText.Until(new DateTime(2026, 11, 4, 23, 0, 0), new DateTime(2026, 11, 4, 23, 30, 0), now));
        Assert.Equal("on Thursday", CalendarText.Until(new DateTime(2026, 11, 5, 9, 0, 0), new DateTime(2026, 11, 5, 9, 30, 0), now));
    }

    [Fact]
    public void Range_and_glance_texts()
    {
        Assert.Equal("10:30 – 10:45", CalendarText.Range(new DateTime(2026, 11, 3, 10, 30, 0), new DateTime(2026, 11, 3, 10, 45, 0)));
        Assert.Equal("2 – 3 PM", CalendarText.Range(new DateTime(2026, 11, 3, 14, 0, 0), new DateTime(2026, 11, 3, 15, 0, 0)));
        Assert.Equal("11:30 AM – 12:15 PM", CalendarText.Range(new DateTime(2026, 11, 3, 11, 30, 0), new DateTime(2026, 11, 3, 12, 15, 0)));
        Assert.Equal("Standup 10:30", CalendarText.Glance("Standup", new DateTime(2026, 11, 3, 10, 30, 0)));
        var g = CalendarText.Glance("Quarterly business review", new DateTime(2026, 11, 3, 14, 0, 0));
        Assert.True(g.Length <= 18);
        Assert.EndsWith("… 2:00", g);
    }

    [Fact]
    public void Glance_priority_depends_on_how_soon()
    {
        var now = new DateTime(2026, 11, 3, 10, 20, 0);
        var soon = CalendarAgenda.Glance(new[] { Occ("Standup", now.AddMinutes(10), 15) }, now);
        Assert.Equal(("Standup 10:30", 50), (soon!.Value.Text, soon.Value.Priority));
        var later = CalendarAgenda.Glance(new[] { Occ("Design review", new DateTime(2026, 11, 3, 14, 0, 0), 60) }, now);
        Assert.Equal(20, later!.Value.Priority);
        Assert.Null(CalendarAgenda.Glance(new[] { Occ("Tomorrow", new DateTime(2026, 11, 4, 14, 0, 0), 60) }, now));
        Assert.Null(CalendarAgenda.Glance(new[] { Occ("Holiday", now.Date, 1440, allDay: true) }, now));
    }

    [Fact]
    public void Pill_shows_from_five_minutes_before_until_five_after_and_needs_a_link()
    {
        var start = new DateTime(2026, 11, 3, 10, 30, 0);
        var items = new[] { Occ("Standup", start, 15), Occ("Lunch", start.AddMinutes(1), 30, link: null) };
        Assert.Null(CalendarAgenda.PillFor(items, start.AddMinutes(-6)));
        Assert.Equal("Standup", CalendarAgenda.PillFor(items, start.AddMinutes(-5))!.Title);
        Assert.Equal("Standup in 2 min", CalendarText.Pill(items[0], start.AddMinutes(-2)));
        Assert.Equal("Standup now", CalendarText.Pill(items[0], start.AddMinutes(1)));
        Assert.Null(CalendarAgenda.PillFor(items, start.AddMinutes(5)));
        Assert.Equal(start.AddMinutes(-5), CalendarAgenda.NextPillAt(items, start.AddMinutes(-30)));
        Assert.Null(CalendarAgenda.NextPillAt(items, start.AddMinutes(-5)));
    }

    [Fact]
    public void Relevance_next_and_week()
    {
        var now = new DateTime(2026, 11, 3, 21, 0, 0);
        Assert.False(CalendarAgenda.IsRelevant(new[] { Occ("Done", now.AddHours(-2), 30) }, now));
        Assert.True(CalendarAgenda.IsRelevant(new[] { Occ("Early", new DateTime(2026, 11, 4, 8, 0, 0), 30) }, now));
        Assert.False(CalendarAgenda.IsRelevant(new[] { Occ("Later", new DateTime(2026, 11, 4, 10, 0, 0), 30) }, now));
        var ongoing = Occ("Call", now.AddMinutes(-10), 30);
        Assert.Same(ongoing, CalendarAgenda.Next(new[] { ongoing, Occ("Next", now.AddHours(1), 30) }, now));
        var week = CalendarAgenda.Week(new DateTime(2026, 11, 3));
        Assert.Equal(new DateTime(2026, 11, 2), week[0]);
        Assert.Equal(DayOfWeek.Sunday, week[6].DayOfWeek);
    }
}
