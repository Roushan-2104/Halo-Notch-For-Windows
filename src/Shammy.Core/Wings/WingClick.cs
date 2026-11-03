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
