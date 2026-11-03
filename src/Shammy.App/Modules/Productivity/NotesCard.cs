using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Connect;
using Shammy.Core.Productivity;
using Shammy.Infrastructure;
using Shammy.Modules.Kit;

namespace Shammy.Modules.Productivity;

/// <summary>One line on the Notes card: a note or a reminder, open or ticked off.</summary>
internal sealed record NoteLine(string Id, string Text, bool Done, DateTime? Due, bool Fired)
{
    public bool IsReminder => Due is not null;
}

/// <summary>
/// The v2 Notes card. Compact (176×176): two open items with checkboxes (a reminder shows its time) and a
/// progress bar for today. Detail (924×248): every note and reminder in a two-column grid; click to tick
/// off, × to delete, and a box to add a note.
/// </summary>
internal sealed class NotesCard
{
    const string Accent = NotchPalette.Purple;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly Func<ProductivityStore> _store;
    readonly Action<string> _toggle, _delete, _add;
    readonly Grid _compact = new() { Margin = new Thickness(14) };

    public NotesCard(Func<ProductivityStore> store, Action<string> toggle, Action<string> delete, Action<string> add)
    {
        _store = store;
        _toggle = toggle;
        _delete = delete;
        _add = add;
        // Times read "Today"/"Tomorrow": rebuild when it comes into view rather than ticking.
        _compact.IsVisibleChanged += (_, _) =>
        {
            if (_compact.IsVisible) Refresh();
        };
        // A screen share starting or ending hides or reveals the items in place.
        PrivacyState.Changed += _ => _compact.Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    static bool Private => PrivacyState.Presenting;

    /// <summary>The calendar's occurrences, for today's upcoming events on the card (read-only rows).</summary>
    public Func<IReadOnlyList<Shammy.Core.Calendar.CalendarOccurrence>>? Events { get; init; }

    /// <summary>Joins an event's call (only events with a recognised meeting link get a Join button).</summary>
    public Action<Shammy.Core.Calendar.CalendarOccurrence>? Join { get; init; }

    List<Shammy.Core.Calendar.CalendarOccurrence> Today(DateTime now, int max) =>
        Events is null ? new() : DayLinks.UpcomingToday(Events(), now, max);

    public FrameworkElement Compact => _compact;

    /// <summary>Open items first (reminders by time, then notes newest first), then ticked-off ones, newest first.</summary>
    internal static List<NoteLine> Lines(ProductivityStore s)
    {
        var open = s.Reminders.Where(r => !r.Done).OrderBy(r => r.Fired).ThenBy(r => r.Due)
            .Select(r => new NoteLine(r.Id, r.Title, false, r.Due, r.Fired))
            .Concat(s.Notes.Where(n => !n.Done).OrderByDescending(n => n.Created).Select(n => new NoteLine(n.Id, n.Text, false, null, false)));
        var done = s.Reminders.Where(r => r.Done).Select(r => (r.DoneAt!.Value, new NoteLine(r.Id, r.Title, true, r.Due, r.Fired)))
            .Concat(s.Notes.Where(n => n.Done).Select(n => (n.DoneAt!.Value, new NoteLine(n.Id, n.Text, true, null, false))))
            .OrderByDescending(x => x.Item1).Select(x => x.Item2);
        return open.Concat(done).ToList();
    }

    /// <summary>Today's progress: everything still open plus what was ticked off today.</summary>
    internal static (int Done, int Total) Progress(ProductivityStore s, DateTime now)
    {
        int open = s.Reminders.Count(r => !r.Done) + s.Notes.Count(n => !n.Done);
        int doneToday = s.Reminders.Count(r => r.DoneAt?.Date == now.Date) + s.Notes.Count(n => n.DoneAt?.Date == now.Date);
        return (doneToday, open + doneToday);
    }

    static string When(DateTime due, DateTime now) =>
        due.Date == now.Date ? due.ToString("h:mm tt", Inv) : TimeFormat.When(due, now);

    // ---- Compact ---------------------------------------------------------------------------------

    public void Refresh()
    {
        var store = _store();
        var now = DateTime.Now;
        var lines = Lines(store);
        var open = lines.Where(l => !l.Done).ToList();
        var (done, total) = Progress(store, now);

        _compact.Children.Clear();
        _compact.RowDefinitions.Clear();
        _compact.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _compact.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _compact.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel();
        var left = CardKit.Label(open.Count == 0 ? "All done" : $"{open.Count} left", 11, FontWeights.SemiBold, CardKit.Brush(Accent));
        DockPanel.SetDock(left, Dock.Right);
        head.Children.Add(left);
        head.Children.Add(CardKit.Caption("Notes"));
        _compact.Children.Add(head);

        var items = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (Private)
        {
            // Counts only: what the notes say stays off a shared screen.
            items.Children.Add(CardKit.Label(PrivacyText.NotesSummary(store.Notes.Count(n => !n.Done), store.Reminders.Count(r => !r.Done)),
                13, FontWeights.SemiBold));
            var hidden = CardKit.Label(PrivacyText.Hidden, 11, FontWeights.Medium, CardKit.Text2);
            hidden.TextWrapping = TextWrapping.Wrap;
            hidden.TextTrimming = TextTrimming.None;
            hidden.Margin = new Thickness(0, 4, 0, 0);
            items.Children.Add(hidden);
        }
        // Today's next calendar event shares the card with the first open item.
        var events = Private ? new() : Today(now, 1);
        if (!Private && open.Count == 0 && events.Count == 0)
        {
            var empty = CardKit.Label(lines.Count == 0 ? "Try “note: buy milk”" : "Nothing left to do", 12, FontWeights.Medium, CardKit.Text2);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            items.Children.Add(empty);
        }
        int shown = Private ? 0 : Math.Min(events.Count > 0 ? 1 : 2, open.Count);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0) items.Children.Add(Separator());
            items.Children.Add(CompactItem(open[i], now));
        }
        foreach (var e in events)
        {
            if (items.Children.Count > 0) items.Children.Add(Separator());
            items.Children.Add(CompactEvent(e, now));
        }
        Grid.SetRow(items, 1);
        _compact.Children.Add(items);

