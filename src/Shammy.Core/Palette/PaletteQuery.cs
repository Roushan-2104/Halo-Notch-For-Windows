using System.Text.RegularExpressions;
using Shammy.Core.Launcher;
using Shammy.Core.Music;
using Shammy.Core.Productivity;

namespace Shammy.Core.Palette;

/// <summary>Media keys the palette offers as rows.</summary>
public enum MediaControl { PlayPause, Next, Previous }

/// <summary>Cheap reads of what the typed text is likely to be, used to decide which rows to offer.</summary>
public static class PaletteQuery
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Question = new(
        @"^(?:how|what|whats|what's|why|when|where|who|whom|whose|which|is|are|was|were|can|could|does|do|did|should|will|would|define|meaning|weather|news|price|best|near)\b", Opts);

    static readonly Regex Mathy = new(@"^[\d\s.,+\-*/^%()=x×÷]+$", Opts);
    static readonly Regex SongChars = new(@"^[\p{L}\p{N}\s'’&.,!\-]+$", Opts);

    static readonly (string Word, MediaControl Control)[] MediaWords =
    {
        ("pause", MediaControl.PlayPause), ("play", MediaControl.PlayPause), ("resume", MediaControl.PlayPause),
        ("next", MediaControl.Next), ("skip", MediaControl.Next),
        ("previous", MediaControl.Previous), ("prev", MediaControl.Previous),
    };

    /// <summary>Reads like a question or a search ("how to…", "weather today", "is it…?").</summary>
    public static bool IsQuestion(string text)
    {
        var t = (text ?? "").Trim();
        return t.Contains('?') || Question.IsMatch(t);
    }

    /// <summary>
    /// Media rows for a typed prefix of a transport word ("pa" → play/pause, "nex" → next). Two letters at
    /// least, and only for a single word, so "next week" stays a search. 100 for the whole word.
    /// </summary>
    public static IReadOnlyList<(MediaControl Control, int Match)> MediaControls(string text)
    {
        var q = AppMatcher.Normalize(text ?? "");
        if (q.Length < 2 || q.Contains(' ')) return Array.Empty<(MediaControl, int)>();
        return MediaWords
            .Where(w => w.Word.StartsWith(q, StringComparison.Ordinal))
            .Select(w => (w.Control, Match: w.Word == q ? 100 : 80))
            .GroupBy(w => w.Control)
            .Select(g => (g.Key, g.Max(x => x.Match)))
            .ToList();
    }

    /// <summary>
    /// What to offer as "Play … on YouTube Music": the song from "play X", or the text itself when it reads
    /// like a song or artist (a few plain words, not a question, not a sum, nothing local matched it
    /// strongly). Null when it shouldn't be offered.
    /// </summary>
    public static string? MusicQuery(string text, bool strongLocalMatch)
    {
        if (MusicCommand.Parse(text) is { Action: MusicAction.PlayYouTubeMusic } intent) return intent.Query;
        if (strongLocalMatch) return null;
        var t = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
        if (t.Length < 3 || t.Length > 60) return null;
        if (IsQuestion(t) || Mathy.IsMatch(t) || !SongChars.IsMatch(t) || !t.Any(char.IsLetter)) return null;
        if (t.Split(' ').Length > 6) return null;
        // What "play X" would actually do; "play store" and friends are searches, not songs.
        return MusicCommand.Parse("play " + t) is { Action: MusicAction.PlayYouTubeMusic } guess ? guess.Query : null;
    }

    /// <summary>What a timer/reminder/note command will do, in a few words; null when there's nothing useful to say.</summary>
    public static string? DescribeProductivity(ProductivityCommand c, DateTime now) => c.Action switch
    {
        ProductivityAction.StartTimer => c.Text.Length > 0
            ? $"Start a {TimeFormat.Describe(c.Duration)} timer · {c.Text}"
            : $"Start a {TimeFormat.Describe(c.Duration)} timer",
        ProductivityAction.StopTimer => "Cancel timer",
        ProductivityAction.PauseTimer => "Pause timer",
        ProductivityAction.ResumeTimer => "Resume timer",
        ProductivityAction.StartStopwatch => "Start a stopwatch",
        ProductivityAction.StopStopwatch => "Stop the stopwatch",
        ProductivityAction.StartPomodoro => "Start a pomodoro",
        ProductivityAction.StopPomodoro => "Stop the pomodoro",
        ProductivityAction.StartFocus => $"Focus for {TimeFormat.Describe(c.Duration)}",
        ProductivityAction.StopFocus => "End focus session",
        ProductivityAction.Remind => $"Reminder · {TimeFormat.When(c.Due, now)} · {c.Text}",
        ProductivityAction.ReminderNeedsTime => "Reminder · add a time",
        ProductivityAction.Note => $"Save note · {c.Text}",
        ProductivityAction.ShowNotes => "Show notes",
        _ => null,
    };
}
