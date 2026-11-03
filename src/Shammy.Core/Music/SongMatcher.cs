using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shammy.Core.Music;

/// <summary>A catalog name found in heard text: which words it covers and how sure the match is (0..1).</summary>
public sealed record SongMatch(string Name, int Start, int Count, double Score);

/// <summary>
/// Fuzzy matching of heard song and artist names against known ones. Romanized Hindi has no fixed
/// spelling (kesaria / kesariya, lambiyan / lambiyaan, chunna / channa) and Whisper adds its own
/// (arigit, arijih), so names are compared by a folded spelling key plus their consonant skeleton.
/// </summary>
public static class SongMatcher
{
    static readonly Regex NonWord = new(@"[^a-z0-9 ]+", RegexOptions.CultureInvariant);
    static readonly Regex Possessive = new(@"'s\b", RegexOptions.CultureInvariant);
    static readonly Regex SoftC = new(@"c(?!h)", RegexOptions.CultureInvariant);
    static readonly Regex YBetweenVowels = new(@"(?<=[aeiou])y(?=[aeiou])", RegexOptions.CultureInvariant);
    static readonly Regex FinalH = new(@"(?<=[aeiou])h\b", RegexOptions.CultureInvariant);
    static readonly Regex Doubles = new(@"([a-z])\1+", RegexOptions.CultureInvariant);
    static readonly Regex Vowels = new(@"[aeiou ]", RegexOptions.CultureInvariant);

    // Order matters: digraphs before the single letters they contain, long vowels before diphthongs.
    static readonly (string From, string To)[] Folds =
    {
        ("ph", "f"), ("ck", "k"), ("q", "k"), ("x", "ks"), ("z", "j"), ("w", "v"),
        ("chh", "c"), ("ch", "c"), ("sh", "s"), ("kh", "k"), ("gh", "g"), ("jh", "j"), ("th", "t"), ("dh", "d"),
        ("bh", "b"), ("rh", "r"),
        ("ee", "i"), ("ii", "i"), ("oo", "u"), ("uu", "u"), ("aa", "a"),
        ("ai", "e"), ("ay", "e"), ("ei", "e"), ("ey", "e"), ("au", "o"), ("ao", "o"), ("ou", "u"),
    };

