using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Productivity;
using Shammy.Views.Cards;

namespace Shammy.Modules.Productivity;

/// <summary>What the timer card needs from the productivity module: its live clocks and the real actions.</summary>
internal interface ITimerCardHost
{
    IReadOnlyList<RunningClock> Clocks { get; }
    void TogglePause(RunningClock clock);
    void Stop(RunningClock clock);
    void Extend(RunningClock clock, TimeSpan more);
    void QuickStart(TimeSpan duration);
}

/// <summary>
/// The v2 timer card (Timer.dc.html): a ring draining in the timer colour around big digits, with pause/resume
/// and stop. Its detail shows the active clock large, everything else running (pomodoro rounds, a paused timer,
/// the stopwatch) and quick-add chips that start real timers. A finished timer shows in the alarm colour for
/// a few seconds. Nothing here runs a clock of its own: the module's per-second tick refreshes it while visible.
/// </summary>
internal sealed class TimerCard
{
    static readonly TimeSpan DoneShown = TimeSpan.FromSeconds(8);

    readonly ITimerCardHost _host;
    readonly Brush _accent = CardKit.Res("TimerAccent"), _alarm = CardKit.Res("AlarmAccent");
    readonly Grid _compact = new() { Width = 176, Height = Shammy.Modules.NotchPalette.CardHeight };
    readonly Ring _ring = new() { Width = 104, Height = 104, Thickness = 7 };
    readonly TextBlock _tag = CardKit.Label("");
    readonly TextBlock _digits = CardKit.Big("", 28);
    readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer _doneTimer = new(DispatcherPriority.Normal) { Interval = DoneShown };

    string? _done; // label of a timer that just finished, while its alarm state shows
    string _buttonsKey = "";
    Detail? _detail;

