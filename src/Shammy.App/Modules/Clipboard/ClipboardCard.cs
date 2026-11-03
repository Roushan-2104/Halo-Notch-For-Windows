using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Clipboard;
using Shammy.Modules.Kit;

namespace Shammy.Modules.ClipboardHistory;

/// <summary>
/// The v2 clipboard card. Compact (176×176): the three latest items as mini rows (link, text, image thumbnail…)
/// and "+ N more". Detail (924×248): a sideways row of preview cards with where each came from, when, Copy,
/// pin and remove; pinned ones are outlined. A search box and a Pinned filter narrow the row.
/// </summary>
internal sealed class ClipboardCard
{
    const string Accent = NotchPalette.Purple;

    readonly ClipHistory _history;
    readonly Action<ClipEntry> _copy;
    readonly Action<string> _open;
    readonly Func<Action?> _clear;
    readonly StackPanel _compactRows = new();
    readonly TextBlock _compactMore = CardKit.Label("", 11, FontWeights.SemiBold, CardKit.Text2);
    readonly Grid _compact = new() { Margin = new Thickness(14) };

    /// <param name="clear">Clears the unpinned history and returns how to undo it (null when nothing was cleared).</param>
    public ClipboardCard(ClipHistory history, Action<ClipEntry> copy, Action<string> open, Func<Action?> clear)
    {
        _history = history;
        _copy = copy;
        _open = open;
        _clear = clear;
        BuildCompactFrame();
        _history.Changed += () =>
        {
            if (_compact.IsVisible) RefreshCompact();
        };
        // Relative times and the latest items: rebuild whenever it comes into view rather than ticking.
        _compact.IsVisibleChanged += (_, _) =>
        {
            if (_compact.IsVisible) RefreshCompact();
        };
        RefreshCompact();
    }

    public FrameworkElement Compact => _compact;

    // ---- Compact ---------------------------------------------------------------------------------

    void BuildCompactFrame()
    {
        _compact.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _compact.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _compact.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel();
        var icon = CardKit.Glyph("", 13, CardKit.Brush(Accent));
        DockPanel.SetDock(icon, Dock.Right);
        head.Children.Add(icon);
        head.Children.Add(CardKit.Caption("Clipboard"));
        _compact.Children.Add(head);

        _compactRows.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_compactRows, 1);
        _compact.Children.Add(_compactRows);

