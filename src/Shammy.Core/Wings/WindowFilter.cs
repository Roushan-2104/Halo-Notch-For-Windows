namespace Shammy.Core.Wings;

/// <summary>What the strip needs to know about a top-level window to decide if it's an "app window".</summary>
public sealed record WindowTraits(
    bool Visible,
    bool Cloaked,
    bool ToolWindow,
    bool AppWindow,
    bool NoActivate,
    bool HasOwner,
    int TitleLength,
    string ClassName,
    bool OwnProcess);

/// <summary>
/// The taskbar's rules for which windows get a button, so the wings show the same set: visible, not
/// cloaked (other virtual desktops, suspended Store apps, half-created frames), and either forced on with
/// WS_EX_APPWINDOW or an unowned, activatable, non-tool window. Untitled windows are left out too: they
/// are almost always invisible helpers that would show up as blank icons.
/// </summary>
public static class WindowFilter
{
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "Windows.UI.Composition.DesktopWindowContentBridge",
    };

    public static bool IsAppWindow(WindowTraits w)
    {
        if (!w.Visible || w.Cloaked || w.OwnProcess || w.TitleLength == 0) return false;
        if (ShellClasses.Contains(w.ClassName)) return false;
        if (w.AppWindow) return true;
        return !w.ToolWindow && !w.NoActivate && !w.HasOwner;
    }
}
