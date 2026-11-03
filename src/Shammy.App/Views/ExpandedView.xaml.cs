using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Notch;
using Shammy.Infrastructure;
using Shammy.Modules;
using Shammy.Motion;
using Shammy.Shelf;

namespace Shammy.Views;

/// <summary>
/// The open notch (v2): a top row of the eyes (home), chips and a search pill, and under it either the home
/// row of cards modules registered with <see cref="AddCard"/> or one view opened in place — a card's detail,
/// the Shelf, Notifications, or an older module's tab. Opening a view swaps the row for it with a quick
/// spring grow, and the notch grows taller to fit (see <see cref="HeightChanged"/>).
/// </summary>
public partial class ExpandedView : UserControl
{
    /// <summary>The open notch's size: 1012 wide; the home row makes it 248 tall, an opened view 320.</summary>
    public const double OpenWidth = 1012, HomeHeight = 248, DetailHeight = 320;

    /// <summary>The Back + title band above a card's detail content.</summary>
    const double CardHeader = 52, CardPad = 16;

    const string ShelfId = "shelf", AlertsId = "alerts", TabPrefix = "tab:";

    ShammyServices? _s;
    readonly DispatcherTimer _clock;
    bool _seeking;
    Point _dragStart;
    ShelfItem? _pressed;

    readonly List<NotchCard> _cards = new();
    readonly List<(string Id, string Label, FrameworkElement Panel)> _tabs = new();
    readonly Dictionary<NotchCard, Grid> _frames = new();
    readonly Dictionary<string, RadioButton> _chipButtons = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<UIElement, ChipSpec> _chipIds = new();
    RadioButton? _moreChip;
    IReadOnlyList<ChipSpec> _chips = Array.Empty<ChipSpec>();
    IReadOnlyList<string> _homeIds = Array.Empty<string>();
    readonly NotchCard _nowPlaying;
    Border? _shelfBadge;
    TextBlock? _shelfBadgeText;
    string? _openId;
    int _alertsNewAtOpen;
    Grid? _pressedFrame;
    Point _pressedAt;

    readonly SpringValue _inOpacity = new(1, 0.002), _inScale = new(1, 0.0005);
    readonly ScaleTransform _inTransform = new(1, 1);
    FrameworkElement? _animTarget;
    bool _animHooked;

    public ExpandedView()
    {
        InitializeComponent();
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Render, (_, _) => Tick(), Dispatcher) { IsEnabled = false };

        Stash.Children.Remove(MediaPanel);
        _nowPlaying = new NotchCard
        {
            Id = "nowplaying",
            Title = "Now playing",
            Width = 340,
            Order = 0,
            Compact = MediaPanel,
            Detail = () => new Grid(),
            IsRelevant = () => _s?.Media.HasSession == true,
        };

