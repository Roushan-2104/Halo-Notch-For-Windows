using System.Globalization;
using System.Text.RegularExpressions;

namespace Halo.Core.Conversions;

public sealed record TranslateRequest(string Text, string LanguageCode, string Language);

/// <summary>"translate good morning to hindi" and the Google Translate links for it and for the results card's chip.</summary>
public static class Translation
{
    static readonly Dictionary<string, string> Codes = new(StringComparer.Ordinal)
    {
        ["english"] = "en", ["hindi"] = "hi", ["spanish"] = "es", ["french"] = "fr", ["german"] = "de", ["italian"] = "it",
        ["portuguese"] = "pt", ["russian"] = "ru", ["japanese"] = "ja", ["korean"] = "ko", ["chinese"] = "zh-CN",
        ["mandarin"] = "zh-CN", ["simplified chinese"] = "zh-CN", ["traditional chinese"] = "zh-TW", ["arabic"] = "ar",
        ["bengali"] = "bn", ["bangla"] = "bn", ["urdu"] = "ur", ["tamil"] = "ta", ["telugu"] = "te", ["marathi"] = "mr",
        ["gujarati"] = "gu", ["kannada"] = "kn", ["malayalam"] = "ml", ["punjabi"] = "pa", ["odia"] = "or", ["oriya"] = "or",
        ["assamese"] = "as", ["nepali"] = "ne", ["sanskrit"] = "sa", ["turkish"] = "tr", ["dutch"] = "nl", ["polish"] = "pl",
        ["swedish"] = "sv", ["norwegian"] = "no", ["danish"] = "da", ["finnish"] = "fi", ["greek"] = "el", ["hebrew"] = "iw",
        ["thai"] = "th", ["vietnamese"] = "vi", ["indonesian"] = "id", ["malay"] = "ms", ["persian"] = "fa", ["farsi"] = "fa",
        ["ukrainian"] = "uk", ["czech"] = "cs", ["romanian"] = "ro", ["hungarian"] = "hu", ["swahili"] = "sw",
        ["filipino"] = "tl", ["tagalog"] = "tl", ["latin"] = "la", ["irish"] = "ga", ["welsh"] = "cy",
    };

    // Common English words; a phrase made mostly of other words is probably another language.
    static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "am", "to", "of", "and", "or", "in", "on", "at", "for",
        "with", "by", "from", "as", "it", "its", "this", "that", "these", "those", "i", "you", "he", "she", "we", "they",
        "me", "him", "her", "us", "them", "my", "your", "his", "our", "their", "what", "which", "who", "how", "when",
        "where", "why", "not", "no", "yes", "do", "does", "did", "have", "has", "had", "will", "would", "can", "could",
        "should", "may", "might", "must", "shall", "if", "but", "so", "than", "then", "there", "here", "all", "any",
        "some", "more", "most", "very", "just", "about", "into", "out", "up", "down", "over", "after", "before", "good",
        "morning", "night", "evening", "hello", "hi", "thanks", "thank", "please", "sorry", "love", "day", "time", "new",
        "now", "one", "two", "get", "go", "make", "know", "see", "like", "want", "need", "free", "today", "only", "also",
        "people", "world", "home", "work", "read", "more", "click", "sign", "buy", "price", "help", "way", "year",
    };

    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex LeadTranslate = new(@"^translate\s+(?:(?:to|into)\s+(?<lang>[a-z]+(?:\s+[a-z]+)??)\s*:?\s+(?<text>.+)|(?<rest>.+))$",
        RegexOptions.IgnoreCase);
    static readonly Regex HowToSay = new(@"^how\s+(?:do\s+(?:you|i|we)|to|would\s+you)\s+say\s+(?<rest>.+)$", RegexOptions.IgnoreCase);

    public static bool TryFindLanguage(string name, out string code) =>
        Codes.TryGetValue(Phrase.Normalize(name), out code!);

    public static string LanguageName(string code)
    {
        var name = Codes.FirstOrDefault(p => p.Value == code).Key ?? code;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    /// <summary>
    /// "translate good morning to hindi", "translate where is the station into spanish",
    /// "translate to french: thank you", "how do you say thank you in japanese". The text keeps its case.
    /// </summary>
    public static bool TryParse(string input, out TranslateRequest request)
    {
        request = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = Spaces.Replace(input.Trim(), " ").TrimEnd('?', '!', '.').Trim();

        var m = LeadTranslate.Match(s);
        if (m.Success)
        {
            if (m.Groups["lang"].Success)
                return TryFindLanguage(m.Groups["lang"].Value, out var code) && Make(m.Groups["text"].Value, code, out request);
            return TrySplit(m.Groups["rest"].Value, new[] { "to", "into", "in" }, out request);
        }
        m = HowToSay.Match(s);
        return m.Success && TrySplit(m.Groups["rest"].Value, new[] { "in" }, out request);
    }

    // "<text> to <language>" where the text may itself contain "to": try the last connector first.
    static bool TrySplit(string rest, string[] connectors, out TranslateRequest request)
    {
        request = null!;
        var words = rest.Split(' ');
        for (int i = words.Length - 2; i >= 1; i--)
        {
            if (Array.IndexOf(connectors, words[i].ToLowerInvariant()) < 0) continue;
            var lang = string.Join(' ', words, i + 1, words.Length - i - 1);
            if (TryFindLanguage(lang, out var code)) return Make(string.Join(' ', words, 0, i), code, out request);
        }
        return false;
    }

    static bool Make(string text, string code, out TranslateRequest request)
    {
        text = text.Trim().Trim('"', '\'', '\u201C', '\u201D', '\u2018', '\u2019').Trim();
        request = new TranslateRequest(text, code, LanguageName(code));
        return text.Length > 0;
    }

    /// <summary>Rough guess whether text is English: mostly ASCII letters and a fair share of common English words.</summary>
    public static bool LooksEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int letters = 0, ascii = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c < 128) ascii++;
        }
        if (letters == 0 || ascii < letters * 0.9) return false;
        var words = Regex.Matches(text.ToLowerInvariant(), "[a-z']+").Select(w => w.Value).ToList();
        if (words.Count == 0) return false;
        int common = words.Count(EnglishWords.Contains);
        return common >= Math.Max(1, words.Count * 0.25);
    }

    /// <summary>Text that already reads as English goes to Hindi; anything else to English.</summary>
    public static string DefaultTarget(string text) => LooksEnglish(text) ? "hi" : "en";

    public static Uri BuildUrl(string text, string languageCode)
    {
        if (text.Length > 4000) text = text[..4000]; // keeps the URL under browser limits
        return new Uri("https://translate.google.com/?sl=auto&tl=" + Uri.EscapeDataString(languageCode) +
                       "&text=" + Uri.EscapeDataString(text) + "&op=translate");
    }
}
