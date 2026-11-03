using System.Text.RegularExpressions;

namespace Shammy.Core.Music;

/// <summary>
/// Picks which YouTube Music result to play. Search often ranks a remix, lofi or festival edit above the
/// original ("play kesariya" opened a Diwali remix), so results whose title marks a variant are passed
/// over, unless the query itself asks for that variant ("kesariya lofi" wants the lofi one).
/// </summary>
public static class SongPick
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// One kind of variant. <c>Loose</c> words ("live", "cover") are ordinary words in plenty of real titles
    /// ("Live Forever", "Cover Me"), so in a title they only count in the decoration: inside brackets or
    /// after a dash, pipe or colon ("Kesariya (Live)", "Tum Hi Ho - Cover"). A query counts them anywhere.
    /// </summary>
    sealed record Variant(string Name, Regex Word, bool Loose = false);

    static Regex W(string pattern) => new(@"(?<![\p{L}\p{N}])(?:" + pattern + @")(?![\p{L}\p{N}])", Opts);

    static readonly Variant[] Variants =
    {
        new("remix", W(@"remix(?:es|ed)?|rmx")),
        new("lofi", W(@"lo-?\s?fi")),
        new("slowed", W(@"slowed|reverb")),
        new("sped up", W(@"sped[\s-]?up|speed[\s-]?up|nightcore")),
        new("mashup", W(@"mash[\s-]?up")),
        new("unplugged", W(@"unplugged")),
        new("8d", W(@"8d(?:\s?audio)?")),
        new("karaoke", W(@"karaoke")),
        new("instrumental", W(@"instrumental")),
        new("bass boosted", W(@"bass[\s-]?boost(?:ed)?")),
        new("festival", W(@"diwali|deepavali|holi|navratri|christmas|xmas|festival|festive")),
        new("live", W(@"live"), Loose: true),
        new("cover", W(@"cover(?:ed)?(?:\s+by)?"), Loose: true),
    };

    // The decoration of a title: bracketed parts, and whatever follows " - ", " | " or ": ".
    static readonly Regex Decoration = new(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}|(?:\s[-–—|]\s|:\s).*$", Opts);

    /// <summary>The variants a title marks ("Kesariya (Diwali Remix)" → remix, festival).</summary>
    public static IReadOnlyList<string> VariantsIn(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();
        var decoration = string.Join(" ", Decoration.Matches(title).Select(m => m.Value));
        return Variants.Where(v => v.Word.IsMatch(v.Loose ? decoration : title)).Select(v => v.Name).ToList();
    }

    /// <summary>The variants a query asks for ("kesariya lofi" → lofi); every word counts here.</summary>
    public static IReadOnlyList<string> VariantsAsked(string query) =>
        string.IsNullOrWhiteSpace(query) ? Array.Empty<string>() : Variants.Where(v => v.Word.IsMatch(query)).Select(v => v.Name).ToList();

    /// <summary>
    /// The result to play: among the results that carry the most of the variants the query asked for, the
    /// first with no variant it didn't ask for; when every one is a variant, the first of them. Null for no results.
    /// </summary>
    public static T? Choose<T>(IReadOnlyList<T> results, Func<T, string> title, string query) where T : class
    {
        if (results.Count == 0) return null;
        var asked = VariantsAsked(query);
        var scored = results.Select(r =>
        {
            var marks = VariantsIn(title(r));
            return (Result: r, Wanted: marks.Count(asked.Contains), Unwanted: marks.Count(m => !asked.Contains(m)));
        }).ToList();
        int best = scored.Max(s => s.Wanted);
        var candidates = scored.Where(s => s.Wanted == best).ToList();
        return candidates.FirstOrDefault(s => s.Unwanted == 0).Result ?? candidates[0].Result;
    }

    public static YouTubeMusicSong? Choose(IReadOnlyList<YouTubeMusicSong> results, string query) =>
        Choose(results, s => s.Title, query);
}
