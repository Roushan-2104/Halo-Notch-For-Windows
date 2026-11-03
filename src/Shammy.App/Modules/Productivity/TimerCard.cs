using System.Windows;
using System.Windows.Controls;
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
/// The timer card on the Focus Wall (Wall.dc.html): a tile whose artwork is the ring itself — white, draining on a
/// dark dial — with the digits inside; focused, the name, "4:32 of 5:00", pause/resume (the primary control) and
/// stop. Its detail grows the dial into the art panel and adds pause/stop, +1/+5/+10 min, quick starts and
/// whatever else is running. A finished timer turns the dial the alarm red for a few seconds. Nothing here runs a
/// clock of its own: the module's per-second tick refreshes it while visible.
/// </summary>
internal sealed class TimerCard
{
    static readonly TimeSpan DoneShown = TimeSpan.FromSeconds(8);

    readonly ITimerCardHost _host;
    readonly WallTile _tile = new();
    readonly Ring _ring = new() { Width = 72, Height = 72, Thickness = 6 };
    readonly Tracked _digits = CardKit.Big("", 18), _rest = CardKit.Label("");
    readonly TextBlock _name, _of;
    readonly StackPanel _buttons = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 10, 0) };
    readonly DispatcherTimer _doneTimer = new(DispatcherPriority.Normal) { Interval = DoneShown };

    string? _done; // label of a timer that just finished, while its alarm state shows
    string _buttonsKey = "";
    Detail? _detail;

    public TimerCard(ITimerCardHost host)
    {
        _host = host;
        _tile.Field = Fields.Timer();
        var dial = new Grid { Width = 72, Height = 72 };
        dial.Children.Add(_ring);
        _digits.HorizontalAlignment = HorizontalAlignment.Center;
        dial.Children.Add(_digits);
        _tile.Art.Children.Add(CardKit.At(dial, HorizontalAlignment.Left, VerticalAlignment.Top, WallTile.Pad, 14));
        _tile.Rest.Children.Add(WallTile.BottomLeft(_rest));
        var words = WallTile.Words("", "", out _name, out _of);
        words.HorizontalAlignment = HorizontalAlignment.Left;
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(WallTile.Pad, 0, 52, 10);
        _tile.Lit.Children.Add(words);
        _tile.Lit.Children.Add(_buttons);

        _tile.IsVisibleChanged += (_, _) => Refresh();
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

    public FrameworkElement Compact => _tile;

    public bool IsRelevant => _host.Clocks.Count > 0 || _done is not null;

    /// <summary>Just finished beats everything but a ringing alarm; a running countdown beats a song.</summary>
    public int Live => _done is not null ? 90 : Active is { Paused: false, IsCountdown: true } ? 40 : Active is { Paused: false } ? 30 : Active is not null ? 10 : 0;

    /// <summary>The clock the card is about: the newest running countdown, else the newest countdown, else the stopwatch.</summary>
    RunningClock? Active => _host.Clocks.LastOrDefault(c => c.IsCountdown && !c.Paused)
                            ?? _host.Clocks.LastOrDefault(c => c.IsCountdown)
                            ?? _host.Clocks.LastOrDefault();

    /// <summary>"Tea 4:32" while something runs.</summary>
    public GlanceItem? Glance()
    {
        if (_done is { } done) return new GlanceItem(TimerCardText.Glance(done, "done"), NotchPalette.Alarm, 40);
        if (Active is not { Paused: false } c) return null;
        return new GlanceItem(TimerCardText.Glance(Name(c), c.Display(DateTime.UtcNow)), NotchPalette.Timer, 40);
    }

    /// <summary>The peek: "Timer · Tea · 4:32 · of 5:00 · ends at 10:25", with pause/resume.</summary>
    public WallPeek? Peek()
    {
        var now = DateTime.UtcNow;
        if (Active is not { } c)
            return _done is { } d ? new WallPeek("Time's up", d, "Finished just now", Fields.Alarm(), new WallAction("Dismiss", Dismiss, Stays: true)) : null;
        var line = c.IsCountdown ? $"{TimerCardText.Of(c.Total)} · {TimerCardText.Ends(c.EndsLocal(now) ?? DateTime.Now, c.Paused, c.Display(now), CardKit.H24).ToLowerInvariant()}" : c.Paused ? "Paused" : "Counting up";
        return new WallPeek(c.Paused ? "Timer · paused" : "Timer", $"{Name(c)} · {c.Display(now)}", line, Fields.Timer(),
            new WallAction(c.Paused ? "Resume" : "Pause", () => _host.TogglePause(c), Stays: true));
    }

    /// <summary>A timer ran out: the alarm red for a few seconds.</summary>
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

    static double RingValue(RunningClock c, DateTime now) =>
        c.IsCountdown ? TimerCardText.RingLeft(c.Remaining(now), c.Total) : TimerCardText.StopwatchRing(c.Elapsed(now));

    /// <summary>Redraws the visible parts; cheap no-op while nothing is on screen.</summary>
    public void Refresh()
    {
        _detail?.Refresh();
        if (!_tile.IsVisible && _buttonsKey.Length > 0) return;
        var now = DateTime.UtcNow;
        var c = Active;
        string key;
        if (c is null)
        {
            bool done = _done is not null;
            _tile.Field = done ? Fields.Alarm() : Fields.Timer();
            _ring.Fill = done ? CardKit.Alarm : CardKit.Ink;
            _ring.Value = done ? 1 : 0;
            _digits.Text = "0:00";
            _rest.Text = done ? $"{_done} · done" : "Timer";
            _name.Text = done ? _done! : "Timer";
            _of.Text = done ? "Time’s up" : "Nothing running";
            key = done ? "done" : "none";
        }
        else
        {
            _tile.Field = Fields.Timer();
            _ring.Fill = CardKit.Ink;
            _ring.Value = RingValue(c, now);
            var display = c.Display(now);
            _digits.Text = display;
            _digits.FontSize = display.Length > 5 ? 14 : 18;
            _rest.Text = c.Paused ? $"{Name(c)} · paused" : Name(c);
            _name.Text = Name(c);
            _of.Text = c.IsCountdown ? $"{display} {TimerCardText.Of(c.Total)}" : c.Paused ? "Paused" : "Counting up";
            key = $"{c.Id}:{c.Paused}";
        }
        CardKit.Name(_tile, c is null ? (_done is { } d ? $"Timer: {d} done" : "Timer") : $"Timer: {Name(c)}, {c.Display(now)}{(c.IsCountdown ? " " + TimerCardText.Of(c.Total) : "")}{(c.Paused ? ", paused" : "")}");
        if (key == _buttonsKey) return;
        _buttonsKey = key;
        _buttons.Children.Clear();
        foreach (var b in Buttons(c, 28, vertical: true)) _buttons.Children.Add(b);
    }

    /// <summary>Pause/resume (the primary) and stop for a clock; dismiss for a finished one.</summary>
    List<Button> Buttons(RunningClock? c, double size, bool vertical)
    {
        var list = new List<Button>();
        if (c is null)
        {
            if (_done is not null)
                list.Add(CardKit.Primary("Dismiss", CardKit.Icon(CardKit.Paths.Check, CardKit.OnAccent, size * 0.45, 2.6), Dismiss, size));
            return list;
        }
        list.Add(CardKit.Primary(c.Paused ? $"Resume {Name(c)}" : $"Pause {Name(c)}", CardKit.PlayPause(!c.Paused, size * 0.4), () => _host.TogglePause(c), size));
        var stop = CardKit.Quiet($"Stop {Name(c)}", CardKit.Glyph(CardKit.Paths.Stop, CardKit.Ink, size * 0.36), () => _host.Stop(c), size);
        stop.Margin = vertical ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);
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
        readonly Ring _ring = new() { Width = 156, Height = 156, Thickness = 10 };
        readonly Tracked _digits = CardKit.Poster("", 40), _of = CardKit.Label("");
        readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        readonly WrapPanel _quick = new() { Margin = new Thickness(0, 8, 0, 0) };
        readonly StackPanel _others = new();
        readonly DockPanel _aside = new() { Width = 232 };
        string _structure = "";

        public Detail(TimerCard card)
        {
            _card = card;
            Root = new WallDetail(Fields.Timer(true), "", "");
            var dial = new Grid { Width = 156, Height = 156 };
            dial.Children.Add(_ring);
            _digits.HorizontalAlignment = _of.HorizontalAlignment = HorizontalAlignment.Center;
            _of.Margin = new Thickness(0, 6, 0, 0);
            var center = CardKit.Column(0, _digits, _of);
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            dial.Children.Add(center);
            Root.Art.Children.Add(CardKit.At(dial, HorizontalAlignment.Left, VerticalAlignment.Top, 72, 46));

            var quickHead = CardKit.Label("Quick start", CardKit.Ink2);
            quickHead.HorizontalAlignment = HorizontalAlignment.Left;
            quickHead.Margin = new Thickness(0, 16, 0, 0);
            foreach (var m in TimerCardText.QuickMinutes)
            {
                var chip = CardKit.Pill($"{m} min", $"Start a {m} minute timer", () => card._host.QuickStart(TimeSpan.FromMinutes(m)));
                chip.Margin = new Thickness(0, 0, 8, 0);
                _quick.Children.Add(chip);
            }
            var body = CardKit.Column(0, _actions, quickHead, _quick);
            Root.Body.Children.Add(body);

            var head = CardKit.Label("Also running", CardKit.Ink2);
            head.HorizontalAlignment = HorizontalAlignment.Left;
            head.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(head, Dock.Top);
            _aside.Children.Add(head);
            _aside.Children.Add(_others);
            Refresh();
        }

        public WallDetail Root { get; }

        public void Refresh()
        {
            var now = DateTime.UtcNow;
            var c = _card.Active;
            var others = _card._host.Clocks.Where(k => k != c).Reverse().Take(4).ToList();

            if (c is null)
            {
                bool done = _card._done is not null;
                Root.Field = done ? Fields.Alarm() : Fields.Timer(true);
                _ring.Fill = done ? CardKit.Alarm : CardKit.Ink;
                _ring.Value = done ? 1 : 0;
                _digits.Text = "0:00";
                _of.Text = done ? "Done" : "Timer";
                Root.Kicker.Text = done ? "Timer · time’s up" : "Timer";
                Root.TitleText.Text = done ? _card._done! : "Start one";
                Root.Line.Text = done ? "Finished just now" : "Pick a length below, or say “Shammy, 10 minute timer”";
            }
            else
            {
                Root.Field = Fields.Timer(true);
                _ring.Fill = CardKit.Ink;
                _ring.Value = RingValue(c, now);
                var display = c.Display(now);
                _digits.Text = display;
                _digits.FontSize = display.Length > 5 ? 30 : 40;
                _of.Text = c.IsCountdown ? TimerCardText.Of(c.Total) : "elapsed";
                Root.Kicker.Text = c.Paused ? "Timer · paused" : "Timer · running";
                Root.TitleText.Text = Name(c);
                Root.Line.Text = c.IsCountdown
                    ? $"{display} {TimerCardText.Of(c.Total)} · {TimerCardText.Ends(c.EndsLocal(now) ?? DateTime.Now, c.Paused, display, CardKit.H24).ToLowerInvariant()}"
                    : c.Paused ? "Stopwatch · paused" : "Stopwatch · counting up";
            }

            // Buttons and rows are rebuilt only when what's running changes, not every second.
            var structure = $"{c?.Id}:{c?.Paused}:{_card._done}|{string.Join(",", others.Select(o => $"{o.Id}:{o.Paused}:{o.PomodoroStep}"))}";
            if (structure != _structure)
            {
                _structure = structure;
                BuildActions(c, others.Count > 0);
                _others.Children.Clear();
                foreach (var o in others) _others.Children.Add(OtherRow(o));
                Root.Aside = others.Count > 0 ? _aside : null;
            }
            // Live values in the rows.
            for (int i = 0; i < others.Count && i < _others.Children.Count; i++)
                if (_others.Children[i] is FrameworkElement { Tag: TextBlock time }) time.Text = others[i].Display(now);
        }

        /// <summary>Pause/resume as an accent pill (the primary), Stop, then the extend buttons (fewer when the aside takes room).</summary>
        void BuildActions(RunningClock? c, bool narrow)
        {
            _actions.Children.Clear();
            if (c is null)
            {
                if (_card._done is not null) _actions.Children.Add(CardKit.PrimaryPill("Dismiss", "Dismiss", _card.Dismiss, 38, 14));
                return;
            }
            _actions.Children.Add(CardKit.PrimaryPill(c.Paused ? "Resume" : "Pause", c.Paused ? $"Resume {Name(c)}" : $"Pause {Name(c)}", () => _card._host.TogglePause(c), 38, 14));
            var stop = CardKit.Pill("Stop", $"Stop {Name(c)}", () => _card._host.Stop(c), 38);
            stop.Margin = new Thickness(8, 0, 0, 0);
            _actions.Children.Add(stop);
            if (!c.IsCountdown) return;
            _actions.Children.Add(new Border { Width = 1, Height = 24, Background = CardKit.Hairline, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var m in narrow ? new[] { 1 } : new[] { 1, 5, 10 })
            {
                var more = CardKit.Pill($"+{m} min", $"Add {m} minute{(m == 1 ? "" : "s")} to {Name(c)}", () => _card._host.Extend(c, TimeSpan.FromMinutes(m)));
                more.Margin = new Thickness(0, 0, 8, 0);
                more.VerticalAlignment = VerticalAlignment.Center;
                _actions.Children.Add(more);
            }
        }

        static FrameworkElement OtherRow(RunningClock c)
        {
            var now = DateTime.UtcNow;
            var time = CardKit.Text(c.Display(now), 13, FontWeights.Bold);
            time.Foreground = c.Paused ? CardKit.Ink2 : CardKit.Ink;
            var meta = c.Kind == ClockKind.Pomodoro ? TimerCardText.PomodoroLine(c.PomodoroStep) : c.Paused ? "paused" : c.IsCountdown ? TimerCardText.Of(c.Total) : "counting up";
            var name = new TextBlock { FontFamily = CardKit.Font, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            name.Inlines.Add(new System.Windows.Documents.Run(Name(c)) { FontWeight = FontWeights.SemiBold, Foreground = CardKit.Ink });
            name.Inlines.Add(new System.Windows.Documents.Run("  " + meta) { FontWeight = FontWeights.Medium, Foreground = CardKit.Ink2 });
            var row = WallDetail.ListRow(WallDetail.Bullet(), name, time, 30);
            row.Tag = time;
            return row;
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        var now = DateTime.UtcNow;
        if (Active is not { } c)
            return _done is { } d ? $"timer: done label={d} ring=1.00 colour=alarm buttons=dismiss" : "timer: nothing running (card not relevant)";
        return $"timer: label={Name(c)} digits={c.Display(now)} ring={RingValue(c, now):0.00} paused={c.Paused} buttons={(c.Paused ? "resume" : "pause")},stop " +
               $"running={_host.Clocks.Count} focused={_tile.IsLit} visible={_tile.IsVisible}";
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
        var others = _host.Clocks.Where(k => k != c).Reverse().Take(4).Select(Row).ToList();
        return $"timer detail: active=[{active}] alsoRunning=[{(others.Count == 0 ? "-" : string.Join(" | ", others))}] " +
               $"quickAdd={string.Join(",", TimerCardText.QuickMinutes.Select(m => m + "m"))} extend=+1,+5,+10";
    }
}
