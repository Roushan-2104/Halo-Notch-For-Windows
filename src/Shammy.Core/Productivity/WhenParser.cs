using System.Globalization;
using System.Text.RegularExpressions;

namespace Shammy.Core.Productivity;

/// <summary>
/// Points in time for reminders: "in 20 minutes", "at 6pm", "at 18:30", "tomorrow at 9", "tonight at 8",
/// "on friday at 5pm", "tomorrow morning". Everything resolves against the <c>now</c> passed in (local time).
/// </summary>
public static class WhenParser
{
    /// <summary>A clock reading. <see cref="Exact"/> is false for "at 9", which could be morning or evening.</summary>
    public readonly record struct Clock(int Hour, int Minute, bool Exact);

    enum Part { Morning, Afternoon, Evening, Night }

    static readonly Regex Meridiem = new(@"(\d)\s*([ap])\.?\s*m\.?(?=\s|$|[,;!?])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex OClock = new(@"\s*\bo'?\s?clock\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ClockWord = new(@"^(\d{1,2})(?:[:.](\d{2}))?(am|pm)?$", RegexOptions.CultureInvariant);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["monday"] = DayOfWeek.Monday, ["tuesday"] = DayOfWeek.Tuesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday, ["friday"] = DayOfWeek.Friday, ["saturday"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday,
    };

    static readonly Dictionary<string, int> Months = new()
    {
        ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3, ["apr"] = 4, ["april"] = 4,
        ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7, ["aug"] = 8, ["august"] = 8,
        ["sep"] = 9, ["sept"] = 9, ["september"] = 9, ["oct"] = 10, ["october"] = 10, ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["december"] = 12,
    };

    static readonly Regex DayOfMonth = new(@"^(\d{1,2})(?:st|nd|rd|th)?$", RegexOptions.CultureInvariant);

    /// <summary>"8 march", "8th of march", "march 8", optionally with a year, starting at w[i]. Returns words used.</summary>
    static int ParseDate(IReadOnlyList<string> w, int i, out int month, out int day, out int? year)
    {
        month = day = 0;
        year = null;
        int used;
        if (DayNumber(w, i) is { } d1)
        {
            int j = i + 1;
            if (j < w.Count && w[j] == "of") j++;
            if (j >= w.Count || !Months.TryGetValue(w[j], out month)) return 0;
            day = d1;
            used = j + 1 - i;
        }
        else if (Months.TryGetValue(w[i], out month) && DayNumber(w, i + 1) is { } d2)
        {
            day = d2;
            used = 2;
        }
        else return 0;
        if (i + used < w.Count && w[i + used].Length == 4 && int.TryParse(w[i + used], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 2000 and <= 2100)
        {
            year = y;
            used++;
        }
        return used;
    }

    static int? DayNumber(IReadOnlyList<string> w, int i) =>
        i < w.Count && DayOfMonth.Match(w[i]) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var d and >= 1 and <= 31 ? d : null;

    /// <summary>True when the text says "on" a calendar date ("on 8 march", "on the 31st of feb"), valid or not.</summary>
    public static bool MentionsDate(string text)
    {
        var w = Words(text);
        for (int i = 0; i + 1 < w.Count; i++)
        {
            if (w[i] != "on") continue;
            int j = w[i + 1] == "the" ? i + 2 : i + 1;
            if (j < w.Count && ParseDate(w, j, out _, out _, out _) > 0) return true;
        }
        return false;
    }

    /// <summary>"6 p.m." becomes "6pm" and "9 o'clock" becomes "9", so each clock reading is one word. Case is kept.</summary>
    public static string Normalize(string text)
    {
        var s = Meridiem.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant() + "m");
        s = OClock.Replace(s, "");
        return Spaces.Replace(s, " ").Trim();
    }

    /// <summary>The whole text must be a time phrase; null otherwise.</summary>
    public static DateTime? Parse(string text, DateTime now) => ParseWords(Words(Normalize(text)), now);

