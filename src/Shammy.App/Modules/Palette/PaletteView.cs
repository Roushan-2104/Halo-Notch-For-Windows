using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Core.Palette;

namespace Shammy.Modules.Palette;

/// <summary>What one row shows; built by the module, drawn by <see cref="PaletteView"/>.</summary>
internal sealed record PaletteRowView(PaletteItem Item, string Glyph, ImageSource? Icon, FrameworkElement? Control);

/// <summary>
/// The rows under the typing bar: icon, title, subtitle and a hint on the right; the chosen row is lit.
/// Rows with a <see cref="PaletteRowView.Control"/> (a slider, a switch) are taller and host it under
/// the title. Nothing here takes keyboard focus, so typing always stays in the box.
/// </summary>
internal sealed class PaletteView : Border
{
    public const double RowHeight = 44, ControlRowHeight = 58, TopPad = 4, BottomPad = 10;

    readonly StackPanel _rows = new();
    readonly List<Border> _rowBorders = new();
    readonly List<TextBlock> _hints = new();
    readonly List<TextBlock> _titles = new();
    IReadOnlyList<PaletteRowView> _shown = Array.Empty<PaletteRowView>();
    Point? _lastMouse;

    public PaletteView()
    {
        Focusable = false;
        Padding = new Thickness(8, TopPad, 8, BottomPad);
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(10, 0, 10, 4),
            Background = Brush("Stroke1"),
        });
        stack.Children.Add(_rows);
        Child = stack;
    }

    /// <summary>A row was clicked; the flag is true with Ctrl held.</summary>
    public event Action<int, bool>? RowInvoked;

    /// <summary>The pointer moved onto a row (it really moved: a notch growing under a still pointer doesn't count).</summary>
    public event Action<int>? RowHovered;

    /// <summary>Draws the rows and returns the height they need (0 for none).</summary>
    public double Render(IReadOnlyList<PaletteRowView> rows, int selected)
    {
        _shown = rows;
        _rows.Children.Clear();
        _rowBorders.Clear();
        _hints.Clear();
        _titles.Clear();
        double height = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = Build(rows[i], i);
            _rows.Children.Add(row);
            height += row.Height;
        }
        Select(selected);
        return rows.Count == 0 ? 0 : height + TopPad + BottomPad + 5;
    }

    /// <summary>Lights a row (-1: none, and then the web row says Enter, since Enter searches as before).</summary>
    public void Select(int selected)
    {
        int enterRow = selected >= 0 ? selected : _shown.Count > 0 && _shown[^1].Item.Kind == PaletteKind.Web ? _shown.Count - 1 : -1;
        for (int i = 0; i < _rowBorders.Count; i++)
        {
            _rowBorders[i].Background = i == selected ? Brush("Fill1") : Brushes.Transparent;
            var item = _shown[i].Item;
            _hints[i].Text = i == enterRow ? EnterHint(item) : item.Hint;
            _hints[i].Foreground = i == enterRow ? Brush("Fg") : Brush("Fg3");
            ((Border)_hints[i].Parent).Background = i == enterRow ? Brush("Fill2") : Brushes.Transparent;
        }
    }

    static string EnterHint(PaletteItem item) => item.Kind switch
    {
        PaletteKind.File => "Enter · Ctrl+Enter folder",
        _ when item.Tag is PaletteEntry { EnterHint: { } hint } => hint(),
        _ when item.Tag is PaletteEntry { Adjustable: true } => "← → · Enter",
        _ => "Enter",
    };

    Border Build(PaletteRowView view, int index)
    {
        var item = view.Item;
        bool tall = view.Control is not null;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(0, tall ? 8 : 0, 0, 0),
            Background = view.Icon is null ? Brush("Fill1") : Brushes.Transparent,
            Child = view.Icon is { } img
                ? new Image { Source = img, Width = 24, Height = 24, Stretch = Stretch.Uniform }
                : new TextBlock
                {
                    Text = view.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                    FontSize = 14,
                    Foreground = Brush("Fg"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
        };
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("Fg"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Children.Add(title);
        _titles.Add(title);
        if (tall)
        {
            view.Control!.Margin = new Thickness(0, 6, 0, 0);
            if (view.Control.Parent is Panel old) old.Children.Remove(view.Control);
            text.Children.Add(view.Control);
        }
        else if (item.Subtitle.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Subtitle,
                FontSize = 11.5,
                Foreground = Brush("Fg2"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var hint = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
        var hintBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(0, tall ? 12 : 0, 0, 0),
            Child = hint,
        };
        Grid.SetColumn(hintBox, 2);
        grid.Children.Add(hintBox);
        _hints.Add(hint);

        var border = new Border
        {
            Height = tall ? ControlRowHeight : RowHeight,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = grid,
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            RowInvoked?.Invoke(index, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        };
        border.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (_lastMouse is { } last && (last - p).Length < 2) return;
            bool first = _lastMouse is null;
            _lastMouse = p;
            if (!first) RowHovered?.Invoke(index);
        };
        _rowBorders.Add(border);
        return border;
    }

    /// <summary>
    /// Changes one row's title in place. A slider being dragged lives in that row; rebuilding the rows
    /// would pull it out of the tree and drop the drag.
    /// </summary>
    public void SetTitle(int index, string title)
    {
        if (index >= 0 && index < _titles.Count) _titles[index].Text = title;
    }

    /// <summary>Forget the pointer position, so the first move after the palette opens isn't read as a hover.</summary>
    public void ResetPointer() => _lastMouse = null;

    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>A thin dark-theme slider (0–1) that never takes focus; drag or click anywhere on the track.</summary>
internal sealed class MiniSlider : Grid
{
    readonly Border _fill;
    readonly Border _thumb;
    double _value;

    public MiniSlider()
    {
        Height = 16;
        Focusable = false;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = PaletteView.Brush("Fill2"), VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _thumb = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(track);
        Children.Add(_fill);
        Children.Add(_thumb);
        SizeChanged += (_, _) => Layout();
        MouseLeftButtonDown += (_, e) =>
        {
            CaptureMouse();
            FromPointer(e);
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (IsMouseCaptured) FromPointer(e);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
        };
    }

    /// <summary>Raised for user changes only (drag, click, arrow keys through the palette).</summary>
    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Layout();
        }
    }

    public void Nudge(double delta)
    {
        Value = _value + delta;
        ValueChanged?.Invoke(_value);
    }

    void FromPointer(MouseEventArgs e)
    {
        if (ActualWidth <= 0) return;
        Value = e.GetPosition(this).X / ActualWidth;
        ValueChanged?.Invoke(_value);
    }

    void Layout()
    {
        double w = Math.Max(0, ActualWidth);
        _fill.Width = w * _value;
        _thumb.Margin = new Thickness(Math.Clamp(w * _value - 6, 0, Math.Max(0, w - 12)), 0, 0, 0);
    }
}

/// <summary>An on/off pill switch.</summary>
internal sealed class MiniSwitch : Border
{
    readonly Border _knob;
    bool? _on;

    public MiniSwitch()
    {
        Width = 36;
        Height = 20;
        CornerRadius = new CornerRadius(10);
        Focusable = false;
        Cursor = Cursors.Hand;
        HorizontalAlignment = HorizontalAlignment.Left;
        _knob = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _knob;
        State = null;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_on is { } on) Toggled?.Invoke(!on);
        };
    }

    public event Action<bool>? Toggled;

    /// <summary>Null while unknown (dimmed).</summary>
    public bool? State
    {
        get => _on;
        set
        {
            _on = value;
            Opacity = value is null ? 0.4 : 1;
            if (value == true) SetResourceReference(BackgroundProperty, "AccentBrush");
            else Background = PaletteView.Brush("Fill3");
            _knob.Background = value == true ? PaletteView.Brush("Fg") : PaletteView.Brush("Fg2");
            _knob.Margin = new Thickness(value == true ? 19 : 3, 0, 0, 0);
        }
    }
}
