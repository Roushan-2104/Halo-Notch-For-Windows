using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Wings;
using Shammy.Interop;

namespace Shammy.Modules.Wings;

/// <summary>What a strip asks of the wings module.</summary>
internal interface IWingsHost
{
    AppVisuals Visuals { get; }
    IntPtr Foreground { get; }
    bool MenuOpen { get; }
    void Click(WingItem item, FrameworkElement anchor, bool newInstance);
    void ShowMenu(WingItem item, FrameworkElement anchor);
    void ShowOverflow(IReadOnlyList<WingItem> items, FrameworkElement anchor);
    void MovePin(int from, int to);
    void StripPlaced(WingStrip strip);
}

/// <summary>
/// One monitor's black strip along the top edge, reserved as an app bar: pinned apps on the left of the
/// centre, that monitor's open apps on the right. On the primary screen the notch (its own topmost window,
/// kept above the strip) sits in the gap in the middle; the strip never draws there, so the notch's hover,
/// drops and fullscreen hiding are untouched. It never takes focus, so "click again to minimise" can see
/// which window was in front.
/// </summary>
internal sealed class WingStrip : Window
{
    const int CallbackMessage = 0x8000 + 0x5A; // WM_APP + n, private to this window
    const double DragThreshold = 5;

    readonly IWingsHost _host;
    readonly Canvas _canvas = new() { Background = Brushes.Black, ClipToBounds = true };
    AppBar? _bar;
    IntPtr _hwnd;
    bool _closed, _covered, _shellFullscreen, _foregroundHere;
    WingsContent _content = new(Array.Empty<WingItem>(), Array.Empty<WingItem>());
    WingLayout? _layout;
    string _drawn = "";

    // Pressed icon (and, once it moves far enough, the pin being dragged).
    FrameworkElement? _pressed;
    WingItem? _pressedItem;
    int _pressedPin = -1;
    double _pressX, _pressLeft;
    bool _dragging;

    public WingStrip(IWingsHost host, IntPtr monitor, bool primary)
    {
        _host = host;
        Monitor = monitor;
        IsPrimary = primary;
        Title = "Shammy wings";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.Resources["ShammyFont"];
        Foreground = (Brush)Application.Current.Resources["Fg"];
        Content = _canvas;
        SizeChanged += (_, _) => Render(force: true);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
    }

    public IntPtr Monitor { get; }
    public bool IsPrimary { get; }
    public bool IsReserved => _bar?.IsRegistered == true;
    public Native.RECT Reserved => _bar?.Reserved ?? default;
    public Native.RECT MonitorRect { get; private set; }
    public uint Dpi { get; private set; } = 96;
    public bool IsHiddenForFullscreen => _covered || (_shellFullscreen && _foregroundHere);
    public WingsContent Items => _content;
    public WingLayout? Layout => _layout;

    /// <summary>True while the user is pressing, dragging or has a menu open: redrawing would yank it away.</summary>
    bool Interacting => _pressed is not null || _host.MenuOpen;

    /// <summary>Creates the window, registers the app bar and moves into place (before showing, so nothing flashes).</summary>
    public void Start()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        WingsNative.SquareCorners(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        _bar = new AppBar(_hwnd, CallbackMessage);
        _bar.Register();
        Place();
        Show();
        // Showing a topmost window puts it above every other one, the notch included; put the notch back on top.
        _host.StripPlaced(this);
    }

