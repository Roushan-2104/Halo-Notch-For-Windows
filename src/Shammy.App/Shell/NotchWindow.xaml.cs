using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Activities;
using Shammy.Infrastructure;
using Shammy.Interop;
using Shammy.Motion;
using Shammy.Notifications;
using Shammy.Views;

namespace Shammy.Shell;

public enum NotchMode { Hidden, Idle, Compact, Notification, Card, Expanded, Drop, Listening, Circling, Thinking, Typing, Answer, Result }

/// <summary>
/// The notch. One fixed-width transparent window pinned to the top-centre of the primary monitor; the
/// black shape inside it morphs between states with springs, and each state's content fades and scales
/// in on top, clipped to the shape, so content is revealed by the shape rather than resized with it.
/// The window only grows taller as needed and never moves, so there's nothing to jitter.
/// </summary>
public partial class NotchWindow : Window
{
    public const double WindowWidth = 760;
    const double ShadowPad = 64;
    const double Cx = WindowWidth / 2;

    readonly ShammyServices _services;
    readonly SpringValue _w = new(186), _h = new(32), _r = new(11), _shadow = new(0, 0.002);
    readonly SpringValue _bubbleOpacity = new(0, 0.002), _bubbleScale = new(0.4, 0.001);
    readonly List<Layer> _layers = new();
    readonly Layer _compactA, _compactB, _expanded, _drop, _searchBar, _answer, _result, _notification, _card;
    (double W, double H) _cardSize;
    bool _cardOpen, _cardInteractive;
    readonly DispatcherTimer _hoverIn, _hoverOut, _dragOut;
    Layer _compactActive;
    IntPtr _hwnd;

    NotchMode _mode = NotchMode.Idle;
    (double W, double H, double R) _spec = (186, 32, 11);
    double _compactWidth = 290;

    /// <summary>Height of the palette rows under the typing bar; the bar grows down by this much.</summary>
    double _paletteHeight;
    double _lastW = -1, _lastH = -1, _lastR = -1;

    Activity? _activity, _secondary;
    NotchMode? _searchMode;
    string? _heldActionId;
    bool _hoverExpanded, _dragging, _fullscreen, _interactive, _held, _hoveringNotification, _visitedWhileHeld;
    (FrameworkElement Panel, bool Hold)? _pendingTab;
    Color? _moduleGlow;
    string? _flashedId;
    object? _flashedPayload;
    readonly DispatcherTimer _holdTimeout;
    int _holdOpen;

