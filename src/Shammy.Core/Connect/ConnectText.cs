using System.Globalization;

namespace Shammy.Core.Connect;

/// <summary>Small texts for the features that link modules together.</summary>
public static class ConnectText
{
    /// <summary>
    /// A download's progress: "42%" when the final size is known (and the temp file hasn't outgrown it),
    /// otherwise the bytes so far ("12.4 MB").
    /// </summary>
    public static string DownloadProgress(long bytes, long? total)
    {
        if (total is > 0 && bytes >= 0 && bytes <= total)
            return ((int)Math.Floor(bytes * 100.0 / total.Value)).ToString(CultureInfo.InvariantCulture) + "%";
        return DropActions.DropPlan.Size(Math.Max(0, bytes));
    }

    /// <summary>"Remind me in 1 hour" on a notification: "WhatsApp · Mom: are you coming?" (one line, trimmed).</summary>
    public static string NotificationReminder(string appName, string title, string body, int max = 120)
    {
        var app = (appName ?? "").Trim();
        var line = Flatten(title);
        var rest = Flatten(body);
        if (rest.Length > 0) line = line.Length > 0 ? $"{line}: {rest}" : rest;
        var text = app.Length > 0 && line.Length > 0 ? $"{app} · {line}" : app.Length > 0 ? app : line;
        if (text.Length == 0) text = "Notification";
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>A copied text or answer kept as a note: trimmed, one paragraph at most a few hundred characters.</summary>
    public static string? NoteFrom(string? text, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }

    static string Flatten(string? s) =>
        string.Join(" ", (s ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
