using System.Text;
using System.Text.RegularExpressions;

namespace Halo.Core.Search;

/// <summary>
/// Whisper sometimes loops on the quiet tail of a recording and says the phrase twice
/// ("Play Tum Hi Ho. Play Tum Hi Ho."), or a fragment then the full phrase ("screen shot. Take a
/// screenshot."). This collapses those repeats so commands and searches see what was actually said.
/// </summary>
public static class TranscriptCleaner
{
    static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public static string RemoveRepeats(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var kept = new List<string>();
        foreach (var raw in SentenceBreak.Split(text.Trim()))
        {
            var sentence = raw.Trim();
            if (sentence.Length == 0) continue;
            if (kept.Count == 0)
            {
                kept.Add(sentence);
                continue;
            }
            string prev = Squash(kept[^1]), cur = Squash(sentence);
            if (cur.Length == 0 || prev.Contains(cur, StringComparison.Ordinal)) continue; // repeat or fragment of the last one
            if (prev.Length > 0 && cur.Contains(prev, StringComparison.Ordinal)) kept[^1] = sentence; // fuller version replaces fragment
            else kept.Add(sentence);
        }
        return CollapseRepeatedWords(string.Join(' ', kept));
    }

    /// <summary>"play x play x" (no punctuation) or the same words three or four times -> one copy.</summary>
    static string CollapseRepeatedWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int copies = 4; copies >= 2; copies--)
        {
            // A single word said twice can be real ("bye bye"); three or more times, or longer phrases, can't.
            int minWords = copies == 2 ? 2 : 1;
            if (words.Length < copies * minWords || words.Length % copies != 0) continue;
            int size = words.Length / copies;
            var first = Squash(string.Join(' ', words.Take(size)));
            bool all = true;
            for (int c = 1; c < copies && all; c++)
                all = Squash(string.Join(' ', words.Skip(c * size).Take(size))) == first;
            if (all) return string.Join(' ', words.Take(size));
        }
        return text;
    }

    /// <summary>Lowercase letters and digits only, so punctuation and spacing differences don't matter.</summary>
    static string Squash(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