    /// <summary>Reserve the strip at this monitor's current scale and put the window exactly on it.</summary>
    public void Place()
    {
        if (_closed || _hwnd == IntPtr.Zero) return;
        var info = Native.MonitorInfo(Monitor);
        if (info.rcMonitor.Width <= 0) return; // the monitor went away; the module rebuilds the strips
        MonitorRect = info.rcMonitor;
        Dpi = Native.GetDpiForMonitor(Monitor, 0, out uint dpi, out _) == 0 && dpi != 0 ? dpi : 96;
        int height = WingLayoutMath.StripHeightPx(Dpi);
        _bar?.Reserve(info.rcMonitor, height);
        var r = IsReserved
            ? _bar!.Reserved
            : new Native.RECT { Left = info.rcMonitor.Left, Top = info.rcMonitor.Top, Right = info.rcMonitor.Right, Bottom = info.rcMonitor.Top + height };
        Native.GetWindowRect(_hwnd, out var now);
        if (now.Left != r.Left || now.Top != r.Top || now.Right != r.Right || now.Bottom != r.Bottom)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE);
        _host.StripPlaced(this);
        Render(force: true);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            case (int)WingsNative.WM_WINDOWPOSCHANGED:
                _bar?.WindowPosChanged();
                break;
            case CallbackMessage:
                switch ((int)wParam.ToInt64())
                {
                    case WingsNative.ABN_POSCHANGED:
                        // Another app bar (the taskbar, or our strip on another screen) moved: check our spot.
                        Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
                        break;
                    case WingsNative.ABN_FULLSCREENAPP:
                        _shellFullscreen = lParam != IntPtr.Zero;
                        UpdateVisibility();
                        break;
                }
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// <paramref name="covered"/>: a window fills this monitor; <paramref name="foregroundHere"/>: the
    /// foreground window is on it (the shell's own fullscreen notice doesn't say which monitor).
    /// </summary>
    public void SetFullscreen(bool covered, bool foregroundHere)
    {
        _covered = covered;
        _foregroundHere = foregroundHere;
        UpdateVisibility();
    }

    void UpdateVisibility()
    {
        if (_closed) return;
        bool hide = IsHiddenForFullscreen;
        if (hide && IsVisible) Hide();
        else if (!hide && !IsVisible)
        {
            Show();
            if (_hwnd != IntPtr.Zero) Native.BringToTopmost(_hwnd);
            _host.StripPlaced(this);
        }
    }

    // ---- Drawing --------------------------------------------------------------------------------

    public void Update(WingsContent content)
    {
        _content = content;
        Render(force: false);
    }

    void Render(bool force)
    {
        if (_closed || Interacting) return;
        double width = ActualWidth;
        if (width < 50) return;
        var fg = _host.Foreground.ToInt64();
        var sig = Signature(width, fg);
        if (!force && sig == _drawn) return;
        _drawn = sig;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double center = ((MonitorRect.Left + MonitorRect.Right) / 2.0 - Reserved.Left) / (scale <= 0 ? 1 : scale);
        if (!IsReserved) center = width / 2;
        double gap = IsPrimary ? WingLayoutMath.NotchGap : WingLayoutMath.SeamGap;
        var layout = WingLayoutMath.Compute(width, center, gap, _content.Pinned.Count, _content.Open.Count);
        _layout = layout;

        _canvas.Children.Clear();
        for (int i = 0; i < layout.Left.Shown; i++) Add(BuildItem(_content.Pinned[i], i, fg), layout.Left.ItemX[i]);
        if (layout.Left.MoreX is { } lm) Add(BuildMore(_content.Pinned.Skip(layout.Left.Shown).ToList()), lm);
        for (int i = 0; i < layout.Right.Shown; i++) Add(BuildItem(_content.Open[i], -1, fg), layout.Right.ItemX[i]);
        if (layout.Right.MoreX is { } rm) Add(BuildMore(_content.Open.Skip(layout.Right.Shown).ToList()), rm);
    }

