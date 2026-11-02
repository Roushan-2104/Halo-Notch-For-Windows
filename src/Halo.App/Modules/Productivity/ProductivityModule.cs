using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Core.Productivity;

namespace Halo.Modules.Productivity;

/// <summary>
/// Timers, a stopwatch, pomodoro cycles, focus sessions, reminders and quick notes, all from typed or
/// spoken commands ("timer 25m", "remind me at 6pm to take medicine", "note: buy milk").
/// Running clocks live in the compact ears; reminders and notes persist and show in a Notes tab.
/// </summary>
internal sealed class ProductivityModule : IHaloModule, ITextCommand
{
    // Above downloads (40), below messages (50): a running timer yields to anything more urgent.
    const int ClockPriority = 45;
    const int AlertPriority = 85;
    const string AlertId = "productivity.alert";

    const string TimerGlyph = "\uE121", StopwatchGlyph = "\uE916", PomodoroGlyph = "\uE945", BreakGlyph = "\uE76E",
        FocusGlyph = "\uE708", BellGlyph = "\uEA8F", NoteGlyph = "\uE70B", PauseGlyph = "\uE769", PlayGlyph = "\uE768";

    static readonly TimeSpan AlertTime = TimeSpan.FromSeconds(8);
    static readonly TimeSpan CardTime = TimeSpan.FromSeconds(30);
    static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(10);

    readonly List<RunningClock> _clocks = new();
    readonly Queue<ReminderItem> _cardQueue = new();
    readonly FocusSession _focus = new();
    readonly DispatcherTimer _tick = new();
    readonly DispatcherTimer _reminders = new();
    readonly DispatcherTimer _glowOff = new() { Interval = AlertTime };
    readonly DispatcherTimer _cardClose = new() { Interval = CardTime };

    ModuleContext _ctx = null!;
    ProductivityStore _store = new();
    NotesPanel _panel = null!;
    ReminderCard? _card;
    bool _glowing;
    int _nextId;

    public string Name => "productivity";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _store = ctx.Load<ProductivityStore>();
        // A hand-edited file can say "Reminders": null.
        _store.Reminders ??= new();
        _store.Notes ??= new();
        // Fired reminders stay listed for a week, then quietly go.
        _store.Reminders.RemoveAll(r => r.FiredAt is { } fired && fired < DateTime.Now.AddDays(-7));

        _tick.Tick += (_, _) => OnTick();
        _reminders.Tick += (_, _) => OnReminderTick();
        _glowOff.Tick += (_, _) => ClearGlow();
        _cardClose.Tick += (_, _) => CloseCard();

        _panel = new NotesPanel(() => _store, Delete, Copy);
        ctx.AddTab(NoteGlyph, "Notes", _panel);
        ctx.AddCommand(this);

        ctx.AddPipeCommand("productivity-timer", PipeTimer);
        ctx.AddPipeCommand("productivity-remind-now", PipeRemindNow);
        ctx.AddPipeCommand("productivity-state", _ => State());
        ctx.AddPipeCommand("productivity-run", text =>
        {
            var r = Run(text, typed: false);
            return $"{(r.IsError ? "error" : "ok")}: {r.Title}{(r.Detail is null ? "" : " | " + r.Detail)}";
        });