    public TimerCard(ITimerCardHost host)
    {
        _host = host;
        _ring.Effect = CardKit.Shadow(CardKit.Color(Shammy.Modules.NotchPalette.Timer), 8, 0.45);
        _tag.HorizontalAlignment = HorizontalAlignment.Center;
        _digits.HorizontalAlignment = HorizontalAlignment.Center;
        var center = CardKit.Column(0, _tag, _digits);
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center;
        var dial = new Grid { Width = 104, Height = 104, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        dial.Children.Add(_ring);
        dial.Children.Add(center);

        var content = new Grid { Margin = CardKit.CompactPadding };
        content.Children.Add(dial);
        _buttons.VerticalAlignment = VerticalAlignment.Bottom;
        content.Children.Add(_buttons);
        _compact.Children.Add(content);

        _compact.IsVisibleChanged += (_, _) => Refresh();
        _doneTimer.Tick += (_, _) =>
        {
            _doneTimer.Stop();
            _done = null;
            Changed?.Invoke();
            Refresh();
        };
        Refresh();
    }

    /// <summary>Relevance may have changed (a finished timer's alarm state ended).</summary>
    public event Action? Changed;

    public FrameworkElement Compact => _compact;

    public bool IsRelevant => _host.Clocks.Count > 0 || _done is not null;

    /// <summary>The clock the card is about: the newest running countdown, else the newest countdown, else the stopwatch.</summary>
    RunningClock? Active => _host.Clocks.LastOrDefault(c => c.IsCountdown && !c.Paused)
                            ?? _host.Clocks.LastOrDefault(c => c.IsCountdown)
                            ?? _host.Clocks.LastOrDefault();

    /// <summary>"Tea 4:32" while something runs.</summary>
    public GlanceItem? Glance()
    {
        if (_done is { } done) return new GlanceItem(TimerCardText.Glance(done, "done"), Shammy.Modules.NotchPalette.Alarm, 40);
        if (Active is not { Paused: false } c) return null;
        return new GlanceItem(TimerCardText.Glance(Name(c), c.Display(DateTime.UtcNow)), Shammy.Modules.NotchPalette.Timer, 40);
    }

    /// <summary>A timer ran out: the alarm colour for a few seconds.</summary>
    public void ShowDone(string label)
    {
        _done = label.Length > 0 ? label : "Timer";
        _doneTimer.Stop();
        _doneTimer.Start();
        Refresh();
    }

    static string Name(RunningClock c) => c.Kind switch
    {
        ClockKind.Stopwatch => "Stopwatch",
        ClockKind.Pomodoro => "Pomodoro",
        ClockKind.Focus => "Focus",
        _ => c.Label.Length > 0 ? c.Label : "Timer",
    };

    /// <summary>Redraws the visible parts; cheap no-op while nothing is on screen.</summary>
    public void Refresh()
    {
        _detail?.Refresh();
        if (!_compact.IsVisible && _buttonsKey.Length > 0) return;
        var now = DateTime.UtcNow;
        var c = Active;
        string key;
        if (c is null)
        {
            bool done = _done is not null;
            _ring.Fill = done ? _alarm : _accent;
            _ring.Value = done ? 1 : 0;
            _tag.Text = done ? $"{_done} \u00B7 done".ToUpperInvariant() : "TIMER";
            _tag.Foreground = done ? _alarm : CardKit.Res("CardText2");
            _digits.Text = "0:00";
            _digits.FontSize = 28;
            _digits.Foreground = CardKit.Res(done ? "CardText" : "CardText2");
            key = done ? "done" : "none";
        }
        else
        {
            _ring.Fill = _accent;
            _ring.Value = c.IsCountdown ? TimerCardText.RingLeft(c.Remaining(now), c.Total) : TimerCardText.StopwatchRing(c.Elapsed(now));
            _tag.Text = (c.Paused ? $"{Name(c)} \u00B7 paused" : Name(c)).ToUpperInvariant();
            _tag.Foreground = CardKit.Res("CardText2");
            var display = c.Display(now);
            _digits.Text = display;
            _digits.FontSize = display.Length > 5 ? 22 : 28;
            _digits.Foreground = CardKit.Res(c.Paused ? "CardText2" : "CardText");
            key = $"{c.Id}:{c.Paused}";
        }
        if (key == _buttonsKey) return;
        _buttonsKey = key;
        _buttons.Children.Clear();
        foreach (var b in Buttons(c, 36)) _buttons.Children.Add(b);
        _ring.Effect = RingGlow(c, 8);
    }

    /// <summary>The ring's soft glow, in the alarm colour once a timer has run out.</summary>
    System.Windows.Media.Effects.Effect RingGlow(RunningClock? c, double blur) =>
        CardKit.Shadow(CardKit.Color(c is null && _done is not null ? Shammy.Modules.NotchPalette.Alarm : Shammy.Modules.NotchPalette.Timer), blur, 0.45);

    /// <summary>Pause/resume (primary) and stop for a clock; dismiss for a finished one.</summary>
    List<Button> Buttons(RunningClock? c, double size)
    {
        var list = new List<Button>();
        var text = CardKit.Res("CardText");
        if (c is null)
        {
            if (_done is not null)
                list.Add(CardKit.RoundButton("Dismiss", CardKit.Icon(CardKit.Paths.Stop, Brushes.Black, 14), Dismiss, _alarm, size));
            return list;
        }
        var toggle = c.Paused
            ? CardKit.Icon(CardKit.Paths.Play, Brushes.Black, 16, 2, Brushes.Black)
            : CardKit.Icon(CardKit.Paths.Pause, Brushes.Black, 16, 2.6);
        list.Add(CardKit.RoundButton(c.Paused ? $"Resume {Name(c)}" : $"Pause {Name(c)}", toggle, () => _host.TogglePause(c), _accent, size));
        var stop = CardKit.RoundButton($"Stop {Name(c)}", CardKit.Icon(CardKit.Paths.Stop, text, 14), () => _host.Stop(c), null, size);
        stop.Margin = new Thickness(12, 0, 0, 0);
        list.Add(stop);
        return list;
    }

    void Dismiss()
    {
        _doneTimer.Stop();
        _done = null;
        Changed?.Invoke();
        Refresh();
    }

    // ---- Detail ---------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var detail = new Detail(this);
        _detail = detail;
        detail.Root.Loaded += (_, _) => _detail = detail;
        detail.Root.Unloaded += (_, _) =>
        {
            if (_detail == detail) _detail = null;
        };
        return detail.Root;
    }

