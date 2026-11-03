using System.Windows.Interop;
using System.Windows.Threading;
using Shammy.Core.Wings;
using Shammy.Interop;
using static Shammy.Modules.Wings.WingsNative;

namespace Shammy.Modules.Wings;

/// <summary>
/// The set of app windows the taskbar would show, kept current from the shell hook (created, destroyed,
/// activated, flashing), WinEvents (foreground, cloaking, a window dragged to another screen) and a slow
/// poll for anything those miss (title changes, apps that never notify). Every change re-enumerates the
/// top-level windows, debounced, which takes a few milliseconds.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    static readonly TimeSpan AppIdTtl = TimeSpan.FromSeconds(10);

    readonly Dispatcher _ui;
    readonly HwndSource _hookWindow;
    readonly uint _shellMessage;
    readonly Native.WinEventProc _winEvent;
    readonly List<IntPtr> _hooks = new();
    readonly DispatcherTimer _poll, _debounce;
    readonly uint _ownPid = (uint)Environment.ProcessId;

    readonly Dictionary<IntPtr, long> _firstSeen = new();
    readonly HashSet<IntPtr> _flashing = new();
    readonly Dictionary<uint, (string? Path, string? PackagedId)> _processes = new();
    readonly Dictionary<IntPtr, (string? Id, DateTime At)> _appIds = new();
    long _sequence;
    bool _disposed;

    public WindowTracker(Dispatcher ui)
    {
        _ui = ui;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Background, (_, _) => Refresh(), ui) { IsEnabled = false };
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Background, (_, _) => Refresh(), ui);

        // A hidden top-level window: shell hook messages aren't delivered to message-only windows.
        _hookWindow = new HwndSource(new HwndSourceParameters("Shammy wings shell hook")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
        });
        _shellMessage = RegisterWindowMessage("SHELLHOOK");
        _hookWindow.AddHook(OnHookMessage);
        if (!RegisterShellHookWindow(_hookWindow.Handle)) Log("shell hook unavailable; using the poll only");

        _winEvent = OnWinEvent;
        uint flags = Native.WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        foreach (var (min, max) in new[]
                 {
                     (Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND),
                     (EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND),
                     (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
                 })
        {
            var hook = Native.SetWinEventHook(min, max, IntPtr.Zero, _winEvent, 0, 0, flags);
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }

        Refresh();
        _poll.Start();
    }

    /// <summary>The current app windows, in first-seen order.</summary>
    public IReadOnlyList<WindowInfo> Windows { get; private set; } = Array.Empty<WindowInfo>();

    public IntPtr Foreground { get; private set; }

    /// <summary>Raised after every re-enumeration (the strips skip redrawing when nothing they show changed).</summary>
    public event Action? Changed;

    /// <summary>Raised when the foreground window changes, before the debounced refresh.</summary>
    public event Action? ForegroundChanged;

    public static Action<string>? Logger { get; set; }
    static void Log(string message) => Logger?.Invoke(message);

    public void RefreshSoon()
    {
        if (_disposed) return;
        _debounce.Stop();
        _debounce.Start();
    }

    IntPtr OnHookMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != (int)_shellMessage || _shellMessage == 0) return IntPtr.Zero;
        int code = (int)(wParam.ToInt64() & 0xFFFFFFFF);
        switch (code)
        {
            case HSHELL_FLASH:
                // Lit until the user looks at it, like the taskbar's orange button.
                if (lParam != IntPtr.Zero && lParam != Foreground) _flashing.Add(lParam);
                break;
            case HSHELL_WINDOWACTIVATED or HSHELL_RUDEAPPACTIVATED:
                _flashing.Remove(lParam);
                break;
            case HSHELL_WINDOWDESTROYED:
                _flashing.Remove(lParam);
                _appIds.Remove(lParam);
                break;
            case HSHELL_WINDOWCREATED or HSHELL_REDRAW or HSHELL_MONITORCHANGED:
                break;
            default:
                return IntPtr.Zero;
        }
        RefreshSoon();
        return IntPtr.Zero;
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed || idObject != 0) return;
        if (evt == Native.EVENT_SYSTEM_FOREGROUND)
        {
            Foreground = Native.GetForegroundWindow();
            _flashing.Remove(Foreground);
            ForegroundChanged?.Invoke();
        }
        RefreshSoon();
    }

    public void Refresh()
    {
        if (_disposed) return;
        _debounce.Stop();
        try
        {
            Foreground = Native.GetForegroundWindow();
            var found = new List<IntPtr>();
            EnumWindowsProc proc = (hwnd, _) =>
            {
                if (IsAppWindow(hwnd, _ownPid)) found.Add(hwnd);
                return true;
            };
            EnumWindows(proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            var alive = found.ToHashSet();
            foreach (var gone in _firstSeen.Keys.Where(h => !alive.Contains(h)).ToList()) _firstSeen.Remove(gone);
            _flashing.RemoveWhere(h => !alive.Contains(h));
            foreach (var gone in _appIds.Keys.Where(h => !alive.Contains(h)).ToList()) _appIds.Remove(gone);

            var pids = new HashSet<uint>();
            var windows = new List<WindowInfo>(found.Count);
            // EnumWindows goes top to bottom in z-order; number new windows bottom-up so that at startup the
            // oldest-looking (furthest back) apps come first, roughly like the taskbar's own order.
            for (int i = found.Count - 1; i >= 0; i--)
            {
                var hwnd = found[i];
                if (!_firstSeen.ContainsKey(hwnd)) _firstSeen[hwnd] = ++_sequence;
            }
            foreach (var hwnd in found)
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                pids.Add(pid);
                if (!_processes.TryGetValue(pid, out var proc2)) _processes[pid] = proc2 = ProcessInfo(pid);
                string? appId = AppIdFor(hwnd) ?? proc2.PackagedId;
                bool packaged = proc2.PackagedId is not null || Native.ClassName(hwnd) == "ApplicationFrameWindow" && appId is not null;
                string? launch = packaged && appId is not null ? "shell:AppsFolder\\" + appId : proc2.Path;
                windows.Add(new WindowInfo(
                    hwnd.ToInt64(),
                    WindowTitle(hwnd),
                    appId,
                    proc2.Path,
                    Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST).ToInt64(),
                    _firstSeen[hwnd],
                    launch,
                    _flashing.Contains(hwnd)));
            }
            foreach (var gone in _processes.Keys.Where(p => !pids.Contains(p)).ToList()) _processes.Remove(gone);
            Windows = windows.OrderBy(w => w.Order).ToList();
        }
        catch (Exception ex)
        {
            Log("enumerating windows failed: " + ex.Message);
        }
        Changed?.Invoke();
    }

    /// <summary>Re-read now and then: Chrome and others set their app ID a moment after the window appears.</summary>
    string? AppIdFor(IntPtr hwnd)
    {
        var now = DateTime.UtcNow;
        if (_appIds.TryGetValue(hwnd, out var cached) && now - cached.At < AppIdTtl) return cached.Id;
        var id = WindowAppId(hwnd);
        _appIds[hwnd] = (id, now);
        return id;
    }

    /// <summary>The taskbar's rules (see <see cref="WindowFilter"/>) applied to a live window.</summary>
    internal static bool IsAppWindow(IntPtr hwnd, uint ownPid)
    {
        bool visible = IsWindowVisible(hwnd);
        if (!visible) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        return WindowFilter.IsAppWindow(new WindowTraits(
            Visible: true,
            Cloaked: IsCloaked(hwnd),
            ToolWindow: (ex & Native.WS_EX_TOOLWINDOW) != 0,
            AppWindow: (ex & WS_EX_APPWINDOW) != 0,
            NoActivate: (ex & Native.WS_EX_NOACTIVATE) != 0,
            HasOwner: GetWindow(hwnd, GW_OWNER) != IntPtr.Zero,
            TitleLength: GetWindowTextLength(hwnd),
            ClassName: Native.ClassName(hwnd),
            OwnProcess: pid == ownPid));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll.Stop();
        _debounce.Stop();
        foreach (var h in _hooks) Native.UnhookWinEvent(h);
        _hooks.Clear();
        DeregisterShellHookWindow(_hookWindow.Handle);
        _hookWindow.RemoveHook(OnHookMessage);
        _hookWindow.Dispose();
    }
}
