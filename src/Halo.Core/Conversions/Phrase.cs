using System.Text.RegularExpressions;

namespace Halo.Core.Conversions;

/// <summary>Text clean-up shared by the converters, so typed, spoken and OCR'd input look alike.</summary>
internal static class Phrase
{
    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex DegreeSpace = new(@"° +");
    static readonly Regex Lead = new(@"^(?:convert|what is|what's|whats|how much is|how much are) ");

    // Labels that often sit next to a circled price or size ("Price: $49", "₹1,299/-", "MRP ₹999 only").
    static readonly Regex SelectionLabel = new(@"^(?:price|mrp|total|now|only|sale|from|just|cost|starting at|starts at|approx\.?|about|~)\s*:?\s*|^[a-z][a-z ]{0,14}:\s*");
    static readonly Regex SelectionTail = new(@"\s*(?:/-|only|each|approx\.?|incl\.? (?:of )?(?:all )?taxes?|\*)$");

    /// <summary>Lower case, single spaces, typographic quotes and degree signs folded, end punctuation dropped.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Trim().ToLowerInvariant()
            .Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u2032', '\'')
            .Replace('\u2033', '"').Replace('\u201C', '"').Replace('\u201D', '"')
            .Replace('\u00BA', '°').Replace('\u02DA', '°').Replace('\u2212', '-');
        s = Spaces.Replace(s, " ");
        s = DegreeSpace.Replace(s, "°");
        return s.TrimEnd('?', '!', '.', ',', ' ').Trim();
    }

    /// <summary>Drops a leading "convert" / "what is" / "how much is".</summary>
    public static string StripLead(string normalized) => Lead.Replace(normalized, "");

    /// <summary>A circled selection reduced to its core: "Price: ₹1,299/-" → "₹1,299". Empty if it's too long to be just an amount.</summary>
    public static string CleanSelection(string? text)
    {
        var s = Normalize(text);
        if (s.Length == 0 || s.Length > 40) return "";
        s = s.Trim('(', ')', '[', ']', ':', ';', '*', ' ');
        s = SelectionLabel.Replace(s, "");
        s = SelectionTail.Replace(s, "");
        return s.Trim();
    }

    /// <summary>Every way to cut the phrase at one connector word: "5 in in cm" → ("5 in", "cm") and ("5", "in cm").</summary>
    public static IEnumerable<(string Left, string Right)> Splits(string normalized, params string[] connectors)
    {
        var words = normalized.Split(' ');
        for (int i = 1; i < words.Length - 1; i++)
        {
            if (Array.IndexOf(connectors, words[i]) < 0) continue;
            yield return (string.Join(' ', words, 0, i), string.Join(' ', words, i + 1, words.Length - i - 1));
        }
    }

    public static readonly string[] ToWords = { "to", "in", "into", "as", "->", "=", "→" };
}
