namespace Shammy.Core.Productivity;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }

/// <summary>The classic cycle: 25 min focus, 5 min break, and every fourth break is 15 min.</summary>
public static class Pomodoro
{
    public static readonly TimeSpan FocusLength = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreakLength = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LongBreakLength = TimeSpan.FromMinutes(15);

    /// <summary>Phase number <paramref name="step"/> counted from 0: focus, break, focus, break, ... long break.</summary>
    public static PomodoroPhase PhaseAt(int step) =>
        step % 2 == 0 ? PomodoroPhase.Focus : step % 8 == 7 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;

    public static TimeSpan Length(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => FocusLength,
        PomodoroPhase.ShortBreak => ShortBreakLength,
        _ => LongBreakLength,
    };

    /// <summary>Which of the four focus rounds this step belongs to (1-4).</summary>
    public static int Round(int step) => step / 2 % 4 + 1;

    public static string Label(int step) => PhaseAt(step) switch
    {
        PomodoroPhase.Focus => $"Focus {Round(step)}/4",
        PomodoroPhase.ShortBreak => "Break",
        _ => "Long break",
    };
}
