using Shammy.Core.Audio;
using NAudio.Wave;

namespace Shammy.Infrastructure;

/// <summary>
/// The ringing alarm: <see cref="AlarmTone"/>'s loop, repeated until stopped, swelling from quiet to full over
/// 30 s (unless "Gentle wake" is off) and scaled by the notch sound volume. Unlike the ping it ignores the "Notch sounds" toggle (an alarm that
/// doesn't sound isn't an alarm) and never goes below <see cref="MinVolume"/>.
/// </summary>
internal sealed class AlarmSound : IDisposable
{
    /// <summary>A volume of 0 would make a silent alarm; this is the floor.</summary>
    public const int MinVolume = 20;

    static byte[]? _loop;

    WaveOutEvent? _output;

    public bool IsPlaying => _output is not null;

    public void Start(int volume, bool gentle = true)
    {
        Stop();
        try
        {
            if (_loop is null)
            {
                var samples = AlarmTone.RenderLoop();
                var bytes = new byte[samples.Length * 2];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                _loop = bytes;
            }
            var output = new WaveOutEvent { DesiredLatency = 120 };
            output.Init(new LoopingRamp(_loop, SoundLevel.Gain(Math.Max(MinVolume, volume)), gentle));
            output.Play();
            _output = output;
        }
        catch (Exception ex)
        {
            // The card still shows (and the edge glows) without a sound device.
            Log.Warn("Playing the alarm sound failed", ex);
            _output = null;
        }
    }

    public void Stop()
    {
        var output = _output;
        _output = null;
        if (output is null) return;
        try
        {
            output.Stop();
            output.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Stopping the alarm sound failed", ex);
        }
    }

    public void Dispose() => Stop();

    /// <summary>Repeats the loop forever, applying the 30 s swell (when gentle) and the volume to each sample.</summary>
    sealed class LoopingRamp : IWaveProvider
    {
        readonly byte[] _pcm;
        readonly double _volume;
        readonly bool _gentle;
        long _played; // samples since the start, for the swell
        int _pos;

        public LoopingRamp(byte[] pcm, double volume, bool gentle)
        {
            _pcm = pcm;
            _volume = volume;
            _gentle = gentle;
        }

        public WaveFormat WaveFormat { get; } = new(AlarmTone.SampleRate, 16, 1);

        public int Read(byte[] buffer, int offset, int count)
        {
            count -= count % 2;
            for (int i = 0; i < count; i += 2)
            {
                double gain = (_gentle ? AlarmTone.RampGain((double)_played / AlarmTone.SampleRate) : 1) * _volume;
                short s = SoundLevel.Scale(BitConverter.ToInt16(_pcm, _pos), gain);
                buffer[offset + i] = (byte)s;
                buffer[offset + i + 1] = (byte)(s >> 8);
                _played++;
                _pos += 2;
                if (_pos + 1 >= _pcm.Length) _pos = 0;
            }
            return count;
        }
    }
}
