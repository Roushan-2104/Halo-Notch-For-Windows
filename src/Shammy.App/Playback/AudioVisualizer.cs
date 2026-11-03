using Shammy.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Shammy.Playback;

/// <summary>
/// Listens to what the speakers are playing (WASAPI loopback) and turns it into a few log-spaced
/// frequency band levels for the notch's equalizer bars. Only runs while something is playing.
/// </summary>
internal sealed class AudioVisualizer : IDisposable
{
    public const int Bands = 5;
    const int FftExponent = 10;
    const int FftSize = 1 << FftExponent;

    static readonly float[] Hann = Enumerable.Range(0, FftSize).Select(i => (float)FastFourierTransform.HannWindow(i, FftSize)).ToArray();

    readonly float[] _levels = new float[Bands];
    readonly float[] _ring = new float[FftSize];
    readonly Complex[] _fft = new Complex[FftSize];
    int _ringPos, _sinceLast;
    WasapiLoopbackCapture? _capture;

    readonly HashSet<object> _wanted = new();
    bool _allowed;

    public bool IsRunning => _capture is not null;

    /// <summary>
    /// A visual that shows the levels is on screen (eq bars). Loopback capture and the FFT run only while
    /// something is playing (<see cref="Start"/>) AND something on screen wants them; otherwise the levels
    /// read 0 and nothing is captured.
    /// </summary>
    public void Want(object who, bool on)
    {
        if (on ? !_wanted.Add(who) : !_wanted.Remove(who)) return;
        Apply();
    }

    /// <summary>Something is playing: capture may run (while a visual wants it).</summary>
    public void Start()
    {
        _allowed = true;
        Apply();
    }

    /// <summary>Nothing is playing: stop capturing.</summary>
    public void Stop()
    {
        _allowed = false;
        Apply();
    }

    void Apply()
    {
        if (_allowed && _wanted.Count > 0) StartCapture();
        else StopCapture();
    }

    public float Level(int band) => Volatile.Read(ref _levels[Math.Clamp(band, 0, Bands - 1)]);

    void StartCapture()
    {
        if (_capture is not null) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            var capture = _capture;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is null) return;
                Log.Warn("Loopback capture stopped", e.Exception);
                // The stream died (sample-rate or spatial-sound change, another app in exclusive mode). Drop
                // it so the bars go flat and the next media change can start a fresh one, instead of the
                // bars freezing at their last heights with Start() refusing because it "is running".
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_capture, capture)) StopCapture();
                });
            };
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Warn("Loopback capture unavailable", ex);
            _capture = null;
        }
    }

    void StopCapture()
    {
        var c = _capture;
        _capture = null;
        if (c is null) return;
        try
        {
            c.DataAvailable -= OnData;
            c.StopRecording();
            c.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Stopping loopback capture failed", ex);
        }
        for (int i = 0; i < Bands; i++) Volatile.Write(ref _levels[i], 0);
    }

    /// <summary>Restart after the default output device changes (loopback is bound to the old one).</summary>
    public void Restart()
    {
        if (_capture is null) return;
        StopCapture();
        StartCapture();
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture cap) return;
        var fmt = cap.WaveFormat;
        int channels = fmt.Channels;
        // The shared-mode mix format is 32-bit float in practice; 16-bit PCM is handled just in case.
        bool isFloat = fmt.BitsPerSample == 32;
        int bytesPerSample = fmt.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        if (frameBytes == 0) return;

        for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int at = offset + c * bytesPerSample;
                sum += isFloat && bytesPerSample == 4
                    ? BitConverter.ToSingle(e.Buffer, at)
                    : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, at) / 32768f : 0;
            }
            _ring[_ringPos] = sum / channels;
            _ringPos = (_ringPos + 1) & (FftSize - 1);
            // About 20 analyses a second is plenty for bars that repaint at 10 fps (was ~94 at 48 kHz).
            if (++_sinceLast >= Math.Max(FftSize / 2, fmt.SampleRate / 20))
            {
                _sinceLast = 0;
                Analyze(fmt.SampleRate);
            }
        }
    }

    void Analyze(int sampleRate)
    {
        for (int i = 0; i < FftSize; i++)
        {
            float window = Hann[i];
            _fft[i].X = _ring[(_ringPos + i) & (FftSize - 1)] * window;
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftExponent, _fft);

        double binHz = (double)sampleRate / FftSize;
        const double lo = 50, hi = 7000;
        for (int b = 0; b < Bands; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / Bands);
            double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / Bands);
            int i0 = Math.Max(1, (int)(f0 / binHz)), i1 = Math.Min(FftSize / 2 - 1, Math.Max(i0 + 1, (int)(f1 / binHz)));
            double peak = 0;
            for (int i = i0; i <= i1; i++)
            {
                double mag = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                if (mag > peak) peak = mag;
            }
            // NAudio's forward FFT is scaled by 1/N; map roughly -70..-12 dB onto 0..1. Music has far less
            // energy up high, so higher bands get a tilt boost (~4.5 dB per band) to keep all bars alive.
            double db = 20 * Math.Log10(peak + 1e-9) + b * 4.5;
            float level = (float)Math.Clamp((db + 70) / 58, 0, 1);
            Volatile.Write(ref _levels[b], level);
        }
    }

    public void Dispose()
    {
        _allowed = false;
        StopCapture();
    }
}