        var foot = new DockPanel();
        var count = CardKit.Label($"{done}/{total}", 11, FontWeights.SemiBold, CardKit.Text2);
        count.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(count, Dock.Right);
        foot.Children.Add(count);
        foot.Children.Add(CardKit.Progress(total == 0 ? 0 : done / (double)total, CardKit.Brush(Accent)));
        Grid.SetRow(foot, 2);
        _compact.Children.Add(foot);

        CardKit.AutomationName(_compact, $"Notes, {open.Count} open items" +
            (open.Count > 0 && !Private ? ": " + string.Join("; ", open.Take(2).Select(l => l.Text)) : "") + ". Open notes");
    }

    FrameworkElement CompactItem(NoteLine line, DateTime now)
    {
        var row = new DockPanel();
        var box = CheckBox(line.Done, 16, () => _toggle(line.Id));
        box.VerticalAlignment = VerticalAlignment.Top;
        box.Margin = new Thickness(0, 1, 9, 0);
        row.Children.Add(box);
        var text = new StackPanel();
        var title = CardKit.Label(line.Text, 13, FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 34;
        text.Children.Add(title);
        if (line.Due is { } due)
        {
            var bell = CardKit.Glyph("", 10, CardKit.Brush(Accent));
            var when = CardKit.Label(When(due, now), 11, FontWeights.Medium, CardKit.Text2);
            var r = CardKit.Row(4, bell, when);
            r.HorizontalAlignment = HorizontalAlignment.Left;
            r.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(r);
        }
        row.Children.Add(text);
        return row;
    }

    static Border Separator() => new() { Height = 1, Background = CardKit.Brush(NotchPalette.CardStroke), Margin = new Thickness(0, 8, 0, 8) };

    /// <summary>A read-only calendar line: a blue calendar glyph, the title and "10:30 AM · Zoom".</summary>
    static FrameworkElement CompactEvent(Shammy.Core.Calendar.CalendarOccurrence e, DateTime now)
    {
        var row = new DockPanel();
        var glyph = CardKit.Glyph("", 13, CardKit.Brush(NotchPalette.Calendar));
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Width = 16;
        glyph.Margin = new Thickness(0, 1, 9, 0);
        row.Children.Add(glyph);
        var text = new StackPanel();
        text.Children.Add(CardKit.Label(e.Title, 13, FontWeights.SemiBold));
        var meta = CardKit.Label(DayLinks.EventMeta(e, now), 11, FontWeights.Medium, CardKit.Text2);
        meta.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(meta);
        row.Children.Add(text);
        return row;
    }

    /// <summary>The rounded accent checkbox; clicking it toggles without opening the card.</summary>
    static Border CheckBox(bool done, double size, Action toggle)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size >= 18 ? 6 : 5),
            BorderBrush = CardKit.Brush(Accent),
            BorderThickness = new Thickness(2),
            Background = done ? CardKit.Brush(Accent) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = done ? CardKit.Glyph("", size * 0.6, CardKit.Brush("#000000")) : null,
            ToolTip = done ? "Mark as not done" : "Mark as done",
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            toggle();
        };
        return b;
    }

    // ---- Detail ----------------------------------------------------------------------------------

    public FrameworkElement BuildDetail() => new Detail(this);

    sealed class Detail : DockPanel
    {
        readonly NotesCard _card;
        readonly TextBlock _summary = CardKit.Label("", 15, FontWeights.Bold);
        readonly ContentControl _bar = new() { Width = 160, Height = 6, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly UniformGrid _grid = new() { Columns = 2, VerticalAlignment = VerticalAlignment.Top };
        readonly TextBlock _empty = CardKit.Label("No notes or reminders yet", 12.5, FontWeights.Medium, CardKit.Text2);
        readonly TextBox _input;

        public Detail(NotesCard card)
        {
            _card = card;
            MinHeight = NotchPalette.DetailHeight;

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
            var title = new StackPanel();
            title.Children.Add(CardKit.Caption("Today"));
            title.Children.Add(_summary);
            header.Children.Add(title);
            header.Children.Add(_bar);

            _input = new TextBox { Style = (Style)Application.Current.Resources["BareTextBox"], FontSize = 12, FontFamily = CardKit.Font };
            var hint = CardKit.Label("Add a note, or say “Hey Shammy, remind me…”", 12, FontWeights.Normal, CardKit.Text2);
            hint.IsHitTestVisible = false;
            _input.TextChanged += (_, _) => hint.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                var text = _input.Text.Trim();
                if (text.Length == 0) return;
                _card._add(text);
                _input.Clear();
                e.Handled = true;
                Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
            };
            var plus = CardKit.Glyph("", 12, CardKit.Brush(Accent));
            plus.Margin = new Thickness(0, 0, 8, 0);
            var inputRow = new DockPanel();
            inputRow.Children.Add(plus);
            inputRow.Children.Add(new Grid { Children = { hint, _input } });
            var box = new Border
            {
                Width = 300,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = CardKit.Raised,
                BorderBrush = CardKit.RaisedStroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 0, 12, 0),
                Child = inputRow,
                Cursor = Cursors.IBeam,
            };
            box.MouseLeftButtonUp += (_, _) => _input.Focus();
            DockPanel.SetDock(box, Dock.Right);
            header.Children.Add(box);
            DockPanel.SetDock(header, Dock.Top);
            Children.Add(header);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = _grid,
            };
            _empty.HorizontalAlignment = HorizontalAlignment.Center;
            _empty.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new Grid { Children = { scroll, _empty } });
            Action<bool> privacy = _ => Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
            Loaded += (_, _) => PrivacyState.Changed += privacy;
            Unloaded += (_, _) => PrivacyState.Changed -= privacy;
            Refresh();
        }

        public void Refresh()
        {
            var store = _card._store();
            var now = DateTime.Now;
            var lines = Lines(store);
            int open = lines.Count(l => !l.Done), done = lines.Count - open;
            _summary.Text = $"{open} to do · {done} done";
            var (doneToday, total) = Progress(store, now);
            _bar.Content = CardKit.Progress(total == 0 ? 0 : doneToday / (double)total, CardKit.Brush(Accent), 6);
            _grid.Children.Clear();
            if (Private)
            {
                _empty.Text = PrivacyText.Hidden;
                _empty.Visibility = Visibility.Visible;
                return;
            }
            // Today's calendar first (read-only, Join where there's a call link), then notes and reminders.
            var events = _card.Today(now, 4);
            foreach (var e in events) _grid.Children.Add(EventItem(e, now));
            foreach (var line in lines) _grid.Children.Add(Item(line, now));
            _empty.Text = "No notes or reminders yet";
            _empty.Visibility = lines.Count == 0 && events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement EventItem(Shammy.Core.Calendar.CalendarOccurrence e, DateTime now)
        {
            var row = new DockPanel();
            var glyph = CardKit.Glyph("", 14, CardKit.Brush(NotchPalette.Calendar));
            glyph.Width = 18;
            glyph.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(glyph);
            if (e.HasLink && _card.Join is { } join)
            {
                var button = CardKit.PillButton("Join", () => join(e), 24, "", CardKit.Brush(NotchPalette.Calendar), CardKit.Brush("#6AACFF"),
                    CardKit.Brush("#000000"), 11, $"Join {e.Title}");
                button.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(button, Dock.Right);
                row.Children.Add(button);
            }
            var pill = new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = CardKit.Tint(NotchPalette.Calendar, 0.12),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Child = CardKit.Label(DayLinks.EventMeta(e, now), 11, FontWeights.SemiBold, CardKit.Brush(NotchPalette.Calendar)),
            };
            DockPanel.SetDock(pill, Dock.Right);
            row.Children.Add(pill);
            row.Children.Add(CardKit.Label(e.Title, 13, FontWeights.SemiBold));
            var item = new Border
            {
                Height = 44,
                Margin = new Thickness(0, 0, 12, 8),
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(12),
                Background = CardKit.Raised,
                BorderBrush = CardKit.RaisedStroke,
                BorderThickness = new Thickness(1),
                Child = row,
                ToolTip = $"{e.Title} · {Shammy.Core.Calendar.CalendarText.Range(e.Start, e.End)}",
            };
            CardKit.AutomationName(item, $"{e.Title}, {DayLinks.EventMeta(e, now)}, from your calendar");
            return item;
        }

        FrameworkElement Item(NoteLine line, DateTime now)
        {
            var row = new DockPanel();
            var check = CheckBox(line.Done, 18, () => Toggle(line.Id));
            check.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(check);

            var remove = CardKit.Button(CardKit.Glyph("", 8, CardKit.Text2), () => Delete(line.Id), Brushes.Transparent, CardKit.RoundHover, 22, 22, tip: "Delete");
            remove.Visibility = Visibility.Hidden;
            remove.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            if (line.Due is { } due)
            {
                var pill = new Border
                {
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = CardKit.Tint(Accent, 0.12),
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(8, 0, 0, 0),
                    Child = CardKit.Row(5, CardKit.Glyph("", 10, CardKit.Brush(Accent)),
                        CardKit.Label(When(due, now), 11, FontWeights.SemiBold, CardKit.Brush(Accent))),
                };
                DockPanel.SetDock(pill, Dock.Right);
                row.Children.Add(pill);
            }

            var text = CardKit.Label(line.Text, 13, FontWeights.SemiBold, line.Done ? CardKit.Text2 : CardKit.Text);
            if (line.Done) text.TextDecorations = TextDecorations.Strikethrough;
            row.Children.Add(text);

            var item = new Border
            {
                Height = 44,
                Margin = new Thickness(0, 0, 12, 8),
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(12),
                Background = CardKit.Raised,
                BorderBrush = CardKit.RaisedStroke,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = row,
                ToolTip = line.Text.Length > 40 ? line.Text : null,
            };
            item.MouseEnter += (_, _) =>
            {
                item.Background = CardKit.Brush("#1E1E22");
                remove.Visibility = Visibility.Visible;
            };
            item.MouseLeave += (_, _) =>
            {
                item.Background = CardKit.Raised;
                remove.Visibility = Visibility.Hidden;
            };
            item.MouseLeftButtonUp += (_, _) => Toggle(line.Id);
            var copy = new MenuItem { Header = "Copy" };
            copy.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(line.Text);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // Clipboard busy; nothing to do.
                }
            };
            var del = new MenuItem { Header = "Delete" };
            del.Click += (_, _) => Delete(line.Id);
            item.ContextMenu = new ContextMenu { Items = { copy, del } };
            CardKit.AutomationName(item, line.Text + (line.Due is { } d ? $", reminder {When(d, now)}" : "") + (line.Done ? ", done" : ", not done"));
            return item;
        }

        void Toggle(string id)
        {
            _card._toggle(id);
            Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
        }

        void Delete(string id)
        {
            _card._delete(id);
            Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
        }
    }
}
