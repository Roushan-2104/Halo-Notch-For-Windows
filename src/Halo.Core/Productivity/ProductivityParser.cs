using System.Text.RegularExpressions;

namespace Halo.Core.Productivity;

public enum ProductivityAction
{
    StartTimer, StopTimer, PauseTimer, ResumeTimer,
    StartStopwatch, StopStopwatch, PauseStopwatch, ResumeStopwatch,
    StartPomodoro, StopPomodoro, SkipPomodoro,
    StartFocus, StopFocus,
    Remind,

    /// <summary>"remind me to call mom": clearly a reminder, but there's no time to set it for.</summary>
    ReminderNeedsTime,
    Note,
    ShowNotes,
}

/// <summary>A recognised command. <see cref="Text"/> is the timer label, reminder or note, as typed.</summary>
public sealed record ProductivityCommand(ProductivityAction Action)
{
    public TimeSpan Duration { get; init; }
    public string Text { get; init; } = "";
    public DateTime Due { get; init; }

    /// <summary>"cancel all timers" / "cancel timers".</summary>
    public bool All { get; init; }
}

/// <summary>
/// Turns typed or spoken text into a timer, stopwatch, pomodoro, focus, reminder or note command.
/// Deliberately strict: anything that could be a normal web search ("what is a pomodoro",
/// "timer for eggs how long", "note 10 pro price") returns null so the search still happens.
/// </summary>
public static class ProductivityParser
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    const string Verb = @"(?:(?:start|set|create|make|begin|add)\s+(?:me\s+)?)?";

    static readonly Regex Spaces = new(@"\s+", Opts);
    static readonly Regex Polite = new(@"^(?:(?:hey|ok|okay)\s+halo,?\s+|halo,?\s+)?(?:please\s+|can\s+you\s+|could\s+you\s+|would\s+you\s+)?(?<body>.+?)(?:,?\s+please)?$", Opts);

    static readonly Regex TimerFirst = new("^" + Verb + @"(?:(?:a|an|the|another)\s+)?timer\s+(?:for\s+|of\s+)?(?<rest>.+)$", Opts);
    static readonly Regex TimerLast = new("^" + Verb + @"(?:(?<art>a|an|another)\s+)?(?<dur>.+?)\s+timer(?:\s+(?:for|called|named|labelled|labeled)\s+(?<label>.+))?$", Opts);
    static readonly Regex TimerControl = new(@"^(?<verb>stop|cancel|end|clear|dismiss|delete|remove|kill|pause|hold|resume|continue|unpause)\s+" +
        @"(?:(?<all>all)\s+(?:(?:the|my)\s+)?|(?:the|my)\s+)?(?:(?<label>[\p{L}\p{N}' ]{1,30}?)\s+)?(?<noun>timers?)" +
        @"(?:\s+(?:for\s+|called\s+|named\s+)?(?<label2>[\p{L}\p{N}' ]{1,30}))?$", Opts);
    static readonly Regex Stopwatch = new(@"^(?:(?<verb>start|begin|stop|end|reset|clear|cancel|pause|resume|continue|unpause)\s+)?(?:(?:a|the|my)\s+)?stop\s?watch" +
        @"(?:\s+(?<verb2>start|stop|end|reset|pause|resume))?$", Opts);
    static readonly Regex PomodoroCommand = new(@"^(?:(?<verb>start|begin|stop|end|cancel|quit|exit|skip)\s+)?(?:(?:a|the|my)\s+)?pomodoro(?:\s+(?:timer|session|mode))?" +
        @"(?:\s+(?<verb2>start|stop|end|cancel|skip))?$", Opts);
    static readonly Regex FocusStart = new(@"^(?:(?<verb>start|begin|enter)\s+)?(?:(?:a|an)\s+)?focus(?:\s+(?<kind>session|mode|time|timer))?(?:\s+(?:for\s+)?(?<dur>.+))?$", Opts);
    static readonly Regex FocusStop = new(@"^(?:stop|end|cancel|exit|quit|finish)\s+(?:(?:the|my)\s+)?focus(?:\s+(?:session|mode|time|timer))?$", Opts);
    static readonly Regex Remind = new(@"^(?<lead>remind\s+me|(?:set|add|create)\s+(?:a\s+|an\s+)?reminder|new\s+reminder|reminder)\s*:?\s+(?<rest>.+)$", Opts);
    static readonly Regex ShowNotes = new(@"^(?:(?:show|open|view|see|list)\s+(?:me\s+)?(?:my\s+|the\s+|all\s+)?)?(?:my\s+)?(?:notes|reminders|notes\s+and\s+reminders)$", Opts);

    // Notes need an unmistakable prefix: "note:", "take a note", "note to self", "remember that ...".
    static readonly Regex NoteColon = new(@"^(?:note|notes|memo)\s*:\s*(?<t>.+)$", Opts);
    static readonly Regex NoteVerb = new(@"^(?:(?:take|make|save|add|create|write)\s+(?:a\s+)?note|note\s+to\s+self|jot\s+down)(?:\s+that)?\s*:?\s+(?<t>.+)$", Opts);
    static readonly Regex NoteThat = new(@"^note\s+that\s+(?<t>.+)$", Opts);
    static readonly Regex RememberThat = new(@"^remember\s+that\s+(?<t>.+)$", Opts);
    static readonly Regex NotePlain = new(@"^note\s+(?<t>.+)$", Opts);

    // "note 10 pro", "note taking apps", "note meaning": searches that merely start with the word.
    static readonly HashSet<string> NotPlainNote = new(StringComparer.OrdinalIgnoreCase)
    {
        "taking", "takers", "taker", "app", "apps", "pad", "book", "books", "card", "cards", "vs", "versus", "pro", "plus",
        "ultra", "max", "lite", "edge", "meaning", "meanings", "definition", "define", "synonym", "synonyms", "antonym",
        "in", "of", "on", "for", "from", "to", "name", "names", "value", "values", "frequency", "frequencies", "chart",
        "symbol", "symbols", "sheet", "sheets", "length", "lengths", "and", "or", "is", "series", "price", "review",
    };

    // "remember that movie where...", "remember that song": a half-remembered thing to search for.
    static readonly HashSet<string> NotRememberThat = new(StringComparer.OrdinalIgnoreCase)
    {
        "movie", "film", "song", "show", "series", "book", "game", "video", "episode", "actor", "actress", "guy", "girl",
        "man", "woman", "band", "meme", "anime", "cartoon", "commercial", "ad", "advert", "one", "scene", "line", "quote",
        "time", "day", "place", "restaurant", "website", "site", "app", "thing", "kid", "character", "tv", "youtube",
    };

    static readonly HashSet<string> LabelLead = new(StringComparer.OrdinalIgnoreCase)
    {
        "for", "called", "named", "labelled", "labeled", "label", "titled", "to", "the", "my",
    };

    static readonly HashSet<string> ReminderLead = new(StringComparer.OrdinalIgnoreCase) { "to", "that", "about", "of" };

    public static ProductivityCommand? Parse(string text, DateTime now)
    {
        var s = Spaces.Replace(text, " ").Trim().TrimEnd('.', '!', '?').Trim();
        if (s.Length == 0 || s.Length > 300) return null;
        var polite = Polite.Match(s);
        if (polite.Success) s = polite.Groups["body"].Value;

        return ParseNote(s)
            ?? ParseShowNotes(s)
            ?? ParseReminder(s, now)
            ?? ParseTimerControl(s)
            ?? ParseStopwatch(s)
            ?? ParsePomodoro(s)
            ?? ParseFocus(s)
            ?? ParseTimer(s);
    }

    // ---- Notes -------------------------------------------------------------------------------

    static ProductivityCommand? ParseNote(string s)
    {
        string? body = null;
        if (NoteColon.Match(s) is { Success: true } colon) body = colon.Groups["t"].Value;
        else if (NoteVerb.Match(s) is { Success: true } verb) body = verb.Groups["t"].Value;
        else if (NoteThat.Match(s) is { Success: true } that) body = that.Groups["t"].Value;
        else if (RememberThat.Match(s) is { Success: true } remember)
        {
            var t = remember.Groups["t"].Value;
            if (!NotRememberThat.Contains(FirstWord(t)) && WordCount(t) >= 3) body = t;
        }
        else if (NotePlain.Match(s) is { Success: true } plain)
        {
            var t = plain.Groups["t"].Value;
            var first = FirstWord(t);
            if (WordCount(t) >= 2 && first.Length > 1 && !first.Any(char.IsDigit) && !NotPlainNote.Contains(first)) body = t;
        }
        body = body?.Trim().Trim('"', '\'', '\u201C', '\u201D').Trim();
        return string.IsNullOrEmpty(body) ? null : new ProductivityCommand(ProductivityAction.Note) { Text = Capitalize(body) };
    }

    static ProductivityCommand? ParseShowNotes(string s) =>
        ShowNotes.IsMatch(s) ? new ProductivityCommand(ProductivityAction.ShowNotes) : null;

    // ---- Reminders ---------------------------------------------------------------------------

    static ProductivityCommand? ParseReminder(string s, DateTime now)
    {
        var m = Remind.Match(s);
        if (!m.Success) return null;
        // Parse lower-cased, punctuation-free words but hand back the reminder text as typed.
        var pairs = WhenParser.Normalize(m.Groups["rest"].Value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => (Word: WhenParser.Clean(w), Original: w))
            .Where(p => p.Word.Length > 0)
            .ToList();
        var words = pairs.Select(p => p.Word).ToList();
        var original = pairs.Select(p => p.Original).ToList();
        if (words.Count == 0) return null;

        // When first: "in 20 minutes to call mom", "tomorrow at 9 submit form". Longest time phrase wins.
        for (int k = words.Count; k >= 1; k--)
        {
            if (WhenParser.ParseWords(words.Take(k).ToList(), now) is not { } due) continue;
            return Reminder(due, original.Skip(k));
        }
        // Text first: "to call mom in 20 minutes", "to take medicine at 6pm tomorrow".
        for (int k = 1; k < words.Count; k++)
        {
            if (WhenParser.ParseWords(words.Skip(k).ToList(), now) is not { } due) continue;
            return Reminder(due, original.Take(k));
        }

        bool remindMe = m.Groups["lead"].Value.StartsWith("remind", StringComparison.OrdinalIgnoreCase);
        return remindMe && ReminderLead.Contains(words[0]) && words.Count > 1
            ? new ProductivityCommand(ProductivityAction.ReminderNeedsTime) { Text = ReminderText(original) }
            : null;
    }

    static ProductivityCommand Reminder(DateTime due, IEnumerable<string> words) =>
        new(ProductivityAction.Remind) { Due = due, Text = ReminderText(words) };

    static string ReminderText(IEnumerable<string> words)
    {
        var list = words.ToList();
        while (list.Count > 0 && (ReminderLead.Contains(list[0].Trim(',', ':', '-')) || list[0] is "-" or ":" or ",")) list.RemoveAt(0);
        var text = string.Join(' ', list).Trim().Trim(',', ':', '-', ' ');
        return Capitalize(text);
    }

    // ---- Timers ------------------------------------------------------------------------------

    static ProductivityCommand? ParseTimer(string s)
    {
        if (TimerFirst.Match(s) is { Success: true } first)
        {
            var rest = first.Groups["rest"].Value;
            if (DurationParser.ParsePrefix(rest, out var label) is { } d)
                return new ProductivityCommand(ProductivityAction.StartTimer) { Duration = d, Text = Label(label) };
            // "timer 10" means minutes.
            if (int.TryParse(rest, out var minutes) && minutes is > 0 and < 1000)
                return new ProductivityCommand(ProductivityAction.StartTimer) { Duration = TimeSpan.FromMinutes(minutes) };
        }
        if (TimerLast.Match(s) is not { Success: true } last) return null;
        // The article belongs to the duration in "an hour timer" but not in "a 10 minute timer".
        var dur = last.Groups["dur"].Value;
        var duration = DurationParser.Parse(dur)
            ?? (last.Groups["art"].Success ? DurationParser.Parse(last.Groups["art"].Value + " " + dur) : null);
        return duration is { } d2
            ? new ProductivityCommand(ProductivityAction.StartTimer) { Duration = d2, Text = Label(last.Groups["label"].Value) }
            : null;
    }

    static ProductivityCommand? ParseTimerControl(string s)
    {
        var m = TimerControl.Match(s);
        if (!m.Success) return null;
        var action = m.Groups["verb"].Value.ToLowerInvariant() switch
        {
            "pause" or "hold" => ProductivityAction.PauseTimer,
            "resume" or "continue" or "unpause" => ProductivityAction.ResumeTimer,
            _ => ProductivityAction.StopTimer,
        };
        var label = Label(m.Groups["label"].Success ? m.Groups["label"].Value : m.Groups["label2"].Value);
        bool all = m.Groups["all"].Success || (m.Groups["noun"].Value.EndsWith('s') && label.Length == 0);
        return new ProductivityCommand(action) { All = all, Text = label };
    }

    static string Label(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0 && (LabelLead.Contains(words[0]) || words[0] is "-" or ":")) words.RemoveAt(0);
        var label = string.Join(' ', words).Trim(' ', ':', '-', ',', '"', '\u201C', '\u201D');
        return label.Length > 40 ? label[..40].TrimEnd() : label;
    }

    // ---- Stopwatch, pomodoro, focus ----------------------------------------------------------

    static ProductivityCommand? ParseStopwatch(string s)
    {
        var m = Stopwatch.Match(s);
        if (!m.Success) return null;
        var verb = (m.Groups["verb"].Success ? m.Groups["verb"].Value : m.Groups["verb2"].Value).ToLowerInvariant();
        return new ProductivityCommand(verb switch
        {
            "stop" or "end" or "reset" or "clear" or "cancel" => ProductivityAction.StopStopwatch,
            "pause" => ProductivityAction.PauseStopwatch,
            "resume" or "continue" or "unpause" => ProductivityAction.ResumeStopwatch,
            _ => ProductivityAction.StartStopwatch,
        });
    }

    static ProductivityCommand? ParsePomodoro(string s)
    {
        var m = PomodoroCommand.Match(s);
        if (!m.Success) return null;
        var verb = (m.Groups["verb"].Success ? m.Groups["verb"].Value : m.Groups["verb2"].Value).ToLowerInvariant();
        return new ProductivityCommand(verb switch
        {
            "stop" or "end" or "cancel" or "quit" or "exit" => ProductivityAction.StopPomodoro,
            "skip" => ProductivityAction.SkipPomodoro,
            _ => ProductivityAction.StartPomodoro,
        });
    }

    static ProductivityCommand? ParseFocus(string s)
    {
        if (FocusStop.IsMatch(s)) return new ProductivityCommand(ProductivityAction.StopFocus);
        var m = FocusStart.Match(s);
        if (!m.Success) return null;
        if (m.Groups["dur"].Success)
        {
            return DurationParser.Parse(m.Groups["dur"].Value) is { } d
                ? new ProductivityCommand(ProductivityAction.StartFocus) { Duration = d }
                : null;
        }
        // Plain "focus" or "focus mode" could be a search; "start focus" / "focus session" can't.
        bool explicitStart = m.Groups["verb"].Success || m.Groups["kind"].Value.Equals("session", StringComparison.OrdinalIgnoreCase);
        return explicitStart ? new ProductivityCommand(ProductivityAction.StartFocus) { Duration = TimeSpan.FromMinutes(30) } : null;
    }

    // ---- Helpers -----------------------------------------------------------------------------

    static string FirstWord(string s)
    {
        var w = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length > 0 ? w[0].Trim(',', ':', '.') : "";
    }

    static int WordCount(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