    /// <summary>One clock word: "6pm", "6:30pm", "6.30pm", "18:30", "9", "noon", "midnight". Null if it isn't one.</summary>
    public static Clock? ParseClock(string word)
    {
        word = word.ToLowerInvariant();
        if (word is "noon" or "midday") return new Clock(12, 0, true);
        if (word == "midnight") return new Clock(0, 0, true);
        var m = ClockWord.Match(word);
        if (!m.Success) return null;
        int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > 59) return null;
        if (m.Groups[3].Success)
        {
            if (hour is < 1 or > 12) return null;
            bool pm = m.Groups[3].Value == "pm";
            return new Clock(hour % 12 + (pm ? 12 : 0), minute, true);
        }
        if (hour > 23) return null;
        // "18:30", "0:15" and "09:00" can only mean one thing; "9" and "9:30" can be either half of the day.
        bool exact = hour is 0 or > 12 || (m.Groups[1].Value.Length == 2 && m.Groups[1].Value[0] == '0');
        if (!m.Groups[2].Success && hour == 0) return null; // a bare "0" is not a time anyone says
        return new Clock(hour, minute, exact);
    }

    /// <summary>Lower-cased words with surrounding punctuation removed.</summary>
    internal static List<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Clean).Where(w => w.Length > 0).ToList();

    internal static string Clean(string word) =>
        word.Trim(',', ';', ':', '!', '?', '"', '(', ')', '-', '\u201C', '\u201D').TrimEnd('.').ToLowerInvariant();

    /// <summary>
    /// "in 1 hour" means one real hour, so add it in UTC: on the night the clocks go back, local wall time
    /// + 1 h would fire two real hours later (and an hour early in spring).
    /// </summary>
    internal static DateTime Ahead(DateTime now, TimeSpan d) =>
        now.Kind == DateTimeKind.Utc ? now + d : DateTime.SpecifyKind(Ahead(now, d, TimeZoneInfo.Local), now.Kind);

    /// <summary><paramref name="local"/> + <paramref name="d"/> of real time, as wall-clock time in <paramref name="zone"/>.</summary>
    public static DateTime Ahead(DateTime local, TimeSpan d, TimeZoneInfo zone)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
        return TimeZoneInfo.ConvertTimeFromUtc(utc + d, zone);
    }

    internal static DateTime? ParseWords(IReadOnlyList<string> w, DateTime now)
    {
        int n = w.Count;
        if (n == 0) return null;

        // Relative: "in 20 minutes", "after an hour", "for 10 minutes", "20 minutes from now", "an hour later"
        if (w[0] is "in" or "after" or "within" or "for" && n > 1 && DurationParser.Parse(string.Join(' ', w.Skip(1))) is { } ahead)
            return Ahead(now, ahead);
        if (n > 2 && w[n - 2] == "from" && w[n - 1] == "now" && DurationParser.Parse(string.Join(' ', w.Take(n - 2))) is { } fromNow)
            return Ahead(now, fromNow);
        if (n > 1 && w[n - 1] == "later" && DurationParser.Parse(string.Join(' ', w.Take(n - 1))) is { } later)
            return Ahead(now, later);

        bool tomorrow = false, today = false, nextWeek = false;
        DayOfWeek? weekday = null;
        Part? part = null;
        Clock? clock = null;
        bool bareNumber = false;
        DateTime? onDate = null;
        bool dateHasYear = false;

        int i = 0;
        while (i < n)
        {
            string word = w[i];
            string? next = i + 1 < n ? w[i + 1] : null;

            // "on 8 march", "the 8th of march", "march 8 2027": a calendar date (checked before "8" reads as a clock).
            int skip = word is "on" or "the" && next is not null ? 1 : 0;
            if (skip == 1 && w[i + 1] == "the" && i + 2 < n) skip = 2;
            if (ParseDate(w, i + skip, out int month, out int dom, out int? year) is var used and > 0)
            {
                if (onDate is not null || today || tomorrow) return null;
                int y = year ?? now.Year;
                if (dom > DateTime.DaysInMonth(y, month)) return null;
                onDate = new DateTime(y, month, dom, 0, 0, 0, now.Kind);
                dateHasYear = year is not null;
                i += skip + used;
                continue;
            }

            if (word is "today" or "tonight")
            {
                if (today || tomorrow || weekday is not null) return null;
                today = true;
                if (word == "tonight")
                {
                    if (part is not null) return null;
                    part = Part.Night;
                }
                i++;
            }
            else if (word is "tomorrow" or "tmrw" or "tmr")
            {
                if (today || tomorrow || weekday is not null) return null;
                tomorrow = true;
                i++;
            }
            else if (word is "on" or "this" or "next" && next is not null && Weekdays.ContainsKey(next))
            {
                nextWeek |= word == "next";
                i++;
            }
            else if (Weekdays.TryGetValue(word, out var day))
            {
                if (today || tomorrow || weekday is not null) return null;
                weekday = day;
                i++;
            }
            else if ((word is "this" or "the" && PartOf(next) is not null) || (word == "in" && next == "the"))
            {
                // "this evening", "in the morning": filler before the part of day
                i++;
            }
            else if (PartOf(word) is { } p)
            {
                if (part is not null) return null;
                part = p;
                i++;
            }
            else if (word is "at" or "by" or "around" or "for" && next == "night")
            {
                if (part is not null) return null;
                part = Part.Night;
                i += 2;
            }
            else if (word is "at" or "by" or "around" or "for" && next is not null && ParseClock(next) is { } c)
            {
                if (clock is not null) return null;
                clock = c;
                i += 2;
            }
            else if (ParseClock(word) is { } bare)
            {
                if (clock is not null) return null;
                clock = bare;
                bareNumber = word.All(char.IsDigit);
                i++;
            }
            else
            {
                return null;
            }
        }

        // A lone "2" is a count ("buy 2"), not a time, unless a day or "morning" says otherwise.
        if (bareNumber && !today && !tomorrow && weekday is null && part is null && onDate is null) return null;
        if (onDate is { } on)
        {
            if (today || tomorrow) return null;
            return ResolveDate(now, on, dateHasYear, part, clock);
        }
        return Resolve(now, tomorrow, weekday, nextWeek, part, clock);
    }

    /// <summary>
    /// A named date: 9 AM unless a time or part of day is given. Without a year, a date already gone means next
    /// year's; with one, a moment in the past is no reminder at all.
    /// </summary>
    static DateTime? ResolveDate(DateTime now, DateTime date, bool hasYear, Part? part, Clock? clock)
    {
        (int hour, int minute) = clock is { } c
            ? (c.Exact ? c.Hour : part is { } p ? WithPart(c.Hour, p) : c.Hour is >= 7 and <= 12 ? c.Hour : c.Hour % 12 + 12, c.Minute)
            : part switch
            {
                Part.Morning => (9, 0),
                Part.Afternoon => (14, 0),
                Part.Evening => (18, 0),
                Part.Night => (21, 0),
                _ => (9, 0),
            };
        var at = date.AddHours(hour).AddMinutes(minute);
        if (at > now) return at;
        if (hasYear) return null;
        var nextYear = date.AddYears(1);
        // 29 Feb → 28 Feb in a common year; better a day early than a reminder that never comes.
        return nextYear.AddHours(hour).AddMinutes(minute);
    }

    static Part? PartOf(string? word) => word switch
    {
        "morning" => Part.Morning,
        "afternoon" => Part.Afternoon,
        "evening" => Part.Evening,
        "night" => Part.Night,
        _ => null,
    };

    static DateTime? Resolve(DateTime now, bool tomorrow, DayOfWeek? weekday, bool nextWeek, Part? part, Clock? clock)
    {
        var date = now.Date;
        bool futureDay = false;
        if (tomorrow)
        {
            date = date.AddDays(1);
            futureDay = true;
        }
        else if (weekday is { } wd)
        {
            int diff = ((int)wd - (int)now.DayOfWeek + 7) % 7;
            if (diff == 0 && nextWeek) diff = 7;
            date = date.AddDays(diff);
            futureDay = diff > 0;
        }
        // Today's time already gone: a named weekday means next week, otherwise tomorrow.
        int rollDays = weekday is not null ? 7 : 1;

        int hour, minute;
        if (clock is { } c)
        {
            minute = c.Minute;
            if (c.Exact) hour = c.Hour;
            else if (part is { } p) hour = WithPart(c.Hour, p);
            else if (futureDay) hour = c.Hour is >= 7 and <= 12 ? c.Hour : c.Hour % 12 + 12; // "tomorrow at 9" is morning, "at 3" afternoon
            else
            {
                // "at 9" today: whichever 9 o'clock comes next.
                var am = date.AddHours(c.Hour % 12).AddMinutes(minute);
                if (am > now) return am;
                if (am.AddHours(12) > now) return am.AddHours(12);
                return am.AddDays(rollDays);
            }
        }
        else if (part is { } p)
        {
            (hour, minute) = p switch
            {
                Part.Morning => (9, 0),
                Part.Afternoon => (14, 0),
                Part.Evening => (18, 0),
                _ => (21, 0),
            };
        }
        else if (futureDay || weekday is not null)
        {
            (hour, minute) = (9, 0);
        }
        else
        {
            return null; // "today" on its own says nothing about when
        }

        var at = date.AddHours(hour).AddMinutes(minute);
        if (at <= now) at = at.AddDays(rollDays);
        return at;
    }

    static int WithPart(int hour, Part part) => part switch
    {
        Part.Morning => hour % 12,
        Part.Night when hour is >= 1 and <= 4 => hour,
        Part.Night when hour == 12 => 0,
        _ => hour % 12 + 12,
    };
}