    void Add(FrameworkElement el, double x)
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, 0);
        _canvas.Children.Add(el);
    }

    string Signature(double width, long fg)
    {
        var sb = new StringBuilder();
        sb.Append(width.ToString("0")).Append('|');
        foreach (var item in _content.Pinned.Concat(_content.Open))
            sb.Append(item.Key).Append(':').Append(item.Windows.Count).Append(item.IsFlashing ? "!" : "")
              .Append(item.Contains(fg) ? "*" : "").Append(';');
        return sb.ToString();
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    static readonly Brush FlashFill = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xB0, 0x20)));
    static readonly Brush FlashDot = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)));

    static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    FrameworkElement BuildItem(WingItem item, int pinIndex, long fg)
    {
        bool active = item.Contains(fg), flashing = item.IsFlashing;
        var grid = new Grid
        {
            Width = WingLayoutMath.Slot,
            Height = WingLayoutMath.StripHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Brush rest = flashing ? FlashFill : Brushes.Transparent;
        var hover = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0),
            Background = rest,
        };
        grid.Children.Add(hover);

        var icon = _host.Visuals.Icon(item);
        if (icon is not null)
        {
            var img = new Image
            {
                Source = icon,
                Width = WingLayoutMath.IconSize,
                Height = WingLayoutMath.IconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 0, 0),
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            grid.Children.Add(img);
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text = "î²ª",
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 18,
                Foreground = Res("Fg2"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        if (item.IsRunning)
        {
            grid.Children.Add(new Border
            {
                Width = active ? 10 : 4,
                Height = active ? 3 : 4,
                CornerRadius = new CornerRadius(2),
                Background = flashing ? FlashDot : active ? Res("AccentBrush") : Res("Fg2"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, active ? 2 : 1.5),
            });
        }

        string name = _host.Visuals.Name(item);
        grid.ToolTip = item.Windows.Count > 1 ? $"{name} Â· {item.Windows.Count} windows" : name;

        grid.MouseEnter += (_, _) => { if (!flashing) hover.Background = Res("Fill1"); };
        grid.MouseLeave += (_, _) => hover.Background = rest;
        grid.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = grid;
            _pressedItem = item;
            _pressedPin = pinIndex;
            _pressX = e.GetPosition(_canvas).X;
            _pressLeft = Canvas.GetLeft(grid);
            _dragging = false;
            grid.CaptureMouse();
            e.Handled = true;
        };
        grid.MouseMove += (_, e) =>
        {
            if (_pressed != grid || _pressedPin < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            double dx = e.GetPosition(_canvas).X - _pressX;
            if (!_dragging && Math.Abs(dx) < DragThreshold) return;
            _dragging = true;
            Panel.SetZIndex(grid, 10);
            grid.Opacity = 0.85;
            Canvas.SetLeft(grid, _pressLeft + dx);
        };
        grid.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressed != grid) return;
            bool dragged = _dragging;
            double left = Canvas.GetLeft(grid);
            EndPress();
            e.Handled = true;
            if (dragged && _layout is { } l)
            {
                int to = WingLayoutMath.DropIndex(left + WingLayoutMath.Slot / 2, l.Left.ItemX);
                if (to != pinIndex) _host.MovePin(pinIndex, to);
                Render(force: true);
            }
            else
            {
                bool shift = Native.GetAsyncKeyState(0x10) < 0;
                _host.Click(item, grid, newInstance: shift);
            }
        };
        grid.LostMouseCapture += (_, _) =>
        {
            if (_pressed != grid) return;
            bool dragged = _dragging;
            EndPress();
            if (dragged) Render(force: true);
        };
        grid.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            _host.Click(item, grid, newInstance: true);
        };
        grid.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _host.ShowMenu(item, grid);
        };
        return grid;
    }

    void EndPress()
    {
        var pressed = _pressed;
        _pressed = null;
        _pressedItem = null;
        _dragging = false;
        if (pressed is not null && pressed.IsMouseCaptured) pressed.ReleaseMouseCapture();
    }

    FrameworkElement BuildMore(IReadOnlyList<WingItem> hidden)
    {
        var bg = new Border
        {
            Width = 30,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 0, 0),
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = "+" + hidden.Count,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("Fg2"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var grid = new Grid
        {
            Width = WingLayoutMath.Slot,
            Height = WingLayoutMath.StripHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = $"{hidden.Count} more",
        };
        grid.Children.Add(bg);
        if (hidden.Any(h => h.IsFlashing)) bg.Background = FlashFill;
        grid.MouseEnter += (_, _) => bg.Background = Res("Fill1");
        grid.MouseLeave += (_, _) => bg.Background = hidden.Any(h => h.IsFlashing) ? FlashFill : Brushes.Transparent;
        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _host.ShowOverflow(hidden, grid);
        };
        return grid;
    }

    /// <summary>Redraw now (after a menu closed, an icon arrived, a pin moved).</summary>
    public void Redraw() => Render(force: true);

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _bar?.Remove();
        base.OnClosed(e);
    }
}
