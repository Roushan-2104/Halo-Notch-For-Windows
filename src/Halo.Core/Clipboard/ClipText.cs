using System.Text.RegularExpressions;

namespace Halo.Core.Clipboard;

public enum ClipIntent
{
    None,
    Show,
    Clear,
}

/// <summary>Short labels for clipboard entries and the typed/spoken clipboard commands.</summary>
public static class ClipText
{
    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex ShowPhrase = new(
        @"^(?:(?:show|open|view|see)\s+(?:me\s+)?)?(?:my\s+|the\s+)?clipboard(?:\s+history)?$", RegexOptions.IgnoreCase);
    static readonly Regex ClearPhrase = new(
        @"^(?:clear|empty|wipe|erase|delete)\s+(?:my\s+|the\s+)?clipboard(?:\s+history)?$", RegexOptions.IgnoreCase);

    /// <summary>"show clipboard", "clipboard history", "clear my clipboard"... Anything else is None so web searches pass.</summary>
    public static ClipIntent Parse(string text)
    {
        var t = Spaces.Replace(text.Trim().TrimEnd('.', '!', '?'), " ");
        if (ShowPhrase.IsMatch(t)) return ClipIntent.Show;
        if (ClearPhrase.IsMatch(t)) return ClipIntent.Clear;
        return ClipIntent.None;
    }

    /// <summary>The whole text on one line, cut to <paramref name="max"/> characters with an ellipsis.</summary>
    public static string Preview(string text, int max = 22) => Cut(Spaces.Replace(text, " ").Trim(), max);

    /// <summary>The first non-blank line, for list rows.</summary>
    public static string FirstLine(string text, int max = 120)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = Spaces.Replace(line, " ").Trim();
            if (t.Length > 0) return Cut(t, max);
        }
        return "";
    }

    public static string FilesTitle(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return "";
        var first = Path.GetFileName(files[0].TrimEnd('\\', '/'));
        if (first.Length == 0) first = files[0];
        return files.Count == 1 ? first : $"{first} + {files.Count - 1} more";
    }

    public static string Dimensions(int width, int height) => $"{width}×{height}";

    /// <summary>Same style as the notification history: now, 5m, 3h, then the date.</summary>
    public static string Relative(DateTime then, DateTime now)
    {
        var age = now - then;
        return age.TotalMinutes < 1 ? "now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}h"
            : then.ToString("d MMM");
    }

    static string Cut(string s, int max) => s.Length <= max ? s : s[..Math.Max(0, max - 1)].TrimEnd() + "…";
}

/// <summary>
/// The clipboard formats password managers and other sensitive apps set to opt out of history
/// (documented for Windows clipboard history and cloud clipboard).
/// </summary>
public static class ClipPrivacy
{
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    public const string CloudFormat = "CanUploadToCloudClipboard";

    /// <summary>Values are null when the format is absent; a present but unreadable value should be passed as 0.</summary>
    public static bool ShouldSkip(bool excludePresent, uint? canIncludeInHistory, uint? canUploadToCloud) =>
        excludePresent || canIncludeInHistory == 0 || canUploadToCloud == 0;

    /// <summary>The DWORD the opt-out formats carry; unreadable data counts as "no".</summary>
    public static uint ReadDword(byte[]? data) => data is { Length: >= 4 } ? BitConverter.ToUInt32(data, 0) : 0;
}

/// <summary>
/// Copies closer together than the window update the preview already on screen instead of
/// announcing again, so holding Ctrl+C or a script copying in a loop doesn't make the notch flicker.
/// </summary>
public sealed class CopyCoalescer
{
    readonly TimeSpan _window;
    DateTime? _last;

    public CopyCoalescer(TimeSpan window) => _window = window;

    /// <summary>True for a fresh preview, false to update the current one in place.</summary>
    public bool ShouldAnnounce(DateTime now)
    {
        bool fresh = _last is not { } last || now - last >= _window;
        _last = now;
        return fresh;
    }
}

/// <summary>
/// Decides whether a clipboard image is a screenshot. Win+Shift+S (or Print Screen) arms it; the
/// first image afterwards counts unless the snip was cancelled with Escape. The snip itself can take
/// a while (the user drags a rectangle), so the arm lasts <see cref="ArmWindow"/> rather than a second.
/// Images placed by the Snipping Tool count regardless of the hotkey.
/// </summary>
public sealed class SnipDetector
{
    public static readonly TimeSpan ArmWindow = TimeSpan.FromSeconds(30);

    DateTime? _armedAt;

    public void Arm(DateTime now) => _armedAt = now;
    public void Disarm() => _armedAt = null;
    public bool IsArmed(DateTime now) => _armedAt is { } t && now - t >= TimeSpan.Zero && now - t <= ArmWindow;

    /// <summary>Checks (and consumes the arm for) an image that just landed on the clipboard.</summary>
    public bool IsScreenshot(DateTime now, string? ownerProcess)
    {
        bool armed = IsArmed(now);
        _armedAt = null;
        return armed || IsSnippingProcess(ownerProcess);
    }

    public static bool IsSnippingProcess(string? name) =>
        name is not null && (name.Contains("SnippingTool", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("ScreenClippingHost", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Snipping Tool both copies and auto-saves; a file and a clipboard image this close are one capture.</summary>
    public static bool IsSameCapture(DateTime a, DateTime b) => (a - b).Duration() <= TimeSpan.FromSeconds(3);
}
