using System.Text;

namespace Shammy.Core.Launcher;

/// <summary>A Start menu entry: what the user sees and what the shell launches (an AUMID or a path).</summary>
public sealed record AppEntry(string Name, string Target);

public readonly record struct AppMatch(AppEntry App, int Score);

/// <summary>
/// Spotlight-style fuzzy matching of a typed or spoken app name. Scores are bands so the kind of match
/// decides the order: exact > nickname ("vs code") > prefix > word initials ("vsc") > word start > substring >
/// one slip per word ("google crome") > subsequence.
/// </summary>
public static class AppMatcher
{
    /// <summary>Good enough to claim "open X" from the web search (and suggest the app if it's not a launch).</summary>
    public const int AcceptScore = 32;

    /// <summary>Good enough to launch without asking.</summary>
    public const int LaunchScore = 40;

    /// <summary>The kind of match "start X" needs.</summary>
    public const int WordStartScore = 70;

    /// <summary>A nickname ("ppt", "vs code") of an installed app: above a prefix, so "word" is Word, not WordPad.</summary>
    public const int AliasScore = 92;

    /// <summary>One slip per word ("google crome"): enough to launch, below every exact kind of match.</summary>
    const int TypoScore = 47, PartialTypoScore = 44;

    /// <summary>
    /// What people call apps, mapped to the (normalized) Start menu names. A nickname only ever picks an app that
    /// is installed under one of these names, or one of them plus an edition ("Microsoft Word 2016").
    /// </summary>
    static readonly Dictionary<string, string[]> Nicknames = BuildNicknames();

