namespace Shammy.Core.Productivity;

public enum AlarmDecision
{
    None,

    /// <summary>An occurrence (or a snooze) is due: start ringing.</summary>
    Ring,

    /// <summary>It is ringing and still within <see cref="AlarmSchedule.RingTimeout"/>.</summary>
    KeepRinging,

    /// <summary>It rang out unanswered: snooze it by itself.</summary>
    AutoSnooze,

    /// <summary>Too late to ring (the PC slept through it), or it rang out after the last auto-snooze.</summary>
    Missed,
}

public readonly record struct AlarmCheck(AlarmDecision Decision, DateTime? OccurrenceUtc = null);

/// <summary>
/// When alarms ring, as pure functions of the alarm, "now" and a time zone, so DST, midnight, sleep and snoozing
/// can be tested. Occurrences are computed on the local wall clock and turned into UTC instants: an alarm set for
/// 7:00 rings at 7:00 whatever the offset that day. A time skipped by the clocks going forward (2:30 on the spring
/// change) rings at the moment the clocks jump; a time that happens twice (autumn) rings the first time only.
/// </summary>
public static class AlarmSchedule
{
    /// <summary>An occurrence found this late (after sleep, or a start-up after it) still rings; later is "missed".</summary>
    public static readonly TimeSpan CatchUp = TimeSpan.FromMinutes(10);

    /// <summary>Ringing this long unanswered snoozes the alarm by itself.</summary>
    public static readonly TimeSpan RingTimeout = TimeSpan.FromMinutes(5);

    public const int MaxAutoSnoozes = 3;
    public const int DefaultSnoozeMinutes = 10;

    public static bool Has(AlarmDays days, DayOfWeek day) => (days & Flag(day)) != 0;