        // Reminders that fell due while Halo wasn't running fire shortly after start.
        _reminders.Interval = TimeSpan.FromSeconds(3);
        _reminders.Start();
    }

    public void Dispose()
    {
        _tick.Stop();
        _reminders.Stop();
        _cardClose.Stop();
        if (_ctx is null) return;
        foreach (var c in _clocks) _ctx.Remove(c.ActivityId);
        _clocks.Clear();
        _focus.Dispose();
        ClearGlow();
    }

    // ---- Commands --------------------------------------------------------------------------------

    public bool CanHandle(string text) => ProductivityParser.Parse(text, DateTime.Now) is not null;

    public Task<CommandResult> RunAsync(string text) => Task.FromResult(Run(text, typed: true));

    CommandResult Run(string text, bool typed)
    {
        if (ProductivityParser.Parse(text, DateTime.Now) is not { } c)
            return new CommandResult(TimerGlyph, "Not a timer, reminder or note", text, IsError: true);

        return c.Action switch
        {
            ProductivityAction.StartTimer => StartTimer(c.Duration, c.Text),
            ProductivityAction.StopTimer or ProductivityAction.PauseTimer or ProductivityAction.ResumeTimer => ControlTimers(c),
            ProductivityAction.StartStopwatch => StartStopwatch(),
            ProductivityAction.StopStopwatch => StopStopwatch(),
            ProductivityAction.PauseStopwatch => PauseClock(Find(ClockKind.Stopwatch), "Stopwatch", pause: true),
            ProductivityAction.ResumeStopwatch => PauseClock(Find(ClockKind.Stopwatch), "Stopwatch", pause: false),
            ProductivityAction.StartPomodoro => StartPomodoro(),
            ProductivityAction.StopPomodoro => StopPomodoro(),
            ProductivityAction.SkipPomodoro => SkipPomodoro(),
            ProductivityAction.StartFocus => StartFocus(c.Duration),
            ProductivityAction.StopFocus => StopFocus(),
            ProductivityAction.Remind => AddReminder(c.Due, c.Text),
            ProductivityAction.ReminderNeedsTime => new CommandResult(BellGlyph, "When should I remind you?",
                "Try \u201Cremind me in 20 minutes to \u2026\u201D", IsError: true),
            ProductivityAction.Note => AddNote(c.Text),
            ProductivityAction.ShowNotes => ShowNotes(typed),
            _ => new CommandResult(TimerGlyph, "Not supported yet", IsError: true),
        };
    }

    CommandResult StartTimer(TimeSpan duration, string label)
    {
        AddClock(ClockKind.Timer, label, TimerGlyph, duration);
        var ends = $"Ends {ClockText(DateTime.Now + duration)}";
        return new CommandResult(TimerGlyph, $"Timer \u00B7 {TimeFormat.Countdown(duration)}", label.Length > 0 ? $"{label} \u00B7 {ends}" : ends);
    }

    CommandResult ControlTimers(ProductivityCommand c)
    {
        // "pause timer" with only a pomodoro or focus session running means that one.
        var candidates = _clocks.Where(k => k.Kind == ClockKind.Timer).ToList();
        if (candidates.Count == 0 && c.Text.Length == 0) candidates = _clocks.Where(k => k.IsCountdown).ToList();
        if (c.Text.Length > 0)
        {
            candidates = candidates.Where(k => k.Label.Contains(c.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) return new CommandResult(TimerGlyph, $"No timer called {c.Text}", IsError: true);
        }
        if (candidates.Count == 0) return new CommandResult(TimerGlyph, "No timer running", IsError: true);

        var now = DateTime.UtcNow;
        if (c.Action == ProductivityAction.StopTimer)
        {
            var stop = c.All ? candidates : new List<RunningClock> { candidates[^1] };
            foreach (var k in stop) RemoveClock(k);
            return stop.Count > 1
                ? new CommandResult(TimerGlyph, $"Stopped {stop.Count} timers")
                : new CommandResult(TimerGlyph, $"{ClockName(stop[0])} stopped", $"{stop[0].Display(now)} was left");
        }

        bool pause = c.Action == ProductivityAction.PauseTimer;
        // Pause the newest one that is running; resume the newest one that is paused.
        var pick = candidates.Where(k => k.Paused != pause).ToList();
        if (pick.Count == 0) return new CommandResult(TimerGlyph, pause ? "Already paused" : "Already running");
        var targets = c.All ? pick : new List<RunningClock> { pick[^1] };
        foreach (var k in targets) SetPaused(k, pause);
        var first = targets[^1];
        return new CommandResult(pause ? PauseGlyph : PlayGlyph,
            targets.Count > 1 ? $"{(pause ? "Paused" : "Resumed")} {targets.Count} timers" : $"{ClockName(first)} {(pause ? "paused" : "resumed")}",
            $"{first.Display(now)} left");
    }

    CommandResult StartStopwatch()
    {
        if (Find(ClockKind.Stopwatch) is { } existing)
        {
            if (existing.Paused) return PauseClock(existing, "Stopwatch", pause: false);
            return new CommandResult(StopwatchGlyph, $"Stopwatch \u00B7 {existing.Display(DateTime.UtcNow)}", "Already running");
        }
        AddClock(ClockKind.Stopwatch, "", StopwatchGlyph, TimeSpan.Zero);
        return new CommandResult(StopwatchGlyph, "Stopwatch started", "Say \u201Cstop stopwatch\u201D to stop it");
    }

    CommandResult StopStopwatch()
    {
        if (Find(ClockKind.Stopwatch) is not { } watch) return new CommandResult(StopwatchGlyph, "No stopwatch running", IsError: true);
        var time = watch.Display(DateTime.UtcNow);
        RemoveClock(watch);
        return new CommandResult(StopwatchGlyph, $"Stopwatch \u00B7 {time}", "Stopped");
    }

    CommandResult PauseClock(RunningClock? clock, string what, bool pause)
    {
        if (clock is null) return new CommandResult(StopwatchGlyph, $"No {what.ToLowerInvariant()} running", IsError: true);
        SetPaused(clock, pause);
        return new CommandResult(pause ? PauseGlyph : PlayGlyph, $"{what} {(pause ? "paused" : "resumed")}", clock.Display(DateTime.UtcNow));
    }

    CommandResult StartPomodoro()
    {
        if (Find(ClockKind.Pomodoro) is { } existing)
            return new CommandResult(PomodoroGlyph, $"Pomodoro \u00B7 {existing.Label}", $"{existing.Display(DateTime.UtcNow)} left");
        AddClock(ClockKind.Pomodoro, Pomodoro.Label(0), PomodoroGlyph, Pomodoro.FocusLength);
        return new CommandResult(PomodoroGlyph, "Pomodoro started",
            $"{TimeFormat.Describe(Pomodoro.FocusLength)} focus, then a {TimeFormat.Describe(Pomodoro.ShortBreakLength)} break");
    }

    CommandResult StopPomodoro()
    {
        if (Find(ClockKind.Pomodoro) is not { } pomodoro) return new CommandResult(PomodoroGlyph, "No pomodoro running", IsError: true);
        int rounds = (pomodoro.PomodoroStep + 1) / 2;
        RemoveClock(pomodoro);
        return new CommandResult(PomodoroGlyph, "Pomodoro stopped", rounds == 1 ? "1 focus round done" : $"{rounds} focus rounds done");
    }

    CommandResult SkipPomodoro()
    {
        if (Find(ClockKind.Pomodoro) is not { } pomodoro) return new CommandResult(PomodoroGlyph, "No pomodoro running", IsError: true);
        NextPomodoroPhase(pomodoro);
        return new CommandResult(pomodoro.Glyph, $"Pomodoro \u00B7 {pomodoro.Label}", TimeFormat.Describe(pomodoro.Total));
    }

    CommandResult StartFocus(TimeSpan duration)
    {
        if (Find(ClockKind.Focus) is { } old) RemoveClock(old);
        AddClock(ClockKind.Focus, "Focus", FocusGlyph, duration);
        bool windows = _focus.TryStart(DateTimeOffset.Now + duration);
        _ctx.Info($"Focus for {TimeFormat.Describe(duration)}; Windows focus session: {_focus.Status}");
        return new CommandResult(FocusGlyph, $"Focus \u00B7 {TimeFormat.Describe(duration)}",
            windows ? "Windows focus is on too" : $"Until {ClockText(DateTime.Now + duration)}");
    }

    CommandResult StopFocus()
    {
        if (Find(ClockKind.Focus) is not { } focus)
        {
            _focus.End();
            return new CommandResult(FocusGlyph, "No focus session", IsError: true);
        }
        var spent = focus.Elapsed(DateTime.UtcNow);
        RemoveClock(focus);
        return new CommandResult(FocusGlyph, "Focus ended", spent.TotalMinutes >= 1 ? $"Focused {TimeFormat.Describe(TimeSpan.FromMinutes(Math.Floor(spent.TotalMinutes)))}" : null);
    }

    CommandResult AddReminder(DateTime due, string text)
    {
        var now = DateTime.Now;
        var item = new ReminderItem { Text = text, Due = due, Created = now };
        _store.Reminders.Add(item);
        Save();
        ScheduleReminders();
        return new CommandResult(BellGlyph, "Reminder set", $"{TimeFormat.When(due, now)} \u00B7 {item.Title}");
    }

    CommandResult AddNote(string text)
    {
        _store.Notes.Add(new NoteItem { Text = text, Created = DateTime.Now });
        Save();
        return new CommandResult(NoteGlyph, "Saved note", text);
    }

    CommandResult ShowNotes(bool typed)
    {
        // OpenTab waits for the command's confirmation card to close by itself.
        _panel.Refresh();
        _ctx.OpenTab(_panel);
        int upcoming = _store.Reminders.Count(r => !r.Fired), notes = _store.Notes.Count;
        return new CommandResult(NoteGlyph, "Notes",
            $"{notes} {(notes == 1 ? "note" : "notes")} \u00B7 {upcoming} upcoming {(upcoming == 1 ? "reminder" : "reminders")}");
    }

    // ---- Clocks ----------------------------------------------------------------------------------

    RunningClock AddClock(ClockKind kind, string label, string glyph, TimeSpan total)
    {
        var clock = new RunningClock(++_nextId, kind, label, glyph, total);
        _clocks.Add(clock);
        PostClock(clock);
        ScheduleTick();
        return clock;
    }

    void PostClock(RunningClock clock) =>
        _ctx.Post(new Activity(clock.ActivityId, ActivityKind.Custom, ClockPriority, clock.Content)
        {
            Click = () => SetPaused(clock, !clock.Paused),
        });

    void RemoveClock(RunningClock clock)
    {
        _clocks.Remove(clock);
        _ctx.Remove(clock.ActivityId);
        if (clock.Kind == ClockKind.Focus) _focus.End();
        ScheduleTick();
    }

    void SetPaused(RunningClock clock, bool pause)
    {
        var now = DateTime.UtcNow;
        if (pause) clock.Pause(now);
        else clock.Resume(now);
        clock.Content.Refresh(now);
        ScheduleTick();
    }

    RunningClock? Find(ClockKind kind) => _clocks.LastOrDefault(c => c.Kind == kind);

    static string ClockName(RunningClock c) => c.Kind switch
    {
        ClockKind.Pomodoro => "Pomodoro",
        ClockKind.Focus => "Focus",
        _ => c.Label.Length > 0 ? $"Timer \u00B7 {c.Label}" : "Timer",
    };

    void OnTick()
    {
        var now = DateTime.UtcNow;
        foreach (var clock in _clocks.ToList())
        {
            if (clock.IsCountdown && !clock.Paused && clock.Remaining(now) <= TimeSpan.Zero)
            {
                Finish(clock);
            }
            else if (clock.Content.NeedsRebuild(now))
            {
                clock.Content = new ClockContent(clock);
                PostClock(clock);
            }
            else
            {
                clock.Content.Refresh(now);
            }
        }
        ScheduleTick();
    }

    /// <summary>Ticks land just after the next second boundary of whichever clock changes first; off when all are paused.</summary>
    void ScheduleTick()
    {
        _tick.Stop();
        var now = DateTime.UtcNow;
        var running = _clocks.Where(c => !c.Paused).ToList();
        if (running.Count == 0) return;
        _tick.Interval = running.Min(c => c.UntilNextSecond(now)) + TimeSpan.FromMilliseconds(15);
        _tick.Start();
    }

    void Finish(RunningClock clock)
    {
        switch (clock.Kind)
        {
            case ClockKind.Pomodoro:
                NextPomodoroPhase(clock);
                var phase = Pomodoro.PhaseAt(clock.PomodoroStep);
                Alert(clock.Glyph, phase switch
                {
                    PomodoroPhase.Focus => $"Back to focus \u00B7 {TimeFormat.Describe(clock.Total)}",
                    PomodoroPhase.LongBreak => $"Long break \u00B7 {TimeFormat.Describe(clock.Total)}",
                    _ => $"Break time \u00B7 {TimeFormat.Describe(clock.Total)}",
                });
                break;
            case ClockKind.Focus:
                RemoveClock(clock);
                Alert(FocusGlyph, $"Focus done \u00B7 {TimeFormat.Describe(clock.Total)}");
                break;
            default:
                RemoveClock(clock);
                Alert(TimerGlyph, $"Timer done \u00B7 {(clock.Label.Length > 0 ? clock.Label : TimeFormat.Describe(clock.Total))}");
                break;
        }
    }

    void NextPomodoroPhase(RunningClock clock)
    {
        int step = clock.PomodoroStep + 1;
        var phase = Pomodoro.PhaseAt(step);
        clock.PomodoroStep = step;
        clock.Restart(Pomodoro.Length(phase), Pomodoro.Label(step), phase == PomodoroPhase.Focus ? PomodoroGlyph : BreakGlyph);
        // The label width changed, so the ears need building again.
        clock.Content = new ClockContent(clock);
        PostClock(clock);
        ScheduleTick();
    }

    // ---- Alerts ----------------------------------------------------------------------------------

    void Alert(string glyph, string text)
    {
        _ctx.Post(new Activity(AlertId, ActivityKind.Message, AlertPriority, new MessagePayload(glyph, text), DateTime.UtcNow + AlertTime)
        {
            Click = () => _ctx.Remove(AlertId),
        });
        Glow();
        Chime();
    }

    void Glow()
    {
        if (Application.Current.Resources["AccentBrush"] is SolidColorBrush accent) _ctx.SetGlow(accent.Color);
        _glowing = true;
        _glowOff.Stop();
        _glowOff.Start();
    }

    void ClearGlow()
    {
        _glowOff.Stop();
        if (!_glowing) return;
        _glowing = false;
        _ctx.SetGlow(null);
    }

    void Chime()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            _ctx.Warn("Playing the alert sound failed", ex);
        }
    }

    // ---- Reminders -------------------------------------------------------------------------------

    void ScheduleReminders()
    {
        _reminders.Stop();
        var next = _store.Reminders.Where(r => !r.Fired).Select(r => (DateTime?)r.Due).Min();
        if (next is not { } due) return;
        // Wake at least every 30 s: a DispatcherTimer doesn't count time the PC spent asleep.
        _reminders.Interval = TimeSpan.FromMilliseconds(Math.Clamp((due - DateTime.Now).TotalMilliseconds, 200, 30_000));
        _reminders.Start();
    }

    void OnReminderTick()
    {
        _reminders.Stop();
        var now = DateTime.Now;
        var due = _store.Reminders.Where(r => !r.Fired && r.Due <= now.AddMilliseconds(500)).OrderBy(r => r.Due).ToList();
        if (due.Count > 0)
        {
            foreach (var r in due) r.FiredAt = now;
            Save();
            foreach (var r in due) Fire(r);
        }
        ScheduleReminders();
    }

    void Fire(ReminderItem reminder)
    {
        _cardQueue.Enqueue(reminder);
        if (_card is null) ShowNextCard();
    }

    void ShowNextCard()
    {
        while (_cardQueue.Count > 0)
        {
            var reminder = _cardQueue.Dequeue();
            if (!_store.Reminders.Contains(reminder)) continue; // deleted while it waited
            var card = new ReminderCard(reminder, () => Done(reminder), () => Snooze(reminder));
            _card = card;
            bool seen = false;
            // The notch closes interactive cards itself on an outside click; notice that and move on.
            card.IsVisibleChanged += (_, _) =>
            {
                if (card.IsVisible) seen = true;
                else if (seen) After(TimeSpan.FromMilliseconds(400), () =>
                {
                    if (_card == card && !card.IsVisible) CloseCard();
                });
            };
            _ctx.ShowCard(card, ReminderCard.CardWidth, ReminderCard.CardHeight, interactive: true);
            Glow();
            Chime();
            _cardClose.Stop();
            _cardClose.Start();
            return;
        }
    }

    /// <summary>Closes our reminder card (if it's still the one showing) and brings on the next queued one.</summary>
    void CloseCard()
    {
        _cardClose.Stop();
        if (_card is not { } card) return;
        _card = null;
        // Another module may have replaced the card since; only close it if it's still ours.
        if (card.Parent is not null) _ctx.CloseCard();
        ClearGlow();
        if (_cardQueue.Count > 0) After(TimeSpan.FromMilliseconds(450), () =>
        {
            if (_card is null) ShowNextCard();
        });
    }

    void Done(ReminderItem reminder)
    {
        _store.Reminders.Remove(reminder);
        Save();
        CloseCard();
    }

    void Snooze(ReminderItem reminder)
    {
        reminder.FiredAt = null;
        reminder.Due = DateTime.Now + SnoozeTime;
        Save();
        ScheduleReminders();
        CloseCard();
        _ctx.ShowMessage(BellGlyph, $"Snoozed until {ClockText(reminder.Due)}");
    }

    // ---- Notes tab -------------------------------------------------------------------------------

    void Delete(string id)
    {
        int removed = _store.Reminders.RemoveAll(r => r.Id == id) + _store.Notes.RemoveAll(n => n.Id == id);
        if (removed == 0) return;
        Save();
        ScheduleReminders();
        if (_card?.Reminder.Id == id) CloseCard();
    }

    void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            _ctx.Warn("Copying a note failed", ex);
        }
    }

    void Save()
    {
        _ctx.Save(_store);
        _panel.Refresh();
    }

    // ---- Test pipe -------------------------------------------------------------------------------

    /// <summary>"productivity-timer 5 tea": a 5 second timer labelled tea.</summary>
    string PipeTimer(string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds is <= 0 or > 86400)
            return "error: usage productivity-timer <seconds> [label]";
        var label = parts.Length > 1 ? parts[1] : "";
        var clock = AddClock(ClockKind.Timer, label, TimerGlyph, TimeSpan.FromSeconds(seconds));
        return $"ok timer#{clock.Id} {clock.Display(DateTime.UtcNow)}{(label.Length > 0 ? " " + label : "")}";
    }

    /// <summary>Adds a reminder that is already due, so its card shows straight away.</summary>
    string PipeRemindNow(string text)
    {
        var now = DateTime.Now;
        var item = new ReminderItem { Text = text.Trim(), Due = now, Created = now, FiredAt = now };
        _store.Reminders.Add(item);
        Save();
        Fire(item);
        return _card?.Reminder == item ? "ok card shown" : $"ok queued ({_cardQueue.Count} waiting)";
    }

    string State()
    {
        var now = DateTime.UtcNow;
        string Describe(RunningClock c) =>
            $"{c.Kind.ToString().ToLowerInvariant()}{(c.Label.Length > 0 ? $"[{c.Label}]" : "")}={c.Display(now)}{(c.Paused ? "(paused)" : "")}";
        int upcoming = _store.Reminders.Count(r => !r.Fired);
        return $"clocks={(_clocks.Count == 0 ? "-" : string.Join(",", _clocks.Select(Describe)))} " +
               $"reminders={upcoming} upcoming/{_store.Reminders.Count - upcoming} fired notes={_store.Notes.Count} " +
               $"card={(_card is null ? "-" : "open")} queued={_cardQueue.Count} glow={_glowing} " +
               $"focus={_focus.Status} tick={(_tick.IsEnabled ? $"{_tick.Interval.TotalMilliseconds:0}ms" : "off")}";
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    static string ClockText(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);

    static void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
