using System.Globalization;
using System.Text.RegularExpressions;
using Shammy.Core.Calendar;

namespace Shammy.Core.Connect;

/// <summary>"remind me 5 minutes before standup": how long before, and which meeting.</summary>
/// <param name="Before">How long before the start (5 minutes when not said).</param>
/// <param name="Target">The meeting's name as said ("standup"); null for "my next meeting".</param>
/// <param name="Text">What to be reminded of ("to send the deck"), when said after a "next meeting" phrase.</param>
public sealed record MeetingReminderRequest(TimeSpan Before, string? Target, string? Text)
{
    public bool IsNextMeeting => Target is null;
}

/// <summary>The reminder to create: when, for which occurrence, with what text.</summary>
public sealed record MeetingReminderPlan(DateTime Due, CalendarOccurrence Meeting, string Text);

/// <summary>Reminders tied to the calendar: "remind me before my next meeting", "remind me 10 min before standup".</summary>
public static partial class MeetingReminder
{
    public static readonly TimeSpan DefaultBefore = TimeSpan.FromMinutes(5);

    /// <summary>How far ahead a named meeting is looked for.</summary>
    static readonly TimeSpan Horizon = TimeSpan.FromDays(7);

    [GeneratedRegex(@"^(?:hey\s+shammy[,\s]+)?remind\s+me\s+(?:(?<n>\d{1,3}|an?|one|two|three|five|ten|fifteen|twenty|thirty)\s*(?<u>minutes?|mins?|m|hours?|hrs?|h)\s+)?before\s+(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Phrase();

    // "my next meeting", "the meeting", "next call", "my next event" (+ optional "to …" for what it's about).
    [GeneratedRegex(@"^(?:my\s+|the\s+)?(?:next\s+)?(?:meeting|call|event)(?:\s+to\s+(?<text>.+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NextMeeting();

    static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["five"] = 5, ["ten"] = 10,
        ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30,
    };

    /// <summary>The request in the text, or null when it isn't a "remind me … before …" phrase.</summary>
    public static MeetingReminderRequest? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Phrase().Match(text.Trim().TrimEnd('.', '!', '?').Trim());
        if (!m.Success) return null;

        var before = DefaultBefore;
        if (m.Groups["n"].Success)
        {
            var raw = m.Groups["n"].Value;
            int n = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : Words.GetValueOrDefault(raw);
            bool hours = m.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase);
            before = hours ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
            if (before <= TimeSpan.Zero || before > TimeSpan.FromHours(12)) return null;
        }

        var target = m.Groups["target"].Value.Trim();
        if (NextMeeting().Match(target) is { Success: true } next)
            return new MeetingReminderRequest(before, null, next.Groups["text"].Success ? next.Groups["text"].Value.Trim() : null);
        target = Regex.Replace(target, @"^(?:my|the)\s+", "", RegexOptions.IgnoreCase).Trim();
        return target.Length == 0 ? null : new MeetingReminderRequest(before, target, null);
    }

    /// <summary>
    /// The meeting the request means: the next timed event that hasn't started, or the soonest upcoming one
    /// (within a week) whose title contains the name. Null when there's none.
    /// </summary>
    public static CalendarOccurrence? Find(MeetingReminderRequest request, IEnumerable<CalendarOccurrence> items, DateTime now)
    {
        var upcoming = items.Where(o => !o.AllDay && o.Start > now && o.Start <= now + Horizon).OrderBy(o => o.Start).ToList();
        if (request.Target is not { } name) return upcoming.FirstOrDefault();
        var key = Normalize(name);
        if (key.Length == 0) return null;
        return upcoming.FirstOrDefault(o => Normalize(o.Title).Contains(key, StringComparison.Ordinal))
            ?? upcoming.FirstOrDefault(o => Normalize(o.Title) is { Length: >= 3 } t && key.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>
    /// When and what: <see cref="MeetingReminderRequest.Before"/> ahead of the start, but never in the past
    /// (a meeting 2 minutes away gets its reminder now). The text defaults to "Standup at 10:30 AM".
    /// </summary>
    public static MeetingReminderPlan? Plan(MeetingReminderRequest request, IEnumerable<CalendarOccurrence> items, DateTime now)
    {
        if (Find(request, items, now) is not { } meeting) return null;
        var due = meeting.Start - request.Before;
        if (due < now) due = now;
        var at = CalendarText.ClockAmPm(meeting.Start);
        var text = request.Text is { Length: > 0 } t ? $"{t} · {meeting.Title} at {at}" : $"{meeting.Title} at {at}";
        return new MeetingReminderPlan(due, meeting, text);
    }

    static string Normalize(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
