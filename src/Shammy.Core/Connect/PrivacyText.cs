using Shammy.Core.Calendar;

namespace Shammy.Core.Connect;

/// <summary>
/// What personal text shows while the screen is shared or recorded (others may be watching): counts and
/// generic words instead of clipboard text, notes, reminder and meeting titles, and lyrics.
/// </summary>
public static class PrivacyText
{
    /// <summary>Shown in place of hidden content in cards and details.</summary>
    public const string Hidden = "Hidden while sharing your screen";

    /// <summary>The "just copied" pill: the text itself, or just "Copied".</summary>
    public static string CopyPreview(string text, bool presenting) => presenting ? "Copied" : text;

    /// <summary>A due reminder's card title: its text, or just "Reminder".</summary>
    public static string Reminder(string title, bool presenting) => presenting ? "Reminder" : title;

    /// <summary>The calendar glance: "Standup 10:30", or "Meeting 10:30".</summary>
    public static string CalendarGlance(string title, DateTime start, bool presenting) =>
        CalendarText.Glance(presenting ? "Meeting" : title, start);

    /// <summary>The meeting pill: "Standup in 2 min", or "Meeting in 2 min".</summary>
    public static string MeetingPill(CalendarOccurrence o, DateTime now, bool presenting) =>
        CalendarText.Pill(presenting ? o with { Title = "Meeting" } : o, now);

    /// <summary>"3 notes · 2 reminders", "1 note", "Nothing saved" for the Notes card while hidden.</summary>
    public static string NotesSummary(int notes, int reminders)
    {
        var parts = new List<string>();
        if (notes > 0) parts.Add(notes == 1 ? "1 note" : $"{notes} notes");
        if (reminders > 0) parts.Add(reminders == 1 ? "1 reminder" : $"{reminders} reminders");
        return parts.Count == 0 ? "Nothing saved" : string.Join(" · ", parts);
    }

    /// <summary>"5 items" / "1 item" / "Empty" for the clipboard card while hidden.</summary>
    public static string ClipboardSummary(int items) => items switch
    {
        0 => "Empty",
        1 => "1 item",
        _ => $"{items} items",
    };
}
