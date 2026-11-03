namespace Shammy.Core.Audio;

/// <summary>
/// The alarm sound: a gentle rising four-note chime (C5 E5 G5 C6, bell-like like <see cref="Ping"/>) followed by
/// a pause, rendered once as a loop segment the player repeats. It starts quiet and swells over
/// <see cref="RampSeconds"/>, so it wakes without startling.
/// </summary>
public static class AlarmTone
{
    public const int SampleRate = Ping.SampleRate;

    /// <summary>Seconds from the first note to full volume.</summary>
    public const double RampSeconds = 30;

    /// <summary>The volume the ramp starts at (of full).</summary>
    public const double StartGain = 0.12;

    /// <summary>One loop: four notes 0.2 s apart, each ringing out, then quiet until 2.4 s.</summary>
    public const double LoopSeconds = 2.4;

    // Louder than a ping (0.32): it has to be heard across a room.
    const double Level = 0.5;

    static readonly double[] Notes = { 523.25, 659.25, 783.99, 1046.5 };

    /// <summary>16-bit mono PCM at <see cref="SampleRate"/>, exactly <see cref="LoopSeconds"/> long, starting and ending at silence.</summary>
    public static short[] RenderLoop()
    {
        var samples = new double[(int)(LoopSeconds * SampleRate)];
        for (int n = 0; n < Notes.Length; n++)
        {
            double freq = Notes[n];
            int from = (int)(n * 0.2 * SampleRate);
            int count = Math.Min((int)(1.1 * SampleRate), samples.Length - from);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double attack = Math.Min(1, t / 0.006);
                double tone = Math.Sin(2 * Math.PI * freq * t) * Math.Exp(-t / 0.32)
                            + 0.3 * Math.Sin(2 * Math.PI * freq * 2 * t) * Math.Exp(-t / 0.12)
                            + 0.12 * Math.Sin(2 * Math.PI * freq * 3 * t) * Math.Exp(-t / 0.05);
                double tail = Math.Min(1, (count - i) / (0.02 * SampleRate));
                samples[from + i] += tone * attack * tail;
            }
        }
        double peak = samples.Max(Math.Abs);
        double scale = peak > 0 ? Level / peak : 0;
        return samples.Select(s => (short)Math.Round(s * scale * short.MaxValue)).ToArray();
    }

    /// <summary>The swell: <see cref="StartGain"/> at the start, full after <see cref="RampSeconds"/>, eased so each step sounds even.</summary>
    public static double RampGain(double secondsRinging)
    {
        double x = Math.Clamp(secondsRinging / RampSeconds, 0, 1);
        return StartGain + (1 - StartGain) * x * x;
    }
}

/// <summary>The notch sound volume setting (0–100) as a sample gain; squared so the slider feels even to the ear.</summary>
public static class SoundLevel
{
    public static double Gain(int volume)
    {
        double v = Math.Clamp(volume, 0, 100) / 100.0;
        return v * v;
    }

    /// <summary>16-bit samples scaled by <paramref name="gain"/>, clipped rather than wrapped.</summary>
    public static short Scale(short sample, double gain) =>
        (short)Math.Clamp(Math.Round(sample * gain), short.MinValue, short.MaxValue);
}
