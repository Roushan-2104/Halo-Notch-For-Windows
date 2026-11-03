using System.Text.RegularExpressions;

namespace Shammy.Core.Search;

/// <summary>Words speech recognition reliably confuses in commands, fixed only where the meaning is clear.</summary>
public static class SpeechFixes
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // "the whether" is never grammatical, and a sentence that opens with "whether in/for/today…" is a forecast ask.
    static readonly Regex TheWhether = new(@"\b(the|today'?s|tomorrow'?s)\s+whether\b", Opts);
    static readonly Regex LeadingWhether = new(
        @"^(\s*)whether(?=\s+(?:in|at|for|of|near|today|tomorrow|tonight|this|next|now|forecast|report|update|like)\b|\s*[.?!]?\s*$)", Opts);

    // "timer 10 minutes" heard as "Time or 10 minutes" / "Timeout 5 minutes": a duration right after means a timer.
    static readonly Regex TimeOr = new(
        @"^(\s*)(?:time\s+or|time\s+of|timeout|time\s+out|timer's)(?=\s+(?:for\s+)?\d+\s*(?:sec|second|min|minute|hour|hr)s?\b)", Opts);

    public static string Apply(string spoken)
    {
        if (string.IsNullOrEmpty(spoken)) return spoken ?? "";
        spoken = TimeOr.Replace(spoken, m => m.Groups[1].Value + Cased("timer", m.Value.TrimStart()));
        var text = TheWhether.Replace(spoken, m => m.Groups[1].Value + " " + Cased("weather", m.Value[m.Groups[1].Length..].TrimStart()));
        return LeadingWhether.Replace(text, m => m.Groups[1].Value + Cased("weather", m.Value.TrimStart()));
    }

    static string Cased(string word, string like) =>
        like.Length > 0 && char.IsUpper(like[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
}
