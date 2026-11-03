namespace Shammy.Core.Notch;

/// <summary>What Shammy the notch pet is feeling, shown by its eyes on the resting notch.</summary>
public enum PetMood { Idle, Music, Notify, TimerDone, Listening, Thinking, MeetingSoon, Tired, Sleepy, Happy }

/// <summary>Everything the pet reacts to, sampled whenever something changes (and on a slow tick).</summary>
public sealed record PetInputs
{
    public DateTime Now { get; init; }

    /// <summary>Wall-clock time, for sleepy hours.</summary>
    public DateTime LocalNow { get; init; }

    public bool Listening { get; init; }
    public bool Thinking { get; init; }

    /// <summary>A timer or alarm has finished and hasn't been noticed yet.</summary>
    public bool TimerDone { get; init; }

    /// <summary>When the latest notification arrived; null when there is none.</summary>
    public DateTime? NotifiedAt { get; init; }

    /// <summary>It was hovered (read) since it arrived.</summary>
    public bool NotificationSeen { get; init; }

    public bool MeetingSoon { get; init; }
    public bool MusicPlaying { get; init; }

    /// <summary>Null on a desktop without a battery.</summary>
    public int? BatteryPercent { get; init; }

    public bool Charging { get; init; }

    /// <summary>The last time the pointer came near the pet (moving the mouse wakes it at night).</summary>
    public DateTime? PointerNearAt { get; init; }

    /// <summary>The last time it was petted (a little wiggle over the resting notch).</summary>
    public DateTime? PettedAt { get; init; }
}

/// <summary>
/// The pet's mood state machine: the most important thing going on wins. Listening and thinking (you're
/// talking to it) come first, then being petted, a finished timer, a fresh notification, a meeting about to
/// start, music, a low battery, late night, and otherwise just hanging out.
/// </summary>
public static class PetMoods
{
    /// <summary>A notification excites it for at most this long (less once it's been seen).</summary>
    public static readonly TimeSpan NotifyFor = TimeSpan.FromSeconds(8);

    /// <summary>Happy after a pat for this long.</summary>
    public static readonly TimeSpan HappyFor = TimeSpan.FromSeconds(2.5);

    /// <summary>A mouse move at night keeps it awake this long.</summary>
    public static readonly TimeSpan AwakeFor = TimeSpan.FromSeconds(12);

    /// <summary>Tired below this battery level when not charging.</summary>
    public const int TiredBelow = 20;

    /// <summary>"Meeting soon" means starting within this many minutes.</summary>
    public const int MeetingWithinMinutes = 5;

    public static PetMood Decide(PetInputs i)
    {
        if (i.Listening) return PetMood.Listening;
        if (i.Thinking) return PetMood.Thinking;
        if (i.PettedAt is { } pet && i.Now - pet < HappyFor && i.Now >= pet) return PetMood.Happy;
        if (i.TimerDone) return PetMood.TimerDone;
        if (i.NotifiedAt is { } n && !i.NotificationSeen && i.Now - n < NotifyFor && i.Now >= n) return PetMood.Notify;
        if (i.MeetingSoon) return PetMood.MeetingSoon;
        if (i.MusicPlaying) return PetMood.Music;
        if (i.BatteryPercent is { } b && b < TiredBelow && !i.Charging) return PetMood.Tired;
        bool awake = i.PointerNearAt is { } near && i.Now - near < AwakeFor;
        if (EyeLogic.IsSleepy(i.LocalNow) && !awake) return PetMood.Sleepy;
        return PetMood.Idle;
    }

    /// <summary>
    /// Whether an event starting at <paramref name="startsAt"/> is about to: within the next
    /// <see cref="MeetingWithinMinutes"/>. One that has already started isn't "soon" any more (its pill is up).
    /// </summary>
    public static bool IsMeetingSoon(DateTimeOffset startsAt, DateTimeOffset now)
    {
        var until = startsAt - now;
        return until >= TimeSpan.Zero && until <= TimeSpan.FromMinutes(MeetingWithinMinutes);
    }
}

/// <summary>
/// Petting: a little side-to-side wiggle of the pointer over the resting notch (two or more changes of
/// direction within 600 ms). Tiny jitters don't count, and one wiggle pets once.
/// </summary>
public sealed class WiggleDetector
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(1200);

    /// <summary>A stroke must travel at least this far (DIPs) before a turn counts.</summary>
    public const double MinStroke = 6;

    public const int Turns = 2;

    readonly List<TimeSpan> _turns = new();
    double _anchor = double.NaN, _extreme;
    int _direction;
    TimeSpan? _lastPet;

    public void Reset()
    {
        _turns.Clear();
        _anchor = double.NaN;
        _direction = 0;
    }

    /// <returns>True once, when the wiggle completes.</returns>
    public bool Add(double x, TimeSpan now)
    {
        if (double.IsNaN(_anchor))
        {
            _anchor = _extreme = x;
            return false;
        }
        if (_direction == 0)
        {
            if (Math.Abs(x - _anchor) >= MinStroke)
            {
                _direction = Math.Sign(x - _anchor);
                _extreme = x;
            }
            return false;
        }
        // Still going the same way: push the extreme further.
        if (Math.Sign(x - _extreme) == _direction)
        {
            _extreme = x;
            return false;
        }
        // Came back far enough from the extreme: that's a turn.
        if (Math.Abs(x - _extreme) < MinStroke) return false;
        _direction = -_direction;
        _extreme = x;
        _turns.Add(now);
        _turns.RemoveAll(t => now - t > Window);
        if (_turns.Count < Turns || (_lastPet is { } last && now - last < Cooldown)) return false;
        _turns.Clear();
        _lastPet = now;
        return true;
    }
}
