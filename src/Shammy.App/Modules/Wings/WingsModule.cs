using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Shammy.Core.Wings;
using Shammy.Interop;
using static Shammy.Modules.Wings.WingsNative;

namespace Shammy.Modules.Wings;

/// <summary>
/// App wings: with the Windows taskbar hidden, a black strip across the top of every monitor keeps apps in
/// reach. Pinned apps sit left of the notch, the monitor's open apps right of it, and clicks behave like
/// taskbar buttons. Each strip is a real app bar, so maximised windows start below it; turning the wings
/// off (or quitting, or crashing) gives the space back.
/// </summary>
internal sealed class WingsModule : IShammyModule, IWingsHost
{
    static readonly Regex AppIdPattern = new(@"^[A-Za-z0-9._!{}\-]{1,256}$", RegexOptions.CultureInvariant);
    static readonly HashSet<string> NeverCovering = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    ModuleContext _ctx = null!;
    WindowTracker? _tracker;
    readonly List<WingStrip> _strips = new();
    DispatcherTimer? _displayDebounce;
    int _menus;
    readonly uint _ownPid = (uint)Environment.ProcessId;

    public string Name => "wings";
    public AppVisuals Visuals { get; } = new();
    public IntPtr Foreground => _tracker?.Foreground ?? Native.GetForegroundWindow();
    public bool MenuOpen => _menus > 0;
    bool Enabled => _tracker is not null;
    List<PinnedApp> Pins => _ctx.Settings.PinnedApps;

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        WindowTracker.Logger = ctx.Info;
        ctx.AddTrayToggle("App wings (apps beside the notch)", () => WantedOn, SetEnabled);
        ctx.AddPipeCommand("wings", Command);
        ctx.AddPipeCommand("wings-state", _ => State());
        // The reserved strips must never outlive Shammy: give them back on the way down, however it goes.
        AppDomain.CurrentDomain.UnhandledException += OnFatal;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _displayDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
        {
            _displayDebounce!.Stop();
            if (!Enabled) return;
            // Same monitors at the same places and scales: only re-check each strip's spot.
            if (MonitorSignature() == _stripsSignature)
            {
                foreach (var strip in _strips) strip.Place();
            }
            else
            {
                RebuildStrips();
            }
        }, ctx.Ui) { IsEnabled = false };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // Turning the taskbar's auto-hide on or off broadcasts a settings change, but not reliably one with
        // a category we can tell apart, so a cheap poll backs it up (one message to the shell).
        _autoCheck = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => FollowAuto(), ctx.Ui);
        _lastAutoHide = TaskbarAutoHides();
        if (WantedOn) Enable();
    }

    DispatcherTimer? _autoCheck;
    bool? _lastAutoHide;

    /// <summary>The user's choice from the tray, or (until they make one) whether the taskbar auto-hides.</summary>
    bool WantedOn => WingsDefault.IsOn(_ctx.Settings.AppWings, TaskbarAutoHides());

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => _ctx.Ui.BeginInvoke(FollowAuto, DispatcherPriority.Background);

    void FollowAuto()
    {
        if (_ctx.Settings.AppWings is not null) return;
        bool want = TaskbarAutoHides();
        // Act on changes of the taskbar setting only, so wings that failed to start aren't retried every poll.
        if (want == _lastAutoHide) return;
        _lastAutoHide = want;
        if (want == Enabled) return;
        _ctx.Info($"taskbar auto-hide {(want ? "on" : "off")}: wings {(want ? "on" : "off")} (automatic)");
        if (want) Enable();
        else Disable();
    }

    void OnFatal(object? sender, UnhandledExceptionEventArgs e) => AppBar.RemoveAll();
    void OnProcessExit(object? sender, EventArgs e) => AppBar.RemoveAll();

    /// <summary>Monitors added, removed, rearranged or rescaled: start over with one strip per monitor.</summary>
    void OnDisplayChanged(object? sender, EventArgs e) => _ctx.Ui.BeginInvoke(() =>
    {
        _displayDebounce?.Stop();
        _displayDebounce?.Start();
    });

    /// <summary>An explicit choice (tray or pipe): from now on it wins over the taskbar's auto-hide.</summary>
    void SetEnabled(bool on)
    {
        _ctx.Settings.AppWings = on;
        _ctx.Settings.Save();
        if (on) Enable();
        else Disable();
    }

    void Enable()
    {
        if (Enabled) return;
        var s = _ctx.Settings;
        if (!s.WingsPinsImported)
        {
            // Once: after this the wings keep their own list and never look at the taskbar's again.
            try { PinImporter.Import(s.PinnedApps, _ctx.Info); }
            catch (Exception ex) { _ctx.Warn("importing taskbar pins failed", ex); }
            s.WingsPinsImported = true;
            s.Save();
        }
        try
        {
            _tracker = new WindowTracker(_ctx.Ui);
            _tracker.Changed += Refresh;
            _tracker.ForegroundChanged += CheckFullscreen;
            BuildStrips();
            Refresh();
            _ctx.Info($"wings on: {_strips.Count} strips, {Pins.Count} pins");
        }
        catch (Exception ex)
        {
            _ctx.Warn("starting the wings failed", ex);
            Disable();
        }
    }

    void Disable(bool placeNotch = true)
    {
        CloseStrips(placeNotch);
        _tracker?.Dispose();
        _tracker = null;
        Visuals.Clear();
        AppBar.RemoveAll();
    }

    string _stripsSignature = "";

    static string MonitorSignature() => string.Join(";", Monitors().Select(m =>
    {
        var r = Native.MonitorInfo(m).rcMonitor;
        Native.GetDpiForMonitor(m, 0, out uint dpi, out _);
        return $"{m}:{r.Left},{r.Top},{r.Right},{r.Bottom}@{dpi}";
    }));

    void BuildStrips()
    {
        _stripsSignature = MonitorSignature();
        foreach (var monitor in Monitors())
        {
            var info = Native.MonitorInfo(monitor);
            var strip = new WingStrip(this, monitor, (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
            try
            {
                strip.Start();
                _strips.Add(strip);
                if (!strip.IsReserved) _ctx.Warn("the shell didn't accept an app bar; the strip covers the top instead of reserving it");
            }
            catch (Exception ex)
            {
                _ctx.Warn("creating a strip failed", ex);
                try { strip.Close(); } catch (InvalidOperationException) { }
            }
        }
        PlaceNotch();
    }

    void CloseStrips(bool placeNotch = true)
    {
        foreach (var strip in _strips)
        {
            try { strip.Close(); }
            catch (InvalidOperationException) { } // already closed by the app shutting down
        }
        _strips.Clear();
        if (placeNotch) PlaceNotch();
    }

    void RebuildStrips()
    {
        CloseStrips();
        BuildStrips();
        Refresh();
    }

    /// <summary>On the primary screen the notch sits on its strip (between the wings), not below it.</summary>
    void PlaceNotch()
    {
        var primary = _strips.FirstOrDefault(s => s.IsPrimary);
        _ctx.Notch.StripTopPx = primary is null ? null : primary.IsReserved ? primary.Reserved.Top : primary.MonitorRect.Top;
        _ctx.Notch.Reposition();
        _ctx.Notch.BringToFront();
    }

    public void StripPlaced(WingStrip strip)
    {
        if (strip.IsPrimary && _strips.Contains(strip)) PlaceNotch();
        else _ctx.Notch.BringToFront();
    }

    void Refresh()
    {
        if (_tracker is null) return;
        var windows = _tracker.Windows;
        foreach (var strip in _strips) strip.Update(WingsModel.Build(Pins, windows, strip.Monitor.ToInt64()));
        CheckFullscreen();
    }

    void RedrawAll()
    {
        foreach (var strip in _strips) strip.Redraw();
    }

    // ---- Fullscreen -----------------------------------------------------------------------------

    void CheckFullscreen()
    {
        if (_strips.Count == 0) return;
        var fg = Native.GetForegroundWindow();
        var fgMonitor = fg == IntPtr.Zero ? IntPtr.Zero : Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
        bool exclusive = Native.SHQueryUserNotificationState(out int quns) == 0 &&
                         quns is Native.QUNS_RUNNING_D3D_FULL_SCREEN or Native.QUNS_PRESENTATION_MODE;
        foreach (var strip in _strips)
        {
            bool here = fgMonitor == strip.Monitor;
            strip.SetFullscreen(CoveredByTopWindow(strip) || exclusive && here, here);
        }
    }

    /// <summary>
    /// Whether the frontmost app window on this monitor fills it. Looking at z-order, not just the foreground
    /// window, catches a video playing fullscreen on one screen while you work on another.
    /// </summary>
    bool CoveredByTopWindow(WingStrip strip)
    {
        var m = strip.MonitorRect;
        int n = 0;
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && n++ < 1000; h = GetWindow(h, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(h) || IsIconic(h)) continue;
            if (Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST) != strip.Monitor) continue;
            if (!WindowTracker.IsAppWindow(h, _ownPid) || NeverCovering.Contains(Native.ClassName(h))) continue;
            if (!Native.GetWindowRect(h, out var r)) continue;
            long style = Native.GetWindowLongPtr(h, GWL_STYLE).ToInt64();
            bool maxCaption = Native.IsZoomed(h) && (style & WS_CAPTION) == WS_CAPTION;
            return StripFullscreen.Covers(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom, maxCaption);
        }
        return false;
    }

    // ---- Clicks ---------------------------------------------------------------------------------

    public void Click(WingItem item, FrameworkElement anchor, bool newInstance) => Act(item, anchor, newInstance);

    /// <summary>Does what a taskbar button would; returns what it did (for the test pipe).</summary>
    string Act(WingItem item, FrameworkElement anchor, bool newInstance)
    {
        var fg = Native.GetForegroundWindow();
        var states = item.Windows.Select(w => new WingWindowState(w.Handle, IsIconic(new IntPtr(w.Handle)))).ToList();
        var launch = LaunchTarget(item);
        var (action, target) = WingClick.Decide(states, fg.ToInt64(), launch is not null, newInstance);
        switch (action)
        {
            case WingAction.Launch: Launch(item, launch!); break;
            case WingAction.Activate: Activate(new IntPtr(target)); break;
            case WingAction.Minimize: Minimize(new IntPtr(target)); break;
            case WingAction.ShowList: ShowWindowList(item, anchor, fg); break;
        }
        return action.ToString();
    }

    static string? LaunchTarget(WingItem item)
    {
        var target = item.Pin?.Launch ?? item.Windows.Select(w => w.Launch).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    void Launch(WingItem item, string target)
    {
        const string apps = "shell:AppsFolder\\";
        bool ok;
        if (target.StartsWith(apps, StringComparison.OrdinalIgnoreCase))
        {
            // App IDs come from other apps' windows and the registry: an ID and nothing that could smuggle arguments.
            ok = AppIdPattern.IsMatch(target[apps.Length..]) && _ctx.Open(target);
        }
        else
        {
            ok = Path.IsPathRooted(target) && File.Exists(target) && _ctx.Open(target);
        }
        if (!ok) _ctx.ShowMessage("", $"Couldn't open {Visuals.Name(item)}", error: true);
        _tracker?.RefreshSoon();
    }

    void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
        Native.ForceForeground(hwnd);
        _tracker?.RefreshSoon();
    }

    void Minimize(IntPtr hwnd)
    {
        // The app's own minimise path, so apps that hide to the tray on minimise still do.
        PostMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MINIMIZE), IntPtr.Zero);
        _tracker?.RefreshSoon();
    }

    /// <summary>A drag rested on an open app's icon: bring its latest window forward to drop into.</summary>
    public void DragActivate(WingItem item)
    {
        if (WingDrag.Target(item.Windows) is { } hwnd && IsWindow(new IntPtr(hwnd))) Activate(new IntPtr(hwnd));
    }

    void CloseWindows(WingItem item)
    {
        foreach (var w in item.Windows) PostMessage(new IntPtr(w.Handle), WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);
        _tracker?.RefreshSoon();
    }

    // ---- Menus ----------------------------------------------------------------------------------

    ContextMenu NewMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 2,
        };
        menu.Opened += (_, _) => _menus++;
        menu.Closed += (_, _) =>
        {
            _menus = Math.Max(0, _menus - 1);
            // Redraws were held while the menu was open.
            _ctx.Ui.BeginInvoke(RedrawAll, DispatcherPriority.Background);
        };
        return menu;
    }

    /// <summary>Menu actions run once the menu has closed, so focus goes where the action sends it.</summary>
    MenuItem Item(string text, ImageSource? icon, Action onClick)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is not null)
        {
            var img = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            header.Children.Add(img);
        }
        header.Children.Add(new TextBlock { Text = text, MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => _ctx.Ui.BeginInvoke(onClick, DispatcherPriority.Input);
        return mi;
    }

    void ShowWindowList(WingItem item, FrameworkElement anchor, IntPtr foregroundAtClick)
    {
        var menu = NewMenu(anchor);
        foreach (var w in item.Windows)
        {
            var hwnd = new IntPtr(w.Handle);
            var title = WindowTitle(hwnd);
            menu.Items.Add(Item(title.Length > 0 ? title : w.Title, Visuals.WindowIconOrApp(w, item), () =>
            {
                if (!IsWindow(hwnd)) return;
                var action = WingClick.ForWindow(new WingWindowState(w.Handle, IsIconic(hwnd)), foregroundAtClick.ToInt64());
                if (action == WingAction.Minimize) Minimize(hwnd);
                else Activate(hwnd);
            }));
        }
        menu.IsOpen = true;
    }

    public void ShowMenu(WingItem item, FrameworkElement anchor)
    {
        var menu = NewMenu(anchor);
        var launch = LaunchTarget(item);
        string name = Visuals.Name(item);
        if (launch is not null)
            menu.Items.Add(Item(item.IsRunning ? "New window" : $"Open {name}", Visuals.Icon(item), () => Launch(item, launch)));
        if (item.Pin is not null) menu.Items.Add(Item("Unpin from wings", null, () => Unpin(item)));
        else if (launch is not null) menu.Items.Add(Item("Pin to wings", null, () => Pin(item)));
        if (item.IsRunning)
            menu.Items.Add(Item(item.Windows.Count == 1 ? "Close window" : $"Close all {item.Windows.Count} windows", null, () => CloseWindows(item)));
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    public void ShowOverflow(IReadOnlyList<WingItem> items, FrameworkElement anchor)
    {
        var menu = NewMenu(anchor);
        foreach (var item in items)
        {
            var label = Visuals.Name(item) + (item.Windows.Count > 1 ? $"  ({item.Windows.Count})" : "");
            menu.Items.Add(Item(label, Visuals.Icon(item), () => Act(item, anchor, newInstance: false)));
        }
        menu.IsOpen = true;
    }

    // ---- Pins -----------------------------------------------------------------------------------

    void Pin(WingItem item)
    {
        var w = item.Windows.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Launch));
        if (w is null) return;
        var app = new PinnedApp { Name = Visuals.Name(item), Launch = w.Launch!, Aumid = w.Aumid, ExePath = w.ExePath };
        if (!PinList.Pin(Pins, app)) return;
        _ctx.Settings.Save();
        Refresh();
    }

    void Unpin(WingItem item)
    {
        var removed = PinList.Unpin(Pins, item.Key);
        if (removed is null) return;
        PinImporter.DeleteCopy(removed);
        _ctx.Settings.Save();
        Refresh();
    }

    public void MovePin(int from, int to)
    {
        if (!PinList.Move(Pins, from, to)) return;
        _ctx.Settings.Save();
        Refresh();
    }

    // ---- Test pipe ------------------------------------------------------------------------------

    /// <summary>"wings on|off|auto|import|refresh|rebuild|click &lt;name&gt;".</summary>
    string Command(string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length == 0 ? "" : parts[0].ToLowerInvariant())
        {
            case "on":
                SetEnabled(true);
                return Enabled ? "on" : "error: didn't start (see log)";
            case "off":
                SetEnabled(false);
                return "off";
            case "auto":
                // Forget the explicit choice: follow the taskbar's auto-hide again.
                _ctx.Settings.AppWings = null;
                _ctx.Settings.Save();
                _lastAutoHide = null;
                FollowAuto();
                return WingsDefault.Describe(null, TaskbarAutoHides()) + (Enabled ? " on" : " off");
            case "import":
            {
                int added = PinImporter.Import(Pins, _ctx.Info);
                _ctx.Settings.WingsPinsImported = true;
                _ctx.Settings.Save();
                Refresh();
                return $"imported {added}";
            }
            case "refresh":
                _tracker?.Refresh();
                return "ok";
            case "rebuild":
                if (Enabled) RebuildStrips();
                return "ok";
            case "click" when parts.Length > 1:
            {
                var strip = _strips.FirstOrDefault(s => s.IsPrimary) ?? _strips.FirstOrDefault();
                if (strip is null) return "error: wings are off";
                var item = strip.Items.Pinned.Concat(strip.Items.Open)
                    .FirstOrDefault(i => Visuals.Name(i).Contains(parts[1].Trim(), StringComparison.OrdinalIgnoreCase));
                return item is null ? "error: no such app" : Act(item, strip, newInstance: false);
            }
            default:
                return "usage: wings on|off|auto|import|refresh|rebuild|click <name>";
        }
    }

    string State()
    {
        static string R(Native.RECT r) => $"{r.Left},{r.Top},{r.Right},{r.Bottom}";
        var sb = new StringBuilder();
        bool autoHide = TaskbarAutoHides();
        sb.Append($"enabled={Enabled} setting={WingsDefault.Describe(_ctx.Settings.AppWings, autoHide)} taskbarAutoHide={autoHide} imported={_ctx.Settings.WingsPinsImported} pins={Pins.Count} " +
                  $"strips={_strips.Count} appbars={AppBar.Count} windows={_tracker?.Windows.Count ?? 0} notchTop={_ctx.Notch.StripTopPx?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        sb.Append(" pinned=[").Append(string.Join(", ", Pins.Select(p => p.Name))).Append(']');
        int i = 0;
        foreach (var s in _strips)
        {
            var info = Native.MonitorInfo(s.Monitor);
            string Names(IEnumerable<WingItem> items) => string.Join(", ", items.Select(it =>
                Visuals.Name(it) + (it.Windows.Count > 1 ? $"({it.Windows.Count})" : "") + (it.Pin is not null && it.IsRunning ? "*" : "") + (it.IsFlashing ? "!" : "")));
            var l = s.Layout;
            sb.Append($" | strip{i++} primary={s.IsPrimary} monitor={R(info.rcMonitor)} work={R(info.rcWork)} reserved={(s.IsReserved ? R(s.Reserved) : "no")} " +
                      $"dpi={s.Dpi} visible={s.IsVisible} fullscreen={s.IsHiddenForFullscreen} " +
                      $"left=[{Names(s.Items.Pinned)}] leftShown={l?.Left.Shown ?? 0}+{l?.Left.Overflow ?? 0} " +
                      $"right=[{Names(s.Items.Open)}] rightShown={l?.Right.Shown ?? 0}+{l?.Right.Overflow ?? 0}");
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _autoCheck?.Stop();
        _displayDebounce?.Stop();
        // Shammy is quitting: the notch is going too, so only the strips and their reserved space matter.
        Disable(placeNotch: false);
        AppDomain.CurrentDomain.UnhandledException -= OnFatal;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
