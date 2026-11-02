using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Halo.Core.Productivity;

namespace Halo.Modules.Productivity;

/// <summary>
/// The Notes tab: upcoming reminders first (soonest at the top), then fired reminders and notes,
/// newest first. Click a note to copy it; the cross deletes. Styled like the notification history.
/// </summary>
internal sealed class NotesPanel : Grid
{
    readonly Func<ProductivityStore> _store;
    readonly Action<string> _delete;
    readonly Action<string> _copy;
    readonly StackPanel _rows = new();
    readonly ScrollViewer _scroll;
    readonly StackPanel _empty;

    public NotesPanel(Func<ProductivityStore> store, Action<string> delete, Action<string> copy)
    {
        _store = store;
        _delete = delete;
        _copy = copy;
        Margin = new Thickness(0, 6, 0, 0);

        _empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _empty.Children.Add(new TextBlock
        {
            Text = "No notes or reminders",
            FontSize = 12.5,
            Foreground = Res("Fg2"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _empty.Children.Add(new TextBlock
        {
            Text = "Try \u201Cnote: buy milk\u201D or \u201Cremind me at 6pm to \u2026\u201D",
            FontSize = 11.5,
            Foreground = Res("Fg3"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Children.Add(_empty);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _rows,
        };
        Children.Add(_scroll);

        // Times read "Today"/"Tomorrow", so rebuild whenever the tab comes into view.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    public void Refresh()
    {
        var store = _store();
        var now = DateTime.Now;
        _rows.Children.Clear();

        foreach (var r in store.Reminders.Where(r => !r.Fired).OrderBy(r => r.Due))
            _rows.Children.Add(Row(r.Id, "\uEA8F", accent: true, r.Title, TimeFormat.When(r.Due, now), null));

        var older = store.Reminders.Where(r => r.Fired)
            .Select(r => (When: r.FiredAt!.Value, Row: (Func<FrameworkElement>)(() =>
                Row(r.Id, "\uEA8F", accent: false, r.Title, $"Reminded \u00B7 {TimeFormat.When(r.Due, now)}", null))))
            .Concat(store.Notes.Select(n => (When: n.Created, Row: (Func<FrameworkElement>)(() =>
                Row(n.Id, "\uE70B", accent: false, n.Text, $"Note \u00B7 {TimeFormat.When(n.Created, now)}", n.Text)))))
            .OrderByDescending(x => x.When);
        foreach (var item in older) _rows.Children.Add(item.Row());

        bool any = _rows.Children.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One 44 px row: icon tile, title and detail, and a delete cross on hover. <paramref name="copyText"/> makes it click-to-copy.</summary>
    FrameworkElement Row(string id, string glyph, bool accent, string title, string detail, string? copyText)
    {
        var row = new Grid { Height = 44, Background = Brushes.Transparent };
        var bg = new Border { CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Margin = new Thickness(-6, 0, -6, 0) };
        row.Children.Add(bg);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(grid);

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (accent) icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        else icon.Foreground = Res("Fg3");
        grid.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Res("Fill1"), Child = icon });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("Fg"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var sub = new TextBlock { Text = detail, FontSize = 11.5, Foreground = Res("Fg2"), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(sub);
        SetColumn(text, 2);
        grid.Children.Add(text);

        var remove = new Button
        {
            Style = (Style)Application.Current.Resources["IconButton"],
            Content = "\uE711",
            FontSize = 8,
            Width = 22,
            Height = 22,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Delete",
            Visibility = Visibility.Hidden,
        };
        remove.Click += (_, _) => _delete(id);
        SetColumn(remove, 3);
        grid.Children.Add(remove);

        row.MouseEnter += (_, _) =>
        {
            bg.Background = Res("Fill1");
            remove.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            bg.Background = Brushes.Transparent;
            remove.Visibility = Visibility.Hidden;
        };

        if (copyText is not null)
        {
            row.Cursor = Cursors.Hand;
            row.ToolTip = "Click to copy";
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (IsInside<Button>(e.OriginalSource as DependencyObject)) return;
                _copy(copyText);
                sub.Text = "Copied";
                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                back.Tick += (_, _) =>
                {
                    back.Stop();
                    sub.Text = detail;
                };
                back.Start();
            };
        }
        return row;
    }

    static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
