using System.Text.RegularExpressions;

namespace Halo.Core.Search;

[Flags]
public enum SearchFilter
{
    None = 0,
    Reddit = 1,
    Recent = 2,
    NoJunk = 4,
    Docs = 8,
}

public enum SearchMode { Ai, Web, Lens }

/// <summary>
/// Turns what the user said into a Google query. Filters are free: on the Web tab they become search
/// operators, and on AI Mode (which ignores most operators) they become a plain-language instruction.
/// </summary>
public static class QueryBuilder
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly (Regex Pattern, SearchFilter Filter)[] SpokenFilters =
    {
        (new Regex(@"\b(?:only\s+)?(?:from|on|in|using)\s+reddit(?:\s+only)?\b", Opts), SearchFilter.Reddit),
        (new Regex(@"\breddit\s+only\b", Opts), SearchFilter.Reddit),
        (new Regex(@"\b(?:from|in|over)\s+the\s+(?:past|last)\s+(?:week|7\s+days)\b", Opts), SearchFilter.Recent),
        (new Regex(@"\bthis\s+week\s+only\b|\brecent\s+(?:results\s+)?only\b", Opts), SearchFilter.Recent),
        (new Regex(@"\bno\s+(?:junk|spam|seo)\b", Opts), SearchFilter.NoJunk),
        (new Regex(@"\b(?:from|in|on)\s+(?:the\s+)?(?:docs|documentation|github|stack\s*overflow)\b", Opts), SearchFilter.Docs),
    };

    static readonly string[] JunkSites =
        { "pinterest.com", "quora.com", "facebook.com", "instagram.com", "tiktok.com" };

    static readonly string[] DocSites =
        { "github.com", "stackoverflow.com", "learn.microsoft.com", "developer.mozilla.org", "docs.python.org" };

    public static (string Query, SearchFilter Filters) ExtractSpokenFilters(string spoken)
    {
        var filters = SearchFilter.None;
        var text = spoken ?? "";
        foreach (var (pattern, filter) in SpokenFilters)
        {
            if (!pattern.IsMatch(text)) continue;
            filters |= filter;
            text = pattern.Replace(text, " ");
        }
        return (Tidy(text), filters);
    }

    public static string Apply(string query, SearchFilter filters, SearchMode mode, DateTime today)
    {
        query = Tidy(query);
        if (filters == SearchFilter.None) return query;

        if (mode == SearchMode.Ai)
        {
            var hints = new List<string>();
            if (filters.HasFlag(SearchFilter.Reddit)) hints.Add("Use Reddit discussions as sources");
            if (filters.HasFlag(SearchFilter.Recent)) hints.Add("only use sources from the past week");
            if (filters.HasFlag(SearchFilter.NoJunk)) hints.Add("avoid low-quality sites like Pinterest and Quora");
            if (filters.HasFlag(SearchFilter.Docs)) hints.Add("use official documentation, GitHub and Stack Overflow");
            var sentence = string.Join("; ", hints);
            return $"{query}. {char.ToUpperInvariant(sentence[0])}{sentence[1..]}.";
        }

        var parts = new List<string> { query };
        if (filters.HasFlag(SearchFilter.Reddit)) parts.Add("site:reddit.com");
        if (filters.HasFlag(SearchFilter.Recent)) parts.Add($"after:{today.AddDays(-7):yyyy-MM-dd}");
        if (filters.HasFlag(SearchFilter.NoJunk)) parts.AddRange(JunkSites.Select(s => $"-site:{s}"));
        if (filters.HasFlag(SearchFilter.Docs)) parts.Add("(" + string.Join(" OR ", DocSites.Select(s => $"site:{s}")) + ")");
        return string.Join(' ', parts);
    }

    /// <summary>udm=50 is Google's AI Mode, udm=14 is the plain "Web" results tab.</summary>
    public static Uri BuildUrl(string query, SearchMode mode) => mode switch
    {
        SearchMode.Web => new Uri("https://www.google.com/search?udm=14&q=" + Uri.EscapeDataString(query)),
        _ => new Uri("https://www.google.com/search?udm=50&q=" + Uri.EscapeDataString(query)),
    };

    /// <summary>Joins what was said with what was circled, keeping the URL a sane length.</summary>
    public static string Combine(string spoken, string selectedText, int maxLength = 1200)
    {
        spoken = Tidy(spoken);
        selectedText = Tidy(selectedText);
        if (selectedText.Length > maxLength) selectedText = selectedText[..maxLength];
        if (spoken.Length == 0) return selectedText;
        if (selectedText.Length == 0) return spoken;
        return $"{spoken}: \"{selectedText}\"";
    }

    static string Tidy(string text)
    {
        text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return text.Trim(' ', ',', ';', '-').TrimEnd('.', '?', '!').Trim();
    }
}
