using System.Text.Json.Serialization;

namespace Shammy.Core.Productivity;

/// <summary>The days an alarm repeats on. None means a one-off alarm.</summary>
[Flags]
public enum AlarmDays
{
    None = 0,
    Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    EveryDay = Weekdays | Weekends,
}

/// <summary>
/// A real alarm, separate from reminders: a time of day that rings (with a looping sound) on some days or once.
/// The schedule (<see cref="Hour"/>, <see cref="Minute"/>, <see cref="Days"/>, <see cref="Date"/>) is what the user
/// set; the rest is ringing state, kept in the same record so a restart, crash or sleep picks up where it left off.
/// All instants are UTC; the time of day and <see cref="Date"/> are local wall-clock values.
/// </summary>
public sealed class Alarm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Hour { get; set; }
    public int Minute { get; set; }
    public string Label { get; set; } = "";

    /// <summary>Repeat days; <see cref="AlarmDays.None"/> rings once.</summary>
    public AlarmDays Days { get; set; }

    /// <summary>A one-off alarm's local date. Null on a one-off means "the next time the clock shows this time".</summary>
    public DateTime? Date { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>This alarm's snooze length in minutes; null uses the setting (10 by default).</summary>
    public int? SnoozeMinutes { get; set; }

    public DateTime Created { get; set; }

    /// <summary>Occurrences at or before this instant never ring: set when the alarm is created, edited or turned on.</summary>
    public DateTime SinceUtc { get; set; }

    /// <summary>The scheduled occurrence that rang (or was missed) most recently; the next one is searched after it.</summary>
    public DateTime? LastOccurrenceUtc { get; set; }

    /// <summary>Set while the alarm is sounding (survives a restart, so it picks the ring up again).</summary>
    public DateTime? RingingSinceUtc { get; set; }

    public DateTime? SnoozedUntilUtc { get; set; }

    /// <summary>Times the current occurrence rang out and snoozed itself (it stops after <see cref="AlarmSchedule.MaxAutoSnoozes"/>).</summary>
    public int AutoSnoozes { get; set; }

    /// <summary>The occurrence that was last missed (PC asleep or off, or rang out unanswered); cleared when seen.</summary>
    public DateTime? MissedUtc { get; set; }

    [JsonIgnore] public bool Repeats => Days != AlarmDays.None;
    [JsonIgnore] public bool IsRinging => RingingSinceUtc is not null;
    [JsonIgnore] public bool IsSnoozed => SnoozedUntilUtc is not null;
    [JsonIgnore] public TimeSpan TimeOfDay => new(Hour, Minute, 0);
}

/// <summary>An alarm as a command asked for it ("alarm at 6:30 every weekday").</summary>
public sealed record AlarmSpec(int Hour, int Minute, AlarmDays Days, DateTime? Date, string Label);

/// <summary>
/// Which alarm a command means ("delete the 7 am alarm"). <see cref="Exact"/> is false for "the 7 alarm", which
/// matches 7:00 and 19:00.
/// </summary>
public sealed record AlarmTarget(int Hour, int Minute, bool Exact)
{
    public bool Matches(Alarm a) =>
        a.Minute == Minute && (a.Hour == Hour || (!Exact && a.Hour % 12 == Hour % 12));
}
