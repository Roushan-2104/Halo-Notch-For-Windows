using Halo.Core.Productivity;

namespace Halo.Core.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("25m", 25 * 60)]
    [InlineData("25 min", 25 * 60)]
    [InlineData("10 minutes", 600)]
    [InlineData("1 minute", 60)]
    [InlineData("1h 30m", 5400)]
    [InlineData("1h30m", 5400)]
    [InlineData("1 hour 30 minutes", 5400)]
    [InlineData("1 hour and 30 minutes", 5400)]
    [InlineData("2 hrs", 7200)]
    [InlineData("90 seconds", 90)]
    [InlineData("45s", 45)]
    [InlineData("2 minutes 30 seconds", 150)]
    [InlineData("half an hour", 1800)]
    [InlineData("a half hour", 1800)]
    [InlineData("half a minute", 30)]
    [InlineData("an hour", 3600)]
    [InlineData("a minute", 60)]
    [InlineData("an hour and a half", 5400)]
    [InlineData("one and a half hours", 5400)]
    [InlineData("1.5 hours", 5400)]
    [InlineData("1.5h", 5400)]
    [InlineData("a quarter of an hour", 900)]
    [InlineData("twenty five minutes", 1500)]
    [InlineData("twenty-five minutes", 1500)]
    [InlineData("ten minutes", 600)]
    [InlineData("a couple of minutes", 120)]
    [InlineData("1:30", 90)]
    [InlineData("1:30:00", 5400)]
    [InlineData("2 days", 172800)]
    [InlineData("10-minute", 600)]
    [InlineData("10 MIN", 600)]
    public void Parses_durations(string text, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), DurationParser.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("eggs")]
    [InlineData("10")]
    [InlineData("0 minutes")]
    [InlineData("minutes")]
    [InlineData("half")]
    [InlineData("1:75")]
    [InlineData("10 minutes eggs")]
    [InlineData("an")]
    [InlineData("1000 days")]
    public void Rejects_non_durations(string text)
    {
        Assert.Null(DurationParser.Parse(text));
    }

    [Fact]
    public void Prefix_returns_the_rest_as_typed()
    {
        var d = DurationParser.ParsePrefix("90 seconds Green Tea", out var rest);
        Assert.Equal(TimeSpan.FromSeconds(90), d);
        Assert.Equal("Green Tea", rest);
    }

    [Fact]
    public void Prefix_keeps_and_when_nothing_follows_it()
    {
        var d = DurationParser.ParsePrefix("10 minutes and stir", out var rest);
        Assert.Equal(TimeSpan.FromMinutes(10), d);
        Assert.Equal("and stir", rest);
    }

    [Fact]
    public void Prefix_without_a_duration_is_null()
    {
        Assert.Null(DurationParser.ParsePrefix("eggs how long", out var rest));
        Assert.Equal("eggs how long", rest);
    }
}

public class WhenParserTests
{
    // Monday 2 November 2026, 2:00 PM
    static readonly DateTime Now = new(2026, 11, 2, 14, 0, 0);

