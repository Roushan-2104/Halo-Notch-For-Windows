namespace Shammy.Core.Notifications;

/// <summary>
/// What a notification shows. During a call or screen capture others may see the notch, so then only the
/// app (and, in the list, the time) is shown, never what the message says.
/// </summary>
public static class NotificationText
{
    /// <summary>The pop-up's second line while private.</summary>
    public const string PopupHidden = "Hidden while you're in a call or sharing your screen";

    /// <summary>A Notifications-tab row's second line while private.</summary>
    public const string ListHidden = "Message hidden during call or screen share";

    /// <summary>
    /// A Notifications-tab row: the bold first line and the "App · body" line under it. Multi-line bodies
    /// are joined into one line, since the row has room for one.
    /// </summary>
    public static (string Title, string Detail) ForList(string appName, string title, string body, bool presenting)
    {
        if (presenting) return (appName, ListHidden);
        var flat = string.Join(" ", (body ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return (title, flat.Length > 0 ? $"{appName} · {flat}" : appName);
    }

    /// <summary>The pop-up's title and body.</summary>
    public static (string Title, string Body) ForPopup(string appName, string title, string body, bool presenting) =>
        presenting ? (appName, PopupHidden) : (title, body);
}