        IsVisibleChanged += (_, _) =>
        {
            _clock.IsEnabled = IsVisible;
            if (!IsVisible)
            {
                // Closed: the next open starts on the home row, as the design does.
                if (_openId is not null) GoHome(animate: false);
                return;
            }
            // Opening (by hover or by a command): no chip is lit until the pointer actually moves over it.
            ClearTabHover();
            Refresh();
            RebuildAlertRows();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _openId is null) return;
            GoHome(animate: true);
            e.Handled = true;
        };
        // The hover-opened notch never takes focus, so a click into a text box (Notes' add box, the clipboard
        // search) has to ask for it, or the typing goes to the app underneath.
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (TextInputAt(e.OriginalSource as DependencyObject) is { } input) TextInputWanted?.Invoke(input);
        };
        RebuildChips();
        RebuildRow(force: true);
    }

    /// <summary>Raised while the pointer has to leave the notch for something it started (a file dragged off the shelf,
    /// a menu), so the notch stays open.</summary>
    internal event Action<bool>? ShelfDragging;

    /// <summary>A notification in the history list was clicked.</summary>
    internal event Action<Notifications.ShammyNotification>? NotificationOpened;

    /// <summary>The search pill was clicked: open the typing palette.</summary>
    internal event Action? SearchRequested;

    /// <summary>The view's height changed (home row ⇄ an opened view); the notch reshapes to match.</summary>
    internal event Action? HeightChanged;

    /// <summary>The user clicked into a text box, or opened a view with one: the notch should take keyboard focus.</summary>
    internal event Action<Control>? TextInputWanted;

    /// <summary>A view opened or went home: whatever had keyboard focus may be gone.</summary>
    internal event Action? ViewChanged;

    /// <summary>The text box (or password box) <paramref name="d"/> is in, if any.</summary>
    Control? TextInputAt(DependencyObject? d)
    {
        for (; d is not null && d != this; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBoxBase or PasswordBox) return IsTypable((Control)d) ? (Control)d : null;
        return null;
    }

    static bool IsTypable(Control c) => c.IsVisible && c.IsEnabled && c.Focusable && c is not TextBoxBase { IsReadOnly: true };

    /// <summary>The first text box in the open view, in tree order.</summary>
    Control? FirstTextInput(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBoxBase or PasswordBox && IsTypable((Control)child)) return (Control)child;
            if (FirstTextInput(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Test hook: ask for the keyboard for the open view's first text box, as a click into it would.</summary>
    internal bool RequestFirstTextInput()
    {
        if (_openId is null || FirstTextInput(DetailFrame) is not { } input) return false;
        TextInputWanted?.Invoke(input);
        return true;
    }

    /// <summary>
    /// Opened by a click on its chip or card: the user is at the notch, so a view with a text box gets the
    /// keyboard straight away (a hover or a command never does; clicking into the box does).
    /// </summary>
    void OpenByClick(string id)
    {
        if (!OpenView(id)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_openId is not null && IsVisible && FirstTextInput(DetailFrame) is { } input) TextInputWanted?.Invoke(input);
        }, DispatcherPriority.Loaded);
    }

    internal void Attach(ShammyServices services)
    {
        _s = services;
        ShelfItems.ItemsSource = services.Shelf.Items;
        services.Shelf.Items.CollectionChanged += (_, _) => RefreshShelf();
        AlertItems.ItemsSource = services.Notifications.History;
        services.Notifications.History.CollectionChanged += (_, _) => Refresh();
        // A call starting or ending hides or reveals the rows' text in place, even with the view open.
        PrivacyState.Changed += _ => Dispatcher.BeginInvoke(RebuildAlertRows);
        Bars.Source = services.Visualizer.Level;
        RefreshShelf();
    }

    // ---- Opening views ------------------------------------------------------------------------

    internal void ShowShelf() => OpenView(ShelfId);
    internal void ShowAlerts() => OpenView(AlertsId);
    internal void ShowHome() => GoHome(animate: IsVisible);

    /// <summary>
    /// An older module's tab, opened by the module (ModuleContext.OpenTab). Once the module has registered a
    /// card with the tab's title, the card has taken the tab's place, so its detail opens instead.
    /// </summary>
    internal void ShowTab(FrameworkElement panel)
    {
        foreach (var (id, label, p) in _tabs)
        {
            if (p != panel) continue;
            var card = _cards.FirstOrDefault(c => string.Equals(c.Title, label, StringComparison.OrdinalIgnoreCase));
            OpenView(card?.Id ?? id);
            return;
        }
    }

    /// <summary>Whether <paramref name="query"/> (a view id or a chip label) names something that can open.</summary>
    internal bool CanOpen(string query) => Resolve(query) is not null || IsHome(query);

    static bool IsHome(string query) => string.Equals(query.Trim(), "home", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Open a view in place by id ("music", "shelf", "alerts", "tab:weather") or chip label ("Weather",
    /// "Alerts"); "home" goes back to the row. Returns false for an unknown name.
    /// </summary>
    internal bool OpenView(string query)
    {
        if (IsHome(query))
        {
            ShowHome();
            return true;
        }
        if (Resolve(query) is not { } view) return false;

        // Leaving the Alerts view: what it listed has been seen.
        if (_openId == AlertsId && view.Id != AlertsId) _s?.Notifications.MarkAllSeen();
        _openId = view.Id;
        ClearTabHover();
        DetailContent.Content = null;
        LegacyHost.Visibility = ShelfView.Visibility = AlertsView.Visibility = Visibility.Collapsed;
        foreach (UIElement child in LegacyHost.Children) child.Visibility = Visibility.Collapsed;

        switch (view.Kind)
        {
            case ViewKind.Card:
                try
                {
                    DetailContent.Content = view.Card!.Detail();
                }
                catch (Exception ex)
                {
                    Log.Warn($"Building the {view.Card!.Id} card's detail failed", ex);
                }
                break;
            case ViewKind.Tab:
                view.Panel!.Visibility = Visibility.Visible;
                LegacyHost.Visibility = Visibility.Visible;
                break;
            case ViewKind.Shelf:
                ShelfView.Visibility = Visibility.Visible;
                RefreshShelf();
                break;
            case ViewKind.Alerts:
                AlertsView.Visibility = Visibility.Visible;
                RebuildAlertRows();
                // "N new" counts what arrived since the view was last open; from now on those count as seen.
                _alertsNewAtOpen = _s?.Notifications.UnreadCount ?? 0;
                _s?.Notifications.MarkAllSeen();
                break;
        }
        DetailTitle.Text = view.Title.ToUpperInvariant();
        HomeRow.Visibility = EmptyText.Visibility = Visibility.Collapsed;
        DetailFrame.Visibility = Visibility.Visible;
        SyncChips();
        Refresh();
        // A card's detail is drawn as 924×248 content, so it gets its own band under the Back/title header;
        // the built-in Shelf and Alerts views (and legacy panels) already leave room for the header.
        bool card = view.Kind == ViewKind.Card;
        DetailContent.Margin = card ? new Thickness(CardPad, CardHeader, CardPad, CardPad) : new Thickness(0);
        DetailContent.Width = card ? 924 - 2 * CardPad : 924;
        DetailContent.Height = card ? 248 - CardPad : 248;
        DetailFrame.Height = 248 + (card ? CardHeader : 0);
        ((RectangleGeometry)DetailFrame.Clip).Rect = new Rect(0, 0, 924, DetailFrame.Height);
        SetBodyHeight(DetailHeight + (card ? CardHeader : 0));
        if (IsVisible) AnimateIn(DetailFrame);
        ViewChanged?.Invoke();
        return true;
    }

    void GoHome(bool animate)
    {
        bool wasOpen = _openId is not null;
        if (_openId == AlertsId) _s?.Notifications.MarkAllSeen();
        _openId = null;
        // Let go of the card's detail so it (and anything it subscribed to) can be collected.
        DetailContent.Content = null;
        DetailFrame.Visibility = Visibility.Collapsed;
        HomeRow.Visibility = Visibility.Visible;
        RebuildRow(force: false);
        SyncChips();
        SetBodyHeight(HomeHeight);
        if (animate && wasOpen) AnimateIn(HomeRow);
        if (wasOpen) ViewChanged?.Invoke();
    }

    enum ViewKind { Card, Tab, Shelf, Alerts }

    sealed record View(string Id, string Title, ViewKind Kind, NotchCard? Card = null, FrameworkElement? Panel = null);

    View? Resolve(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return null;
        bool Is(string s) => string.Equals(s, query, StringComparison.OrdinalIgnoreCase);

        foreach (var c in _cards)
            if (Is(c.Id)) return CardView(c);
        if (Is(ShelfId)) return new View(ShelfId, "Shelf", ViewKind.Shelf);
        if (Is(AlertsId) || Is("notifications")) return new View(AlertsId, "Alerts", ViewKind.Alerts);
        foreach (var (id, label, panel) in _tabs)
            if (Is(id)) return new View(id, label, ViewKind.Tab, Panel: panel);
        // By label: the chip's label first (a card may have taken a tab's chip), then any card's title.
        foreach (var chip in _chips)
            if (Is(chip.Label)) return Resolve(chip.ViewId);
        foreach (var c in _cards)
            if (Is(c.Title)) return CardView(c);
        foreach (var (id, label, panel) in _tabs)
            if (Is(label)) return new View(id, label, ViewKind.Tab, Panel: panel);
        return null;
    }

    static View CardView(NotchCard c) => new(c.Id, c.Title, ViewKind.Card, Card: c);

    void SetBodyHeight(double height)
    {
        if (Height == height) return;
        Height = height;
        HeightChanged?.Invoke();
    }

    /// <summary>A quick spring grow (scale 0.96 → 1 with a touch of overshoot, fading in) for the view that just appeared.</summary>
    void AnimateIn(FrameworkElement el)
    {
        if (_animTarget is not null && _animTarget != el)
        {
            _animTarget.Opacity = 1;
            _animTarget.RenderTransform = Transform.Identity;
        }
        _animTarget = el;
        el.RenderTransformOrigin = new Point(0.5, 0);
        el.RenderTransform = _inTransform;
        _inOpacity.Snap(0);
        _inScale.Snap(0.96);
        _inOpacity.AnimateTo(1, 0.22, 1);
        _inScale.AnimateTo(1, 0.32, 0.72);
        ApplyAnim();
        if (_animHooked) return;
        _animHooked = true;
        Animator.Frame += OnAnimFrame;
    }

    void OnAnimFrame(double dt)
    {
        ApplyAnim();
        if (!_inOpacity.IsSettled || !_inScale.IsSettled) return;
        Animator.Frame -= OnAnimFrame;
        _animHooked = false;
    }

    void ApplyAnim()
    {
        if (_animTarget is null) return;
        _animTarget.Opacity = Math.Clamp(_inOpacity.Value, 0, 1);
        _inTransform.ScaleX = _inTransform.ScaleY = _inScale.Value;
    }

    void OnHomeClicked(object sender, RoutedEventArgs e) => ShowHome();
    void OnBackClicked(object sender, RoutedEventArgs e) => ShowHome();
    void OnSearchClicked(object sender, MouseButtonEventArgs e) => SearchRequested?.Invoke();

    // ---- Chips --------------------------------------------------------------------------------

    void ClearTabHover()
    {
        foreach (var chip in _chipButtons.Values) TabHover.Clear(chip);
        if (_moreChip is not null) TabHover.Clear(_moreChip);
    }

    /// <summary>Chips for the built-in views, older tabs and cards that asked for one (see <see cref="NotchChips"/>).</summary>
    void RebuildChips()
    {
        var legacy = new List<LegacyView> { new("Shelf", ShelfId), new("Alerts", AlertsId) };
        legacy.AddRange(_tabs.Select(t => new LegacyView(t.Label, t.Id)));
        var chips = NotchChips.Build(legacy, _cards.Select(c => new ChipCard(c.Id, c.Title, c.Chip)));
        if (chips.SequenceEqual(_chips)) return;
        _chips = chips;

        ChipStrip.Children.Clear();
        _chipButtons.Clear();
        _chipIds.Clear();
        _shelfBadge = null;
        foreach (var spec in chips)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("V2Chip"),
                GroupName = "V2Chips",
                Content = spec.ViewId == ShelfId ? ShelfChipContent(spec.Label) : spec.Label,
            };
            string id = spec.ViewId;
            chip.Click += (_, _) => OpenByClick(id);
            TabHover.Track(chip);
            ChipStrip.Children.Add(chip);
            _chipButtons[id] = chip;
            _chipIds[chip] = spec;
        }
        ChipStrip.Children.Add(MoreChip);
        ChipStrip.More = MoreChip;
        SyncChips();
        RefreshShelf();
    }

    /// <summary>The overflow chip: lists the chips that don't fit in the row (ChipPanel decides which).</summary>
    RadioButton MoreChip => _moreChip ??= CreateMoreChip();

    RadioButton CreateMoreChip()
    {
        var more = new RadioButton { Style = (Style)FindResource("V2Chip"), GroupName = "V2More", Content = "More", ToolTip = "More views" };
        more.Click += (_, _) => ShowMoreMenu(more);
        TabHover.Track(more);
        return more;
    }

    void ShowMoreMenu(RadioButton more)
    {
        SyncChips();
        var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
        foreach (var chip in ChipStrip.Hidden)
        {
            if (!_chipIds.TryGetValue(chip, out var spec)) continue;
            var item = new MenuItem { Header = spec.Label, IsChecked = string.Equals(spec.ViewId, _openId, StringComparison.OrdinalIgnoreCase) };
            string id = spec.ViewId;
            item.Click += (_, _) => OpenByClick(id);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) return;
        // Keep the notch open while the menu is up (the pointer leaves the notch to reach it).
        ShelfDragging?.Invoke(true);
        menu.Closed += (_, _) => ShelfDragging?.Invoke(false);
        menu.IsOpen = true;
    }

    /// <summary>The chips that didn't fit the row, for the test pipe.</summary>
    internal IEnumerable<string> HiddenChips => ChipStrip.Hidden.Select(c => _chipIds.TryGetValue(c, out var s) ? s.Label : "?");

    /// <summary>"Shelf" plus the number of files on it, in a small purple badge.</summary>
    object ShelfChipContent(string label)
    {
        _shelfBadgeText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _shelfBadge = new Border
        {
            Background = (Brush)FindResource("PurpleAccent"),
            CornerRadius = new CornerRadius(8),
            Height = 16,
            MinWidth = 16,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _shelfBadgeText,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_shelfBadge);
        return row;
    }

    /// <summary>Light the chip of the open view (none on the home row or for a card without a chip).</summary>
    void SyncChips()
    {
        foreach (var (id, chip) in _chipButtons)
            chip.IsChecked = _openId is not null && string.Equals(id, _openId, StringComparison.OrdinalIgnoreCase);
        // "More" lights when the open view's own chip is one of those tucked under it.
        MoreChip.IsChecked = _openId is not null && ChipStrip.Hidden.Any(c => _chipIds.TryGetValue(c, out var s)
            && string.Equals(s.ViewId, _openId, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Registration -------------------------------------------------------------------------

    /// <summary>Add an older module's tab: a chip that opens its panel (about 436×150) in the view area.</summary>
    internal void AddTab(string glyph, string tooltip, FrameworkElement panel)
    {
        string id = TabPrefix + tooltip.ToLowerInvariant();
        panel.Visibility = Visibility.Collapsed;
        LegacyHost.Children.Add(panel);
        _tabs.Add((id, tooltip, panel));
        RebuildChips();
    }

    /// <summary>The v2 home-row cards registered so far, in display order.</summary>
    internal IReadOnlyList<NotchCard> Cards => _cards.OrderBy(c => c.Order).ToList();

    /// <summary>Registers a v2 card (a card with the same id replaces the earlier one).</summary>
    internal void AddCard(NotchCard card)
    {
        foreach (var old in _cards.Where(c => c.Id == card.Id).ToList())
        {
            if (_frames.Remove(old, out var frame)) frame.Children.Clear();
            _cards.Remove(old);
        }
        _cards.Add(card);
        CardsChanged();
    }

    /// <summary>A card's relevance may have changed: re-lay the home row (and the chips).</summary>
    internal void CardsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(CardsChanged);
            return;
        }
        RebuildChips();
        if (_openId is null) RebuildRow(force: false);
    }

    /// <summary>Every card that can sit on the home row, including the built-in now-playing card while no module provides music.</summary>
    IEnumerable<NotchCard> RowCandidates =>
        _cards.Any(c => string.Equals(c.Id, "music", StringComparison.OrdinalIgnoreCase)) ? _cards : _cards.Prepend(_nowPlaying);

    IReadOnlyList<string> ComputeRow() =>
        HomeRowLayout.Fit(RowCandidates.Select(c => new RowCard(c.Id, c.Width, c.Order, IsRelevant(c))));

    static bool IsRelevant(NotchCard c)
    {
        try { return c.IsRelevant(); }
        catch (Exception ex)
        {
            Log.Warn($"The {c.Id} card's relevance check failed", ex);
            return false;
        }
    }

    /// <summary>Lay out the home row; skipped when the same cards would show, so frequent CardsChanged calls cost nothing.</summary>
    void RebuildRow(bool force)
    {
        var ids = ComputeRow();
        if (!force && ids.SequenceEqual(_homeIds) && HomeRow.Children.Count == ids.Count)
        {
            EmptyText.Visibility = _openId is null && ids.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        _homeIds = ids;
        HomeRow.Children.Clear();
        var byId = RowCandidates.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ids.Count; i++)
        {
            var frame = FrameFor(byId[ids[i]]);
            frame.Margin = new Thickness(i == 0 ? 0 : HomeRowLayout.Gap, 0, 0, 0);
            HomeRow.Children.Add(frame);
        }
        EmptyText.Visibility = _openId is null && ids.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static readonly Brush FrameHoverBg = Freeze(Color.FromRgb(0x15, 0x15, 0x18)), FrameHoverStroke = Freeze(Color.FromRgb(0x2E, 0x2E, 0x33));

    static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The notch's frame around a card's compact view: the card background, 1 px stroke and 20 px corners
    /// under content that gets the full Width × 176. Clicking anywhere the card's own controls don't handle
    /// opens its detail.
    /// </summary>
    Grid FrameFor(NotchCard card)
    {
        if (_frames.TryGetValue(card, out var existing)) return existing;
        var bg = new Border
        {
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("CardStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
        };
        var frame = new Grid
        {
            Width = card.Width,
            Height = NotchPalette.CardHeight,
            Clip = new RectangleGeometry(new Rect(0, 0, card.Width, NotchPalette.CardHeight), 20, 20),
            Cursor = card == _nowPlaying ? null : Cursors.Hand,
        };
        (card.Compact.Parent as Panel)?.Children.Remove(card.Compact);
        frame.Children.Add(bg);
        frame.Children.Add(card.Compact);
        frame.MouseEnter += (_, _) =>
        {
            bg.Background = FrameHoverBg;
            bg.BorderBrush = FrameHoverStroke;
        };
        frame.MouseLeave += (_, _) =>
        {
            bg.Background = (Brush)FindResource("CardBg");
            bg.BorderBrush = (Brush)FindResource("CardStroke");
        };
        // Buttons inside the card handle the press, so it never gets here; a drag that started on the card
        // (dragging a file out, scrubbing) isn't a click either.
        frame.MouseLeftButtonDown += (_, e) =>
        {
            _pressedFrame = frame;
            _pressedAt = e.GetPosition(this);
        };
        frame.MouseLeftButtonUp += (_, e) =>
        {
            bool click = _pressedFrame == frame && !e.Handled && (e.GetPosition(this) - _pressedAt).Length < 6;
            _pressedFrame = null;
            if (!click || card == _nowPlaying) return;
            e.Handled = true;
            OpenByClick(card.Id);
        };
        _frames[card] = frame;
        return frame;
    }

    /// <summary>One line for the test pipe: chips, the home row's card ids and the open view.</summary>
    internal string DescribeV2() =>
        $"chips=[{string.Join(",", _chips.Select(c => c.Label))}] more=[{string.Join(",", HiddenChips)}] home=[{string.Join(",", ComputeRow())}] " +
        $"detail={_openId ?? "-"} cards=[{string.Join(",", Cards.Select(c => c.Id))}] height={Height}";

    // ---- Refresh --------------------------------------------------------------------------------

    /// <summary>
    /// Rebuild the notification rows so their text (and "5m" ages) is read again: rows bind once, and
    /// whether the text shows depends on <see cref="PrivacyState"/>, which isn't a bindable property.
    /// </summary>
    void RebuildAlertRows()
    {
        if (_s is not null && AlertItems.ItemsSource is not null) AlertItems.Items.Refresh();
    }

    /// <summary>What the Notifications view lists right now, rows separated by " || " (test pipe; replies are one line).</summary>
    internal string DescribeAlertRows() => _s is null ? "" : string.Join(" || ",
        _s.Notifications.History.Select(n => $"{n.ListTitle} | {n.ListDetail} | {n.TimeText}"));

    /// <summary>Extra actions on a shelf tile's right-click menu (registered by modules).</summary>
    internal List<ShelfAction> ShelfActions { get; set; } = new();

    internal void Refresh()
    {
        if (_s is null || !IsInitialized) return;
        if (_openId is null) RebuildRow(force: false);

        var n = _s.Notifications;
        int alertCount = n.History.Count;
        // New since the view was last open: what was new when it opened, plus anything that arrived while open.
        AlertsCount.Text = (_openId == AlertsId ? _alertsNewAtOpen + n.UnreadCount : n.UnreadCount).ToString();
        AlertsNote.Text = !n.IsAvailable ? n.Problem ?? "Notifications are starting…"
            : PrivacyState.Presenting ? "Messages are hidden while you share your screen"
            : "Click one to open its app";
        AlertsEmptyText.Text = n.IsAvailable ? "All caught up" : n.Problem ?? "Notifications are starting…";
        AlertsEmptyIcon.Visibility = n.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        AlertsEmpty.Visibility = alertCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsScroll.Visibility = alertCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        AlertsClear.Visibility = alertCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        var media = _s.Media;
        if (media.HasSession)
        {
            TitleText.Text = media.Title;
            ArtistText.Text = media.Artist;
            CompactView.ApplyArt(ArtFrame, media, 24);
            Bars.Color = media.ArtColor;
            Bars.Active = media.IsPlaying && _homeIds.Contains(_nowPlaying.Id);
            PlayButton.Content = media.IsPlaying ? "" : "";
            PrevButton.IsEnabled = media.CanSkipPrevious;
            NextButton.IsEnabled = media.CanSkipNext;
            Track.IsEnabled = media.CanSeek;
        }
        else
        {
            Bars.Active = false;
        }
        Tick();
    }

    void Tick()
    {
        if (_s is null) return;
        var media = _s.Media;
        if (!media.HasSession || _seeking) return;
        var dur = media.Duration;
        var pos = media.Position;
        bool hasTimeline = dur > TimeSpan.Zero;
        Track.Visibility = ElapsedText.Visibility = RemainingText.Visibility = hasTimeline ? Visibility.Visible : Visibility.Hidden;
        if (!hasTimeline) return;
        SetProgress(pos.TotalSeconds / dur.TotalSeconds);
        ElapsedText.Text = Format(pos);
        RemainingText.Text = "-" + Format(dur - pos);
    }

    void SetProgress(double fraction)
    {
        double w = Track.ActualWidth;
        TrackFill.Width = Math.Clamp(fraction, 0, 1) * w;
    }

    static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    void OnPlayPause(object sender, RoutedEventArgs e) => _s?.Media.TogglePlayPause();
    void OnNext(object sender, RoutedEventArgs e) => _s?.Media.Next();
    void OnPrev(object sender, RoutedEventArgs e) => _s?.Media.Previous();

    void OnArtClicked(object sender, MouseButtonEventArgs e)
    {
        _s?.Media.TogglePlayPause();
        e.Handled = true;
    }

    void OnTrackDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        Track.CaptureMouse();
        SetProgress(e.GetPosition(Track).X / Track.ActualWidth);
        e.Handled = true;
    }

    void OnTrackMove(object sender, MouseEventArgs e)
    {
        if (_seeking) SetProgress(e.GetPosition(Track).X / Track.ActualWidth);
    }

    void OnTrackUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        Track.ReleaseMouseCapture();
        e.Handled = true;
        if (_s is null) return;
        double f = Math.Clamp(e.GetPosition(Track).X / Track.ActualWidth, 0, 1);
        _s.Media.Seek(TimeSpan.FromSeconds(f * _s.Media.Duration.TotalSeconds));
    }

    // ---- Shelf ----------------------------------------------------------------------------------

    void RefreshShelf()
    {
        if (_s is null) return;
        int n = _s.Shelf.Items.Count;
        ShelfScroll.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShelfClear.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfCountRun.Text = n == 0 ? "Empty" : n == 1 ? "1 file" : $"{n} files";
        ShelfHintRun.Text = n == 0 ? "  · drop files on the notch to keep them here" : "  · drag any file out to use it";
        if (_shelfBadge is not null && _shelfBadgeText is not null)
        {
            _shelfBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            _shelfBadgeText.Text = n.ToString();
        }
    }

    void OnClearShelf(object sender, RoutedEventArgs e) => _s?.Shelf.Clear();

    void OnShelfWheel(object sender, MouseWheelEventArgs e)
    {
        ShelfScroll.ScrollToHorizontalOffset(ShelfScroll.HorizontalOffset - e.Delta * 0.6);
        e.Handled = true;
    }

    void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = (sender as FrameworkElement)?.DataContext as ShelfItem;
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && _pressed is not null)
        {
            Open(_pressed.Path);
            _pressed = null;
        }
    }

    void OnTileMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _pressed;
        _pressed = null;
        var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
        ShelfDragging?.Invoke(true);
        try
        {
            var result = DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (result == DragDropEffects.Move) _s?.Shelf.Remove(item);
        }
        finally
        {
            ShelfDragging?.Invoke(false);
        }
    }

    void OnTileUp(object sender, MouseButtonEventArgs e) => _pressed = null;

    void OnTileRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShelfItem item) return;
        var menu = new ContextMenu();
        void Add(string header, Action run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
        }
        Add("Open", () => Open(item.Path));
        Add("Show in folder", () => Process.Start(Interop.Native.ExplorerPath, $"/select,\"{item.Path}\""));
        foreach (var action in ShelfActions.Where(a => a.AppliesTo?.Invoke(item) ?? true)) Add(action.Label, () => action.Run(item));
        Add("Remove from shelf", () => _s?.Shelf.Remove(item));
        menu.PlacementTarget = (UIElement)sender;
        // Keep the notch open while the menu is up (the pointer leaves the notch to reach it).
        ShelfDragging?.Invoke(true);
        menu.Closed += (_, _) => ShelfDragging?.Invoke(false);
        menu.IsOpen = true;
        e.Handled = true;
    }

    void OnRemoveDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShelfItem item) _s?.Shelf.Remove(item);
        e.Handled = true;
    }

    static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Opening a shelf item failed", ex); }
    }

    // ---- Notifications --------------------------------------------------------------------------

    void OnClearAlerts(object sender, RoutedEventArgs e) => _s?.Notifications.ClearHistory();

    void OnAlertClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if ((sender as FrameworkElement)?.DataContext is Notifications.ShammyNotification n) NotificationOpened?.Invoke(n);
    }

    /// <summary>"Remind me in 1 hour" on a notification row's right-click menu.</summary>
    internal event Action<Notifications.ShammyNotification>? NotificationRemindRequested;

    void OnAlertRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Notifications.ShammyNotification n) return;
        var menu = new ContextMenu();
        void Add(string header, Action run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
        }
        Add("Open", () => NotificationOpened?.Invoke(n));
        Add("Remind me in 1 hour", () => NotificationRemindRequested?.Invoke(n));
        Add("Dismiss", () => _s?.Notifications.History.Remove(n));
        menu.PlacementTarget = (UIElement)sender;
        // Keep the notch open while the menu is up, as the shelf's menu does.
        ShelfDragging?.Invoke(true);
        menu.Closed += (_, _) => ShelfDragging?.Invoke(false);
        menu.IsOpen = true;
        e.Handled = true;
    }

    void OnDismissAlert(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Notifications.ShammyNotification n) _s?.Notifications.History.Remove(n);
        e.Handled = true;
    }
}
