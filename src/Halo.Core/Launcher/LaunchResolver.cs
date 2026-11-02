using System.Text.RegularExpressions;

namespace Halo.Core.Launcher;

public enum LaunchKind { App, Url, Folder }

/// <summary>"open X" split into what X is. <see cref="Target"/> is the app query, the URL or the folder key.</summary>
public sealed record LaunchRequest(LaunchKind Kind, string Target);

/// <summary>What to do with a launch request: <see cref="Match"/> is the best app (apps only).</summary>
public sealed record LaunchPlan(LaunchRequest Request, AppMatch? Match)
{
    public bool CanLaunch => Request.Kind != LaunchKind.App || Match is { Score: >= AppMatcher.LaunchScore };
}

/// <summary>
/// Parses "open/launch/start &lt;app | domain | known folder&gt;". Deliberately strict: a phrase is only
/// claimed when its remainder is a domain, a known folder or fuzzy-matches an installed app, so
/// "open source licenses" or "how to open a pdf" still go to the web.
/// </summary>
public static class LaunchResolver
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Verb = new(@"^(?:open|launch|start)\s+(?:up\s+)?(?<rest>.+)$", Opts);
    static readonly Regex Politeness = new(@"\s+(?:please|for me)$", Opts);
    static readonly Regex Article = new(@"^the\s+", Opts);
    static readonly Regex Possessive = new(@"^my\s+", Opts);
    static readonly Regex AppSuffix = new(@"\s+(?:app|application|program)$", Opts);
    static readonly Regex FolderSuffix = new(@"\s+folder$", Opts);
    static readonly Regex SpokenDot = new(@"\s+dot\s+", Opts);

    static readonly Regex Domain = new(
        @"^(?<host>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?<tld>[a-z]{2,24}))(?<port>:\d{1,5})?(?<path>[/?#]\S*)?$", Opts);

    // "open notes.txt" is a file, not a website, even though some of these are also real TLDs.
    static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv", "json", "xml", "log", "ini",
        "exe", "msi", "dll", "bat", "cmd", "ps1", "py", "cs", "js", "ts", "zip", "rar", "png", "jpg", "jpeg",
        "gif", "bmp", "svg", "mp3", "mp4", "wav", "mkv", "mov", "avi", "lnk",
    };

    // "open it", "open up", "open the" are never app names, however well "the" matches "LetsView on the Web".
    static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "it", "this", "that", "these", "those", "up", "over", "again", "now", "here", "there", "me",
        "my", "them", "all", "everything", "something", "anything", "new", "back",
    };

    public static readonly IReadOnlyList<string> KnownFolders =
        new[] { "downloads", "documents", "desktop", "pictures", "music", "videos" };

    public static LaunchRequest? Parse(string text)
    {
        var m = Verb.Match(text.Trim());
        if (!m.Success) return null;
        var rest = Politeness.Replace(m.Groups["rest"].Value.Trim().TrimEnd('.', '!', '?'), "").Trim();
        rest = Article.Replace(rest, "");
        if (rest.Length == 0) return null;

        // "my documents" is a folder, but "my computer" shouldn't become "Computer Management".
        var folder = FolderSuffix.Replace(Possessive.Replace(rest, ""), "").ToLowerInvariant();
        if (KnownFolders.Contains(folder)) return new LaunchRequest(LaunchKind.Folder, folder);

        if (Uri.TryCreate(rest, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !rest.Contains(' '))
            return new LaunchRequest(LaunchKind.Url, uri.AbsoluteUri);

        // Speech gives "github dot com".
        var spoken = SpokenDot.Replace(rest, ".");
        var d = Domain.Match(spoken);
        if (d.Success && (!FileExtensions.Contains(d.Groups["tld"].Value) || d.Groups["path"].Success))
            return new LaunchRequest(LaunchKind.Url,
                "https://" + d.Groups["host"].Value.ToLowerInvariant() + d.Groups["port"].Value + d.Groups["path"].Value);

        var app = AppSuffix.Replace(rest, "").Trim();
        return app.Length == 0 || NotApps.Contains(app) ? null : new LaunchRequest(LaunchKind.App, app);
    }

    /// <summary>Null when the text isn't a launch the notch should claim.</summary>
    public static LaunchPlan? Resolve(string text, IEnumerable<AppEntry> apps)
    {
        var request = Parse(text);
        if (request is null) return null;
        if (request.Kind == LaunchKind.Folder) return new LaunchPlan(request, null);
        if (request.Kind == LaunchKind.Url)
        {
            // An app that is literally called "Paint.NET" wins over the website.
            var raw = Verb.Match(text.Trim()).Groups["rest"].Value.Trim();
            var exact = AppMatcher.Best(raw, apps);
            return exact is { Score: 100 } ? new LaunchPlan(new LaunchRequest(LaunchKind.App, raw), exact) : new LaunchPlan(request, null);
        }
        var best = AppMatcher.Best(request.Target, apps);
        // "start" begins so many ordinary requests ("start over", "start a timer") that it needs a strong match.
        int needed = text.TrimStart().StartsWith("start", StringComparison.OrdinalIgnoreCase) ? AppMatcher.WordStartScore : AppMatcher.AcceptScore;
        return best is { } b && b.Score >= needed ? new LaunchPlan(request, best) : null;
    }
}
