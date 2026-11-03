using Shammy.Core.Audio;
using NAudio.Wave;

namespace Shammy.Infrastructure;

/// <summary>
/// Plays Shammy's own ping (see <see cref="Ping"/>) through the default output device. Rendered once per
/// kind and kept in memory; each play gets its own short-lived output so overlapping pings are fine.
/// </summary>
internal static class NotchSound
{
    static readonly Dictionary<PingKind, byte[]> Cache = new();

    /// <summary>Off from the tray ("Notch sounds").</summary>
    public static bool Enabled { get; set; } = true;

    public static void Play(PingKind kind)
    {
        if (!Enabled) return;
        try
        {
            byte[] pcm;
            lock (Cache)
            {
                if (!Cache.TryGetValue(kind, out pcm!))
                {
                    var samples = Ping.Render(kind);
                    pcm = new byte[samples.Length * 2];
                    Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                    Cache[kind] = pcm;
                }
            }
            var output = new WaveOutEvent { DesiredLatency = 80 };
            output.PlaybackStopped += (_, _) => output.Dispose();
            output.Init(new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(Ping.SampleRate, 16, 1)));
            output.Play();
        }
        catch (Exception ex)
        {
            // No output device, or it was unplugged: a missing ping isn't worth more than a log line.
            Log.Warn("Playing the notch sound failed", ex);
        }
    }
}
