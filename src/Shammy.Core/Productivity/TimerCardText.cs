using System.Globalization;

namespace Shammy.Core.Productivity;

/// <summary>A pomodoro round dot on the timer card: done, the one running now, or still to come.</summary>
public enum RoundDot { Done, Current, Pending }

/// <summary>Words and numbers for the v2 timer card and its glance.</summary>
public static class TimerCardText
{
    /// <summary>"Tea 4:32", or "Timer 4:32" for an unnamed one; fitted to the ~18-character glance.</summary>
    public static string Glance(string label, string display)
    {
        var name = label.Trim().Length > 0 ? label.Trim() : "Timer";
        int room = Math.Max(1, 18 - display.Length - 1);
        if (name.Length > room) name = name[..Math.Max(1, room - 1)].TrimEnd() + "\u2026";
        return $"{name} {display}";
    }

    /// <summary>How much of a countdown is left, 0..1 (the ring drains as time runs out).</summary>
    public static double RingLeft(TimeSpan remaining, TimeSpan total) =>
        total <= TimeSpan.Zero ? 0 : Math.Clamp(remaining.TotalSeconds / total.TotalSeconds, 0, 1);

    /// <summary>A stopwatch has no end, so its ring sweeps once a minute.</summary>
    public static double StopwatchRing(TimeSpan elapsed) => elapsed.TotalSeconds % 60 / 60;

    /// <summary>"Ends at 10:24 AM", "Paused · 4:32 left".</summary>
    public static string Ends(DateTime endsLocal, bool paused, string display) =>
        paused ? $"Paused \u00B7 {display} left" : "Ends at " + endsLocal.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"of 5:00": the length under the big digits.</summary>
    public static string Of(TimeSpan total) => "of " + TimeFormat.Countdown(total);

    /// <summary>
    /// The four focus-round dots for pomodoro phase <paramref name="step"/> (0 = first focus): rounds before
    /// the current one are done; during a break, the round just finished counts as done.
    /// </summary>
    public static RoundDot[] PomodoroDots(int step)
    {
        int round = Pomodoro.Round(step); // 1..4
        bool onBreak = Pomodoro.PhaseAt(step) != PomodoroPhase.Focus;
        var dots = new RoundDot[4];
        for (int i = 1; i <= 4; i++)
            dots[i - 1] = i < round || (onBreak && i == round) ? RoundDot.Done : i == round ? RoundDot.Current : RoundDot.Pending;
        return dots;
    }

    /// <summary>"Focus · round 2 of 4", "Break after round 2".</summary>
    public static string PomodoroLine(int step)
    {
        int round = Pomodoro.Round(step);
        return Pomodoro.PhaseAt(step) switch
        {
            PomodoroPhase.Focus => $"Focus \u00B7 round {round} of 4",
            PomodoroPhase.LongBreak => "Long break",
            _ => $"Break after round {round}",
        };
    }

    /// <summary>The quick-add chips' lengths (5m, 10m, 25m).</summary>
    public static readonly int[] QuickMinutes = { 5, 10, 25 };
}