    // Words that never start or end a name on their own; a window made only of these isn't a name.
    static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "on", "in", "by", "to", "me", "my", "and", "some", "song", "songs", "music", "please",
        "ke", "ka", "ki", "ko", "gaana", "gana", "gaane", "gane", "wala", "wale", "wali", "play",
    };

    /// <summary>
    /// The folded spelling of a name: lower case ASCII, aspirates and doubled letters merged, long vowels and
    /// diphthongs folded ("Raataan Lambiyaan" and "ratan lambian" give the same key). Spaces are kept.
    /// </summary>
    public static string Key(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = Possessive.Replace(StripAccents(text.ToLowerInvariant()), "");
        s = NonWord.Replace(s, " ");
        s = SoftC.Replace(s, "k");
        foreach (var (from, to) in Folds) s = s.Replace(from, to, StringComparison.Ordinal);
        s = YBetweenVowels.Replace(s, "");
        s = FinalH.Replace(s, "");
        s = Doubles.Replace(s, "$1");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>How alike two names sound, 0..1: the better of the folded spellings alone and their average
    /// with the consonant skeletons (vowels are the least reliable part of a romanized name).</summary>
    public static double Similarity(string a, string b) => SimilarityOfKeys(Key(a), Key(b));

    static double SimilarityOfKeys(string ka, string kb)
    {
        string a = ka.Replace(" ", ""), b = kb.Replace(" ", "");
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        double full = Ratio(a, b);
        string sa = Vowels.Replace(ka, ""), sb = Vowels.Replace(kb, "");
        if (sa.Length < 3 || sb.Length < 3) return full;
        return Math.Max(full, (full + Ratio(sa, sb)) / 2);
    }

    /// <summary>The score a name needs to count as heard: short names must match nearly exactly.</summary>
    public static double Threshold(string name) => ThresholdOfKey(Key(name));

    static double ThresholdOfKey(string key)
    {
        int n = key.Replace(" ", "").Length;
        return n < 4 ? 1.0 : n < 6 ? 0.85 : n < 9 ? 0.8 : 0.75;
    }

    // Lengths that can't reach the threshold anyway aren't worth an edit distance.
    static bool SimilarLength(string a, string b)
    {
        int la = a.Length - a.Count(c => c == ' '), lb = b.Length - b.Count(c => c == ' ');
        return Math.Abs(la - lb) <= Math.Max(2, Math.Max(la, lb) * 0.3);
    }

    // History and catalog names are matched on every play command; their keys don't change.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Keys = new(StringComparer.Ordinal);

    static string CachedKey(string name)
    {
        if (Keys.TryGetValue(name, out var key)) return key;
        key = Key(name);
        if (Keys.Count < 5000) Keys[name] = key;
        return key;
    }

    /// <summary>
    /// The known name that best matches a run of words in <paramref name="text"/>, or null if none is close
    /// enough. Runs of fewer words than the name are tried too, since Whisper merges unfamiliar words
    /// ("terrahone lagahu", "Kalsubah"), and one more, since it also splits them ("Chan-na-mi-reya").
    /// </summary>
    public static SongMatch? FindBest(string text, IEnumerable<string> names) => FindBest(Words(text), names, null);

    static SongMatch? FindBest(IReadOnlyList<string> words, IEnumerable<string> names, bool[]? taken)
    {
        var keys = words.Select(w => Key(w)).ToArray();
        var filler = words.Select(IsFiller).ToArray();
        SongMatch? best = null;
        foreach (var name in names)
        {
            var nameKey = CachedKey(name);
            if (nameKey.Length == 0) continue;
            double need = ThresholdOfKey(nameKey);
            int n = nameKey.Split(' ').Length;
            for (int size = 1; size <= n + 1; size++)
            {
                for (int start = 0; start + size <= words.Count; start++)
                {
                    if (taken is not null && Enumerable.Range(start, size).Any(i => taken[i])) continue;
                    if (filler[start] || filler[start + size - 1]) continue;
                    var window = string.Join(' ', keys, start, size);
                    if (!SimilarLength(window, nameKey)) continue;
                    double score = SimilarityOfKeys(window, nameKey);
                    if (score < need) continue;
                    // Prefer the closer match, then the longer name ("Tum Hi Ho" over "Tum").
                    if (best is null || score > best.Score + 1e-9 || (Math.Abs(score - best.Score) < 1e-9 && name.Length > best.Name.Length))
                        best = new SongMatch(name, start, size, score);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Respells the names in a song query the way the catalog spells them ("Arijih Singh's songs" becomes
    /// "Arijit Singh songs"). At most two names (a song and its artist); everything else is kept as heard.
    /// </summary>
    public static string Correct(string query, IEnumerable<string> names, out int corrected)
    {
        corrected = 0;
        var words = Words(query);
        if (words.Count == 0) return query;
        var list = names as IReadOnlyCollection<string> ?? names.ToList();
        var taken = new bool[words.Count];
        var replacements = new List<SongMatch>();
        for (int round = 0; round < 2; round++)
        {
            var match = FindBest(words, list, taken);
            if (match is null) break;
            replacements.Add(match);
            for (int i = match.Start; i < match.Start + match.Count; i++) taken[i] = true;
        }
        if (replacements.Count == 0) return query;
        var output = new List<string>();
        // "a rejig singh songs": a lone letter in front of the name is the name's first sound heard twice.
        int from = replacements.Any(m => m.Start == 1) && words[0].Length == 1 && char.IsLetter(words[0][0]) && words[0] != "I" ? 1 : 0;
        if (from == 1) corrected++;
        for (int i = from; i < words.Count;)
        {
            var r = replacements.FirstOrDefault(m => m.Start == i);
            if (r is not null)
            {
                output.Add(r.Name);
                if (!string.Equals(string.Join(' ', words.Skip(i).Take(r.Count)), r.Name, StringComparison.Ordinal)) corrected++;
                i += r.Count;
            }
            else
            {
                output.Add(words[i]);
                i++;
            }
        }
        return string.Join(' ', output);
    }

    /// <summary>
    /// The names to prime a play command's second pass with, most useful last (Whisper keeps the end of an
    /// over-long prompt): names that sound like what the first pass heard, then the user's own listening
    /// history, then popular songs, until <paramref name="budgetChars"/> is used up.
    /// </summary>
    public static IReadOnlyList<string> PromptNames(string heard, IEnumerable<string> history, IEnumerable<string> builtIn, int budgetChars)
    {
        var words = Words(heard);
        var historyList = history.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var builtInList = builtIn.ToList();
        var ranked = new List<string>();
        if (words.Count > 0)
        {
            // Close but not necessarily over the match threshold: these are the likeliest candidates.
            var keys = words.Where(w => !IsFiller(w)).Select(w => Key(w)).ToList();
            ranked.AddRange(historyList.Concat(builtInList)
                .Select(n => (Name: n, Score: BestWindowScore(keys, CachedKey(n))))
                .Where(x => x.Score >= 0.55)
                .OrderByDescending(x => x.Score)
                .Take(12)
                .Select(x => x.Name));
        }
        ranked.AddRange(historyList);
        ranked.AddRange(builtInList);

        var picked = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int used = 0;
        foreach (var name in ranked)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 60 || !seen.Add(CachedKey(trimmed))) continue;
            if (used + trimmed.Length + 2 > budgetChars) continue;
            used += trimmed.Length + 2;
            picked.Add(trimmed);
        }
        picked.Reverse();
        return picked;
    }

    static double BestWindowScore(IReadOnlyList<string> keys, string nameKey)
    {
        if (nameKey.Length == 0 || keys.Count == 0) return 0;
        int n = nameKey.Split(' ').Length;
        double best = 0;
        for (int size = 1; size <= n + 1; size++)
            for (int start = 0; start + size <= keys.Count; start++)
            {
                var window = string.Join(' ', keys.Skip(start).Take(size));
                if (SimilarLength(window, nameKey)) best = Math.Max(best, SimilarityOfKeys(window, nameKey));
            }
        return best;
    }

    static bool IsFiller(string word) => Filler.Contains(Possessive.Replace(word.ToLowerInvariant(), ""));

    static List<string> Words(string text) =>
        Regex.Replace(text ?? "", @"[\s,]+", " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', '!', '?', ';', ':', '"', (char)0x201C, (char)0x201D))
            .Where(w => w.Length > 0)
            .ToList();

    static double Ratio(string a, string b) => 1.0 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);

    static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
