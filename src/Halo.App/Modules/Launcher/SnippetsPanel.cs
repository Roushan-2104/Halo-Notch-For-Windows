using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Halo.Modules.Launcher;

/// <summary>The "Snippets" tab: trigger → expansion rows, styled like the notification history.</summary>
internal sealed class SnippetsPanel : Grid
{
    readonly StackPanel _rows = new();
    readonly ScrollViewer _scroll;
    readonly TextBlock _empty;

    public event Action<string>? DeleteRequested;

    public SnippetsPanel()
    {
        Margin = new Thickness(0, 6, 0, 0);
        _empty = new TextBlock
        {
            Text = "No snippets yet. Type “snippet ;sig Best regards” to add one.",
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _rows,
        };
        Children.Add(_empty);
        Children.Add(_scroll);
    }

    public void Show(IEnumerable<KeyValuePair<string, string>> snippets)
    {
        _rows.Children.Clear();
        foreach (var (trigger, expansion) in snippets.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
            _rows.Children.Add(Row(trigger, expansion));
        bool any = _rows.Children.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement Row(string trigger, string expansion)
    {
        var row = new Grid { Height = 40, Background = Brushes.Transparent };
        var bg = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(-6, 0, -6, 0), Background = Brushes.Transparent };
        row.Children.Add(bg);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(content);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150,
            Child = new TextBlock { Text = trigger, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Fill1");
        content.Children.Add(chip);

        // One line: show line breaks as a return arrow.
        var text = new TextBlock
        {
            Text = expansion.Replace("\r\n", "\n").Replace("\n", " ↵ "),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = expansion,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        Grid.SetColumn(text, 2);
        content.Children.Add(text);

        var delete = new Button
        {
            Content = "",
            FontSize = 9,
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Delete snippet",
            Visibility = Visibility.Hidden,
        };
        delete.SetResourceReference(StyleProperty, "IconButton");
        delete.Click += (_, _) => DeleteRequested?.Invoke(trigger);
        Grid.SetColumn(delete, 3);
        content.Children.Add(delete);

        row.MouseEnter += (_, _) =>
        {
            bg.SetResourceReference(Border.BackgroundProperty, "Fill1");
            delete.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            bg.Background = Brushes.Transparent;
            delete.Visibility = Visibility.Hidden;
        };
        return row;
    }
}