    static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);

    [Theory]
    [InlineData("6pm", 18, 0, true)]
    [InlineData("6 pm", 18, 0, true)]
    [InlineData("6:30pm", 18, 30, true)]
    [InlineData("6.30pm", 18, 30, true)]
    [InlineData("12am", 0, 0, true)]
    [InlineData("12pm", 12, 0, true)]
    [InlineData("12:15am", 0, 15, true)]
    [InlineData("18:30", 18, 30, true)]
    [InlineData("09:00", 9, 0, true)]
    [InlineData("0:15", 0, 15, true)]
    [InlineData("9", 9, 0, false)]
    [InlineData("9:45", 9, 45, false)]
    [InlineData("noon", 12, 0, true)]
    [InlineData("midnight", 0, 0, true)]
    public void Parses_clock_words(string word, int hour, int minute, bool exact)
    {
        var c = WhenParser.ParseClock(WhenParser.Normalize(word).Replace(" ", ""));
        Assert.Equal(new WhenParser.Clock(hour, minute, exact), c);
    }

    [Theory]
    [InlineData("13pm")]
    [InlineData("0am")]
    [InlineData("24:00")]
    [InlineData("6:75")]
    [InlineData("six")]
    [InlineData("0")]
    public void Rejects_bad_clocks(string word)
    {
        Assert.Null(WhenParser.ParseClock(word));
    }

    [Fact]
    public void Normalizes_spoken_meridiem() => Assert.Equal("at 6pm", WhenParser.Normalize("at 6 p.m."));

    [Fact]
    public void Drops_o_clock() => Assert.Equal("at 9", WhenParser.Normalize("at 9 o'clock"));

    [Fact]
    public void In_minutes() => Assert.Equal(Now.AddMinutes(20), WhenParser.Parse("in 20 minutes", Now));

    [Fact]
    public void In_half_an_hour() => Assert.Equal(Now.AddMinutes(30), WhenParser.Parse("in half an hour", Now));

    [Fact]
    public void From_now() => Assert.Equal(Now.AddHours(2), WhenParser.Parse("2 hours from now", Now));

    [Fact]
    public void Later_today_pm() => Assert.Equal(At(2, 18), WhenParser.Parse("at 6pm", Now));

    [Fact]
    public void Past_time_today_rolls_to_tomorrow() => Assert.Equal(At(3, 9), WhenParser.Parse("at 9am", Now));

    [Fact]
    public void Past_24_hour_time_rolls_to_tomorrow() => Assert.Equal(At(3, 13, 30), WhenParser.Parse("at 13:30", Now));

    [Fact]
    public void Twenty_four_hour_time() => Assert.Equal(At(2, 18, 30), WhenParser.Parse("at 18:30", Now));

    [Fact]
    public void Midnight_am() => Assert.Equal(At(3, 0), WhenParser.Parse("at 12am", Now));

    [Fact]
    public void Noon_pm_tomorrow_when_past() => Assert.Equal(At(3, 12), WhenParser.Parse("at 12pm", Now));

    [Fact]
    public void Ambiguous_hour_picks_the_next_one() => Assert.Equal(At(2, 21), WhenParser.Parse("at 9", Now));

    [Fact]
    public void Ambiguous_hour_in_the_morning_is_am()
    {
        var early = new DateTime(2026, 11, 2, 7, 0, 0);
        Assert.Equal(At(2, 9), WhenParser.Parse("at 9", early));
    }

    [Fact]
    public void Tomorrow_at_bare_hour_is_morning() => Assert.Equal(At(3, 9), WhenParser.Parse("tomorrow at 9", Now));

    [Fact]
    public void Tomorrow_at_small_hour_is_afternoon() => Assert.Equal(At(3, 15), WhenParser.Parse("tomorrow at 3", Now));

    [Fact]
    public void Tomorrow_alone_is_nine() => Assert.Equal(At(3, 9), WhenParser.Parse("tomorrow", Now));

    [Fact]
    public void Clock_before_day() => Assert.Equal(At(3, 18), WhenParser.Parse("at 6pm tomorrow", Now));

    [Fact]
    public void Tomorrow_morning() => Assert.Equal(At(3, 9), WhenParser.Parse("tomorrow morning", Now));

    [Fact]
    public void Tonight_at_eight() => Assert.Equal(At(2, 20), WhenParser.Parse("tonight at 8", Now));

    [Fact]
    public void At_seven_in_the_evening() => Assert.Equal(At(2, 19), WhenParser.Parse("at 7 in the evening", Now));

    [Fact]
    public void This_evening() => Assert.Equal(At(2, 18), WhenParser.Parse("this evening", Now));

    [Fact]
    public void Weekday() => Assert.Equal(At(6, 17), WhenParser.Parse("on friday at 5pm", Now));

    [Fact]
    public void Same_weekday_already_past_means_next_week() => Assert.Equal(At(9, 9), WhenParser.Parse("monday at 9am", Now));

    [Fact]
    public void Same_weekday_later_today() => Assert.Equal(At(2, 17), WhenParser.Parse("monday at 5pm", Now));

    [Fact]
    public void Next_weekday_is_never_today() => Assert.Equal(At(9, 17), WhenParser.Parse("next monday at 5pm", Now));

    [Theory]
    [InlineData("")]
    [InlineData("today")]
    [InlineData("2")]
    [InlineData("at work")]
    [InlineData("in 5")]
    [InlineData("tomorrow tomorrow")]
    [InlineData("at 6pm at 7pm")]
    [InlineData("whenever")]
    public void Rejects_non_times(string text)
    {
        Assert.Null(WhenParser.Parse(text, Now));
    }
}

public class ProductivityParserTests
{
    static readonly DateTime Now = new(2026, 11, 2, 14, 0, 0);

