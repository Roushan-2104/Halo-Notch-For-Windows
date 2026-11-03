using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Shammy.Infrastructure;
using Shammy.Shelf;

namespace Shammy.Views;

public partial class ExpandedView : UserControl
{
    ShammyServices? _s;
    readonly DispatcherTimer _clock;
    bool _seeking;
    Point _dragStart;
    ShelfItem? _pressed;

    public ExpandedView()
    {
        InitializeComponent();
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Render, (_, _) => Tick(), Dispatcher) { IsEnabled = false };
        IsVisibleChanged += (_, _) =>
        {
            _clock.IsEnabled = IsVisible;
            if (IsVisible) Refresh();
        };
    }

    /// <summary>Raised while a file is being dragged off the shelf, so the notch stays open.</summary>
    internal event Action<bool>? ShelfDragging;

    /// <summary>A notification in the history list was clicked.</summary>
    internal event Action<Notifications.ShammyNotification>? NotificationOpened;

    internal void Attach(ShammyServices services)
    {
        _s = services;
        ShelfItems.ItemsSource = services.Shelf.Items;
        services.Shelf.Items.CollectionChanged += (_, _) => RefreshShelf();
        AlertItems.ItemsSource = services.Notifications.History;
        services.Notifications.History.CollectionChanged += (_, _) => Refresh();
        Bars.Source = services.Visualizer.Level;
        RefreshShelf();
    }

    internal void ShowShelf() => ShelfTab.IsChecked = true;
    internal void ShowHome() => HomeTab.IsChecked = true;
    internal void ShowAlerts() => AlertsTab.IsChecked = true;

    readonly List<(RadioButton Tab, FrameworkElement Panel)> _moduleTabs = new();

    /// <summary>Extra actions on a shelf tile's right-click menu (registered by modules).</summary>
    internal List<Modules.ShelfAction> ShelfActions { get; set; } = new();

    /// <summary>Add a module's tab; its panel fills the card body (about 436×150).</summary>
    internal void AddTab(string glyph, string tooltip, FrameworkElement panel)
    {
        var tab = new RadioButton
        {
            Style = (Style)FindResource("TabButton"),
            GroupName = "NotchTabs",
            Content = glyph,
            ToolTip = tooltip,
            Margin = new Thickness(4, 0, 0, 0),
        };
        tab.Checked += OnTabChanged;
        TabStrip.Children.Add(tab);
        Grid.SetRow(panel, 1);
        panel.Visibility = Visibility.Collapsed;
        CardGrid.Children.Add(panel);
        _moduleTabs.Add((tab, panel));
    }

    internal void ShowTab(FrameworkElement panel)
    {
        foreach (var (tab, p) in _moduleTabs)
            if (p == panel) tab.IsChecked = true;
    }

    void OnTabChanged(object sender, RoutedEventArgs e) => Refresh();

    internal void Refresh()
    {
        if (_s is null || !IsInitialized) return;
        bool shelf = ShelfTab.IsChecked == true;
        bool alerts = AlertsTab.IsChecked == true;
        bool moduleTab = false;
        foreach (var (tab, panel) in _moduleTabs)
        {
            bool on = tab.IsChecked == true;
            panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            moduleTab |= on;
        }
        bool home = !shelf && !alerts && !moduleTab;
        var media = _s.Media;
        MediaPanel.Visibility = home && media.HasSession ? Visibility.Visible : Visibility.Collapsed;
        ClockPanel.Visibility = home && !media.HasSession ? Visibility.Visible : Visibility.Collapsed;
        ShelfPanel.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        AlertsPanel.Visibility = alerts ? Visibility.Visible : Visibility.Collapsed;
        int alertCount = _s.Notifications.History.Count;
        AlertsEmpty.Text = _s.Notifications.IsAvailable ? "No notifications" : _s.Notifications.Problem ?? "Notifications are starting…";
        AlertsEmpty.Visibility = alertCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsScroll.Visibility = alertCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShelfClear.Visibility = (shelf && _s.Shelf.Items.Count > 0) || (alerts && alertCount > 0) ? Visibility.Visible : Visibility.Collapsed;

        BatteryPanel.Visibility = Visibility.Visible;
        Battery.Visibility = BatteryText.Visibility = _s.Battery.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (_s.Battery.HasBattery)
        {
            Battery.Set(_s.Battery.Percent, _s.Battery.IsCharging);
            BatteryText.Text = $"{_s.Battery.Percent}%";
        }

        if (media.HasSession)
        {
            TitleText.Text = media.Title;
            ArtistText.Text = media.Artist;
            CompactView.ApplyArt(ArtFrame, media, 24);
            Bars.Color = media.ArtColor;
            Bars.Active = media.IsPlaying;
            PlayButton.Content = media.IsPlaying ? "" : "";
            PrevButton.IsEnabled = media.CanSkipPrevious;
            NextButton.IsEnabled = media.CanSkipNext;
            Track.IsEnabled = media.CanSeek;
        }
        Tick();
    }

    void Tick()
    {
        if (_s is null) return;
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t");
        DateText.Text = now.ToString("dddd, d MMMM");

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
    void OnArtClicked(object sender, MouseButtonEventArgs e) => _s?.Media.TogglePlayPause();

    void OnTrackDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        Track.CaptureMouse();
        SetProgress(e.GetPosition(Track).X / Track.ActualWidth);
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
        if (_s is null) return;
        double f = Math.Clamp(e.GetPosition(Track).X / Track.ActualWidth, 0, 1);
        _s.Media.Seek(TimeSpan.FromSeconds(f * _s.Media.Duration.TotalSeconds));
    }

    void RefreshShelf()
    {
        if (_s is null) return;
        int n = _s.Shelf.Items.Count;
        ShelfEmpty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfScroll.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShelfBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfCount.Text = n.ToString();
        if (ShelfTab.IsChecked == true) ShelfClear.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnClearShelf(object sender, MouseButtonEventArgs e)
    {
        if (AlertsTab.IsChecked == true) _s?.Notifications.ClearHistory();
        else _s?.Shelf.Clear();
    }

    void OnAlertClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Notifications.ShammyNotification n) NotificationOpened?.Invoke(n);
    }

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
        foreach (var action in ShelfActions) Add(action.Label, () => action.Run(item));
        Add("Remove from shelf", () => _s?.Shelf.Remove(item));
        menu.PlacementTarget = (UIElement)sender;
        // Keep the card open while the menu is up (the pointer leaves the notch to reach it).
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
}
