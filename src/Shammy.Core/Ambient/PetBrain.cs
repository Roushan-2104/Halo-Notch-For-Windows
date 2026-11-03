namespace Shammy.Core.Ambient;

public enum PetMood { Awake, Excited, Asleep }

/// <summary>
/// The pet's mood from coarse signals only: how long since the last input, whether input keeps arriving
/// (sampled a few times a second, so sustained typing reads as "busy") and whether music is playing.
/// </summary>
public sealed class PetBrain
{
    public static readonly TimeSpan SleepAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BusyWindow = TimeSpan.FromSeconds(3);
    public const double BusyFraction = 0.7;
    const int MinBusySamples = 6;

    readonly Queue<(DateTime At, bool Active)> _samples = new();

    public PetMood Mood { get; private set; } = PetMood.Awake;

    /// <summary>Set by tests/screenshots to pin a mood; null follows the signals.</summary>
    public PetMood? Forced { get; set; }

    /// <summary>True right after the update that woke the pet (the app plays a hop).</summary>
    public bool JustWoke { get; private set; }

    public bool IsBusy(DateTime now)
    {
        Trim(now);
        return _samples.Count >= MinBusySamples && _samples.Count(s => s.Active) >= _samples.Count * BusyFraction;
    }

    /// <returns>true when the mood changed.</returns>
    public bool Update(DateTime now, TimeSpan idle, bool inputSinceLastSample, bool musicPlaying)
    {
        _samples.Enqueue((now, inputSinceLastSample));
        Trim(now);
        var next = Forced ?? Decide(now, idle, musicPlaying);
        JustWoke = Mood == PetMood.Asleep && next != PetMood.Asleep;
        bool changed = next != Mood;
        Mood = next;
        return changed;
    }

    PetMood Decide(DateTime now, TimeSpan idle, bool musicPlaying)
    {
        if (musicPlaying) return PetMood.Excited;
        if (idle >= SleepAfter) return PetMood.Asleep;
        return IsBusy(now) ? PetMood.Excited : PetMood.Awake;
    }

    void Trim(DateTime now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().At > BusyWindow) _samples.Dequeue();
    }
}