    internal NotchWindow(ShammyServices services)
    {
        _services = services;
        InitializeComponent();
        Shape.CenterX = ShadowShape.CenterX = Edge.CenterX = Cx;
        Edge.Enabled = services.Settings.ShammyGlow;

        _compactA = AddLayer(CompactA);
        _compactB = AddLayer(CompactB);
        _expanded = AddLayer(Expanded);
        _drop = AddLayer(DropZone);
        _searchBar = AddLayer(SearchBar);
        SearchBar.PaletteHeightChanged += h =>
        {
            _paletteHeight = h;
            if (_mode == NotchMode.Typing) Transition(NotchMode.Typing);
        };
        _answer = AddLayer(Answer);
        _result = AddLayer(Result);
        _notification = AddLayer(NotificationCard);
        _card = AddLayer(CardHost);
        _compactActive = _compactA;

        _hoverIn = new DispatcherTimer(TimeSpan.FromMilliseconds(110), DispatcherPriority.Input, OnHoverIn, Dispatcher) { IsEnabled = false };
        _hoverOut = new DispatcherTimer(TimeSpan.FromMilliseconds(320), DispatcherPriority.Input, OnHoverOut, Dispatcher) { IsEnabled = false };
        _dragOut = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Input, OnDragOut, Dispatcher) { IsEnabled = false };
        _holdTimeout = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Input, (timer, _) =>
        {
            if (!_visitedWhileHeld && !Root.IsMouseOver) ReleaseHold();
            else ((DispatcherTimer)timer!).Stop();
        }, Dispatcher) { IsEnabled = false };

        Expanded.Attach(services);
        Expanded.ShelfDragging += dragging =>
        {
            _holdOpen += dragging ? 1 : -1;
            if (!dragging && !Root.IsMouseOver) _hoverOut.Start();
        };
        Result.CompactLayout = services.Settings.CompactResults;

        Root.MouseEnter += OnRootMouseEnter;
        Root.MouseLeave += OnRootMouseLeave;
        Root.DragEnter += OnDragEnter;
        Root.DragOver += OnDragEnter;
        Root.DragLeave += (_, _) => _dragOut.Start();
        Root.Drop += OnDrop;
        Bubble.MouseLeftButtonUp += (_, _) => _secondary?.Click?.Invoke();
        // Pressing on the notch means a click is coming; don't let a pending hover expand it away first.
        Root.PreviewMouseLeftButtonDown += (_, _) => _hoverIn.Stop();
        // The pill has already run its click (it handles the event first); a done action then goes away.
        Root.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == NotchMode.Compact && IsAction(_activity)) ActionClicked?.Invoke(_activity!.Id);
        };
        // Moving from the side bubble onto the pill (both inside Root) raises no new MouseEnter.
        Bubble.MouseLeave += (_, _) =>
        {
            if (Root.IsMouseOver && !_hoverExpanded) StartHoverIn();
        };
        Deactivated += (_, _) =>
        {
            if (_cardOpen && _cardInteractive && _searchMode is null) CloseCard();
            else if (_interactive) InteractiveDeactivated?.Invoke();
        };
        Root.MouseWheel += (_, e) =>
        {
            if (_mode is NotchMode.Idle or NotchMode.Compact or NotchMode.Notification)
            {
                NotchWheel?.Invoke(e.Delta);
                e.Handled = true;
            }
        };

        SourceInitialized += OnSourceInitialized;
        Animator.Frame += OnFrame;
        Reposition();
        ApplyVisuals(force: true);
    }

    public event Action? InteractiveDeactivated;
    public event Action<string[]>? FilesDropped;

    /// <summary>The pointer entered/left a notification on display (so the host can pause its timer).</summary>
    public event Action<bool>? NotificationHovered;

    /// <summary>Vertical wheel over the idle/compact notch.</summary>
    public event Action<int>? NotchWheel;

    /// <summary>Horizontal wheel or two-finger swipe over the notch (positive = right).</summary>
    public event Action<int>? NotchHorizontalWheel;

    public NotchMode Mode => _mode;
    internal SearchBarView SearchBarView => SearchBar;
    internal ResultView ResultView => Result;
    internal AnswerView AnswerView => Answer;
    internal ExpandedView ExpandedView => Expanded;
    internal NotificationView NotificationView => NotificationCard;

    sealed class Layer
    {
        public Layer(FrameworkElement el)
        {
            El = el;
            el.RenderTransformOrigin = new Point(0.5, 0);
            el.RenderTransform = Scale;
            el.Opacity = 0;
            el.Visibility = Visibility.Collapsed;
            Canvas.SetTop(el, 0);
        }

        public readonly FrameworkElement El;
        public readonly ScaleTransform Scale = new(0.94, 0.94);
        public readonly SpringValue Opacity = new(0, 0.002);
        public readonly SpringValue Size = new(0.94, 0.0005);
        public bool Active;
    }

    Layer AddLayer(FrameworkElement el)
    {
        var layer = new Layer(el);
        _layers.Add(layer);
        return layer;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Reposition();
        // The primary monitor's scale changed (or the window moved to a monitor with another): WPF rescales
        // the window, and the centre has to be worked out again at the new size.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE && !_interactive)
        {
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && Root.IsMouseOver)
        {
            NotchHorizontalWheel?.Invoke((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Centre on the top of the primary monitor's usable area, so the notch works with the taskbar on
    /// any side: with a top taskbar it hangs from the taskbar's bottom edge instead of covering it.
    /// Call again whenever the work area or displays change.
    /// </summary>
    public void Reposition()
    {
        if (_hwnd != IntPtr.Zero && PlaceInPixels()) return;
        var area = SystemParameters.WorkArea;
        if (_services.Settings.PinToScreenEdge)
        {
            Left = (SystemParameters.PrimaryScreenWidth - WindowWidth) / 2;
            Top = 0;
        }
        else
        {
            Left = area.Left + (area.Width - WindowWidth) / 2;
            Top = area.Top;
        }
    }

    /// <summary>
    /// Centres the notch on the primary monitor in physical pixels at that monitor's own scale.
    /// SystemParameters (used before the window exists) are in the system DPI captured at sign-in, which is
    /// wrong once the primary monitor's scale differs from it (docking a 150% laptop to a 100% screen,
    /// changing the scale in Settings): the notch ended up hundreds of pixels off centre.
    /// </summary>
    bool PlaceInPixels()
    {
        var monitor = Native.PrimaryMonitor();
        if (Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0 || dpi == 0) return false;
        var info = Native.MonitorInfo(monitor);
        var r = _services.Settings.PinToScreenEdge || StripTopPx is not null ? info.rcMonitor : info.rcWork;
        int width = (int)Math.Round(WindowWidth * dpi / 96.0);
        int x = r.Left + (r.Right - r.Left - width) / 2;
        return Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, StripTopPx ?? r.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Set by the app wings while they reserve a strip along the top of the primary monitor (its top edge,
    /// in pixels): the notch sits on that strip, centred between the wings, instead of below it at the top
    /// of the shrunken work area.
    /// </summary>
    internal int? StripTopPx { get; set; }

    public void BringToFront()
    {
        if (_hwnd != IntPtr.Zero) Native.BringToTopmost(_hwnd);
    }

    /// <summary>Keep the notch out of Shammy's own screen freeze (it's drawn live on top anyway).</summary>
    public void ExcludeFromCapture(bool exclude)
    {
        if (_hwnd != IntPtr.Zero) Native.SetWindowDisplayAffinity(_hwnd, exclude ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
    }

    // ---- Inputs -------------------------------------------------------------------------------

    internal void SetActivities(Activity? current, Activity? secondary)
    {
        bool wasAction = IsAction(_activity);
        bool wasNotification = _mode == NotchMode.Notification;
        _activity = current;
        _secondary = secondary;
        Recompute();
        UpdateBubble();
        FlashForActivity();

        // A notification that arrives (or is replaced by the next one) under a resting pointer is being
        // read: hold it, as MouseEnter would have. Only while it still has a countdown, so the hold's own
        // re-post doesn't loop back here.
        if (_mode == NotchMode.Notification && Root.IsMouseOver && current?.ExpiresAt is not null)
        {
            _hoveringNotification = true;
            NotificationHovered?.Invoke(true);
        }
        // The notification under the pointer went away: hovering opens the notch again.
        if (wasNotification && _mode != NotchMode.Notification && Root.IsMouseOver && !_hoverExpanded)
        {
            _hoveringNotification = false;
            StartHoverIn();
        }

        bool isAction = IsAction(current);
        // An action that arrives under the pointer (e.g. while the notch is open) waits until it's seen.
        // A different pill (or none) is up now: the one we were holding gets its countdown back.
        if (_heldActionId is { } held && (!isAction || current!.Id != held)) SetActionHold(false);
        if (isAction && Root.IsMouseOver && _heldActionId is null) SetActionHold(true);
        // The pointer stayed on the notch while an action pill was up; once it's gone, hovering works again.
        if (wasAction && !isAction && Root.IsMouseOver && !_hoverExpanded && _mode != NotchMode.Notification) StartHoverIn();
    }

    /// <summary>Raised while the pointer is on a one-shot action pill, so its countdown pauses.</summary>
    public event Action<string, bool>? ActionHovered;

    /// <summary>Raised after a one-shot action pill was clicked, with its activity id.</summary>
    public event Action<string>? ActionClicked;

    /// <summary>
    /// One-shot messages you act on by clicking ("Update ready · click to restart", "Saved · click to
    /// show"). Hovering must not expand the notch over them, or the thing to click disappears.
    /// </summary>
    static bool IsAction(Activity? a) =>
        a is { Click: not null, Kind: ActivityKind.Message or ActivityKind.DownloadComplete };

    /// <summary>
    /// Holds or releases by id, so a release still finds the right pill after something with a higher
    /// priority (a volume HUD, a notification) has pushed in front of it.
    /// </summary>
    void SetActionHold(bool on)
    {
        var id = on ? _activity?.Id : _heldActionId;
        _heldActionId = on ? id : null;
        if (id is not null) ActionHovered?.Invoke(id, on);
    }

    public void SetSearchMode(NotchMode? mode)
    {
        _searchMode = mode;
        if (mode is not null)
        {
            _hoverExpanded = false;
            _hoverIn.Stop();
        }
        Recompute();
        UpdateBubble();
        if (mode is null && _pendingTab is { } pending)
        {
            _pendingTab = null;
            OpenTab(pending.Panel, pending.Hold);
        }
    }

    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;
        if (fullscreen) _hoverExpanded = false;
        Recompute();
        UpdateBubble();
    }

    /// <summary>Let the notch take keyboard focus (typing, scrolling results) or go back to never stealing it.</summary>
    public void SetInteractive(bool on)
    {
        if (_interactive == on) return;
        _interactive = on;
        if (_hwnd == IntPtr.Zero) return;
        if (on)
        {
            Native.SetExStyle(_hwnd, 0, Native.WS_EX_NOACTIVATE);
            Native.ForceForeground(_hwnd);
            Activate();
        }
        else
        {
            Native.SetExStyle(_hwnd, Native.WS_EX_NOACTIVATE);
        }
    }

    public void RefreshMedia()
    {
        ((CompactView)_compactActive.El).RefreshMedia(_services.Media);
        if (Expanded.IsVisible) Expanded.Refresh();
        UpdateBubble();
    }

    public void RefreshExpanded()
    {
        if (Expanded.IsVisible) Expanded.Refresh();
    }

    /// <summary>
    /// Open the expanded card on a tab, as if hovered (after a drop, a click, or a command). With
    /// <paramref name="hold"/> it stays open until the pointer has visited and left, or for 8 s if the
    /// pointer never comes over, instead of closing straight away because nobody is hovering.
    /// </summary>
    public void ExpandTo(bool shelf, bool hold = false) =>
        Open(() => { if (shelf) Expanded.ShowShelf(); else Expanded.ShowHome(); }, hold);

    /// <summary>
    /// Open a module's tab. While a search or command card is showing, the tab opens as soon as that
    /// card closes, so a command can confirm first and then show its tab.
    /// </summary>
    internal void OpenTab(FrameworkElement panel, bool hold)
    {
        if (_searchMode is not null)
        {
            _pendingTab = (panel, hold);
            return;
        }
        Open(() => Expanded.ShowTab(panel), hold);
    }

    void Open(Action selectTab, bool hold)
    {
        selectTab();
        _hoverExpanded = true;
        if (hold)
        {
            if (!_held)
            {
                _held = true;
                _holdOpen++;
            }
            _visitedWhileHeld = Root.IsMouseOver;
            _holdTimeout.Stop();
            _holdTimeout.Start();
        }
        Recompute();
        if (!Root.IsMouseOver) _hoverOut.Start();
    }

    /// <summary>Drop this window's own hold; other holds (a drag, a context menu) still keep it open.</summary>
    void ReleaseHold()
    {
        _holdTimeout.Stop();
        if (!_held) return;
        _held = false;
        _holdOpen--;
        if (!Root.IsMouseOver) _hoverOut.Start();
    }

    /// <summary>
    /// The browser engine only starts once its control is visible, so warm it up inside the result
    /// layer while that layer is clipped away by the small notch shape (nothing shows, nothing is clickable).
    /// </summary>
    internal async Task WarmUpResultsAsync()
    {
        if (_result.Active) return;
        Result.IsHitTestVisible = false;
        Result.Visibility = Visibility.Visible;
        try
        {
            await Result.EnsureReadyAsync();
        }
        finally
        {
            Result.IsHitTestVisible = true;
            if (!_result.Active) Result.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Show module content in the notch at the given size (see ModuleContext.ShowCard).</summary>
    internal void ShowCard(FrameworkElement content, double width, double height, bool interactive)
    {
        CardHost.Content = content;
        CardHost.Width = width;
        CardHost.Height = height;
        _cardSize = (width, height);
        _cardOpen = true;
        _cardInteractive = interactive;
        _hoverExpanded = false;
        Recompute();
        if (interactive && _searchMode is null) SetInteractive(true);
    }

    internal void CloseCard()
    {
        if (!_cardOpen) return;
        _cardOpen = false;
        if (_cardInteractive && _searchMode is null) SetInteractive(false);
        _cardInteractive = false;
        Recompute();
    }

    static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58), Red = Color.FromRgb(0xFF, 0x45, 0x3A);

    /// <summary>Keep the edge lit in a module's colour (e.g. a finished timer); null releases it.</summary>
    internal void SetGlow(Color? color)
    {
        _moduleGlow = color;
        UpdateEdgeHold();
    }

    /// <summary>A short flash of the edge (e.g. a new song in its album colour).</summary>
    internal void FlashEdge(Color color) => Edge.Flash(color);

    public void SetShammyEnabled(bool on) => Edge.Enabled = on;

    /// <summary>
    /// A newly arrived activity flashes the edge in its status colour. Re-posts of the same one (a new
    /// expiry on hover) keep the same payload, so they don't flash again.
    /// </summary>
    void FlashForActivity()
    {
        var a = _activity;
        if (a is null || (a.Id == _flashedId && ReferenceEquals(a.Payload, _flashedPayload))) return;
        _flashedId = a.Id;
        _flashedPayload = a.Payload;
        Color? color = a switch
        {
            { Kind: ActivityKind.Notification } => Theme.Accent.Current,
            { Kind: ActivityKind.Charging } => Green,
            { Kind: ActivityKind.LowBattery } => Red,
            { Payload: MessagePayload { IsError: true } } => Red,
            _ => null,
        };
        if (color is { } c) Edge.Flash(c);
    }

    /// <summary>A steady edge while listening or searching; otherwise only a module's held colour.</summary>
    void UpdateEdgeHold()
    {
        Edge.Shown = _mode != NotchMode.Hidden;
        Edge.Hold(_mode is NotchMode.Listening or NotchMode.Circling or NotchMode.Thinking ? Theme.Accent.Current : _moduleGlow);
    }

    public void Collapse()
    {
        _pendingTab = null;
        _holdTimeout.Stop();
        if (_held)
        {
            _held = false;
            _holdOpen--;
        }
        _hoverExpanded = false;
        Recompute();
    }

    // ---- State machine ------------------------------------------------------------------------

    void Recompute()
    {
        NotchMode mode;
        if (_searchMode is { } s) mode = s;
        else if (_fullscreen && _activity?.Kind != ActivityKind.Hud) mode = NotchMode.Hidden;
        else if (_dragging) mode = NotchMode.Drop;
        else if (_cardOpen) mode = NotchMode.Card;
        else if (_activity?.Kind == ActivityKind.Notification && !_hoverExpanded) mode = NotchMode.Notification;
        else if (_hoverExpanded) mode = NotchMode.Expanded;
        else if (_activity is not null) mode = NotchMode.Compact;
        else mode = NotchMode.Idle;

        if (mode == NotchMode.Compact) PrepareCompact();
        if (mode == NotchMode.Notification && _activity?.Payload is NotificationPayload p) NotificationCard.Show(p.Notification, p.More);
        if (mode != _mode || mode is NotchMode.Compact or NotchMode.Card) Transition(mode);
        // The side bubble belongs next to the compact pill only; hide it whenever a card, tab or drop zone opens.
        UpdateBubble();
    }

    void PrepareCompact()
    {
        var a = _activity!;
        var current = (CompactView)_compactActive.El;
        if (current.ShownId == a.Id && current.ShownKind == a.Kind && _compactActive.Active)
        {
            _compactWidth = current.Update(a, _services);
            return;
        }
        // Different activity: build it in the spare layer and cross-fade.
        var next = _compactActive == _compactA ? _compactB : _compactA;
        if (_mode != NotchMode.Compact) next = _compactActive;
        _compactWidth = ((CompactView)next.El).Show(a, _services);
        _compactActive = next;
    }

    (double W, double H, double R) SpecFor(NotchMode mode) => mode switch
    {
        NotchMode.Hidden => (150, 0, 0),
        NotchMode.Idle => (186, 32, 11),
        NotchMode.Compact => (_compactWidth, 32, 12),
        NotchMode.Notification => (460, 92, 30),
        NotchMode.Card => (_cardSize.W, _cardSize.H, Math.Min(32, _cardSize.H / 2)),
        NotchMode.Expanded => (480, 204, 32),
        NotchMode.Drop => (480, 150, 32),
        NotchMode.Listening or NotchMode.Circling or NotchMode.Thinking => (420, 54, 27),
        NotchMode.Typing => (560, 58 + _paletteHeight, _paletteHeight > 0 ? 30 : 29),
        NotchMode.Answer => (440, 120, 32),
        NotchMode.Result => (560, 680, 34),
        _ => (186, 32, 11),
    };

    Layer? LayerFor(NotchMode mode) => mode switch
    {
        NotchMode.Compact => _compactActive,
        NotchMode.Notification => _notification,
        NotchMode.Card => _card,
        NotchMode.Expanded => _expanded,
        NotchMode.Drop => _drop,
        NotchMode.Listening or NotchMode.Circling or NotchMode.Thinking or NotchMode.Typing => _searchBar,
        NotchMode.Answer => _answer,
        NotchMode.Result => _result,
        _ => null,
    };

    void Transition(NotchMode mode)
    {
        var spec = SpecFor(mode);
        bool grow = spec.W * spec.H > _spec.W * _spec.H + 1;
        bool changed = mode != _mode || spec != _spec;
        _mode = mode;
        _spec = spec;

        if (changed)
        {
            // Growing is a touch bouncy, like Dynamic Island; shrinking settles without overshoot.
            double response = grow ? 0.5 : 0.4;
            double damping = grow ? 0.72 : 0.9;
            if (mode == NotchMode.Hidden) (response, damping) = (0.32, 1);
            _w.AnimateTo(spec.W, response, damping);
            _h.AnimateTo(spec.H, response, damping);
            _r.AnimateTo(spec.R, response, Math.Min(1, damping + 0.08));
            // A soft shadow always grounds the notch; bigger cards cast a fuller one.
            _shadow.AnimateTo(mode == NotchMode.Hidden ? 0 : spec.H > 60 ? 1 : 0.5, 0.45, 1);
        }
        UpdateEdgeHold();

        if (mode == NotchMode.Typing) SearchBar.Width = spec.W;
        else if (mode is NotchMode.Listening or NotchMode.Circling or NotchMode.Thinking) SearchBar.Width = spec.W;
        SearchBar.Height = spec.H;

        var target = LayerFor(mode);
        foreach (var layer in _layers) SetLayerActive(layer, layer == target, grow);

        EnsureWindowHeight(Math.Max(_h.Value, spec.H));
        Animator.Kick();
    }

    void SetLayerActive(Layer layer, bool active, bool grow)
    {
        if (active)
        {
            if (!layer.Active)
            {
                if (layer.Opacity.Value < 0.05) layer.Size.Snap(grow ? 0.9 : 1.05);
                layer.El.Visibility = Visibility.Visible;
            }
            Canvas.SetLeft(layer.El, Cx - layer.El.Width / 2);
            double delay = grow ? 0.06 : 0.03;
            layer.Opacity.AnimateTo(1, 0.3, 1, delay);
            layer.Size.AnimateTo(1, 0.5, 0.78, delay);
        }
        else if (layer.Active || layer.Opacity.Target > 0)
        {
            layer.Opacity.AnimateTo(0, 0.14, 1);
            layer.Size.AnimateTo(grow ? 1.04 : 0.94, 0.25, 1);
        }
        layer.Active = active;
    }

    void UpdateBubble()
    {
        bool show = _secondary is not null && _mode is NotchMode.Compact or NotchMode.Idle;
        if (show)
        {
            BubbleContent.Content = _secondary!.Kind switch
            {
                ActivityKind.NowPlaying when _services.Media.Art is { } art => new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
                },
                ActivityKind.NowPlaying => CompactView.Glyph("", 13, Brushes.White),
                ActivityKind.Downloading => new Spinner { Width = 16, Height = 16, Stroke = (Brush)Application.Current.Resources["AccentBrush"] },
                ActivityKind.Custom when _secondary.Payload is Modules.ICompactContent c && c.BuildBubble() is { } bubble => bubble,
                _ => CompactView.Glyph("", 13, Brushes.White),
            };
        }
        _bubbleOpacity.AnimateTo(show ? 1 : 0, 0.35, 1);
        _bubbleScale.AnimateTo(show ? 1 : 0.4, 0.45, show ? 0.65 : 1);
    }

    // ---- Per-frame rendering ------------------------------------------------------------------

    void OnFrame(double dt) => ApplyVisuals(force: false);

    void ApplyVisuals(bool force)
    {
        double w = _w.Value, h = _h.Value, r = _r.Value;
        if (force || w != _lastW || h != _lastH || r != _lastR)
        {
            _lastW = w;
            _lastH = h;
            _lastR = r;
            Shape.Update(w, h, r);
            ShadowShape.Update(w, h, r);
            Edge.UpdateShape(w, h, r);
            ContentHost.Clip = Shape.Geometry;

            bool visible = h > 1;
            Canvas.SetLeft(HitArea, Cx - w / 2 - 12);
            Canvas.SetTop(HitArea, 0);
            HitArea.Width = visible ? w + 24 : 0;
            HitArea.Height = visible ? h + 8 : 0;
        }
        ShadowShape.Opacity = _shadow.Value;

        foreach (var layer in _layers)
        {
            double o = layer.Opacity.Value;
            layer.El.Opacity = o;
            layer.Scale.ScaleX = layer.Scale.ScaleY = layer.Size.Value;
            if (!layer.Active && o <= 0.003 && layer.El.Visibility == Visibility.Visible) layer.El.Visibility = Visibility.Collapsed;
        }

        Bubble.Opacity = _bubbleOpacity.Value;
        BubbleScale.ScaleX = BubbleScale.ScaleY = _bubbleScale.Value;
        Canvas.SetLeft(Bubble, Cx + w / 2 + 10);
        Canvas.SetTop(Bubble, 0);
        Bubble.IsHitTestVisible = _bubbleOpacity.Target > 0;

        if (_w.IsSettled && _h.IsSettled && _shadow.IsSettled)
        {
            double want = _spec.H + ShadowPad;
            if (Math.Abs(Height - want) > 0.5) Height = want;
        }
    }

    void EnsureWindowHeight(double contentHeight)
    {
        double want = contentHeight + ShadowPad;
        if (want > Height) Height = want;
    }

    // ---- Hover ---------------------------------------------------------------------------------

    internal string HoverDiag =>
        $"root={Root.IsMouseOver} bubble={Bubble.IsMouseOver} bubbleHit={Bubble.IsHitTestVisible} bubbleLeft={Canvas.GetLeft(Bubble):0} " +
        $"bubbleOpacity={Bubble.Opacity:0.00} mode={_mode} hoverExpanded={_hoverExpanded} hoverInMs={_hoverIn.Interval.TotalMilliseconds} dpiX={System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX}";

    bool CanHover => _searchMode is null && !_fullscreen && !_dragging;

    void OnRootMouseEnter(object sender, MouseEventArgs e)
    {
        _hoverOut.Stop();
        if (_held) _visitedWhileHeld = true;
        if (_mode == NotchMode.Card) return;
        // Hovering a notification holds it on screen (to read or click) instead of opening the card.
        if (_mode == NotchMode.Notification)
        {
            _hoveringNotification = true;
            NotificationHovered?.Invoke(true);
            return;
        }
        if (IsAction(_activity))
        {
            if (_heldActionId is null) SetActionHold(true);
            if (_mode == NotchMode.Compact) return;
        }
        if (!_hoverExpanded) StartHoverIn();
    }

    /// <summary>
    /// Hovering opens the notch quickly, except over a pill that does something when clicked (the meeting
    /// mic, a running timer): there it waits long enough for a click to land first.
    /// </summary>
    void StartHoverIn()
    {
        if (!CanHover || Bubble.IsMouseOver) return;
        bool clickable = _mode == NotchMode.Compact && _activity is { Click: not null, Kind: not ActivityKind.NowPlaying };
        _hoverIn.Interval = TimeSpan.FromMilliseconds(clickable ? 700 : 110);
        _hoverIn.Start();
    }

    void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverIn.Stop();
        if (_heldActionId is not null) SetActionHold(false);
        if (_hoveringNotification)
        {
            _hoveringNotification = false;
            NotificationHovered?.Invoke(false);
        }
        // A tab opened for the user (by a command or a click) closes once they've looked and moved on.
        if (_held && _visitedWhileHeld) ReleaseHold();
        if (_hoverExpanded) _hoverOut.Start();
    }

    void OnHoverIn(object? sender, EventArgs e)
    {
        _hoverIn.Stop();
        // A pressed button means a drag is passing through; the drop zone handles that case.
        if (Mouse.LeftButton == MouseButtonState.Pressed || !Root.IsMouseOver || !CanHover || Bubble.IsMouseOver) return;
        if (_mode == NotchMode.Compact && IsAction(_activity)) return;
        // Hovering a notification holds it to be read; it never opens the notch over it.
        if (_mode == NotchMode.Notification) return;
        _hoverExpanded = true;
        Recompute();
        UpdateBubble();
    }

    void OnHoverOut(object? sender, EventArgs e)
    {
        _hoverOut.Stop();
        if (Root.IsMouseOver || _holdOpen > 0) return;
        _hoverExpanded = false;
        Recompute();
        UpdateBubble();
    }

    // ---- Drag and drop onto the shelf -----------------------------------------------------------

    void OnDragEnter(object sender, DragEventArgs e)
    {
        _dragOut.Stop();
        // A tab a module opened for you (held for 8 s) mustn't block dropping files; only a shelf drag does.
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop) && _searchMode is null && _holdOpen - (_held ? 1 : 0) == 0;
        e.Effects = files ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
        if (files && !_dragging)
        {
            _dragging = true;
            _hoverIn.Stop();
            Recompute();
        }
    }

    void OnDragOut(object? sender, EventArgs e)
    {
        _dragOut.Stop();
        if (!_dragging) return;
        _dragging = false;
        Recompute();
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        _dragOut.Stop();
        _dragging = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            FilesDropped?.Invoke(files);
            ExpandTo(shelf: true);
        }
        else
        {
            Recompute();
        }
        e.Handled = true;
    }
}