    static ProductivityCommand Parse(string text) => ProductivityParser.Parse(text, Now) ?? throw new Xunit.Sdk.XunitException($"'{text}' was not recognised");

    [Theory]
    [InlineData("timer 25m", 1500, "")]
    [InlineData("timer 1h 30m", 5400, "")]
    [InlineData("set a timer for 10 minutes", 600, "")]
    [InlineData("Set a timer for 10 minutes", 600, "")]
    [InlineData("start a timer for half an hour", 1800, "")]
    [InlineData("10 minute timer", 600, "")]
    [InlineData("10-minute timer", 600, "")]
    [InlineData("set a 5 min timer for eggs", 300, "eggs")]
    [InlineData("timer 90 seconds tea", 90, "tea")]
    [InlineData("timer 10 minutes for the pasta", 600, "pasta")]
    [InlineData("timer for 1.5 hours", 5400, "")]
    [InlineData("timer 10", 600, "")]
    [InlineData("timer 5:00", 300, "")]
    [InlineData("please set a timer for 3 minutes", 180, "")]
    [InlineData("an hour timer", 3600, "")]
    [InlineData("set timer 10 min", 600, "")]
    [InlineData("start a 25 minute timer", 1500, "")]
    [InlineData("Set a timer for ten minutes", 600, "")]
    public void Timers(string text, int seconds, string label)
    {
        var c = Parse(text);
        Assert.Equal(ProductivityAction.StartTimer, c.Action);
        Assert.Equal(TimeSpan.FromSeconds(seconds), c.Duration);
        Assert.Equal(label, c.Text);
    }

    [Theory]
    [InlineData("stop timer", ProductivityAction.StopTimer, false, "")]
    [InlineData("stop the timer", ProductivityAction.StopTimer, false, "")]
    [InlineData("cancel timer", ProductivityAction.StopTimer, false, "")]
    [InlineData("cancel timers", ProductivityAction.StopTimer, true, "")]
    [InlineData("cancel all timers", ProductivityAction.StopTimer, true, "")]
    [InlineData("stop all the timers", ProductivityAction.StopTimer, true, "")]
    [InlineData("cancel the tea timer", ProductivityAction.StopTimer, false, "tea")]
    [InlineData("stop timer tea", ProductivityAction.StopTimer, false, "tea")]
    [InlineData("pause timer", ProductivityAction.PauseTimer, false, "")]
    [InlineData("pause the timer", ProductivityAction.PauseTimer, false, "")]
    [InlineData("resume timer", ProductivityAction.ResumeTimer, false, "")]
    [InlineData("continue the timer", ProductivityAction.ResumeTimer, false, "")]
    public void Timer_controls(string text, ProductivityAction action, bool all, string label)
    {
        var c = Parse(text);
        Assert.Equal(action, c.Action);
        Assert.Equal(all, c.All);
        Assert.Equal(label, c.Text);
    }

    [Theory]
    [InlineData("stopwatch", ProductivityAction.StartStopwatch)]
    [InlineData("start stopwatch", ProductivityAction.StartStopwatch)]
    [InlineData("start the stopwatch", ProductivityAction.StartStopwatch)]
    [InlineData("stop watch", ProductivityAction.StartStopwatch)]
    [InlineData("stop stopwatch", ProductivityAction.StopStopwatch)]
    [InlineData("reset the stopwatch", ProductivityAction.StopStopwatch)]
    [InlineData("stopwatch stop", ProductivityAction.StopStopwatch)]
    [InlineData("pause stopwatch", ProductivityAction.PauseStopwatch)]
    [InlineData("resume stopwatch", ProductivityAction.ResumeStopwatch)]
    [InlineData("pomodoro", ProductivityAction.StartPomodoro)]
    [InlineData("start pomodoro", ProductivityAction.StartPomodoro)]
    [InlineData("start a pomodoro timer", ProductivityAction.StartPomodoro)]
    [InlineData("stop pomodoro", ProductivityAction.StopPomodoro)]
    [InlineData("end the pomodoro", ProductivityAction.StopPomodoro)]
    [InlineData("skip pomodoro", ProductivityAction.SkipPomodoro)]
    [InlineData("stop focus", ProductivityAction.StopFocus)]
    [InlineData("end focus session", ProductivityAction.StopFocus)]
    [InlineData("notes", ProductivityAction.ShowNotes)]
    [InlineData("show notes", ProductivityAction.ShowNotes)]
    [InlineData("show my notes", ProductivityAction.ShowNotes)]
    [InlineData("open my reminders", ProductivityAction.ShowNotes)]
    public void Simple_commands(string text, ProductivityAction action)
    {
        Assert.Equal(action, Parse(text).Action);
    }