    public static AlarmDays Flag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => AlarmDays.Monday,
        DayOfWeek.Tuesday => AlarmDays.Tuesday,
        DayOfWeek.Wednesday => AlarmDays.Wednesday,
        DayOfWeek.Thursday => AlarmDays.Thursday,
        DayOfWeek.Friday => AlarmDays.Friday,
        DayOfWeek.Saturday => AlarmDays.Saturday,
        _ => AlarmDays.Sunday,
    };

    /// <summary>The first occurrence strictly after <paramref name="afterUtc"/>, or null when there is none (a past one-off).</summary>
    public static DateTime? NextOccurrenceUtc(Alarm a, DateTime afterUtc, TimeZoneInfo zone)
    {
        var tod = a.TimeOfDay;
        if (!a.Repeats)
        {
            // A one-off rings once: after its occurrence has been handled there is nothing more.
            if (a.LastOccurrenceUtc is not null) return null;
            if (a.Date is { } date)
            {
                var at = ToUtc(date.Date + tod, zone);
                return at > afterUtc ? at : null;
            }
        }
        var startDay = TimeZoneInfo.ConvertTimeFromUtc(Utc(afterUtc), zone).Date;
        // A day either side covers offsets that move the wall-clock date relative to the UTC instant.
        for (int i = -1; i <= 8; i++)
        {
            var day = startDay.AddDays(i);
            if (a.Repeats && !Has(a.Days, day.DayOfWeek)) continue;
            var at = ToUtc(day + tod, zone);
            if (at > afterUtc) return at;
        }
        return null;
    }

    /// <summary>When it rings next as the user sees it (a snooze counts), or null when off or a spent one-off.</summary>
    public static DateTime? UpcomingUtc(Alarm a, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (a.RingingSinceUtc is { } r) return r;
        if (!a.Enabled) return null;
        if (a.SnoozedUntilUtc is { } s) return s;
        var after = a.LastOccurrenceUtc ?? a.SinceUtc;
        if (after < nowUtc && a.Repeats) after = nowUtc;
        return NextOccurrenceUtc(a, after, zone);
    }

    /// <summary>What should happen to <paramref name="a"/> at <paramref name="nowUtc"/>.</summary>
    public static AlarmCheck Check(Alarm a, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (a.RingingSinceUtc is { } ringing)
        {
            var rang = nowUtc - ringing;
            if (rang < RingTimeout) return new AlarmCheck(AlarmDecision.KeepRinging, a.LastOccurrenceUtc);
            // Ringing for longer than the catch-up window means the PC slept (or Shammy was closed) mid-ring.
            bool missed = a.AutoSnoozes >= MaxAutoSnoozes || rang > RingTimeout + CatchUp;
            return new AlarmCheck(missed ? AlarmDecision.Missed : AlarmDecision.AutoSnooze, a.LastOccurrenceUtc);
        }
        if (!a.Enabled) return default;
        if (a.SnoozedUntilUtc is { } snoozed)
        {
            if (nowUtc < snoozed) return default;
            return new AlarmCheck(nowUtc - snoozed <= CatchUp ? AlarmDecision.Ring : AlarmDecision.Missed, a.LastOccurrenceUtc ?? snoozed);
        }

        var next = NextOccurrenceUtc(a, a.LastOccurrenceUtc ?? a.SinceUtc, zone);
        if (next is not { } due || due > nowUtc) return default;
        // After a long sleep several occurrences may have passed: only the latest matters.
        for (int guard = 0; guard < 1000 && a.Repeats; guard++)
        {
            if (NextOccurrenceUtc(a, due, zone) is not { } later || later > nowUtc) break;
            due = later;
        }
        return new AlarmCheck(nowUtc - due <= CatchUp ? AlarmDecision.Ring : AlarmDecision.Missed, due);
    }

    /// <summary>The earliest moment any of the alarms needs looking at again (for the wake-up timer), or null.</summary>
    public static DateTime? NextWakeUtc(IEnumerable<Alarm> alarms, DateTime nowUtc, TimeZoneInfo zone)
    {
        DateTime? best = null;
        foreach (var a in alarms)
        {
            DateTime? at = a.RingingSinceUtc is { } r ? r + RingTimeout
                : !a.Enabled ? null
                : a.SnoozedUntilUtc ?? NextOccurrenceUtc(a, a.LastOccurrenceUtc ?? a.SinceUtc, zone);
            if (at is { } t && (best is null || t < best)) best = t;
        }
        return best;
    }

    // ---- State changes ----------------------------------------------------------------------------

    /// <summary>A due occurrence (or the end of a snooze) starts ringing.</summary>
    public static void StartRinging(Alarm a, DateTime? occurrenceUtc, DateTime nowUtc)
    {
        // Ringing again after a snooze keeps the original occurrence.
        if (a.SnoozedUntilUtc is null && occurrenceUtc is { } occ) a.LastOccurrenceUtc = occ;
        a.SnoozedUntilUtc = null;
        a.RingingSinceUtc = nowUtc;
    }

    /// <summary>Stop: done with this occurrence. A one-off turns itself off, like a phone's.</summary>
    public static void Stop(Alarm a)
    {
        a.RingingSinceUtc = null;
        a.SnoozedUntilUtc = null;
        a.AutoSnoozes = 0;
        if (!a.Repeats) a.Enabled = false;
    }

    /// <summary>Snooze for <paramref name="minutes"/>; <paramref name="auto"/> when it rang out unanswered.</summary>
    public static void Snooze(Alarm a, DateTime nowUtc, int minutes, bool auto)
    {
        a.RingingSinceUtc = null;
        a.SnoozedUntilUtc = nowUtc + TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 120));
        if (auto) a.AutoSnoozes++;
    }

    public static void MarkMissed(Alarm a, DateTime? occurrenceUtc)
    {
        a.RingingSinceUtc = null;
        a.SnoozedUntilUtc = null;
        a.AutoSnoozes = 0;
        if (occurrenceUtc is { } occ)
        {
            a.MissedUtc = occ;
            if (a.LastOccurrenceUtc is null || occ > a.LastOccurrenceUtc) a.LastOccurrenceUtc = occ;
        }
        else a.MissedUtc ??= a.LastOccurrenceUtc;
        if (!a.Repeats) a.Enabled = false;
    }

    /// <summary>Turned on, created or edited: forget the old ringing state and count occurrences from now.</summary>
    public static void Reset(Alarm a, DateTime nowUtc)
    {
        a.SinceUtc = nowUtc;
        a.LastOccurrenceUtc = null;
        a.RingingSinceUtc = null;
        a.SnoozedUntilUtc = null;
        a.AutoSnoozes = 0;
        a.MissedUtc = null;
    }

    public static int SnoozeLength(Alarm a, int defaultMinutes) => Math.Clamp(a.SnoozeMinutes ?? defaultMinutes, 1, 60);

    // ---- Time zone helpers -------------------------------------------------------------------------

    /// <summary>A local wall-clock time as a UTC instant: a skipped time is moved to the jump, a doubled one takes the first.</summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            var t = local;
            for (int i = 0; i < 24 * 60 && zone.IsInvalidTime(t); i++) t = t.AddMinutes(1);
            // Whole minutes past the gap: 2:30 in a 2:00–3:00 gap becomes 3:00, the instant the clocks jumped.
            local = t;
        }
        if (zone.IsAmbiguousTime(local))
        {
            var largest = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - largest, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Utc);
}
