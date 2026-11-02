namespace Halo.Core.Search;

/// <summary>
/// Cuts the silence before and after speech in a hold-to-talk recording. Whisper hallucinates and
/// repeats itself on long quiet stretches, and shorter clips also transcribe faster.
/// </summary>
public static class AudioTrim
{
    /// <param name="sampleRate">Samples per second (mono).</param>
    /// <param name="keepBeforeMs">Audio kept before the first voiced frame so soft onsets survive.</param>
    /// <param name="keepAfterMs">Audio kept after the last voiced frame so trailing consonants survive.</param>
    public static float[] TrimSilence(float[] samples, int sampleRate, int keepBeforeMs = 200, int keepAfterMs = 350)
    {
        int frame = sampleRate * 30 / 1000;
        int frames = samples.Length / frame;
        if (frames < 3) return samples;

        var rms = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = f * frame; i < (f + 1) * frame; i++) sum += samples[i] * samples[i];
            rms[f] = Math.Sqrt(sum / frame);
        }

        // Voiced = clearly above this recording's own noise floor (quietest 20% of frames).
        var sorted = rms.OrderBy(r => r).ToArray();
        double floor = sorted[(int)(frames * 0.2)];
        double threshold = Math.Max(0.015, floor * 3);

        int first = Array.FindIndex(rms, r => r > threshold);
        int last = Array.FindLastIndex(rms, r => r > threshold);
        if (first < 0) return samples;

        int start = Math.Max(0, first * frame - sampleRate * keepBeforeMs / 1000);
        int end = Math.Min(samples.Length, (last + 1) * frame + sampleRate * keepAfterMs / 1000);
        return samples[start..end];
    }
}