    sealed class Detail
    {
        readonly TimerCard _card;
        readonly Ring _ring = new() { Width = 160, Height = 160, Thickness = 9.6 };
        readonly TextBlock _tag = CardKit.Label(""), _digits = CardKit.Big("", 40), _of = CardKit.Text("", 11, FontWeights.Medium, secondary: true);
        readonly TextBlock _state = CardKit.Label("Active"), _name = CardKit.Text("", 20, FontWeights.Bold), _ends = CardKit.Text("", 12, FontWeights.Medium, secondary: true);
        readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        readonly ContentControl _extend = new() { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel _others = new() { Margin = new Thickness(0, 10, 0, 0) };
        readonly TextBlock _nothing = CardKit.Text("Nothing else running", 13, FontWeights.Medium, secondary: true);
        string _structure = "";

        public Detail(TimerCard card)
        {
            _card = card;
            _ring.Effect = CardKit.Shadow(CardKit.Color(Shammy.Modules.NotchPalette.Timer), 10, 0.45);
            foreach (var t in new[] { _tag, _digits, _of }) t.HorizontalAlignment = HorizontalAlignment.Center;
            var center = CardKit.Column(0, _tag, _digits, _of);
            center.VerticalAlignment = VerticalAlignment.Center;
            var dial = new Grid { Width = 160, Height = 160 };
            dial.Children.Add(_ring);
            dial.Children.Add(center);
            var info = CardKit.Column(4, _state, _name, _ends);
            info.Children.Add(_buttons);
            info.Children.Add(_extend);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(18, 0, 0, 0);
            var active = CardKit.Row(0, dial, info);
            active.Width = 290;
            active.VerticalAlignment = VerticalAlignment.Center;

            var also = new DockPanel();
            var alsoHead = CardKit.Label("Also running");
            DockPanel.SetDock(alsoHead, Dock.Top);
            also.Children.Add(alsoHead);
            _nothing.Margin = new Thickness(0, 10, 0, 0);
            _nothing.VerticalAlignment = VerticalAlignment.Top;
            var alsoBody = new Grid();
            alsoBody.Children.Add(_others);
            alsoBody.Children.Add(_nothing);
            also.Children.Add(alsoBody);

            var quick = new DockPanel { Width = 170 };
            var quickHead = CardKit.Label("Quick add");
            DockPanel.SetDock(quickHead, Dock.Top);
            quick.Children.Add(quickHead);
            var hint = CardKit.Text("Or say \u201CShammy, 10 minute timer\u201D", 11, FontWeights.Medium, secondary: true);
            hint.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(hint, Dock.Bottom);
            quick.Children.Add(hint);
            var chips = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            foreach (var m in TimerCardText.QuickMinutes)
            {
                var chip = CardKit.Chip($"{m}m", $"Start a {m} minute timer", () => card._host.QuickStart(TimeSpan.FromMinutes(m)));
                chip.Margin = new Thickness(0, 0, 8, 8);
                chips.Children.Add(chip);
            }
            quick.Children.Add(chips);

            var row = new DockPanel();
            DockPanel.SetDock(active, Dock.Left);
            row.Children.Add(active);
            var d1 = CardKit.VerticalDivider();
            DockPanel.SetDock(d1, Dock.Left);
            row.Children.Add(d1);
            DockPanel.SetDock(quick, Dock.Right);
            row.Children.Add(quick);
            var d2 = CardKit.VerticalDivider();
            DockPanel.SetDock(d2, Dock.Right);
            row.Children.Add(d2);
            row.Children.Add(also);

            Root = CardKit.DetailPanel(row);
            Refresh();
        }

        public Border Root { get; }

        public void Refresh()
        {
            var now = DateTime.UtcNow;
            var c = _card.Active;
            var others = _card._host.Clocks.Where(k => k != c).Reverse().Take(3).ToList();

            if (c is null)
            {
                bool done = _card._done is not null;
                _ring.Fill = done ? _card._alarm : _card._accent;
                _ring.Value = done ? 1 : 0;
                _tag.Text = done ? "DONE" : "TIMER";
                _tag.Foreground = done ? _card._alarm : CardKit.Res("CardText2");
                _digits.Text = "0:00";
                _of.Text = "";
                _state.Text = done ? "TIME\u2019S UP" : "NO TIMER";
                _state.Foreground = done ? _card._alarm : CardKit.Res("CardText2");
                _name.Text = done ? _card._done! : "Start one";
                _ends.Text = done ? "Finished just now" : "Pick a length on the right";
            }
            else
            {
                _ring.Fill = _card._accent;
                _ring.Value = c.IsCountdown ? TimerCardText.RingLeft(c.Remaining(now), c.Total) : TimerCardText.StopwatchRing(c.Elapsed(now));
                _tag.Text = (c.Paused ? $"{Name(c)} \u00B7 paused" : Name(c)).ToUpperInvariant();
                _tag.Foreground = CardKit.Res("CardText2");
                var display = c.Display(now);
                _digits.Text = display;
                _digits.FontSize = display.Length > 5 ? 32 : 40;
                _digits.Foreground = CardKit.Res(c.Paused ? "CardText2" : "CardText");
                _of.Text = c.IsCountdown ? TimerCardText.Of(c.Total) : "elapsed";
                _state.Text = c.Paused ? "PAUSED" : "ACTIVE";
                _state.Foreground = _card._accent;
                _name.Text = Name(c);
                _ends.Text = c.IsCountdown
                    ? TimerCardText.Ends(c.EndsLocal(now) ?? DateTime.Now, c.Paused, display)
                    : c.Paused ? "Paused" : "Running";
            }

            // Buttons and rows are rebuilt only when what's running changes, not every second.
            var structure = $"{c?.Id}:{c?.Paused}:{_card._done}|{string.Join(",", others.Select(o => $"{o.Id}:{o.Paused}:{o.PomodoroStep}"))}";
            if (structure != _structure)
            {
                _structure = structure;
                _ring.Effect = _card.RingGlow(c, 10);
                _buttons.Children.Clear();
                foreach (var b in _card.Buttons(c, 36)) _buttons.Children.Add(b);
                _extend.Content = c is { IsCountdown: true }
                    ? CardKit.Chip("+1 min", $"Add one minute to {Name(c)}", () => _card._host.Extend(c, TimeSpan.FromMinutes(1)), 30, 12, 15)
                    : null;
                _others.Children.Clear();
                foreach (var o in others) _others.Children.Add(OtherRow(o));
                _nothing.Visibility = others.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            // Live values in the rows.
            for (int i = 0; i < others.Count && i < _others.Children.Count; i++)
                if (_others.Children[i] is Border { Tag: OtherParts parts }) parts.Update(others[i], now);
        }

        sealed record OtherParts(TextBlock Time, Border? Fill, double FillWidth)
        {
            public void Update(RunningClock c, DateTime now)
            {
                Time.Text = c.Display(now);
                if (Fill is not null) Fill.Width = FillWidth * (1 - TimerCardText.RingLeft(c.Remaining(now), c.Total));
            }
        }

        Border OtherRow(RunningClock c)
        {
            var now = DateTime.UtcNow;
            var icon = CardKit.Icon(c.Kind == ClockKind.Stopwatch ? CardKit.Paths.Stopwatch : CardKit.Paths.Timer,
                c.Paused ? CardKit.Res("CardText2") : _card._accent, 18);
            var time = CardKit.Big(c.Display(now), 20);
            time.Foreground = CardKit.Res(c.Paused ? "CardText2" : "CardText");
            var title = CardKit.Text(Name(c) + (c.Paused ? " \u00B7 paused" : ""), 13, FontWeights.SemiBold);
            var sub = new StackPanel();
            sub.Children.Add(title);
            Border? fill = null;
            const double barWidth = 150;
            if (c.Kind == ClockKind.Pomodoro)
            {
                var line = CardKit.Row(8, CardKit.Text(TimerCardText.PomodoroLine(c.PomodoroStep), 11, FontWeights.Medium, secondary: true), Dots(c.PomodoroStep));
                sub.Children.Add(line);
            }
            else if (c.IsCountdown)
            {
                fill = CardKit.Bar(_card._accent, 0, 4, 2);
                fill.Opacity = c.Paused ? 0.55 : 1;
                fill.HorizontalAlignment = HorizontalAlignment.Left;
                var track = new Grid { Width = barWidth, Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
                track.Children.Add(CardKit.Bar(CardKit.RingTrack, barWidth, 4, 2));
                track.Children.Add(fill);
                sub.Children.Add(track);
            }
            else
            {
                sub.Children.Add(CardKit.Text("Counting up", 11, FontWeights.Medium, secondary: true));
            }
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            DockPanel.SetDock(time, Dock.Right);
            dock.Children.Add(time);
            sub.Margin = new Thickness(12, 0, 12, 0);
            sub.VerticalAlignment = VerticalAlignment.Center;
            dock.Children.Add(sub);
            var parts = new OtherParts(time, fill, barWidth);
            parts.Update(c, now);
            return new Border
            {
                Height = 52,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(14),
                Background = CardKit.RowBg,
                Margin = new Thickness(0, 0, 0, 6),
                Child = dock,
                Tag = parts,
            };
        }

        FrameworkElement Dots(int step)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = $"Round {Pomodoro.Round(step)} of 4" };
            foreach (var dot in TimerCardText.PomodoroDots(step))
            {
                row.Children.Add(new Border
                {
                    Width = 7,
                    Height = 7,
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(3.5),
                    Background = dot == RoundDot.Pending ? CardKit.ControlHover : _card._accent,
                    BorderBrush = dot == RoundDot.Current ? CardKit.Hex("#9ADDFC") : null,
                    BorderThickness = new Thickness(dot == RoundDot.Current ? 1.5 : 0),
                });
            }
            return row;
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        var now = DateTime.UtcNow;
        if (Active is not { } c)
            return _done is { } d ? $"timer: done label={d} ring=1.00 colour=alarm buttons=dismiss" : "timer: nothing running (card not relevant)";
        double ring = c.IsCountdown ? TimerCardText.RingLeft(c.Remaining(now), c.Total) : TimerCardText.StopwatchRing(c.Elapsed(now));
        return $"timer: label={Name(c)} digits={c.Display(now)} ring={ring:0.00} paused={c.Paused} buttons={(c.Paused ? "resume" : "pause")},stop " +
               $"running={_host.Clocks.Count} visible={_compact.IsVisible}";
    }

    public string DescribeDetail()
    {
        var now = DateTime.UtcNow;
        var c = Active;
        string Row(RunningClock k) => k.Kind == ClockKind.Pomodoro
            ? $"{Name(k)} {k.Display(now)} {TimerCardText.PomodoroLine(k.PomodoroStep)} dots={string.Join("", TimerCardText.PomodoroDots(k.PomodoroStep).Select(d => d switch { RoundDot.Done => "x", RoundDot.Current => "o", _ => "." }))}"
            : $"{Name(k)} {k.Display(now)}{(k.Paused ? " paused" : "")}";
        var active = c is null ? (_done is { } d ? $"done({d})" : "none")
            : $"{Name(c)} {c.Display(now)} {(c.IsCountdown ? TimerCardText.Of(c.Total) : "elapsed")} {(c.IsCountdown ? TimerCardText.Ends(c.EndsLocal(now) ?? DateTime.Now, c.Paused, c.Display(now)) : "")}".TrimEnd();
        var others = _host.Clocks.Where(k => k != c).Reverse().Take(3).Select(Row).ToList();
        return $"timer detail: active=[{active}] alsoRunning=[{(others.Count == 0 ? "-" : string.Join(" | ", others))}] " +
               $"quickAdd={string.Join(",", TimerCardText.QuickMinutes.Select(m => m + "m"))}";
    }
}
