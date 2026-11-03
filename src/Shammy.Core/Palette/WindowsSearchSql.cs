using System.Text;

namespace Shammy.Core.Palette;

/// <summary>
/// Builds the Windows Search (SystemIndex) query for typed text. The text is never pasted in as-is:
/// each word is cut down to letters, digits and a few name characters, so quotes, wildcards and SQL
/// can't get through, and the scope folder's quotes are doubled.
/// </summary>
public static class WindowsSearchSql
{
    public const int MaxTokens = 5;
    public const int MaxTokenLength = 40;

    // Kept inside a word ("q3-report", "notes_v2.txt"); everything else (quotes, *, %, &, parentheses) goes.
    static readonly HashSet<char> NameChars = new() { '-', '_', '.' };

    /// <summary>The words to match, cleaned; empty when nothing searchable is left.</summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        var tokens = new List<string>();
        foreach (var raw in (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
                if (char.IsLetterOrDigit(ch) || NameChars.Contains(ch)) sb.Append(ch);
            var t = sb.ToString().Trim('-', '.', '_');
            if (t.Length == 0) continue;
            if (t.Length > MaxTokenLength) t = t[..MaxTokenLength];
            if (!tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) tokens.Add(t);
            if (tokens.Count == MaxTokens) break;
        }
        return tokens;
    }

    /// <summary>"file:C:/Users/me" for SCOPE, quotes doubled.</summary>
    public static string ScopeUrl(string folder) =>
        "file:" + Literal(folder.Replace('\\', '/').TrimEnd('/'));

    /// <summary>
    /// Null when the text has no usable word or is too short to be worth a query (a single letter would
    /// match half the disk). Each word must start a word of the file name, newest files first.
    /// </summary>
    public static string? Build(string text, string scopeFolder, int top = 20)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0 || tokens.Sum(t => t.Length) < 2) return null;
        if (string.IsNullOrWhiteSpace(scopeFolder) || scopeFolder.Any(char.IsControl)) return null;
        top = Math.Clamp(top, 1, 100);

        var sql = new StringBuilder();
        sql.Append("SELECT TOP ").Append(top)
           .Append(" System.ItemPathDisplay, System.ItemType FROM SystemIndex WHERE SCOPE='")
           .Append(ScopeUrl(scopeFolder)).Append('\'');
        foreach (var t in tokens)
            sql.Append(" AND CONTAINS(System.FileName, '\"").Append(Literal(t)).Append("*\"')");
        // Package folders, build output and app data would otherwise fill the top rows (one project's
        // node_modules has hundreds of README.md). Full-text NOT CONTAINS on the folder path costs ~100 ms
        // for the whole list; NOT LIKE '%…%' scans and took seconds. It matches whole words, so a folder
        // called "Build Week" is skipped too; dotted names (.vs, .idea) are filtered afterwards instead,
        // since the word breaker would turn ".next" into "next".
        foreach (var dir in ExcludedFolderWords)
            sql.Append(" AND NOT CONTAINS(System.ItemFolderPathDisplay, '\"").Append(dir).Append("\"')");
        sql.Append(" ORDER BY System.DateModified DESC");
        return sql.ToString();
    }

    /// <summary>Folder words excluded in the query itself (developer folders and app data).</summary>
    public static readonly IReadOnlyList<string> ExcludedFolderWords = new[]
    {
        "node_modules", "AppData", "__pycache__", "venv", "env", "obj", "bin", "dist", "build", "target",
        "packages", "coverage",
    };

    static string Literal(string s) => s.Replace("'", "''");
}

/// <summary>A file the index found.</summary>
public sealed record FileHit(string Path, bool IsFolder);

/// <summary>Turns raw index hits into the few worth showing.</summary>
public static class FileResults
{
    /// <summary>Folder names never worth showing files from: VCS, IDE, package and build-output folders.</summary>
    static readonly HashSet<string> NoiseDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "$recycle.bin", "__pycache__", "venv", ".venv", "env", "bin", "obj", "dist", "build",
        ".vs", ".idea", ".gradle", "target", "packages", ".next", ".nuxt", "coverage",
    };

    /// <summary>App data, build output, VCS internals, Office lock files and dotfiles nobody opens by name.</summary>
    public static bool IsNoise(string path, string profile)
    {
        if (string.IsNullOrEmpty(path)) return true;
        var p = path.Replace('/', '\\');
        if (profile.Length > 0 && p.StartsWith(profile.TrimEnd('\\') + "\\AppData\\", StringComparison.OrdinalIgnoreCase)) return true;
        var segments = p.TrimEnd('\\').Split('\\');
        // Any folder on the way, or the item itself when it is one of those folders.
        if (segments.Any(NoiseDirs.Contains)) return true;
        // Hidden dot-folders and dotfiles (.agents, .config, .env): never what someone types a name to find.
        if (segments.Any(s => s.StartsWith('.'))) return true;
        var name = segments[^1];
        return name.Length == 0 || name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.') ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether to ask the index at all. A command stays the top row, but files are still worth showing
    /// under it once there are 3+ letters ("resume" is a media command and a CV). Sums and exact settings
    /// words ("sound", "wifi") are never file names anyone means.
    /// </summary>
    public static bool ShouldSearch(string text, bool hasCommand, bool isCalculation, bool exactSetting)
    {
        int length = (text ?? "").Trim().Length;
        if (length < 2 || isCalculation || exactSetting) return false;
        return !hasCommand || length >= 3;
    }

    /// <summary>Best name matches first (the index order, newest first, breaks ties); at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<(FileHit Hit, int Match)> Rank(string query, IEnumerable<FileHit> hits, string profile, int max = 4) =>
        hits.Where(h => !IsNoise(h.Path, profile))
            .GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase)
            .Select((g, i) => (Hit: g.First(), Index: i))
            .Select(x => (x.Hit, x.Index, Match: Score(query, x.Hit)))
            .OrderByDescending(x => x.Match)
            .ThenBy(x => x.Index)
            .Take(max)
            .Select(x => (x.Hit, x.Match))
            .ToList();

    /// <summary>How well the name matches; the index already said it matches, so never below 30.</summary>
    public static int Score(string query, FileHit hit)
    {
        var name = System.IO.Path.GetFileName(hit.Path.TrimEnd('\\', '/'));
        var stem = hit.IsFolder ? name : System.IO.Path.GetFileNameWithoutExtension(name);
        int s = Math.Max(Launcher.AppMatcher.Score(query, stem), Launcher.AppMatcher.Score(query, name));
        return Math.Clamp(s, 30, 100);
    }
}