        var foot = CardKit.Row(6, CardKit.Dot(6, CardKit.Brush(Accent)), _compactMore);
        Grid.SetRow(foot, 2);
        _compact.Children.Add(foot);
    }

    void RefreshCompact()
    {
        _compactRows.Children.Clear();
        var items = _history.Items.OrderByDescending(e => e.Time).ToList();
        if (items.Count == 0)
        {
            var empty = CardKit.Label("Copy something and it shows up here", 12, FontWeights.Medium, CardKit.Text2);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            empty.Margin = new Thickness(0, 6, 0, 0);
            _compactRows.Children.Add(empty);
            _compactMore.Text = "Nothing yet";
            CardKit.AutomationName(_compact, "Clipboard, empty. Open clipboard");
            return;
        }
        foreach (var e in items.Take(3))
        {
            var row = new DockPanel { Height = 30, VerticalAlignment = VerticalAlignment.Center };
            var tile = MiniTile(e);
            tile.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(tile);
            row.Children.Add(CardKit.Label(ShortText(e), 11, FontWeights.Medium));
            _compactRows.Children.Add(new Border
            {
                Height = 30,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(10),
                Background = CardKit.Raised,
                BorderBrush = CardKit.RaisedStroke,
                BorderThickness = new Thickness(1),
                Child = row,
            });
        }
        int more = items.Count - 3;
        _compactMore.Text = more > 0 ? $"+ {more} more" : items.Count == 1 ? "1 item" : $"{items.Count} items";
        CardKit.AutomationName(_compact, $"Clipboard, {items.Count} items. Latest: {ShortText(items[0])}. Open clipboard");
    }

    static string ShortText(ClipEntry e) => ClipCardText.Shape(e) switch
    {
        ClipShape.Image => $"Image · {ClipText.Dimensions(e.Width, e.Height)}",
        ClipShape.Files => ClipText.FilesTitle(e.Files),
        ClipShape.Link => ClipCardText.LinkLabel(e.Text),
        _ => ClipText.FirstLine(e.Text, 60),
    };

    /// <summary>The 16 px kind tile: white link, purple lines, a colour swatch, or the image itself.</summary>
    static FrameworkElement MiniTile(ClipEntry e, double size = 16)
    {
        var shape = ClipCardText.Shape(e);
        if (shape == ClipShape.Image && e.Tag is ClipImage img) return ClipImages.Tile(img.Thumb, size * 1.4, size, 4);
        if (shape == ClipShape.Colour && ClipCardText.ColourOf(e.Text) is { } c)
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) };
        var (glyph, bg, fg) = shape switch
        {
            ClipShape.Link => ("", "#F5F5F7", "#000000"),
            ClipShape.Code => ("", "#1E2A3A", NotchPalette.Calendar),
            ClipShape.Files => ("", "#2A2236", Accent),
            ClipShape.Image => ("", "#2A2236", Accent),
            _ => ("", "#2A2236", Accent),
        };
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = CardKit.Brush(bg),
            Child = CardKit.Glyph(glyph, size * 0.6, CardKit.Brush(fg)),
        };
    }

    // ---- Detail ----------------------------------------------------------------------------------

    public FrameworkElement BuildDetail() => new Detail(this);

    sealed class Detail : DockPanel
    {
        readonly ClipboardCard _card;
        readonly TextBlock _count = CardKit.Label("", 15, FontWeights.Bold);
        readonly TextBlock _pinnedLabel = CardKit.Label("", 11, FontWeights.SemiBold, CardKit.Brush(Accent));
        readonly Border _pinnedChip, _allChip, _clearChip, _undoChip;
        readonly System.Windows.Threading.DispatcherTimer _undoExpiry = new() { Interval = ClipboardModule.UndoTime };
        Action? _undo;
        readonly TextBox _search;
        readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
        readonly ScrollViewer _scroll;
        readonly TextBlock _empty = CardKit.Label("", 12.5, FontWeights.Medium, CardKit.Text2);
        bool _pinnedOnly;

        public Detail(ClipboardCard card)
        {
            _card = card;
            LastChildFill = true;
            MinHeight = NotchPalette.DetailHeight;

            var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(_count);

            _pinnedChip = CardKit.Button(CardKit.Row(6, CardKit.Glyph("", 11, CardKit.Brush(Accent)), _pinnedLabel),
                () => SetFilter(!_pinnedOnly), CardKit.Tint(Accent, 0.12), CardKit.Tint(Accent, 0.22), 26, tip: "Show only pinned");
            _pinnedChip.BorderBrush = CardKit.Tint(Accent, 0.35);
            _pinnedChip.BorderThickness = new Thickness(1);
            _pinnedChip.Padding = new Thickness(10, 0, 10, 0);
            _pinnedChip.Margin = new Thickness(14, 0, 0, 0);
            header.Children.Add(_pinnedChip);

            _allChip = CardKit.PillButton("All", () => SetFilter(false), 26, background: CardKit.Raised, hover: CardKit.RoundHover,
                foreground: CardKit.Text2, tip: "Show everything");
            _allChip.BorderBrush = CardKit.RaisedStroke;
            _allChip.BorderThickness = new Thickness(1);
            _allChip.Margin = new Thickness(6, 0, 0, 0);
            header.Children.Add(_allChip);

            _clearChip = CardKit.PillButton("Clear", Clear, 26, background: CardKit.Raised, hover: CardKit.RoundHover,
                foreground: CardKit.Text2, tip: "Clear everything that isn't pinned");
            _clearChip.BorderBrush = CardKit.RaisedStroke;
            _clearChip.BorderThickness = new Thickness(1);
            _clearChip.Margin = new Thickness(6, 0, 0, 0);
            header.Children.Add(_clearChip);
            // For 8 s after clearing, the Clear chip's place offers Undo.
            _undoChip = CardKit.PillButton("Undo", Undo, 26, background: CardKit.Raised, hover: CardKit.RoundHover,
                foreground: CardKit.Text, tip: "Put the cleared items back");
            _undoChip.BorderBrush = CardKit.RaisedStroke;
            _undoChip.BorderThickness = new Thickness(1);
            _undoChip.Margin = new Thickness(6, 0, 0, 0);
            _undoChip.Visibility = Visibility.Collapsed;
            header.Children.Add(_undoChip);
            _undoExpiry.Tick += (_, _) => ShowUndo(null);

            _search = new TextBox { Style = (Style)Application.Current.Resources["BareTextBox"], FontSize = 12, FontFamily = CardKit.Font };
            var hint = CardKit.Label("Search clipboard", 12, FontWeights.Normal, CardKit.Text2);
            hint.IsHitTestVisible = false;
            _search.TextChanged += (_, _) =>
            {
                hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Refresh();
            };
            var searchRow = new DockPanel();
            var lens = CardKit.Glyph("", 12, CardKit.Text2);
            lens.Margin = new Thickness(0, 0, 8, 0);
            searchRow.Children.Add(lens);
            searchRow.Children.Add(new Grid { Children = { hint, _search } });
            var searchBox = new Border
            {
                Width = 260,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = CardKit.Raised,
                BorderBrush = CardKit.RaisedStroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 0, 12, 0),
                Child = searchRow,
            };
            DockPanel.SetDock(searchBox, Dock.Right);
            header.Children.Add(searchBox);
            DockPanel.SetDock(header, Dock.Top);
            Children.Add(header);

            _scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = _row,
            };
            // A vertical wheel scrolls the row sideways.
            _scroll.PreviewMouseWheel += (_, e) =>
            {
                _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            };
            _empty.HorizontalAlignment = HorizontalAlignment.Center;
            _empty.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new Grid { Children = { _scroll, _empty } });

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _search.Focus();
                    e.Handled = true;
                }
            };

            Action changed = () => Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
            Loaded += (_, _) => _card._history.Changed += changed.Invoke;
            Unloaded += (_, _) =>
            {
                _card._history.Changed -= changed.Invoke;
                _undoExpiry.Stop();
            };
            Refresh();
        }

        void Clear() => ShowUndo(_card._clear());

        void Undo()
        {
            var undo = _undo;
            ShowUndo(null);
            undo?.Invoke();
        }

        /// <summary>Offer Undo in the Clear chip's place for a few seconds (null puts Clear back).</summary>
        void ShowUndo(Action? undo)
        {
            _undo = undo;
            _undoExpiry.Stop();
            if (undo is not null) _undoExpiry.Start();
            _undoChip.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
            Refresh();
        }

        void SetFilter(bool pinnedOnly)
        {
            _pinnedOnly = pinnedOnly;
            Refresh();
        }

        void Refresh()
        {
            var now = DateTime.Now;
            var all = _card._history.Items;
            int pinned = _card._history.PinnedCount;
            _count.Inlines.Clear();
            _count.Inlines.Add(new System.Windows.Documents.Run(all.Count == 1 ? "1 item" : $"{all.Count} items"));
            _count.Inlines.Add(new System.Windows.Documents.Run("  · last 24 hours") { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = CardKit.Text2 });
            _pinnedLabel.Text = $"Pinned · {pinned}";
            _pinnedChip.Visibility = pinned > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (pinned == 0) _pinnedOnly = false;
            _pinnedChip.Opacity = _pinnedOnly ? 1 : 0.75;
            _allChip.Opacity = _pinnedOnly ? 0.75 : 1;
            _clearChip.Visibility = _undo is null && all.Count > pinned ? Visibility.Visible : Visibility.Collapsed;

            var query = _search.Text.Trim();
            var shown = all.Where(e => !_pinnedOnly || e.Pinned).Where(e => Matches(e, query)).ToList();
            _row.Children.Clear();
            foreach (var e in shown) _row.Children.Add(_card.PreviewCard(e, now));
            _empty.Text = all.Count == 0 ? "Copy something and it shows up here" : shown.Count == 0 ? "Nothing matches" : "";
            _empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static bool Matches(ClipEntry e, string query)
        {
            if (query.Length == 0) return true;
            return e.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Files.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
                || (e.Kind == ClipKind.Image && "image".Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>One preview card in the detail row (196 wide; a colour is 150).</summary>
    FrameworkElement PreviewCard(ClipEntry e, DateTime now)
    {
        var shape = ClipCardText.Shape(e);
        var body = new DockPanel { LastChildFill = true };

        // Footer: source and age, Copy (and Open for links).
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        TextBlock? copyLabel = null;
        var copy = CardKit.PillButton("Copy", () =>
        {
            _copy(e);
            if (copyLabel is not null) Flash(copyLabel);
        }, 26, tip: "Copy");
        copyLabel = (copy.Child as StackPanel)?.Children.OfType<TextBlock>().LastOrDefault();
        DockPanel.SetDock(copy, Dock.Right);
        footer.Children.Add(copy);
        if (shape == ClipShape.Link)
        {
            var open = CardKit.RoundButton("", () => _open(e.Text.Trim()), 26, "Open link", 11);
            open.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(open, Dock.Right);
            footer.Children.Add(open);
        }
        footer.Children.Add(CardKit.Label(ClipCardText.Footer(e, now), 11, FontWeights.Normal, CardKit.Text2));
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);

        // Header: kind tile and label, then pin and remove.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var remove = CardKit.Button(CardKit.Glyph("", 8, CardKit.Text2), () => _history.Remove(e), Brushes.Transparent, CardKit.RoundHover, 20, 20, tip: "Remove");
        remove.Visibility = Visibility.Hidden;
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);
        var pin = CardKit.Button(CardKit.Glyph(e.Pinned ? "" : "", 11, e.Pinned ? CardKit.Brush(Accent) : CardKit.Text2),
            () => _history.SetPinned(e, !e.Pinned), Brushes.Transparent, CardKit.RoundHover, 20, 20, tip: e.Pinned ? "Unpin" : "Pin");
        pin.Visibility = e.Pinned ? Visibility.Visible : Visibility.Hidden;
        DockPanel.SetDock(pin, Dock.Right);
        header.Children.Add(pin);

        bool picture = shape is ClipShape.Image or ClipShape.Colour;
        if (!picture)
        {
            var tile = MiniTile(e, 18);
            tile.Margin = new Thickness(0, 0, 6, 0);
            header.Children.Add(tile);
            header.Children.Add(CardKit.Caption(ClipCardText.KindLabel(shape, e.Files.Length)));
        }
        DockPanel.SetDock(header, Dock.Top);
        if (!picture) body.Children.Add(header);

        body.Children.Add(Content(e, shape, picture ? header : null));

        var card = new Border
        {
            Width = shape == ClipShape.Colour ? 150 : 196,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            Background = CardKit.Raised,
            BorderBrush = e.Pinned ? CardKit.Tint(Accent, 0.45) : CardKit.RaisedStroke,
            BorderThickness = new Thickness(1),
            Child = body,
            Cursor = Cursors.Hand,
            ToolTip = e.Kind == ClipKind.Text ? (e.Text.Length > 400 ? e.Text[..400] + "…" : e.Text)
                : e.Kind == ClipKind.Files ? string.Join("\n", e.Files.Take(12)) : null,
        };
        card.MouseEnter += (_, _) =>
        {
            remove.Visibility = Visibility.Visible;
            pin.Visibility = Visibility.Visible;
        };
        card.MouseLeave += (_, _) =>
        {
            remove.Visibility = Visibility.Hidden;
            pin.Visibility = e.Pinned ? Visibility.Visible : Visibility.Hidden;
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            _copy(e);
            if (copyLabel is not null) Flash(copyLabel);
        };
        CardKit.AutomationName(card, $"{ClipCardText.KindLabel(shape, e.Files.Length)}: {ShortText(e)}{(e.Pinned ? ", pinned" : "")}. Click to copy");
        return card;
    }

    FrameworkElement Content(ClipEntry e, ClipShape shape, DockPanel? overlayHeader)
    {
        var stack = new StackPanel();
        switch (shape)
        {
            case ClipShape.Image:
            {
                var grid = new Grid { Height = 84 };
                if (e.Tag is ClipImage img)
                {
                    ImageSource src = img.Thumb;
                    try
                    {
                        src = ClipImages.Decode(img.Png, 352);
                    }
                    catch (Exception)
                    {
                        // Keep the small thumbnail.
                    }
                    grid.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill } });
                }
                else grid.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = CardKit.Brush("#2A2236") });
                if (overlayHeader is not null)
                {
                    overlayHeader.VerticalAlignment = VerticalAlignment.Top;
                    overlayHeader.Margin = new Thickness(4);
                    grid.Children.Add(overlayHeader);
                }
                stack.Children.Add(grid);
                var label = CardKit.Label("", 12, FontWeights.SemiBold);
                label.Inlines.Add(new System.Windows.Documents.Run("Image"));
                label.Inlines.Add(new System.Windows.Documents.Run($" · {ClipText.Dimensions(e.Width, e.Height)}") { Foreground = CardKit.Text2, FontWeight = FontWeights.Medium });
                label.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(label);
                break;
            }
            case ClipShape.Colour:
            {
                var c = ClipCardText.ColourOf(e.Text)!.Value;
                var grid = new Grid { Height = 72 };
                grid.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) });
                if (overlayHeader is not null)
                {
                    overlayHeader.VerticalAlignment = VerticalAlignment.Top;
                    overlayHeader.Margin = new Thickness(4);
                    grid.Children.Add(overlayHeader);
                }
                stack.Children.Add(grid);
                var hex = CardKit.Label($"#{c.R:X2}{c.G:X2}{c.B:X2}", 15, FontWeights.Bold);
                hex.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(hex);
                stack.Children.Add(CardKit.Label($"rgb({c.R}, {c.G}, {c.B})", 11, FontWeights.Normal, CardKit.Text2));
                break;
            }
            case ClipShape.Code:
            {
                var lines = string.Join("\n", e.Text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(6));
                stack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = CardKit.Brush("#0B0B0D"),
                    BorderBrush = CardKit.Brush(NotchPalette.CardStroke),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 7, 8, 7),
                    Height = 88,
                    ClipToBounds = true,
                    Child = new TextBlock
                    {
                        Text = lines,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 10.5,
                        Foreground = CardKit.Text,
                        TextWrapping = TextWrapping.NoWrap,
                    },
                });
                break;
            }
            case ClipShape.Link:
            {
                var title = CardKit.Label(ClipCardText.LinkLabel(e.Text), 13, FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.Wrap;
                title.MaxHeight = 70;
                stack.Children.Add(title);
                break;
            }
            case ClipShape.Files:
            {
                var title = CardKit.Label(ClipText.FilesTitle(e.Files), 13, FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.Wrap;
                title.MaxHeight = 52;
                stack.Children.Add(title);
                var where = CardKit.Label(e.Files.Length == 1 ? Path.GetDirectoryName(e.Files[0]) ?? "" : $"{e.Files.Length} files", 11, FontWeights.Normal, CardKit.Text2);
                where.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(where);
                break;
            }
            default:
            {
                var first = ClipText.FirstLine(e.Text, 200);
                var title = CardKit.Label(first, 13, FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.Wrap;
                title.MaxHeight = 36;
                stack.Children.Add(title);
                var rest = e.Text.Trim();
                int at = rest.IndexOf(first.TrimEnd('…'), StringComparison.Ordinal);
                rest = at >= 0 ? rest[(at + first.TrimEnd('…').Length)..].Trim() : "";
                if (rest.Length > 0)
                {
                    var more = CardKit.Label(ClipText.Preview(rest, 160), 11, FontWeights.Normal, CardKit.Text2);
                    more.TextWrapping = TextWrapping.Wrap;
                    more.MaxHeight = 45;
                    more.Margin = new Thickness(0, 4, 0, 0);
                    stack.Children.Add(more);
                }
                break;
            }
        }
        return stack;
    }

    static void Flash(TextBlock label)
    {
        label.Text = "Copied";
        var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        back.Tick += (_, _) =>
        {
            back.Stop();
            label.Text = "Copy";
        };
        back.Start();
    }
}
