namespace Shammy.Core.Wings;

public enum WingAction { None, Launch, Activate, Minimize, ShowList }

/// <summary>A window's state at the moment of a click.</summary>
public sealed record WingWindowState(long Handle, bool Minimized);

/// <summary>
/// What clicking a wing icon does, like the taskbar: start a pin that isn't running, bring a lone window
/// forward or (if it's already in front) minimise it, and offer a list when the app has several windows.
/// Middle-click or Shift+click always starts another instance.
/// </summary>
public static class WingClick
{
    public static (WingAction Action, long Target) Decide(IReadOnlyList<WingWindowState> windows, long foreground,
        bool canLaunch, bool newInstance = false)
    {
        if (newInstance) return canLaunch ? (WingAction.Launch, 0) : (WingAction.None, 0);
        if (windows.Count == 0) return canLaunch ? (WingAction.Launch, 0) : (WingAction.None, 0);
        if (windows.Count > 1) return (WingAction.ShowList, 0);
        return (ForWindow(windows[0], foreground), windows[0].Handle);
    }

    /// <summary>A single window (an app's only one, or one picked from the list).</summary>
    public static WingAction ForWindow(WingWindowState w, long foreground) =>
        w.Handle == foreground && !w.Minimized ? WingAction.Minimize : WingAction.Activate;
}

/// <summary>Dragging a file over an icon, like the taskbar: after a short hover the app comes forward to drop into.</summary>
public static class WingDrag
{
    public const double HoverSeconds = 0.7;

    /// <summary>
    /// The window to bring forward: the one used most recently (else the newest), or null for a pin that
    /// isn't running, which does nothing on drag-over.
    /// </summary>
    public static long? Target(IReadOnlyList<WindowInfo> windows) =>
        windows.Count == 0 ? null : windows.OrderByDescending(w => w.LastActive).ThenByDescending(w => w.Order).First().Handle;
}

/// <summary>Whether the wings are on: the user's explicit choice, else follow the taskbar's auto-hide.</summary>
public static class WingsDefault
{
    public static bool IsOn(bool? explicitChoice, bool taskbarAutoHides) => explicitChoice ?? taskbarAutoHides;

    public static string Describe(bool? explicitChoice, bool taskbarAutoHides) =>
        explicitChoice is { } on ? $"explicit({(on ? "on" : "off")})" : $"auto({(taskbarAutoHides ? "on" : "off")})";
}

/// <summary>When a strip should get out of the way of a fullscreen game, video or presentation.</summary>
public static class StripFullscreen
{
    /// <summary>
    /// A window covers the whole monitor. A maximised window with a title bar overhangs it by its resize
    /// border without being fullscreen; games, videos and F11 browsers drop the caption.
    /// </summary>
    public static bool Covers(int left, int top, int right, int bottom, int monLeft, int monTop, int monRight, int monBottom,
        bool maximizedWithCaption) =>
        !maximizedWithCaption && left <= monLeft && top <= monTop && right >= monRight && bottom >= monBottom;
}
