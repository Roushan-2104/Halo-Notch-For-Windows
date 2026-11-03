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

    public static string Apply(string spoken)
    {
        if (string.IsNullOrEmpty(spoken)) return spoken ?? "";
        var text = TheWhether.Replace(spoken, m => m.Groups[1].Value + " " + Cased("weather", m.Value[m.Groups[1].Length..].TrimStart()));
        return LeadingWhether.Replace(text, m => m.Groups[1].Value + Cased("weather", m.Value.TrimStart()));
    }

    static string Cased(string word, string like) =>
        like.Length > 0 && char.IsUpper(like[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
}
