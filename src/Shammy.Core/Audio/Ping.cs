namespace Shammy.Core.Audio;

public enum PingKind
{
    /// <summary>A notification: one soft, bright tap.</summary>
    Notification,

    /// <summary>A timer or reminder: two rising notes, a little more noticeable.</summary>
    Alert,
}

/// <summary>
/// Shammy's own sounds, synthesised rather than shipped as files: a short bell-like tone (a sine with a
/// quieter octave-and-a-fifth partial) that starts softly and dies away, so it reads as a "ping" and never as
/// Windows' default chime.
/// </summary>
public static class Ping
{
    public const int SampleRate = 48000;

    // Peak level well under full scale: it should be heard, not startle.
    const double Level = 0.32;

    /// <summary>16-bit mono PCM at <see cref="SampleRate"/>.</summary>
    public static short[] Render(PingKind kind) => kind switch
    {
        // E6
        PingKind.Notification => Mix(Note(1318.5, 0.0, 0.42)),
        // E6 then A6
        _ => Mix(Note(1318.5, 0.0, 0.5), Note(1760.0, 0.14, 0.6)),
    };

    static (double Freq, double Start, double Length) Note(double freq, double start, double length) => (freq, start, length);

    static short[] Mix(params (double Freq, double Start, double Length)[] notes)
    {
        double end = notes.Max(n => n.Start + n.Length);
        var samples = new double[(int)(end * SampleRate)];
        foreach (var (freq, start, length) in notes)
        {
            int from = (int)(start * SampleRate);
            int count = Math.Min((int)(length * SampleRate), samples.Length - from);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                // 4 ms fade-in (no click), then an exponential ring-down; the overtone fades faster, like a bell.
                double attack = Math.Min(1, t / 0.004);
                double tone = Math.Sin(2 * Math.PI * freq * t) * Math.Exp(-t / 0.11)
                            + 0.25 * Math.Sin(2 * Math.PI * freq * 3 * t) * Math.Exp(-t / 0.035);
                // A short tail fade so the note ends at exactly zero.
                double tail = Math.Min(1, (count - i) / (0.01 * SampleRate));
                samples[from + i] += tone * attack * tail;
            }
        }
        double peak = samples.Max(Math.Abs);
        double scale = peak > 0 ? Level / peak : 0;
        return samples.Select(s => (short)Math.Round(s * scale * short.MaxValue)).ToArray();
    }
}
