using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Halo.Infrastructure;
using Whisper.net;

namespace Halo.Search;

/// <summary>
/// Offline speech-to-text with whisper.cpp. The multilingual "base" model (~148 MB) is downloaded once
/// on first launch and kept loaded so each query only pays for transcription.
/// </summary>
internal sealed class WhisperService : IDisposable
{
    // Pinned to one published revision and its SHA-256: the file is parsed by native code, so a swapped
    // or tampered model must never load. (Hash = the repository's own LFS SHA-256 for this file.)
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.bin";
    const string ModelSha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";
    const long MinModelBytes = 140_000_000;

    static readonly Regex Noise = new(@"\[[^\]]*\]|\([^)]*\)|♪", RegexOptions.Compiled);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly int _threads = Math.Clamp(Environment.ProcessorCount - 2, 2, 8);
    WhisperFactory? _factory;
    WhisperProcessor? _processor;
    Task? _init;

    public static string ModelPath => Path.Combine(Paths.Models, "ggml-base.bin");

    public bool IsReady => _processor is not null;
    public bool IsDownloading { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? Failure { get; private set; }

    public event Action<double>? Progress;
    public event Action? Ready;
    public event Action<string>? Failed;

    public Task InitAsync() => _init ??= InitCoreAsync();

    async Task InitCoreAsync()
    {
        try
        {
            if (!File.Exists(ModelPath) || !await Task.Run(() => HasExpectedHash(ModelPath)))
            {
                if (File.Exists(ModelPath)) Log.Warn("Voice model didn't match its expected hash; downloading it again");
                await DownloadAsync();
            }
            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(ModelPath);
                _processor = Build(1500);
                // Warm the most common size so the first query isn't slower. Under the gate: a tray
                // language change can reset the processors at the same time.
                _gate.Wait();
                try { Processor(512); }
                finally { _gate.Release(); }
            });
            Failure = null;
            Log.Info("Whisper model loaded");
            Ready?.Invoke();
        }
        catch (Exception ex)
        {
            _init = null;
            Failure = ex is HttpRequestException ? "Couldn't download the voice model (offline?)" : "Voice model failed to load";
            Log.Error("Whisper init failed", ex);
            Failed?.Invoke(Failure);
        }
    }

    async Task DownloadAsync()
    {
        IsDownloading = true;
        var part = ModelPath + ".part";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Halo/0.1");
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 147_951_465;
            await using (var src = await response.Content.ReadAsStreamAsync())
            await using (var dst = File.Create(part))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                double lastReported = -1;
                int n;
                while ((n = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    DownloadProgress = (double)read / total;
                    if (DownloadProgress - lastReported >= 0.01)
                    {
                        lastReported = DownloadProgress;
                        Progress?.Invoke(DownloadProgress);
                    }
                }
            }
            if (new FileInfo(part).Length < MinModelBytes) throw new HttpRequestException("Model download was truncated");
            if (!await Task.Run(() => HasExpectedHash(part))) throw new HttpRequestException("Model download failed its integrity check");
            File.Move(part, ModelPath, true);
        }
        finally
        {
            IsDownloading = false;
            if (File.Exists(part)) File.Delete(part);
        }
    }

    static bool HasExpectedHash(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < MinModelBytes) return false;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
        return string.Equals(hash, ModelSha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        if (_processor is null) return "";
        // The quiet tail while the key is still held is what Whisper loops on ("Play X. Play X.").
        samples = Halo.Core.Search.AudioTrim.TrimSilence(samples, MicRecorder.SampleRate);
        // Whisper is unreliable on clips shorter than a second; pad with silence.
        if (samples.Length < MicRecorder.SampleRate)
            Array.Resize(ref samples, MicRecorder.SampleRate + MicRecorder.SampleRate / 4);

        await _gate.WaitAsync(ct);
        try
        {
            // Whisper normally encodes a full 30 s window (1500 frames) even for a 3 s query. Sizing the
            // audio context to the clip (50 frames per second, plus headroom) cuts encode time several-fold.
            double seconds = (double)samples.Length / MicRecorder.SampleRate;
            int context = Math.Clamp(((int)(seconds * 50) + 128 + 127) / 128 * 128, 384, 1500);
            var processor = Processor(context);
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct)) text.Append(segment.Text);
            return Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Processors are cached per context size: creating one allocates whisper's working buffers.</summary>
    WhisperProcessor Processor(int audioContext)
    {
        if (audioContext >= 1500) return _processor!;
        if (!_byContext.TryGetValue(audioContext, out var p)) _byContext[audioContext] = p = Build(audioContext);
        return p;
    }

    readonly Dictionary<int, WhisperProcessor> _byContext = new();

    /// <summary>"auto" detects the language per query (Hindi, English...) at the cost of an extra pass.</summary>
    public string Language { get; set; } = "en";

    WhisperProcessor Build(int audioContext) => _factory!.CreateBuilder()
        .WithLanguage(Language)
        .WithThreads(_threads)
        .WithAudioContextSize(audioContext)
        .WithNoContext()
        .WithSingleSegment()
        .Build();

    static string Clean(string text) =>
        Halo.Core.Search.TranscriptCleaner.RemoveRepeats(Regex.Replace(Noise.Replace(text, " "), @"\s+", " ").Trim());

    /// <summary>Drop cached processors so the next query uses current settings (e.g. language).</summary>
    public void ResetProcessors()
    {
        _gate.Wait();
        try
        {
            foreach (var p in _byContext.Values) p.Dispose();
            _byContext.Clear();
            if (_factory is not null)
            {
                _processor?.Dispose();
                _processor = Build(1500);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        ResetProcessors();
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
