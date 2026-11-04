namespace Shammy.Core.Updates;

/// <summary>
/// The update checker's status ("up to date", "ready 0.5.3", "downloading 0.5.3 42", "check failed: timed out") and
/// what the tray and Settings say about it, so a check always ends in something the user can read.
/// </summary>
public static class UpdateText
{
    public const string NotChecked = "not checked";
    public const string Checking = "checking";
    public const string UpToDate = "up to date";
    public const string NotInstalled = "off (not installed with Setup)";

    const string ReadyPrefix = "ready ";
    const string DownloadingPrefix = "downloading ";
    const string FailedPrefix = "check failed";

    /// <summary>A failed check tries again soon; otherwise every few hours.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan NormalInterval = TimeSpan.FromHours(4);

    public static string Ready(string version) => ReadyPrefix + version;
    public static string Downloading(string version, int percent) => $"{DownloadingPrefix}{version} {Math.Clamp(percent, 0, 100)}";
    public static string Failed(string reason) => $"{FailedPrefix}: {reason}";

    public static bool IsReady(string result) => result.StartsWith(ReadyPrefix, StringComparison.Ordinal);
    public static bool IsFailed(string result) => result.StartsWith(FailedPrefix, StringComparison.Ordinal);
    public static bool IsBusy(string result) => result == Checking || result.StartsWith(DownloadingPrefix, StringComparison.Ordinal);

    public static TimeSpan NextCheck(string result) => IsFailed(result) ? RetryAfterFailure : NormalInterval;

    static string ReadyVersion(string result) => result[ReadyPrefix.Length..].Trim();

    static (string Version, int Percent) Download(string result)
    {
        var parts = result[DownloadingPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 0 ? parts[0] : "", parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0);
    }

    /// <summary>The tray menu item.</summary>
    public static string TrayLabel(string result)
    {
        if (result == Checking) return "Checking for updates…";
        if (IsReady(result)) return $"Restart to update to {ReadyVersion(result)}";
        if (IsBusy(result))
        {
            var (version, percent) = Download(result);
            return $"Downloading {version}… {percent}%";
        }
        if (result == UpToDate) return "Shammy is up to date";
        if (IsFailed(result)) return "Couldn’t check · try again";
        return "Check for updates";
    }

    /// <summary>The hint under the version in Settings.</summary>
    public static string Hint(string result, bool installed)
    {
        if (!installed) return "Updates come with the installed copy.";
        if (result == Checking) return "Checking…";
        if (IsReady(result)) return $"Update {ReadyVersion(result)} is ready";
        if (IsBusy(result))
        {
            var (version, percent) = Download(result);
            return $"Downloading {version} · {percent}%";
        }
        if (result == UpToDate) return "Up to date";
        if (IsFailed(result)) return "Couldn’t check · tries again in 30 minutes";
        return "Checks by itself every few hours";
    }

    /// <summary>What Settings says once a check it started has finished.</summary>
    public static string Said(string result) =>
        IsReady(result) ? "An update is ready" :
        result == UpToDate ? "You have the latest version" :
        "Couldn’t check for updates";
}
