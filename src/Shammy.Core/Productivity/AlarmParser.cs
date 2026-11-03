using System.Text.RegularExpressions;

namespace Shammy.Core.Productivity;

/// <summary>
/// Alarm commands: "set an alarm for 7 am", "alarm at 6:30 every weekday", "wake me up at 7 on weekends",
/// "alarms", "delete the 7 am alarm", "turn off my alarms", "snooze", "stop the alarm". Strict like the rest of
/// <see cref="ProductivityParser"/>: "alarm clock app" or "alarm sounds" stay searches. A time without am/pm
/// reads as an alarm would mean it: "at 7" is 7 AM and "at 2" is 2 PM (4–11 morning, 12 noon, 1–3 afternoon).
/// </summary>
public static class AlarmParser
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex SetAlarm = new(@"^(?:(?:set|create|make|add|start|put)\s+(?:me\s+)?(?:up\s+)?)?(?:an?\s+|the\s+|my\s+)?(?:new\s+)?alarm(?:\s+(?:for|on)\b)?\s*(?<when>.*)$", Opts);
    static readonly Regex TimeFirst = new(@"^(?:set|create|make|add|put)\s+(?:me\s+)?(?:an?\s+)?(?<time>\d{1,2}(?:[:.]\d{2})?(?:am|pm)?)\s+alarm\b\s*(?<rest>.*)$", Opts);
    static readonly Regex WakeMe = new(@"^wake\s+me(?:\s+up)?\s+(?<when>.+)$", Opts);
    static readonly Regex ShowAlarms = new(@"^(?:(?:show|open|view|see|list|check)\s+(?:me\s+)?(?:all\s+)?(?:of\s+)?(?:my\s+|the\s+)?)?(?:my\s+)?alarms(?:\s+list)?$", Opts);
    static readonly Regex StopRinging = new(@"^(?:stop|dismiss|silence|shut\s+up|shut\s+off|end)\s+(?:the\s+|that\s+|this\s+|my\s+)?alarm$", Opts);
    static readonly Regex Snooze = new(@"^snooze(?:\s+(?:the\s+|that\s+|this\s+|my\s+)?alarm)?(?:\s+(?:for\s+)?(?<dur>.+))?$", Opts);
    static readonly Regex Manage = new(@"^(?<verb>delete|remove|cancel|clear|erase|turn\s+off|switch\s+off|disable|deactivate|turn\s+on|switch\s+on|enable|activate)\s+" +
        @"(?<all>all\s+)?(?:(?:of\s+)?(?:the|my)\s+)?(?<when>.*?)\s*\b(?<noun>alarms?)(?:\s+(?:for|at|set\s+for)\s+(?<when2>.+))?$", Opts);

    static readonly HashSet<string> PartWords = new(StringComparer.OrdinalIgnoreCase) { "morning", "afternoon", "evening", "night", "tonight" };

    static readonly HashSet<string> MonthWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "jan", "january", "feb", "february", "mar", "march", "apr", "april", "may", "jun", "june", "jul", "july", "aug",
        "august", "sep", "sept", "september", "oct", "october", "nov", "november", "dec", "december",
    };

    static readonly HashSet<string> DayWords = new(new[]
    {
        "today", "tonight", "tomorrow", "tmrw", "tmr", "parso", "parson", "parsoon", "parsun", "day", "monday", "tuesday",
        "wednesday", "thursday", "friday", "saturday", "sunday",
    }.Concat(MonthWords), StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> LabelLead = new(StringComparer.OrdinalIgnoreCase)
    {
        "for", "to", "called", "named", "labelled", "labeled", "label", "titled", "saying", "about", "-", ":",
    };

    static readonly Dictionary<string, AlarmDays> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = AlarmDays.Monday, ["mon"] = AlarmDays.Monday,
        ["tuesday"] = AlarmDays.Tuesday, ["tue"] = AlarmDays.Tuesday, ["tues"] = AlarmDays.Tuesday,
        ["wednesday"] = AlarmDays.Wednesday, ["wed"] = AlarmDays.Wednesday,
        ["thursday"] = AlarmDays.Thursday, ["thu"] = AlarmDays.Thursday, ["thur"] = AlarmDays.Thursday, ["thurs"] = AlarmDays.Thursday,
        ["friday"] = AlarmDays.Friday, ["fri"] = AlarmDays.Friday,
        ["saturday"] = AlarmDays.Saturday, ["sat"] = AlarmDays.Saturday,
        ["sunday"] = AlarmDays.Sunday, ["sun"] = AlarmDays.Sunday,
    };

    public static ProductivityCommand? Parse(string s, DateTime now)
    {
        if (ShowAlarms.IsMatch(s)) return new ProductivityCommand(ProductivityAction.ShowAlarms);
        if (StopRinging.IsMatch(s)) return new ProductivityCommand(ProductivityAction.StopAlarm);
        if (Snooze.Match(s) is { Success: true } snooze) return ParseSnooze(snooze);
        if (Manage.Match(s) is { Success: true } manage) return ParseManage(manage);
        return ParseSet(s, now);
    }

    // ---- Setting one ------------------------------------------------------------------------------

    static ProductivityCommand? ParseSet(string s, DateTime now)
    {
        s = WhenParser.Normalize(s);
        string when;
        bool asked;
        if (TimeFirst.Match(s) is { Success: true } first)
        {
            when = first.Groups["time"].Value + " " + first.Groups["rest"].Value;
            asked = true;
        }
        else if (SetAlarm.Match(s) is { Success: true } set)
        {
            when = set.Groups["when"].Value;
            asked = !s.StartsWith("alarm", StringComparison.OrdinalIgnoreCase);
        }
        else if (WakeMe.Match(s) is { Success: true } wake)
        {
            when = wake.Groups["when"].Value;
            asked = true;
        }
        else return null;

        // Parse lower-cased, punctuation-free words but hand back the label as typed.
        var pairs = when.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(SplitRange)
            .Select(w => (Word: WhenParser.Clean(w), Original: w))
            .Where(p => p.Word.Length > 0)
            .ToList();
        var days = TakeRepeat(pairs);
        if (pairs.Count == 0)
            return asked || days != AlarmDays.None ? new ProductivityCommand(ProductivityAction.ReminderNeedsTime) { Text = "Alarm" } : null;

        var words = pairs.Select(p => p.Word).ToList();
        // Time first ("at 7 for gym"), longest phrase wins; then label first ("gym at 7").
        for (int k = words.Count; k >= 1; k--)
        {
            if (Time(words.Take(k).ToList(), now, days != AlarmDays.None) is not { } t) continue;
            return Alarm(t, days, pairs.Skip(k).Select(p => p.Original));
        }
        for (int k = 1; k < words.Count; k++)
        {
            if (Time(words.Skip(k).ToList(), now, days != AlarmDays.None) is not { } t) continue;
            return Alarm(t, days, pairs.Take(k).Select(p => p.Original));
        }
        return null;
    }

    static ProductivityCommand Alarm((int Hour, int Minute, DateTime? Date) t, AlarmDays days, IEnumerable<string> labelWords)
    {
        var label = Label(labelWords);
        return new ProductivityCommand(ProductivityAction.SetAlarm)
        {
            Alarm = new AlarmSpec(t.Hour, t.Minute, days, days == AlarmDays.None ? t.Date : null, label),
            Text = label,
            Due = t.Date is { } d ? d.Date.AddHours(t.Hour).AddMinutes(t.Minute) : default,
        };
    }

    /// <summary>"monday-friday" is two days and a range.</summary>
    static IEnumerable<string> SplitRange(string word)
    {
        var parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && DayNames.ContainsKey(parts[0].TrimEnd('s')) && DayNames.ContainsKey(parts[1].TrimEnd('s')))
            return new[] { parts[0], "to", parts[1] };
        return new[] { word };
    }

    /// <summary>Finds and removes the repeat phrase ("every weekday", "on weekends", "mondays and fridays", "daily").</summary>
    static AlarmDays TakeRepeat(List<(string Word, string Original)> pairs)
    {
        var days = AlarmDays.None;
        string W(int i) => i >= 0 && i < pairs.Count ? pairs[i].Word : "";
        for (int i = 0; i < pairs.Count; i++)
        {
            string w = W(i), next = W(i + 1);
            bool lead = w is "every" or "each" or "on" or "all";
            int start = i, end;
            if (w is "daily" or "everyday") { days |= AlarmDays.EveryDay; end = i + 1; }
            else if (w is "every" or "each" && next == "day") { days |= AlarmDays.EveryDay; end = i + 2; }
            else if (w == "all" && next == "days") { days |= AlarmDays.EveryDay; end = i + 2; }
            else if (IsWeekday(w) || (lead && IsWeekday(next)) || (lead && next == "the" && IsWeekday(W(i + 2))))
            {
                days |= AlarmDays.Weekdays;
                end = IsWeekday(w) ? i + 1 : next == "the" ? i + 3 : i + 2;
            }
            else if (IsWeekend(w) || (lead && IsWeekend(next)) || (lead && next == "the" && IsWeekend(W(i + 2))))
            {
                days |= AlarmDays.Weekends;
                end = IsWeekend(w) ? i + 1 : next == "the" ? i + 3 : i + 2;
            }
            else if (DayList(pairs, lead ? i + 1 : i, out var listed, out int listEnd, out bool plural, out int count) &&
                     (w is "every" or "each" || plural || count > 1))
            {
                days |= listed;
                end = listEnd;
            }
            else continue;
            pairs.RemoveRange(start, end - start);
            i = start - 1;
        }
        return days;
    }

    static bool IsWeekday(string w) => w is "weekday" or "weekdays" or "workday" or "workdays";
    static bool IsWeekend(string w) => w is "weekend" or "weekends";

    /// <summary>"monday and wednesday", "mon, wed, fri", "monday to friday" starting at <paramref name="i"/>.</summary>
    static bool DayList(List<(string Word, string Original)> pairs, int i, out AlarmDays days, out int end, out bool plural, out int count)
    {
        days = AlarmDays.None;
        end = i;
        plural = false;
        count = 0;
        int j = i;
        while (j < pairs.Count)
        {
            var word = pairs[j].Word;
            bool s = word.EndsWith('s') && DayNames.ContainsKey(word[..^1]);
            if (!DayNames.TryGetValue(s ? word[..^1] : word, out var day)) break;
            // "monday to friday": every day between.
            if (j + 2 < pairs.Count && pairs[j + 1].Word is "to" or "through" or "till" or "until" &&
                DayNames.TryGetValue(pairs[j + 2].Word.TrimEnd('s'), out var last))
            {
                days |= Range(day, last);
                count += 2;
                plural = true;
                j += 3;
            }
            else
            {
                days |= day;
                plural |= s;
                count++;
                j++;
            }
            end = j;
            // "monday and wednesday"; "mon, wed" needs nothing (the comma was cleaned off the word).
            if (j + 1 < pairs.Count && pairs[j].Word is "and" or "or" or "&" && IsDay(pairs[j + 1].Word)) j++;
        }
        return count > 0;
    }

    static bool IsDay(string w) => DayNames.ContainsKey(w) || (w.EndsWith('s') && DayNames.ContainsKey(w[..^1]));

    static AlarmDays Range(AlarmDays from, AlarmDays to)
    {
        // Bits run Monday (1) to Sunday (64); a range can wrap ("friday to monday").
        var result = AlarmDays.None;
        int bit = (int)from;
        for (int guard = 0; guard < 7; guard++)
        {
            result |= (AlarmDays)bit;
            if (bit == (int)to) break;
            bit = bit == 64 ? 1 : bit << 1;
        }
        return result;
    }

    /// <summary>
    /// The time the words say, the way an alarm means it, with a one-off's date. Null when the words aren't a time
    /// (or say a day for a repeating alarm, or are relative like "in 20 minutes" for one).
    /// </summary>
    static (int Hour, int Minute, DateTime? Date)? Time(List<string> w, DateTime now, bool repeating)
    {
        if (w.Count == 0) return null;
        var due = WhenParser.ParseWords(w, now);
        if (due is null && w[0] is not ("at" or "in" or "after" or "for")) due = WhenParser.ParseWords(new[] { "at" }.Concat(w).ToList(), now);
        if (due is not { } at) return null;

        bool relative = w[0] is "in" or "after" or "within" || w[^1] is "later" or "now" || (w[0] == "for" && WhenParser.ParseClock(w.Count > 1 ? w[1] : "") is null);
        bool dayGiven = w.Any(DayWords.Contains);
        if (relative) return repeating ? null : (at.Hour, at.Minute, at.Date);
        if (repeating && dayGiven) return null;

        bool part = w.Any(PartWords.Contains);
        var clock = ClockIn(w);
        if (clock is { Exact: false } c && !part)
        {
            int hour = AlarmHour(c.Hour);
            if (repeating) return (hour, c.Minute, null);
            var date = dayGiven ? at.Date : now.Date;
            var local = date.AddHours(hour).AddMinutes(c.Minute);
            if (local <= now) local = dayGiven ? at : local.AddDays(1);
            return (local.Hour, local.Minute, local.Date);
        }
        return (at.Hour, at.Minute, repeating ? null : at.Date);
    }

    static WhenParser.Clock? ClockIn(List<string> w)
    {
        for (int i = 0; i + 1 < w.Count; i++)
            if (w[i] is "at" or "by" or "around" or "for" && WhenParser.ParseClock(w[i + 1]) is { } c) return c;
        // Without "at", a number next to a month is the date ("8 march"), not the time.
        if (w.Any(MonthWords.Contains)) return null;
        foreach (var x in w)
            if (WhenParser.ParseClock(x) is { } c) return c;
        return null;
    }

    /// <summary>"7" for an alarm is 7 AM; "2" is 2 PM; 12 is noon.</summary>
    public static int AlarmHour(int hour) => hour switch
    {
        >= 4 and <= 11 => hour,
        12 => 12,
        _ => hour % 12 + 12,
    };

    static string Label(IEnumerable<string> words)
    {
        var list = words.ToList();
        while (list.Count > 0 && LabelLead.Contains(list[0].Trim(',', ':', '-'))) list.RemoveAt(0);
        var label = string.Join(' ', list).Trim().Trim(',', ':', '-', ' ', '"', '“', '”');
        if (label.Length > 40) label = label[..40].TrimEnd();
        return label.Length == 0 ? "" : char.ToUpperInvariant(label[0]) + label[1..];
    }

    // ---- Managing them ----------------------------------------------------------------------------

    static ProductivityCommand? ParseSnooze(Match m)
    {
        if (!m.Groups["dur"].Success) return new ProductivityCommand(ProductivityAction.SnoozeAlarm);
        var text = m.Groups["dur"].Value.Trim();
        var d = DurationParser.Parse(text)
                ?? (int.TryParse(text, out var minutes) && minutes is > 0 and <= 60 ? TimeSpan.FromMinutes(minutes) : null);
        return d is { } duration && duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(2)
            ? new ProductivityCommand(ProductivityAction.SnoozeAlarm) { Duration = duration }
            : null;
    }

    static ProductivityCommand? ParseManage(Match m)
    {
        var verb = Regex.Replace(m.Groups["verb"].Value.ToLowerInvariant(), @"\s+", " ");
        var action = verb switch
        {
            "turn off" or "switch off" or "disable" or "deactivate" => ProductivityAction.DisableAlarms,
            "turn on" or "switch on" or "enable" or "activate" => ProductivityAction.EnableAlarms,
            _ => ProductivityAction.DeleteAlarm,
        };
        var whenText = (m.Groups["when2"].Success ? m.Groups["when2"].Value : m.Groups["when"].Value).Trim();
        bool plural = m.Groups["noun"].Value.EndsWith('s');
        bool all = m.Groups["all"].Success || (plural && whenText.Length == 0);

        if (whenText.Length == 0)
        {
            // "turn off the alarm" while one rings means stop it; the module decides when nothing rings.
            return new ProductivityCommand(action) { All = all };
        }
        var words = WhenParser.Normalize(whenText).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(WhenParser.Clean).Where(x => x.Length > 0).ToList();
        if (words.Count > 0 && words[0] is "at" or "for") words.RemoveAt(0);
        if (words.Count == 1 && WhenParser.ParseClock(words[0]) is { } c)
            return new ProductivityCommand(action) { All = all, Target = new AlarmTarget(c.Hour, c.Minute, c.Exact) };
        // "delete the gym alarm": by label. A long phrase is more likely a search ("cancel the alarm clock subscription").
        if (words.Count is 0 or > 3 || words.Any(x => x.Any(char.IsDigit))) return null;
        return new ProductivityCommand(action) { All = all, Text = string.Join(' ', words) };
    }
}