    [Theory]
    [InlineData("focus 45m", 45)]
    [InlineData("focus for an hour", 60)]
    [InlineData("focus for 25 minutes", 25)]
    [InlineData("start a focus session for 90 minutes", 90)]
    [InlineData("focus mode for 1 hour", 60)]
    [InlineData("start focus", 30)]
    [InlineData("focus session", 30)]
    public void Focus(string text, int minutes)
    {
        var c = Parse(text);
        Assert.Equal(ProductivityAction.StartFocus, c.Action);
        Assert.Equal(TimeSpan.FromMinutes(minutes), c.Duration);
    }

    [Theory]
    [InlineData("remind me in 20 minutes to call mom", 2, 14, 20, "Call mom")]
    [InlineData("remind me at 6pm to take medicine", 2, 18, 0, "Take medicine")]
    [InlineData("remind me at 6 p.m. to take medicine", 2, 18, 0, "Take medicine")]
    [InlineData("remind me tomorrow at 9 to submit form", 3, 9, 0, "Submit form")]
    [InlineData("remind me at 18:30 to water the plants", 2, 18, 30, "Water the plants")]
    [InlineData("remind me to call mom in 20 minutes", 2, 14, 20, "Call mom")]
    [InlineData("remind me to take medicine at 6pm tomorrow", 3, 18, 0, "Take medicine")]
    [InlineData("remind me to buy 2 eggs at 6pm", 2, 18, 0, "Buy 2 eggs")]
    [InlineData("Remind me in an hour and a half to stretch", 2, 15, 30, "Stretch")]
    [InlineData("remind me in half an hour that the oven is on", 2, 14, 30, "The oven is on")]
    [InlineData("remind me at 9am to stand up", 3, 9, 0, "Stand up")]
    [InlineData("remind me tonight at 8 to call Priya", 2, 20, 0, "Call Priya")]
    [InlineData("set a reminder for 5pm to leave", 2, 17, 0, "Leave")]
    [InlineData("reminder: pay rent tomorrow", 3, 9, 0, "Pay rent")]
    [InlineData("remind me in 10 minutes, check the oven", 2, 14, 10, "Check the oven")]
    [InlineData("remind me in 10 minutes", 2, 14, 10, "")]
    [InlineData("remind me about the meeting at 3", 2, 15, 0, "The meeting")]
    [InlineData("remind me on friday at 5pm to submit the report", 6, 17, 0, "Submit the report")]
    [InlineData("remind me at 12am to sleep", 3, 0, 0, "Sleep")]
    [InlineData("remind me at 12pm to eat", 3, 12, 0, "Eat")]
    [InlineData("can you remind me in 1.5 hours to leave", 2, 15, 30, "Leave")]
    public void Reminders(string text, int day, int hour, int minute, string reminder)
    {
        var c = Parse(text);
        Assert.Equal(ProductivityAction.Remind, c.Action);
        Assert.Equal(new DateTime(2026, 11, day, hour, minute, 0), c.Due);
        Assert.Equal(reminder, c.Text);
    }

    [Fact]
    public void Reminder_without_a_time_asks_for_one()
    {
        var c = Parse("remind me to call mom");
        Assert.Equal(ProductivityAction.ReminderNeedsTime, c.Action);
        Assert.Equal("Call mom", c.Text);
    }

    [Theory]
    [InlineData("note: buy milk", "Buy milk")]
    [InlineData("Note: Buy milk, eggs", "Buy milk, eggs")]
    [InlineData("note buy milk", "Buy milk")]
    [InlineData("take a note buy milk", "Buy milk")]
    [InlineData("make a note that the car is on level 3", "The car is on level 3")]
    [InlineData("note to self: call the bank", "Call the bank")]
    [InlineData("remember that the wifi password is hunter2", "The wifi password is hunter2")]
    [InlineData("note: set a timer for 10 minutes", "Set a timer for 10 minutes")]
    [InlineData("jot down 42 is the answer", "42 is the answer")]
    public void Notes(string text, string note)
    {
        var c = Parse(text);
        Assert.Equal(ProductivityAction.Note, c.Action);
        Assert.Equal(note, c.Text);
    }

