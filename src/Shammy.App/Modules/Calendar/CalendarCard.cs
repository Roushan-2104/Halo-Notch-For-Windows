using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shammy.Core.Calendar;
using Shammy.Modules.Kit;

namespace Shammy.Modules.Calendar;

/// <summary>
/// The calendar card from the v2 mockup. Compact (260×176): the week with event dots, then the next meeting's
/// time, title, "in 10 min" and a Join button. Detail (924×248): today's timeline (8 AM–8 PM, stretched to
/// take in earlier or later events) with a red now-line, and the next three days. Times follow the user's
/// 12/24-hour clock; a failed refresh says so (with when it last worked) on both.
/// </summary>
internal sealed class CalendarCard
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] Shades = { NotchPalette.Calendar, "#2F74E0", "#8AB9FF" };

    readonly CalendarModule _module;
    readonly Grid _compact = new() { Margin = new Thickness(14) };
    readonly UniformGrid _week = new() { Columns = 7, Rows = 1, VerticalAlignment = VerticalAlignment.Top };
    readonly StackPanel _next = new() { VerticalAlignment = VerticalAlignment.Bottom };
    readonly ContentControl _join = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 18) };

    public CalendarCard(CalendarModule module)
    {
        _module = module;
        _compact.Children.Add(_week);
        _compact.Children.Add(_next);
        _compact.Children.Add(_join);
        Refresh();
    }

    public FrameworkElement Compact => _compact;

    // ---- Compact ---------------------------------------------------------------------------------

    public void Refresh()
    {
        var now = DateTime.Now;
        var items = _module.Items;
        BuildWeek(_week, items, now);
        _next.Children.Clear();
        _join.Content = null;

        if (!_module.Connected && items.Count == 0)
        {
            _next.Children.Add(CardKit.Caption("Calendar"));
            _next.Children.Add(CardKit.Label("Connect Google Calendar", 14, FontWeights.Bold));
            var connect = CardKit.PillButton("Connect…", _module.OpenSetup, height: 26, background: CardKit.Brush(NotchPalette.Calendar),
                hover: CardKit.Brush("#6AACFF"), foreground: CardKit.Brush("#000000"), fontSize: 12);
            connect.HorizontalAlignment = HorizontalAlignment.Left;
            connect.Margin = new Thickness(0, 8, 0, 0);
            _next.Children.Add(connect);
            AutomationName("Calendar: not connected. Connect Google Calendar");
            return;
        }

        bool h24 = CardKit.H24;
        var next = CalendarAgenda.Next(items, now);
        if (next is null)
        {
            _next.Children.Add(WithProblem(CardKit.Caption("Next")));
            _next.Children.Add(CardKit.Label("Nothing coming up", 16, FontWeights.Bold));
            _next.Children.Add(CardKit.Label("Your week is clear", 12, FontWeights.Medium, CardKit.Text2));
            AutomationName("Calendar: nothing coming up");
            return;
        }

        var caption = CardKit.Row(6, CardKit.Dot(6, CardKit.Brush(NotchPalette.Calendar)), CardKit.Caption("Next"));
        _next.Children.Add(WithProblem(caption));
        var time = new StackPanel { Orientation = Orientation.Horizontal };
        time.Children.Add(CardKit.Big(next.Start.Date == now.Date ? CalendarText.Clock(next.Start, h24) : next.Start.ToString("ddd", Inv)));
        var small = CardKit.Label(next.Start.Date == now.Date ? CalendarText.AmPm(next.Start, h24) : CalendarText.ClockAmPm(next.Start, h24), 12, FontWeights.SemiBold, CardKit.Text2);
        small.VerticalAlignment = VerticalAlignment.Bottom;
        small.Margin = new Thickness(5, 0, 0, 6);
        time.Children.Add(small);
        _next.Children.Add(time);

        var line = new TextBlock { FontFamily = CardKit.Font, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = CardKit.Text2 };
        line.Inlines.Add(new System.Windows.Documents.Run(next.Title) { Foreground = CardKit.Text, FontWeight = FontWeights.SemiBold });
        var until = CalendarText.Until(next.Start, next.End, now);
        line.Inlines.Add(new System.Windows.Documents.Run($" · {until}" + (next.Provider is { } p ? $" · {p}" : "")) { FontWeight = FontWeights.Medium });
        _next.Children.Add(line);

        if (next.HasLink)
        {
            _join.Content = JoinButton(next);
            // Leave the title room to the left of the Join button.
            line.Margin = new Thickness(0, 0, 0, 0);
            time.Margin = new Thickness(0, 0, 80, 0);
        }
        AutomationName($"Calendar: next {next.Title} at {CalendarText.ClockAmPm(next.Start, h24)}, {until}");
    }

    /// <summary>The caption, plus "· Couldn't refresh" in the alarm colour while the last fetch failed (the detail says when it last worked).</summary>
    FrameworkElement WithProblem(FrameworkElement caption)
    {
        if (_module.SyncProblem is not { } problem) return caption;
        var note = CardKit.Label("· Couldn't refresh", 11, FontWeights.SemiBold, CardKit.Brush(NotchPalette.Alarm));
        note.ToolTip = problem;
        return CardKit.Row(6, caption, note);
    }

    void AutomationName(string name) => CardKit.AutomationName(_compact, name);

    Border JoinButton(CalendarOccurrence o) =>
        CardKit.PillButton("Join", () => _module.Join(o), height: 30, glyph: "", background: CardKit.Brush(NotchPalette.Calendar),
            hover: CardKit.Brush("#6AACFF"), foreground: CardKit.Brush("#000000"), fontSize: 13, tip: $"Join {o.Title} on {o.Provider}");

    static void BuildWeek(UniformGrid grid, IReadOnlyList<CalendarOccurrence> items, DateTime now)
    {
        grid.Children.Clear();
        var accent = CardKit.Brush(NotchPalette.Calendar);
        foreach (var day in CalendarAgenda.Week(now))
        {
            bool today = day == now.Date;
            bool any = CalendarAgenda.OnDay(items, day).Count > 0;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var letter = CardKit.Label(day.ToString("ddd", Inv)[..1], 10, FontWeights.SemiBold, CardKit.Text2);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(letter);
            var num = CardKit.Label(day.Day.ToString(Inv), 12, today ? FontWeights.Bold : FontWeights.SemiBold, today ? CardKit.Brush("#000000") : CardKit.Text);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = today ? accent : Brushes.Transparent,
                Margin = new Thickness(0, 3, 0, 3),
                Child = num,
            });
            var dot = CardKit.Dot(4, any ? (today ? accent : CardKit.Text2) : Brushes.Transparent);
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(dot);
            grid.Children.Add(col);
        }
    }

    // ---- Detail ----------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var now = DateTime.Now;
        var items = _module.Items;
        var root = new Grid { MinHeight = NotchPalette.DetailHeight };

        if (!_module.Connected && items.Count == 0)
        {
            root.Children.Add(new CalendarSetup(_module, null, compact: true) { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 });
            return root;
        }

        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });

        root.Children.Add(TodayPanel(items, now));
        var right = UpcomingPanel(items, now);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        return root;
    }

    FrameworkElement TodayPanel(IReadOnlyList<CalendarOccurrence> items, DateTime now)
    {
        var panel = new DockPanel { LastChildFill = true };

        bool h24 = CardKit.H24;
        // Header: the date on the left, the next meeting and Join on the right.
        var header = new DockPanel { LastChildFill = false };
        var date = new StackPanel();
        date.Children.Add(CardKit.Caption("Today"));
        date.Children.Add(CardKit.Label(now.ToString("dddd, d MMMM", Inv), 17, FontWeights.Bold));
        header.Children.Add(date);
        if (CalendarAgenda.Next(items, now) is { } next && next.Start.Date == now.Date)
        {
            if (next.HasLink)
            {
                var join = JoinButton(next);
                join.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(join, Dock.Right);
                header.Children.Add(join);
            }
            var info = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var cap = CardKit.Row(6, CardKit.Dot(6, CardKit.Brush(NotchPalette.Calendar)),
                CardKit.Caption($"Next · {CalendarText.Until(next.Start, next.End, now)}"));
            cap.HorizontalAlignment = HorizontalAlignment.Right;
            info.Children.Add(cap);
            var what = CardKit.Label($"{next.Title} · {CalendarText.ClockAmPm(next.Start, h24)}" + (next.Provider is { } p ? $" · {p}" : ""), 13, FontWeights.SemiBold);
            what.HorizontalAlignment = HorizontalAlignment.Right;
            what.MaxWidth = 300;
            info.Children.Add(what);
            DockPanel.SetDock(info, Dock.Right);
            header.Children.Add(info);
        }
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        // Footer: source and the now marker legend.
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var src = CardKit.Row(6, CardKit.Glyph("", 11, CardKit.Text2), CardKit.Label("From your Google Calendar", 11, FontWeights.Medium, CardKit.Text2));
        // Why it's out of date, or when it last updated if that was a while ago.
        if (_module.SyncProblem is { } problem)
            src.Children.Add(CardKit.Label($" · {problem}", 11, FontWeights.SemiBold, CardKit.Brush(NotchPalette.Alarm)));
        else if (_module.FetchedAt is { } f && now - f > TimeSpan.FromMinutes(30))
            src.Children.Add(CardKit.Label($" · updated {CalendarText.ClockAmPm(f, h24)}", 11, FontWeights.Medium, CardKit.Text2));
        footer.Children.Add(src);
        var nowLegend = CardKit.Row(5, new Border { Width = 8, Height = 2, CornerRadius = new CornerRadius(1), Background = CardKit.Brush(NotchPalette.Alarm) },
            CardKit.Label($"Now {CalendarText.ClockAmPm(now, h24)}", 11, FontWeights.Medium, CardKit.Text2));
        nowLegend.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(nowLegend);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        var today = CalendarAgenda.OnDay(items, now).Where(o => !o.AllDay).ToList();
        var allDay = CalendarAgenda.OnDay(items, now).Where(o => o.AllDay).ToList();
        var timeline = new Timeline(today, now, CalendarAgenda.TimelineHours(today, now), h24) { Margin = new Thickness(4, 14, 4, 0), VerticalAlignment = VerticalAlignment.Top };
        var middle = new StackPanel();
        middle.Children.Add(timeline);
        if (allDay.Count > 0)
        {
            var chip = CardKit.Chip(CardKit.Label("All day · " + string.Join(", ", allDay.Select(a => a.Title)), 11, FontWeights.SemiBold,
                CardKit.Brush(NotchPalette.Calendar)), NotchPalette.Calendar, 22);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.Margin = new Thickness(4, 6, 0, 0);
            middle.Children.Add(chip);
        }
        if (today.Count == 0)
        {
            var free = CardKit.Label("Nothing else on today", 12, FontWeights.Medium, CardKit.Text2);
            free.Margin = new Thickness(4, 8, 0, 0);
            middle.Children.Add(free);
        }
        panel.Children.Add(middle);
        return panel;
    }

    static FrameworkElement UpcomingPanel(IReadOnlyList<CalendarOccurrence> items, DateTime now)
    {
        var outer = new Border { BorderBrush = CardKit.Brush(NotchPalette.CardStroke), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(18, 0, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(CardKit.Caption("Next 3 days"));
        int shown = 0;
        for (int i = 1; i <= 3; i++)
        {
            var day = now.Date.AddDays(i);
            var list = CalendarAgenda.OnDay(items, day).Where(o => o.Start >= day || o.AllDay).ToList();
            if (list.Count == 0) continue;
            var first = list.FirstOrDefault(o => !o.AllDay) ?? list[0];
            int more = list.Count - 1;
            string meta = first.AllDay ? "All day" : CalendarText.ClockAmPm(first.Start, CardKit.H24);
            meta += more > 0 ? $" · {more} more {(more == 1 ? "event" : "events")}" : first.Provider is { } p ? $" · {p}" : $" · {CalendarText.Length(first)}";
            stack.Children.Add(UpcomingRow(day, first.Title, meta, Shades[shown % Shades.Length]));
            shown++;
        }
        if (shown == 0)
        {
            var none = CardKit.Label("Nothing in the next 3 days", 12, FontWeights.Medium, CardKit.Text2);
            none.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(none);
        }
        outer.Child = stack;
        return outer;
    }

    static Border UpcomingRow(DateTime day, string title, string meta, string colour)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dayCol = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dow = CardKit.Label(day.ToString("ddd", Inv).ToUpperInvariant(), 10, FontWeights.SemiBold, CardKit.Brush(NotchPalette.Calendar));
        dow.HorizontalAlignment = HorizontalAlignment.Center;
        var num = CardKit.Label(day.Day.ToString(Inv), 18, FontWeights.Bold);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        dayCol.Children.Add(dow);
        dayCol.Children.Add(num);
        g.Children.Add(dayCol);

        var bar = new Border { CornerRadius = new CornerRadius(2), Background = CardKit.Brush(colour) };
        Grid.SetColumn(bar, 2);
        g.Children.Add(bar);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(CardKit.Label(title, 13, FontWeights.SemiBold));
        text.Children.Add(CardKit.Label(meta, 11, FontWeights.Medium, CardKit.Text2));
        Grid.SetColumn(text, 4);
        g.Children.Add(text);

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = CardKit.Brush("#18181B"),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = g,
        };
    }

    /// <summary>Hour labels, the day's bar (8 AM–8 PM or wider) with event blocks and the red now-line, and event labels under it; lays itself out by width.</summary>
    sealed class Timeline : Grid
    {
        readonly List<CalendarOccurrence> _events;
        readonly DateTime _now;
        readonly int _startHour, _endHour;
        readonly bool _h24;
        readonly Canvas _hours = new() { Height = 14 }, _bar = new() { Height = 34, ClipToBounds = false }, _labels = new() { Height = 34 };

        public Timeline(List<CalendarOccurrence> events, DateTime now, (int Start, int End) hours, bool h24)
        {
            _events = events;
            _now = now;
            (_startHour, _endHour) = hours;
            _h24 = h24;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            Children.Add(_hours);
            var track = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = CardKit.Brush("#18181B"),
                BorderBrush = CardKit.Brush(NotchPalette.CardStroke),
                BorderThickness = new Thickness(1),
            };
            SetRow(track, 2);
            Children.Add(track);
            SetRow(_bar, 2);
            Children.Add(_bar);
            SetRow(_labels, 4);
            Children.Add(_labels);
            SizeChanged += (_, e) =>
            {
                if (e.WidthChanged) Lay(e.NewSize.Width);
            };
        }

        static double Hours(DateTime t, DateTime day) => (t - day.Date).TotalHours;

        void Lay(double width)
        {
            _hours.Children.Clear();
            _bar.Children.Clear();
            _labels.Children.Clear();
            if (width <= 0) return;
            double dayStart = _startHour, daySpan = Math.Max(2, _endHour - _startHour);
            double X(double hour) => Math.Clamp((hour - dayStart) / daySpan, 0, 1) * width;

            // A label every 2 hours (every 4 on a stretched day, so they never crowd).
            int step = daySpan > 12 ? 4 : 2;
            for (int h = _startHour; h <= _endHour; h += step)
            {
                var label = CardKit.Label(CalendarText.HourLabel(h, _h24), 10, FontWeights.SemiBold, CardKit.Text2);
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lw = label.DesiredSize.Width;
                double x = X(h);
                Canvas.SetLeft(label, h == _startHour ? 0 : h >= _endHour ? x - lw : x - lw / 2);
                _hours.Children.Add(label);
                if (h > _startHour && h < _endHour)
                {
                    var line = new Border { Width = 1, Height = 34, Background = CardKit.Brush("#232326") };
                    Canvas.SetLeft(line, x);
                    _bar.Children.Add(line);
                }
            }

            double lastLabelRight = double.NegativeInfinity;
            for (int i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                double s = Hours(e.Start, _now), en = Hours(e.End, _now);
                if (en <= dayStart || s >= dayStart + daySpan) continue;
                double left = X(s), right = X(en);
                var block = new Border
                {
                    Width = Math.Max(6, right - left),
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = CardKit.Brush(Shades[i % Shades.Length]),
                    Opacity = e.End <= _now ? 0.45 : 1,
                    ToolTip = $"{e.Title} · {CalendarText.Range(e.Start, e.End, _h24)}",
                };
                Canvas.SetLeft(block, left);
                Canvas.SetTop(block, 5);
                _bar.Children.Add(block);

                // Labels go under their block; one that would collide with the previous is skipped (the tooltip has it).
                var label = new StackPanel();
                label.Children.Add(CardKit.Label(e.Title, 12, FontWeights.SemiBold));
                label.Children.Add(CardKit.Label(CalendarText.Range(e.Start, e.End, _h24), 11, FontWeights.Medium, CardKit.Text2));
                label.Measure(new Size(160, double.PositiveInfinity));
                double lw = Math.Min(160, label.DesiredSize.Width);
                label.MaxWidth = 160;
                double lx = Math.Min(left, width - lw);
                if (lx < lastLabelRight + 8) continue;
                Canvas.SetLeft(label, lx);
                _labels.Children.Add(label);
                lastLabelRight = lx + lw;
            }

            double nowH = Hours(_now, _now);
            if (nowH >= dayStart && nowH <= dayStart + daySpan)
            {
                var red = CardKit.Brush(NotchPalette.Alarm);
                var line = new Border { Width = 2, Height = 46, CornerRadius = new CornerRadius(1), Background = red };
                Canvas.SetLeft(line, X(nowH) - 1);
                Canvas.SetTop(line, -6);
                _bar.Children.Add(line);
                var dot = CardKit.Dot(8, red);
                Canvas.SetLeft(dot, X(nowH) - 4);
                Canvas.SetTop(dot, -9);
                _bar.Children.Add(dot);
            }
        }
    }
}
