using System.Text;

namespace Halo.Core.Launcher;

/// <summary>A Start menu entry: what the user sees and what the shell launches (an AUMID or a path).</summary>
public sealed record AppEntry(string Name, string Target);

public readonly record struct AppMatch(AppEntry App, int Score);

/// <summary>
/// Spotlight-style fuzzy matching of a typed or spoken app name. Scores are bands so the kind of match
/// decides the order: exact > prefix > word initials ("vsc") > word start > substring > subsequence.
/// </summary>
public static class AppMatcher
{
    /// <summary>Good enough to claim "open X" from the web search (and suggest the app if it's not a launch).</summary>
    public const int AcceptScore = 32;

    /// <summary>Good enough to launch without asking.</summary>
    public const int LaunchScore = 40;

    /// <summary>The kind of match "start X" needs.</summary>
    public const int WordStartScore = 70;

    /// <summary>Also matches the program's file name, so "cmd" finds Command Prompt (…\cmd.exe).</summary>
    public static int Score(string query, AppEntry app)
    {
        int score = Score(query, app.Name);
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

        // Subsequences are only trusted for one dense word ("spotfy"); multi-word phrases like
        // "source licenses" would otherwise scatter across long app names.
        if (oneWord && qc.Length >= 3)
        {
            double density = SubsequenceDensity(qc, n);
            if (density >= 0.4) return (int)(20 + 30 * density);
        }
        return 0;
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