    // Ordinary searches must reach the web, never a timer or note.
    [Theory]
    [InlineData("timer for eggs how long")]
    [InlineData("what is a pomodoro")]
    [InlineData("pomodoro technique")]
    [InlineData("how to set a timer on iphone")]
    [InlineData("how to set a timer for 10 minutes")]
    [InlineData("egg timer")]
    [InlineData("timer")]
    [InlineData("stopwatch online")]
    [InlineData("best stopwatch app")]
    [InlineData("focus")]
    [InlineData("focus mode")]
    [InlineData("focus on the family")]
    [InlineData("remember the name of that actor")]
    [InlineData("remember that movie where the guy is stuck on mars")]
    [InlineData("remember that song from the 90s")]
    [InlineData("note 10 pro price")]
    [InlineData("note taking apps")]
    [InlineData("note meaning in hindi")]
    [InlineData("note")]
    [InlineData("notes app for windows")]
    [InlineData("remind me")]
    [InlineData("remind me how to say hello in french")]
    [InlineData("reminder app for android")]
    [InlineData("10 minutes")]
    [InlineData("how long is 90 minutes")]
    [InlineData("half an hour timer music")]
    [InlineData("time in tokyo")]
    [InlineData("")]
    public void Leaves_searches_alone(string text)
    {
        Assert.Null(ProductivityParser.Parse(text, Now));
    }
}

public class TimeFormatTests
{
    [Theory]
    [InlineData(1500, "25:00")]
    [InlineData(1499.2, "25:00")]
    [InlineData(59.5, "1:00")]
    [InlineData(0.4, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-3, "0:00")]
    [InlineData(3909, "1:05:09")]
    public void Countdown_rounds_up(double seconds, string text)
    {
        Assert.Equal(text, TimeFormat.Countdown(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0.9, "0:00")]
    [InlineData(61.7, "1:01")]
    [InlineData(3600, "1:00:00")]
    public void Elapsed_rounds_down(double seconds, string text)
    {
        Assert.Equal(text, TimeFormat.Elapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1500, "25 min")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(90, "1 min 30 s")]
    [InlineData(45, "45 s")]
    [InlineData(7200, "2 h")]
    [InlineData(86400, "1 day")]
    public void Describes_durations(int seconds, string text)
    {
        Assert.Equal(text, TimeFormat.Describe(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void When_is_relative_to_today()
    {
        var now = new DateTime(2026, 11, 2, 14, 0, 0);
        Assert.Equal("Today 6:00 PM", TimeFormat.When(new DateTime(2026, 11, 2, 18, 0, 0), now));
        Assert.Equal("Tomorrow 9:00 AM", TimeFormat.When(new DateTime(2026, 11, 3, 9, 0, 0), now));
        Assert.Equal("Fri 5:30 PM", TimeFormat.When(new DateTime(2026, 11, 6, 17, 30, 0), now));
        Assert.Equal("20 Nov 12:00 AM", TimeFormat.When(new DateTime(2026, 11, 20, 0, 0, 0), now));
    }
}

public class PomodoroTests
{
    [Fact]
    public void Cycles_focus_and_breaks_with_a_long_fourth_break()
    {
        var phases = Enumerable.Range(0, 10).Select(Pomodoro.PhaseAt).ToArray();
        Assert.Equal(new[]
        {
            PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.ShortBreak,
            PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.LongBreak,
            PomodoroPhase.Focus, PomodoroPhase.ShortBreak,
        }, phases);
        Assert.Equal(TimeSpan.FromMinutes(25), Pomodoro.Length(PomodoroPhase.Focus));
        Assert.Equal(TimeSpan.FromMinutes(5), Pomodoro.Length(PomodoroPhase.ShortBreak));
        Assert.Equal(TimeSpan.FromMinutes(15), Pomodoro.Length(PomodoroPhase.LongBreak));
    }

    [Fact]
    public void Labels_rounds()
    {
        Assert.Equal("Focus 1/4", Pomodoro.Label(0));
        Assert.Equal("Break", Pomodoro.Label(1));
        Assert.Equal("Focus 4/4", Pomodoro.Label(6));
        Assert.Equal("Long break", Pomodoro.Label(7));
        Assert.Equal("Focus 1/4", Pomodoro.Label(8));
    }
}
