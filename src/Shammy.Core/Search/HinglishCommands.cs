using System.Globalization;
using System.Text.RegularExpressions;

namespace Shammy.Core.Search;

/// <summary>
/// Timer, alarm and reminder commands said in Hinglish, rewritten as the English the command parsers know
/// ("kal subah 7 baje ka alarm laga do" becomes "alarm tomorrow morning at 7"):
/// "paanch minute ka timer laga do" becomes "timer 5 minutes", "kal subah 7 baje yaad dilana ki doctor ko
/// call karna" becomes "remind me tomorrow morning at 7 to doctor ko call karna". Devanagari is spelt out
/// first. Anything else returns null and is searched as said (Google understands Hindi).
/// </summary>
public static class HinglishCommands
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Dictionary<string, double> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ek"] = 1, ["do"] = 2, ["teen"] = 3, ["tin"] = 3, ["char"] = 4, ["chaar"] = 4, ["paanch"] = 5, ["panch"] = 5,
        ["paanchh"] = 5, ["chhe"] = 6, ["chhah"] = 6, ["che"] = 6, ["chah"] = 6, ["saat"] = 7, ["sat"] = 7, ["sath"] = 7, ["aath"] = 8,
        ["ath"] = 8, ["nau"] = 9, ["das"] = 10, ["dus"] = 10, ["gyarah"] = 11, ["gyara"] = 11, ["barah"] = 12, ["bara"] = 12,
        ["terah"] = 13, ["chaudah"] = 14, ["pandrah"] = 15, ["pandra"] = 15, ["solah"] = 16, ["satrah"] = 17,
        ["atharah"] = 18, ["unnis"] = 19, ["bees"] = 20, ["bis"] = 20, ["pachees"] = 25, ["pachis"] = 25, ["tees"] = 30,
        ["tis"] = 30, ["chalees"] = 40, ["chalis"] = 40, ["pachaas"] = 50, ["pachas"] = 50, ["saath"] = 60,
        ["dedh"] = 1.5, ["dhai"] = 2.5, ["dhaai"] = 2.5, ["aadha"] = 0.5, ["adha"] = 0.5, ["aadhe"] = 0.5,
    };

    static readonly Regex Minute = new(@"^(?:minutes?|minat|minit|mint|minute|minton|mins?)$", Opts);
    static readonly Regex Second = new(@"^(?:seconds?|sekand|second|secs?)$", Opts);
    // The English model hears "ghante" as "gante".
    static readonly Regex Hour = new(@"^(?:gh?a+nta|gh?a+nte|gh?a+nton|hours?|hrs?)$", Opts);
    // "alam" is how the English pass hears it ("Ka Alam Lagado").
    static readonly Regex AlarmWord = new(@"^(?:alarm|alaarm|alaram|alarum|alam)$", Opts);
    static readonly Regex TimerWord = new(@"^(?:timer|taimar|taimer|taaimar|timar|taymar)$", Opts);

    // "laga do", "lagado", "laga dijiye", "set karo", "start kar do", "chalu karo", and the Hinglish model's
    // long vowels ("lagaa doon", "lagaadoo", even "lagaajoo").
    static readonly Regex SetVerb = new(@"(?<=^|\s)(?:la+ga+\s*(?:do+n?|du+|jo+|dijiye|dena|de)|la+ga+o|la+ga+|" +
        @"(?:set|start|shuru|chalu|chaalu)\s*(?:karo|kar\s*do+n?|kardo|kar\s*dijiye|kijiye|kar\s*dena|kar))(?=\s|$)", Opts);
    static readonly Regex RemindVerb = new(@"\b(?:ya+d\s+(?:dil(?:a+)?(?:na|o|do|dena|\s+do|\s+dena|\s+dijiye)?|kara+(?:na|o|\s+do|\s+dena)?)|" +
        @"remind\s+(?:kar(?:na|o|do|\s+do|\s+dena|\s+dijiye)?|karana))\b", Opts);
    static readonly Regex Spaces = new(@"\s+", Opts);

    static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "mujhe", "please", "zara", "jara", "ki", "ke", "ka", "ko", "liye", "mera", "meri", "mere", "ek", "aap", "plz",
    };

    /// <summary>English for a Hinglish timer, alarm or reminder command, or null when it isn't one.</summary>
    public static string? ToEnglish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        if (Devanagari.Contains(t))
        {
            // Spelt-out Devanagari has long vowels ("taaimar lagaa do"); typed Hinglish mostly doesn't.
            t = Devanagari.ToLatin(t).Replace("aa", "a").Replace("ee", "i").Replace("oo", "u");
        }
        t = Regex.Replace(t, @"[^\p{L}\p{N}:.\s]", " ");
        t = Regex.Replace(t, @"(?<!\d)\.|\.(?!\d)", " ");
        t = Spaces.Replace(t, " ").Trim();
        if (t.Length == 0) return null;
        return Reminder(t) ?? Alarm(t) ?? Timer(t);
    }

    static string? Timer(string t)
    {
        var verb = SetVerb.Match(t);
        if (!verb.Success) return null;
        var words = (t[..verb.Index] + " " + t[(verb.Index + verb.Length)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int timer = words.FindIndex(w => TimerWord.IsMatch(w));
        if (timer < 0) return null;
        // "5 minute ka timer" or "timer 5 minute ka": everything but the word timer must be the duration.
        var rest = words.Where((w, i) => i != timer && !Filler.Contains(w) || (w == "ek" && i + 1 < words.Count && Unit(words[i + 1]) is not null)).ToList();
        var duration = Duration(rest, out int used);
        if (duration is null || used != rest.Count) return null;
        return "timer " + duration;
    }

    static string? Reminder(string t)
    {
        var verb = RemindVerb.Match(t);
        if (!verb.Success) return null;
        var words = (t[..verb.Index] + " " + t[(verb.Index + verb.Length)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (When(words, out var task) is not { } when) return null;
        return task.Count == 0 ? $"remind me {when}" : $"remind me {when} to {string.Join(' ', task)}";
    }

    /// <summary>"kal subah 7 baje ka alarm laga do" as "alarm tomorrow morning at 7" (an alarm is a reminder titled Alarm).</summary>
    static string? Alarm(string t)
    {
        var verb = SetVerb.Match(t);
        if (!verb.Success) return null;
        var words = (t[..verb.Index] + " " + t[(verb.Index + verb.Length)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int alarm = words.FindIndex(w => AlarmWord.IsMatch(w));
        if (alarm < 0) return null;
        words.RemoveAt(alarm);
        // Nothing but the time may be left ("ka", "mera" aside): "alarm laga do gaana" isn't an alarm.
        if (When(words, out var rest) is not { } when || rest.Count > 0) return null;
        return "alarm " + when;
    }

    /// <summary>The time phrase in Hinglish words, as English ("tomorrow morning at 7", "in 10 minutes"); the
    /// other words are left in <paramref name="task"/> with filler trimmed from both ends.</summary>
    static string? When(List<string> words, out List<string> task)
    {
        string? day = null, part = null, clock = null, inTime = null;
        task = new List<string>();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w == "aaj" || w == "aj") { day = "today"; continue; }
            if (w == "kal") { day = "tomorrow"; continue; }
            if (PartOfDay(w) is { } p) { part = p; continue; }
            // "7 baje", "saade 7 baje"
            // "06:30" / "7:30 baje": a clock reading as is.
            if (ClockReading.IsMatch(w))
            {
                clock = w;
                if (i + 1 < words.Count && IsBaje(words[i + 1])) i++;
                continue;
            }
            if (i + 1 < words.Count && IsBaje(words[i + 1]) && Number(w) is { } h && h == Math.Floor(h) && h is >= 1 and <= 12)
            {
                bool half = task.Count > 0 && task[^1] is "saade" or "sadhe" or "saadhe";
                if (half) task.RemoveAt(task.Count - 1);
                clock = half ? $"{h}:30" : h.ToString(CultureInfo.InvariantCulture);
                i++;
                continue;
            }
            // "10 minute baad", "do ghante mein"
            if (i + 2 < words.Count && Number(w) is not null && Unit(words[i + 1]) is not null && words[i + 2] is "baad" or "bad" or "mein" or "me" or "main")
            {
                inTime = Duration(new List<string> { w, words[i + 1] }, out _);
                i += 2;
                continue;
            }
            task.Add(w);
        }
        if (day is null && part is null && clock is null && inTime is null) return null;

        while (task.Count > 0 && Filler.Contains(task[0])) task.RemoveAt(0);
        while (task.Count > 0 && Filler.Contains(task[^1])) task.RemoveAt(task.Count - 1);

        string when = inTime is not null ? "in " + inTime
            : string.Join(' ', new[] { day, part, clock is null ? null : "at " + clock }.Where(s => s is not null));
        if (day == "today" && part == "night") when = when.Replace("today night", "tonight", StringComparison.Ordinal);
        return when;
    }

    static readonly Regex ClockReading = new(@"^(?:[01]?\d|2[0-3]):[0-5]\d$", Opts);

    // "baje", "baaje", "bajay", and with the next "ka" glued on ("sath bajeka alarm").
    static readonly Regex Baje = new(@"^b?ba+j(?:e|ay|ey|ei)(?:ka|ke)?$|^bje$", Opts);
    static bool IsBaje(string w) => Baje.IsMatch(w);

    static string? PartOfDay(string w) => w switch
    {
        "subah" or "subha" or "subeh" or "savere" or "sawere" => "morning",
        "dopahar" or "dopehar" or "dophar" => "afternoon",
        "shaam" or "sham" => "evening",
        "raat" or "rat" => "night",
        _ => null,
    };

    static double? Number(string w) =>
        Numbers.TryGetValue(w, out var n) ? n : double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;

    static string? Unit(string w) => Minute.IsMatch(w) ? "minutes" : Second.IsMatch(w) ? "seconds" : Hour.IsMatch(w) ? "hours" : null;

    /// <summary>"5 minute", "1 ghanta 30 minute", "dedh ghante" as English ("90 minutes"); counts the words read.</summary>
    static string? Duration(List<string> words, out int used)
    {
        used = 0;
        double seconds = 0;
        int i = 0;
        while (i + 1 < words.Count && Number(words[i]) is { } n && Unit(words[i + 1]) is { } unit)
        {
            seconds += n * (unit == "hours" ? 3600 : unit == "minutes" ? 60 : 1);
            i += 2;
        }
        used = i;
        if (seconds <= 0) return null;
        if (seconds % 3600 == 0) return Plural(seconds / 3600, "hour");
        if (seconds % 60 == 0) return Plural(seconds / 60, "minute");
        return Plural(seconds, "second");
    }

    static string Plural(double n, string unit) =>
        $"{n.ToString(CultureInfo.InvariantCulture)} {unit}{(n == 1 ? "" : "s")}";
}
