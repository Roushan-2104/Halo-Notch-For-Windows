using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Halo.Core.Clipboard;

namespace Halo.Modules.ClipboardHistory;

/// <summary>The "Clipboard" tab: history rows (click to copy back), pin/remove on hover, and Clear.</summary>
public partial class ClipboardPanel : UserControl
{
    readonly ClipHistory _history;
    readonly Action<ClipEntry> _copy;
    readonly DispatcherTimer _flash;

    internal ClipboardPanel(ClipHistory history, Action<ClipEntry> copy)
    {
        InitializeComponent();
        _history = history;
        _copy = copy;
        _flash = new DispatcherTimer(TimeSpan.FromSeconds(1.4), DispatcherPriority.Background, (_, _) =>
        {
            _flash!.Stop();
            UpdateHeader();
        }, Dispatcher) { IsEnabled = false };
        _history.Changed += () =>
        {
            if (IsVisible) Refresh();
        };
        // Times are relative ("5m"), so rebuild whenever the tab is shown rather than ticking.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    /// <summary>Briefly replace the header with a confirmation ("Copied").</summary>
    internal void Flash(string text)
    {
        Header.Text = text;
        Header.Foreground = (Brush)FindResource("Fg");
        _flash.Stop();
        _flash.Start();
    }

    void Refresh()
    {
        var now = DateTime.Now;
        Rows.ItemsSource = _history.Items.Select(e => new ClipRow(e, now)).ToList();
        bool empty = _history.Items.Count == 0;
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = _history.Items.Count > _history.PinnedCount ? Visibility.Visible : Visibility.Collapsed;
        if (!_flash.IsEnabled) UpdateHeader();
    }

    void UpdateHeader()
    {
        int n = _history.Items.Count, pinned = _history.PinnedCount;
        Header.Foreground = (Brush)FindResource("Fg2");
        Header.Text = n == 0 ? "Clipboard"
            : (n == 1 ? "1 item" : $"{n} items") + (pinned > 0 ? $" · {pinned} pinned" : "");
    }

    static ClipEntry? EntryOf(object sender) => ((sender as FrameworkElement)?.DataContext as ClipRow)?.Entry;

    void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (EntryOf(sender) is { } entry) _copy(entry);
    }

    void OnPinClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (EntryOf(sender) is { } entry) _history.SetPinned(entry, !entry.Pinned);
    }

    void OnRemoveClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (EntryOf(sender) is { } entry) _history.Remove(entry);
    }

    void OnClear(object sender, MouseButtonEventArgs e) => _history.Clear();
}

/// <summary>What one history row shows.</summary>
internal sealed class ClipRow
{
    public ClipRow(ClipEntry entry, DateTime now)
    {
        Entry = entry;
        switch (entry.Kind)
        {
            case ClipKind.Image:
                Glyph = "";
                Title = "Image";
                Subtitle = ClipText.Dimensions(entry.Width, entry.Height);
                if (entry.Tag is ClipImage img)
                {
                    var brush = new ImageBrush(img.Thumb) { Stretch = Stretch.UniformToFill };
                    brush.Freeze();
                    Thumb = brush;
                }
                break;
            case ClipKind.Files:
                Glyph = "";
                Title = ClipText.FilesTitle(entry.Files);
                Subtitle = entry.Files.Length == 1 ? Path.GetDirectoryName(entry.Files[0]) ?? "File" : $"{entry.Files.Length} files";
                Tip = string.Join("\n", entry.Files.Take(12)) + (entry.Files.Length > 12 ? "\n…" : "");
                break;
            default:
                Glyph = "";
                Title = ClipText.FirstLine(entry.Text);
                int lines = entry.Text.Split('\n').Count(l => l.Trim().Length > 0);
                Subtitle = (lines > 1 ? $"{lines} lines · " : "") + $"{entry.Text.Length:N0} characters";
                Tip = entry.Text.Length > 400 ? entry.Text[..400] + "…" : entry.Text;
                break;
        }
        TimeText = ClipText.Relative(entry.Time, now);
        PinGlyph = entry.Pinned ? "" : "";
        PinTip = entry.Pinned ? "Unpin" : "Pin to top";
        PinVisibility = entry.Pinned ? Visibility.Visible : Visibility.Collapsed;
    }

    public ClipEntry Entry { get; }
    public string Glyph { get; } = "";
    public string Title { get; } = "";
    public string Subtitle { get; } = "";
    public string? Tip { get; }
    public Brush? Thumb { get; }
    public string TimeText { get; }
    public string PinGlyph { get; }
    public string PinTip { get; }
    public Visibility PinVisibility { get; }
}
