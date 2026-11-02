using System.Diagnostics;
using System.Windows.Threading;
using Halo.Interop;

namespace Halo.Shell;

/// <summary>
/// Detects games, videos and presentations taking over the primary monitor so the notch can get out
/// of the way. Reacts instantly to foreground changes and re-checks on a slow timer for in-place
/// switches like F11 in a browser.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow" };

    readonly Native.WinEventProc _proc;
    readonly IntPtr _hook;
    readonly DispatcherTimer _timer;
    readonly uint _pid = (uint)Environment.ProcessId;
    bool _state;

    public FullscreenWatcher(Dispatcher ui)
    {
        _proc = (_, _, _, _, _, _, _) => ui.BeginInvoke(Check);
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Background, (_, _) => Check(), ui);
        _timer.Start();
    }

    public event Action<bool>? Changed;

    /// <summary>Raised on every foreground change, so the notch can re-assert topmost.</summary>
    public event Action? ForegroundChanged;

    public void Check()
    {
        bool now = Evaluate();
        ForegroundChanged?.Invoke();
        if (now == _state) return;
        _state = now;
        Changed?.Invoke(now);
    }

    bool Evaluate()
    {
        if (Native.SHQueryUserNotificationState(out int quns) == 0 &&
            quns is Native.QUNS_RUNNING_D3D_FULL_SCREEN or Native.QUNS_PRESENTATION_MODE)
            return true;

        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        // Our own windows are never a fullscreen app (the freeze overlay never takes focus, and when
        // Windows hands focus to one of Halo's background windows the fullscreen app is gone).
        if (pid == _pid) return false;
        if (ShellClasses.Contains(Native.ClassName(fg))) return false;

        var primary = Native.PrimaryMonitor();
        if (Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST) != primary) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var m = Native.MonitorBounds(primary);
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
    }
}
