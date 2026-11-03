using System.Text;

namespace Shammy.Core.Search;

/// <summary>
/// Hindi in Devanagari spelt out in plain Latin letters, the way people type Hinglish ("कल सुबह 7 बजे"
/// becomes "kal subah 7 baje"). Approximate on purpose: it only has to be close enough for command
/// parsing and song matching, which are spelling-tolerant anyway.
/// </summary>
public static class Devanagari
{
    const char Virama = '्', Nukta = '़';

    static readonly Dictionary<char, string> Consonants = new()
    {
        ['क'] = "k", ['ख'] = "kh", ['ग'] = "g", ['घ'] = "gh", ['ङ'] = "n",
        ['च'] = "ch", ['छ'] = "chh", ['ज'] = "j", ['झ'] = "jh", ['ञ'] = "n",
        ['ट'] = "t", ['ठ'] = "th", ['ड'] = "d", ['ढ'] = "dh", ['ण'] = "n",
        ['त'] = "t", ['थ'] = "th", ['द'] = "d", ['ध'] = "dh", ['न'] = "n",
        ['प'] = "p", ['फ'] = "ph", ['ब'] = "b", ['भ'] = "bh", ['म'] = "m",
        ['य'] = "y", ['र'] = "r", ['ल'] = "l", ['व'] = "v", ['श'] = "sh", ['ष'] = "sh", ['स'] = "s", ['ह'] = "h",
        // The precomposed dotted letters (qa, khha, ghha, za, dddha, rha, fa, yya).
        ['क़'] = "q", ['ख़'] = "kh", ['ग़'] = "g", ['ज़'] = "z", ['ड़'] = "d", ['ढ़'] = "dh",
        ['फ़'] = "f", ['य़'] = "y",
    };

    // With a nukta: the dotted letters as two code points.
    static readonly Dictionary<char, string> WithNukta = new()
    {
        ['क'] = "q", ['ख'] = "kh", ['ग'] = "g", ['ज'] = "z", ['ड'] = "d", ['ढ'] = "dh", ['फ'] = "f", ['य'] = "y",
    };

    static readonly Dictionary<char, string> Vowels = new()
    {
        ['अ'] = "a", ['आ'] = "aa", ['इ'] = "i", ['ई'] = "ee", ['उ'] = "u", ['ऊ'] = "oo", ['ऋ'] = "ri",
        ['ए'] = "e", ['ऐ'] = "ai", ['ओ'] = "o", ['औ'] = "au", ['ऑ'] = "o", ['ऍ'] = "e",
    };

    static readonly Dictionary<char, string> Signs = new()
    {
        ['ा'] = "aa", ['ि'] = "i", ['ी'] = "ee", ['ु'] = "u", ['ू'] = "oo", ['ृ'] = "ri",
        ['े'] = "e", ['ै'] = "ai", ['ो'] = "o", ['ौ'] = "au", ['ॉ'] = "o", ['ॅ'] = "e",
    };

    public static bool Contains(string? text) => text is not null && text.Any(c => c is >= 'ऀ' and <= 'ॿ');

    /// <summary>Latin spelling of any Devanagari in <paramref name="text"/>; everything else is kept.</summary>
    public static string ToLatin(string? text)
    {
        if (string.IsNullOrEmpty(text) || !Contains(text)) return text ?? "";
        var sb = new StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (Consonants.TryGetValue(c, out var cons))
            {
                if (i + 1 < text.Length && text[i + 1] == Nukta && WithNukta.TryGetValue(c, out var dotted))
                {
                    cons = dotted;
                    i++;
                }
                sb.Append(cons);
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (Signs.TryGetValue(next, out var sign))
                {
                    sb.Append(sign);
                    i++;
                }
                else if (next == Virama)
                {
                    i++;
                }
                else if (!EndsWord(text, i + 1))
                {
                    // The inherent vowel, which Hindi drops at the end of a word ("कल" is "kal", not "kala").
                    sb.Append('a');
                }
                continue;
            }
            if (Vowels.TryGetValue(c, out var vowel)) sb.Append(vowel);
            else if (Signs.TryGetValue(c, out var loose)) sb.Append(loose);
            else if (c is 'ं' or 'ँ') sb.Append('n');
            else if (c == 'ः') sb.Append('h');
            else if (c is '।' or '॥') sb.Append('.');
            else if (c is >= '०' and <= '९') sb.Append((char)('0' + (c - '०')));
            else if (c is Virama or Nukta or '‌' or '‍') { }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // A word ends at anything that isn't a Devanagari letter or sign; anusvara and visarga still belong to it.
    static bool EndsWord(string text, int at)
    {
        if (at >= text.Length) return true;
        char c = text[at];
        return !(c is >= 'ऀ' and <= 'ॿ') || c is '।' or '॥' || (c is >= '०' and <= '९');
    }
}