    static Dictionary<string, string[]> BuildNicknames()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string[] names, params string[] nicknames)
        {
            foreach (var n in nicknames) map[n.Replace(" ", "")] = names;
        }
        Add(new[] { "visual studio code" }, "vs code", "vscode", "code");
        Add(new[] { "powerpoint", "microsoft powerpoint" }, "ppt", "powerpoint", "power point", "ms powerpoint");
        Add(new[] { "excel", "microsoft excel" }, "excel", "ms excel");
        Add(new[] { "word", "microsoft word" }, "word", "ms word", "microsoft word");
        Add(new[] { "outlook", "microsoft outlook", "outlook new", "outlook classic" }, "outlook", "ms outlook");
        Add(new[] { "onenote", "microsoft onenote" }, "onenote", "one note");
        Add(new[] { "command prompt" }, "cmd", "command prompt", "command line");
        Add(new[] { "whatsapp", "whatsapp beta" }, "wa", "whatsapp", "whats app");
        Add(new[] { "youtube music" }, "yt music", "ytm", "youtube music");
        Add(new[] { "youtube" }, "yt", "youtube");
        Add(new[] { "google chrome" }, "chrome", "google chrome");
        Add(new[] { "microsoft edge" }, "edge", "ms edge");
        Add(new[] { "firefox", "mozilla firefox" }, "ff", "firefox");
        Add(new[] { "microsoft teams", "teams" }, "teams", "ms teams");
        Add(new[] { "notepad++" }, "npp", "notepad plus plus");
        Add(new[] { "task manager" }, "taskmgr", "task manager");
        Add(new[] { "file explorer" }, "explorer", "windows explorer");
        Add(new[] { "instagram" }, "insta");
        return map;
    }

    /// <summary>True when <paramref name="query"/> is a nickname of this app ("vs code" for "Visual Studio Code").</summary>
    public static bool IsNickname(string query, string appName)
    {
        if (!Nicknames.TryGetValue(Normalize(query).Replace(" ", ""), out var names)) return false;
        var n = Normalize(appName);
        return names.Any(t => n == t || n.StartsWith(t + " ", StringComparison.Ordinal));
    }

    /// <summary>Also nicknames, and the program's file name, so "cmd" finds Command Prompt (…\cmd.exe).</summary>
    public static int Score(string query, AppEntry app)
    {
        int score = Score(query, app.Name);
        if (score < AliasScore && IsNickname(query, app.Name)) score = AliasScore;
        if (score < 85 && app.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exe = Path.GetFileNameWithoutExtension(app.Target);
            if (exe.Length >= 2 && Normalize(query).Replace(" ", "") == Normalize(exe).Replace(" ", "")) score = 85;
        }
        return score;
    }

    public static int Score(string query, string name)
    {
        var q = Normalize(query);
        var n = Normalize(name);
        if (q.Length == 0 || n.Length == 0) return 0;
        if (q == n) return 100;

        string qc = q.Replace(" ", ""), nc = n.Replace(" ", "");
        bool oneWord = !q.Contains(' ');
        if (qc == nc) return 95;
        if (q.Length >= 2 && n.StartsWith(q, StringComparison.Ordinal)) return 90;
        if (qc.Length >= 3 && nc.StartsWith(qc, StringComparison.Ordinal)) return 85;

        // Two letters ("ai", "it") are too often a plain word to be read as initials.
        if (oneWord && qc.Length >= 3)
        {
            var (plain, humps) = Initials(name);
            if (qc == plain || qc == humps) return 80;
            if ((plain.Length > qc.Length && plain.StartsWith(qc, StringComparison.Ordinal)) ||
                (humps.Length > qc.Length && humps.StartsWith(qc, StringComparison.Ordinal))) return 72;
        }

        if (q.Length >= 2 && n.Contains(" " + q, StringComparison.Ordinal)) return 70;
        if (!oneWord && WordsStartInOrder(q.Split(' '), n.Split(' '))) return 65;
        if (q.Length >= 3 && n.Contains(q, StringComparison.Ordinal)) return 50;
        if (!oneWord && OneWordMisheard(q.Split(' '), n.Split(' ')) is int misheard) return misheard;

        int slips = WithSlips(q.Split(' '), n.Split(' '));

        // Subsequences are only trusted for one dense word ("spotfy"); multi-word phrases like
        // "source licenses" would otherwise scatter across long app names.
        if (oneWord && qc.Length >= 3)
        {
            double density = SubsequenceDensity(qc, n);
            if (density >= 0.4) return Math.Max(slips, (int)(20 + 30 * density));
        }
        return slips;
    }

    /// <summary>
    /// Every query word, in order, is a name word: exactly, or with one slip in a word of 4+ letters (two in 8+)
    /// that keeps its first letter ("crome" for Chrome, "notpad", "whatsap"). The last word may also start a name
    /// word. Short words must be exact, so "microsoft word" never becomes Edge nor "google maps" Chrome.
    /// </summary>
    static int WithSlips(string[] query, string[] name)
    {
        if (query.Length > name.Length) return 0;
        int j = 0;
        bool slipped = false;
        for (int i = 0; i < query.Length; i++)
        {
            var w = query[i];
            bool last = i == query.Length - 1, found = false;
            while (j < name.Length && !found)
            {
                var candidate = name[j++];
                if (candidate == w || (last && w.Length >= 2 && candidate.StartsWith(w, StringComparison.Ordinal))) found = true;
                else if (IsSlip(w, candidate)) found = slipped = true;
            }
            if (!found) return 0;
        }
        if (!slipped) return 0;
        return query.Length == name.Length ? TypoScore : PartialTypoScore;
    }

    static bool IsSlip(string typed, string word)
    {
        if (typed.Length < 4 || word.Length < 4 || typed[0] != word[0]) return false;
        int allowed = Math.Min(typed.Length, word.Length) >= 8 ? 2 : 1;
        if (Math.Abs(typed.Length - word.Length) > allowed) return false;
        int d = Distance(typed, word);
        return d >= 1 && d <= allowed;
    }

    /// <summary>Edits between two words, where swapping two neighbouring letters ("chorme") counts as one.</summary>
    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Best matches first; equal scores prefer the shorter (more specific) name.</summary>
    public static IReadOnlyList<AppMatch> Rank(string query, IEnumerable<AppEntry> apps, int max = 3) =>
        apps.Select(a => new AppMatch(a, Score(query, a)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.App.Name.Length)
            .ThenBy(m => m.App.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

    public static AppMatch? Best(string query, IEnumerable<AppEntry> apps) =>
        Rank(query, apps, 1) is [var top] ? top : null;

    /// <summary>Lowercase words separated by single spaces; '+' and '#' stay so "Notepad++" isn't "Notepad".</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = true;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is '+' or '#')
            {
                sb.Append(char.ToLowerInvariant(ch));
                space = false;
            }
            else if (!space)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>First letter of each word, and also of each camel hump ("Windows PowerShell" → "wp", "wps").</summary>
    static (string Plain, string Humps) Initials(string name)
    {
        var plain = new StringBuilder();
        var humps = new StringBuilder();
        bool start = true;
        char prev = ' ';
        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                start = true;
                prev = ch;
                continue;
            }
            if (start)
            {
                plain.Append(char.ToLowerInvariant(ch));
                humps.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsUpper(ch) && char.IsLower(prev))
            {
                humps.Append(char.ToLowerInvariant(ch));
            }
            start = false;
            prev = ch;
        }
        return (plain.ToString(), humps.ToString());
    }

    static bool WordsStartInOrder(string[] query, string[] name)
    {
        int j = 0;
        foreach (var w in query)
        {
            while (j < name.Length && !name[j].StartsWith(w, StringComparison.Ordinal)) j++;
            if (j == name.Length) return false;
            j++;
        }
        return true;
    }

    /// <summary>
    /// Speech often gets one word of a name wrong ("Visual Studio board"). At least two other words, 8+ letters,
    /// must start name words in order, so "Microsoft Word" never becomes Microsoft Edge and "Google Maps" stays
    /// a search. A misheard word that still sounds a bit like the word it replaced ranks above one that doesn't.
    /// </summary>
    static int? OneWordMisheard(string[] query, string[] name)
    {
        if (query.Length < 3 || name.Length < query.Length) return null;
        int? best = null;
        for (int skip = 0; skip < query.Length; skip++)
        {
            var kept = query.Where((_, i) => i != skip).ToArray();
            if (!WordsStartInOrder(kept, name)) continue;
            int letters = kept.Sum(w => w.Length);
            if (letters < 8) continue;
            var heard = query[skip];
            bool alike = name.Any(w => !kept.Any(k => w.StartsWith(k, StringComparison.Ordinal)) && Similarity(heard, w) > 0.19);
            int score = LaunchScore + 1 + (alike ? 1 : 0);
            best = Math.Max(best ?? 0, score);
        }
        return best;
    }

    /// <summary>1 minus the edit distance over the longer length.</summary>
    static double Similarity(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) d[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = d[0];
            d[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }
        return 1 - (double)d[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// Query length over the shortest stretch of the name that contains it in order, starting at a word
    /// start. 1 means contiguous; 0 means no match.
    /// </summary>
    static double SubsequenceDensity(string query, string name)
    {
        int best = int.MaxValue;
        for (int s = 0; s < name.Length; s++)
        {
            if (name[s] != query[0] || (s > 0 && name[s - 1] != ' ')) continue;
            int qi = 1, i = s + 1, spaces = 0;
            for (; i < name.Length && qi < query.Length; i++)
            {
                if (name[i] == ' ') spaces++;
                else if (name[i] == query[qi]) qi++;
            }
            if (qi == query.Length) best = Math.Min(best, i - s - spaces);
        }
        return best == int.MaxValue ? 0 : (double)query.Length / best;
    }
}
