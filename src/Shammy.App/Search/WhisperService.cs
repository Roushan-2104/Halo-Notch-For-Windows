using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Shammy.Core.Music;
using Shammy.Core.Search;
using Shammy.Infrastructure;
using Whisper.net;

namespace Shammy.Search;

/// <summary>
/// Offline speech-to-text with whisper.cpp.
/// <list type="bullet">
/// <item>The small multilingual "base" model (~148 MB) is fetched first, so voice works within a minute
/// of installing.</item>
/// <item>In the background Shammy then probes the hardware once and upgrades to a model that suits it:
/// large-v3-turbo on a capable graphics card (via Vulkan), or "small" on the CPU. On a benchmark of real
/// Indian-English commands that cut word errors from 41% to about 17%.</item>
/// <item>Every model is pinned to one published revision and its SHA-256, because the file is parsed by
/// native code and a swapped or tampered model must never load.</item>
/// <item>Speech is decoded as English once. Only two cases listen again: a play command, primed with song
/// names so Hindi titles come out spelled right, and a transcript that looks wrong for English (another
/// script, low confidence, Hinglish words), retried as Hindi written in Latin letters.</item>
/// </list>
/// </summary>
internal sealed class WhisperService : IDisposable
{
    internal sealed record VoiceModel(string Name, string File, long Bytes, string Sha256, bool FullWindow)
    {
        public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/{File}";
        public string LocalPath => Path.Combine(Paths.Models, File);
    }

    const string Revision = "5359861c739e955e79d9a303bcbc70fb988958b1";

    // FullWindow: on a GPU one fixed 30 s window is fastest (each window size costs a slow GPU setup), and
    // the full window is also the more accurate; on the CPU, windows sized to the clip are several times faster.
    static readonly VoiceModel Base = new("base", "ggml-base.bin", 147_951_465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", false);
    static readonly VoiceModel Small = new("small", "ggml-small.bin", 487_601_967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", false);
    static readonly VoiceModel Turbo = new("large-v3-turbo", "ggml-large-v3-turbo-q5_0.bin", 574_041_195, "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", true);

    // A graphics card that runs "base" over a full window this fast runs large-v3-turbo in about a second.
    static readonly TimeSpan GpuGoodEnough = TimeSpan.FromMilliseconds(450);

    static readonly Regex Noise = new(@"\[[^\]]*\]|\([^)]*\)|♪", RegexOptions.Compiled);

    // A play command's second pass. ~250 characters is about 20 names: past that the CPU pass slows down
    // more than the extra names help (most requests are history or top hits, and the catalog match after
    // the pass catches near misses anyway).
    const int SongPromptBudget = 250;

    // The Hindi retry is told what Hinglish looks like in Latin letters, so it writes "kal subah ... laga do"
    // rather than Devanagari: readable in the notch, and what the Hinglish command rewrite expects.
    const string HinglishPrompt = "kal subah alarm laga do. mujhe ek achha phone batao. aaj mausam kaisa hai. paanch minute ka timer laga do.";

    static readonly string[] BuiltInSongs = SongCatalog.Songs.Concat(SongCatalog.Artists).ToArray();

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly int _threads = Math.Clamp(Environment.ProcessorCount - 2, 2, 8);
    readonly AppSettings _settings;
    Engine? _engine;
    Task? _init;

    // "base" on the CPU, only for the second pass of play commands: with a prompt full of song names it
    // spells them right as well as large-v3-turbo does, in about half the time (and the GPU stays free).
    WhisperFactory? _songFactory;
    Task? _songInit;

    public WhisperService(AppSettings settings) => _settings = settings;

    public bool IsReady => _engine is not null;
    public bool IsDownloading { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? Failure { get; private set; }

    /// <summary>"base", "small" or "large-v3-turbo", plus "(GPU n)" when it runs on a graphics card.</summary>
    public string ActiveModel => _engine?.Describe() ?? "-";

    public event Action<double>? Progress;
    public event Action? Ready;
    public event Action<string>? Failed;

    /// <summary>Raised (with the model name) once a better model has replaced the first one.</summary>
    public event Action<string>? Upgraded;

    /// <summary>"auto" detects the language per query (Hindi, English...) at the cost of an extra pass.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Words to prime recognition with (commands, installed app names); read when a model loads.</summary>
    public Func<IEnumerable<string>>? Vocabulary { get; set; }

    /// <summary>Song titles and artists this PC played lately, best first; read on every play command.</summary>
    public Func<IEnumerable<string>>? SongHistory { get; set; }

    public Task InitAsync() => _init ??= InitCoreAsync();

    async Task InitCoreAsync()
    {
        try
        {
            // Start with whatever good model is already here (the upgrade, if it happened before), else base.
            var chosen = ChosenModel(out int gpu);
            var startWith = chosen is not null && await Task.Run(() => HasExpectedHash(chosen)) ? chosen : Base;
            if (startWith == Base && !await Task.Run(() => HasExpectedHash(Base)))
            {
                if (File.Exists(Base.LocalPath)) Log.Warn("Voice model didn't match its expected hash; downloading it again");
                await DownloadAsync(Base, report: true);
            }
            var engine = await Task.Run(() => Load(startWith, startWith == chosen ? gpu : -1));
            Swap(engine);
            Failure = null;
            Log.Info($"Voice model loaded: {engine.Describe()}");
            Ready?.Invoke();
            if (startWith == Base) _ = UpgradeAsync();
            _ = PrepareSongPassAsync();
        }
        catch (Exception ex)
        {
            _init = null;
            Failure = ex is HttpRequestException ? "Couldn't download the voice model (offline?)" : "Voice model failed to load";
            Log.Error("Whisper init failed", ex);
            Failed?.Invoke(Failure);
        }
    }

    /// <summary>The model this PC was matched to, or null before the hardware probe ran. gpu = -1 for the CPU.</summary>
    VoiceModel? ChosenModel(out int gpu)
    {
        gpu = -1;
        var choice = _settings.VoiceEngine;
        if (string.IsNullOrEmpty(choice)) return null;
        var parts = choice.Split('@');
        var model = parts[0] == Turbo.Name ? Turbo : parts[0] == Small.Name ? Small : null;
        if (parts.Length > 1 && int.TryParse(parts[1], out var g)) gpu = g;
        return model;
    }

    /// <summary>Probe the hardware (once), fetch the model that suits it in the background, then switch to it.</summary>
    async Task UpgradeAsync()
    {
        try
        {
            if (ChosenModel(out _) is null)
            {
                var (model, gpu) = await Task.Run(Probe);
                _settings.VoiceEngine = gpu >= 0 ? $"{model.Name}@{gpu}" : model.Name;
                _settings.Save();
            }
            var target = ChosenModel(out int device)!;
            if (!await Task.Run(() => HasExpectedHash(target))) await DownloadAsync(target, report: false);
            var engine = await Task.Run(() => Load(target, device));
            Swap(engine);
            Log.Info($"Voice upgraded to {engine.Describe()}");
            Upgraded?.Invoke(target.Name);
        }
        catch (Exception ex)
        {
            // The base model keeps working; the upgrade is retried next start.
            Log.Warn("Voice upgrade failed; staying on the base model", ex);
        }
    }

    /// <summary>
    /// Times "base" over a full window on each graphics card and on the CPU. A fast card gets turbo on that
    /// card, anything else gets "small" on the CPU. A marker is saved first: if a bad graphics driver crashes
    /// the probe, the next start skips the graphics cards entirely.
    /// </summary>
    (VoiceModel Model, int Gpu) Probe()
    {
        if (_settings.VoiceProbeStarted)
        {
            Log.Warn("The last voice hardware probe didn't finish; using the CPU model");
            return (Small, -1);
        }
        _settings.VoiceProbeStarted = true;
        _settings.Save();
        var silence = new float[MicRecorder.SampleRate * 3];
        (int Gpu, TimeSpan Time) best = (-1, TimeSpan.MaxValue);
        for (int device = 0; device < 4; device++)
        {
            try
            {
                using var factory = WhisperFactory.FromPath(Base.LocalPath, new WhisperFactoryOptions { UseGpu = true, GpuDevice = device });
                using var p = factory.CreateBuilder().WithLanguage("en").WithThreads(_threads).WithNoContext().WithSingleSegment().Build();
                Run(p, silence);
                var sw = Stopwatch.StartNew();
                Run(p, silence);
                Log.Info($"Voice probe: graphics device {device} took {sw.ElapsedMilliseconds} ms");
                if (sw.Elapsed < best.Time) best = (device, sw.Elapsed);
            }
            catch (Exception ex)
            {
                Log.Info($"Voice probe: no usable graphics device {device} ({ex.GetType().Name})");
                break;
            }
        }
        _settings.VoiceProbeStarted = false;
        _settings.Save();
        return best.Gpu >= 0 && best.Time <= GpuGoodEnough ? (Turbo, best.Gpu) : (Small, -1);
    }

    Engine Load(VoiceModel model, int gpu)
    {
        try
        {
            var options = new WhisperFactoryOptions { UseGpu = gpu >= 0, GpuDevice = Math.Max(0, gpu) };
            var engine = new Engine(this, model, WhisperFactory.FromPath(model.LocalPath, options), gpu);
            engine.Warm();
            return engine;
        }
        catch (Exception ex) when (gpu >= 0)
        {
            // The graphics card refused (driver update, card gone): run the same model on the CPU instead.
            Log.Warn($"Loading {model.Name} on graphics device {gpu} failed; using the CPU", ex);
            _settings.VoiceEngine = Small.Name;
            _settings.Save();
            return Load(HasExpectedHash(Small) ? Small : Base, -1);
        }
    }

    void Swap(Engine engine)
    {
        _gate.Wait();
        Engine? old;
        try
        {
            old = _engine;
            _engine = engine;
        }
        finally
        {
            _gate.Release();
        }
        old?.Dispose();
    }

    async Task DownloadAsync(VoiceModel model, bool report)
    {
        if (report) IsDownloading = true;
        var part = model.LocalPath + ".part";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Shammy/0.4");
            using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? model.Bytes;
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
                    if (!report) continue;
                    DownloadProgress = (double)read / total;
                    if (DownloadProgress - lastReported >= 0.01)
                    {
                        lastReported = DownloadProgress;
                        Progress?.Invoke(DownloadProgress);
                    }
                }
            }
            if (!await Task.Run(() => HasExpectedHash(model, part))) throw new HttpRequestException($"{model.Name} model download failed its integrity check");
            File.Move(part, model.LocalPath, true);
        }
        finally
        {
            if (report) IsDownloading = false;
            if (File.Exists(part)) File.Delete(part);
        }
    }

    static bool HasExpectedHash(VoiceModel model) => HasExpectedHash(model, model.LocalPath);

    static bool HasExpectedHash(VoiceModel model, string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        if (fs.Length != model.Bytes) return false;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
        return string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        if (_engine is null) return "";
        // The quiet tail while the key is still held is what Whisper loops on ("Play X. Play X.").
        samples = Shammy.Core.Search.AudioTrim.TrimSilence(samples, MicRecorder.SampleRate);
        // Whisper is unreliable on clips shorter than a second; pad with silence.
        if (samples.Length < MicRecorder.SampleRate)
            Array.Resize(ref samples, MicRecorder.SampleRate + MicRecorder.SampleRate / 4);

        await _gate.WaitAsync(ct);
        try
        {
            var engine = _engine;
            var (result, probability) = await DecodeAsync(engine.For(samples.Length), samples, ct);
            if (EchoesPrompt(result, engine.CurrentPrompt))
            {
                // It recited the priming text instead of hearing the user: listen again without it.
                Log.Info("Voice: the transcript echoed the prompt; retrying without it");
                (result, probability) = await DecodeAsync(engine.Unprimed(samples.Length), samples, ct);
            }

            // Only play commands and doubtful transcripts pay for another pass; plain English never does.
            if (MusicCommand.LooksLikePlay(result))
            {
                result = await SongPassAsync(samples, result, probability, ct);
            }
            else if (Language == "en" && TranscriptCheck.Assess(result, probability) is var doubt and not TranscriptDoubt.None)
            {
                var sw = Stopwatch.StartNew();
                var (hindi, hindiProbability) = await DecodeAsync(engine.Hindi(samples.Length), samples, ct);
                bool use = TranscriptCheck.PreferRetry(doubt, result, probability, hindi, hindiProbability);
                Log.Info($"Voice: English looked wrong ({doubt}); Hindi pass {sw.ElapsedMilliseconds} ms, {(use ? "used it" : "kept English")}");
                if (use) result = hindi;
            }

            // A Hindi song request in Devanagari is searched for in Latin letters, as music services spell it.
            if (Devanagari.Contains(result) && Devanagari.ToLatin(result) is var latin && MusicCommand.LooksLikePlay(latin)) result = latin;
            return MusicCommand.CorrectSong(result, q => SongMatcher.Correct(q, KnownSongs(), out _));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The first pass heard a play command, but song names (Hindi ones especially) are what it gets wrong:
    /// "play kesariya" comes out "Play case Rhea". Listen again primed with song names, the user's own first,
    /// and keep whichever transcript names a known song (else the more confident one).
    /// </summary>
    async Task<string> SongPassAsync(float[] samples, string first, float firstProbability, CancellationToken ct)
    {
        if (_songFactory is not { } factory) return first;
        var sw = Stopwatch.StartNew();
        var known = KnownSongs();
        var names = SongMatcher.PromptNames(first, History(), BuiltInSongs, SongPromptBudget);
        string second;
        float secondProbability;
        await using (var processor = SongProcessor(factory, "Play " + string.Join(", ", names) + ".", samples.Length))
            (second, secondProbability) = await DecodeAsync(processor, samples, ct);
        second = MusicCommand.FirstRequest(second);

        static int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        // A transcript much longer than the first one is the name list recited back, not the user.
        bool recited = Words(second) > Words(first) + 6;
        var firstMatch = MusicCommand.Parse(first) is { } a ? SongMatcher.FindBest(a.Query, known) : null;
        var secondMatch = MusicCommand.Parse(second) is { } b ? SongMatcher.FindBest(b.Query, known) : null;
        bool useSecond = !recited && MusicCommand.LooksLikePlay(second) &&
                         (secondMatch is not null ? firstMatch is null || secondMatch.Score >= firstMatch.Score
                                                  : firstMatch is null && secondProbability > firstProbability);
        Log.Info($"Voice: song pass {sw.ElapsedMilliseconds} ms, {(useSecond ? "used it" : "kept the first")}");
        return useSecond ? second : first;
    }

    WhisperProcessor SongProcessor(WhisperFactory factory, string prompt, int sampleCount) =>
        factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(_threads)
            .WithNoContext()
            .WithSingleSegment()
            .WithProbabilities()
            .WithPrompt(prompt)
            .WithAudioContextSize(ClipContext(sampleCount))
            // One try, capped: a slow fallback or a looping recital costs more than it could fix.
            .WithTemperatureInc(0)
            .WithMaxTokensPerSegment(48)
            .Build();

    /// <summary>Loads "base" for the song pass in the background (it is the model voice started with, so it's here).</summary>
    Task PrepareSongPassAsync() => _songInit ??= Task.Run(() =>
    {
        try
        {
            if (!HasExpectedHash(Base))
            {
                Log.Info("Voice: the base model isn't here; play commands go without a second pass");
                return;
            }
            var factory = WhisperFactory.FromPath(Base.LocalPath, new WhisperFactoryOptions { UseGpu = false });
            using (var p = SongProcessor(factory, "Play Kesariya.", MicRecorder.SampleRate * 2)) Run(p, new float[MicRecorder.SampleRate * 2]);
            _songFactory = factory;
        }
        catch (Exception ex)
        {
            Log.Warn("Preparing the song pass failed; play commands go without it", ex);
        }
    });

    IEnumerable<string> History()
    {
        try { return SongHistory?.Invoke().ToList() ?? new List<string>(); }
        catch (Exception ex)
        {
            Log.Warn("Reading the song history failed", ex);
            return Array.Empty<string>();
        }
    }

    IReadOnlyList<string> KnownSongs() => History().Concat(BuiltInSongs).ToList();

    /// <summary>The cleaned transcript and Whisper's average token probability (0..1).</summary>
    static async Task<(string Text, float Probability)> DecodeAsync(WhisperProcessor processor, float[] samples, CancellationToken ct)
    {
        var text = new StringBuilder();
        float sum = 0;
        int count = 0;
        await foreach (var segment in processor.ProcessAsync(samples, ct))
        {
            text.Append(segment.Text);
            sum += segment.Probability;
            count++;
        }
        return (Clean(text.ToString()), count > 0 ? sum / count : 0);
    }

    /// <summary>Encoder frames for a clip on the CPU: 50 per second plus headroom, in steps of 128.</summary>
    static int ClipContext(int sampleCount)
    {
        double seconds = (double)sampleCount / MicRecorder.SampleRate;
        return Math.Clamp(((int)(seconds * 50) + 128 + 127) / 128 * 128, 384, 1500);
    }

    static void Run(WhisperProcessor p, float[] samples)
    {
        foreach (var _ in p.ProcessAsync(samples).ToBlockingEnumerable()) { }
    }

    /// <summary>
    /// Primes Whisper with what Shammy listens for, so names and commands come out spelled right. It reads like
    /// earlier speech, never like instructions: instruction-like prompts get recited back as the transcript.
    /// Kept short, since the prompt uses up the model's context.
    /// </summary>
    string Prompt()
    {
        // No numbers in the examples: they'd nudge "volume 40" towards whatever number is written here.
        var sb = new StringBuilder("Play the next song. Open Spotify. Search files. Set the brightness. Turn on Bluetooth. " +
            "Turn up the volume. Mute the mic. Start a timer. Remind me tomorrow. Translate good morning to Hindi.");
        try
        {
            if (Vocabulary?.Invoke() is { } extra)
            {
                var names = extra.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count > 0)
                {
                    sb.Append(" Open ");
                    int start = sb.Length;
                    foreach (var name in names)
                    {
                        if (sb.Length - start + name.Length > 320) break;
                        if (sb.Length > start) sb.Append(", ");
                        sb.Append(name);
                    }
                    sb.Append('.');
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Building the voice vocabulary failed", ex);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when the "transcript" is mostly the prompt recited back (Whisper does this on unclear audio):
    /// six or more consecutive prompt words in a row (a real command repeats at most a few).
    /// </summary>
    internal static bool EchoesPrompt(string text, string prompt)
    {
        static string[] Words(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var t = Words(text);
        const int Run = 6;
        if (t.Length < Run) return false;
        var p = " " + string.Join(' ', Words(prompt)) + " ";
        for (int i = 0; i + Run <= t.Length; i++)
            if (p.Contains(" " + string.Join(' ', t, i, Run) + " ", StringComparison.Ordinal)) return true;
        return false;
    }

    static string Clean(string text) =>
        Shammy.Core.Search.SpeechFixes.Apply(
            Shammy.Core.Search.TranscriptCleaner.RemoveRepeats(Regex.Replace(Noise.Replace(text, " "), @"\s+", " ").Trim()));

    /// <summary>Drop cached processors so the next query uses current settings (e.g. language).</summary>
    public void ResetProcessors()
    {
        _gate.Wait();
        try
        {
            // Rebuild now (with the current language and vocabulary) so the next query isn't the slow one.
            _engine?.Reset();
            _engine?.Warm();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _engine?.Dispose();
            _engine = null;
            _songFactory?.Dispose();
            _songFactory = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One loaded model and its processors (cached per window size on the CPU, a single one on a GPU).</summary>
    sealed class Engine : IDisposable
    {
        readonly WhisperService _owner;
        readonly VoiceModel _model;
        readonly WhisperFactory _factory;
        readonly int _gpu;
        readonly Dictionary<int, WhisperProcessor> _byContext = new();
        readonly Dictionary<int, WhisperProcessor> _unprimed = new();
        readonly Dictionary<int, WhisperProcessor> _hindi = new();
        string _prompt = "";

        public string CurrentPrompt => _prompt;

        public Engine(WhisperService owner, VoiceModel model, WhisperFactory factory, int gpu)
        {
            _owner = owner;
            _model = model;
            _factory = factory;
            _gpu = gpu;
        }

        public string Describe() => _gpu >= 0 ? $"{_model.Name} (GPU {_gpu})" : $"{_model.Name} (CPU)";

        /// <summary>Builds (and runs once) the processors a typical query will use, so the first one isn't slow.</summary>
        public void Warm()
        {
            _prompt = _owner.Prompt();
            var silence = new float[MicRecorder.SampleRate * 2];
            Run(For(silence.Length), silence);
            // On a GPU the retry's window size is set up now, not on the first Hinglish sentence.
            if (_gpu >= 0 && _owner.Language == "en") Run(Hindi(silence.Length), silence);
        }

        public WhisperProcessor For(int sampleCount) => Get(sampleCount, _byContext, _prompt);

        /// <summary>Same settings without the prompt; made only when a transcript echoed the prompt.</summary>
        public WhisperProcessor Unprimed(int sampleCount) => Get(sampleCount, _unprimed, null);

        /// <summary>
        /// The Hindi retry for doubtful English transcripts. On a GPU it uses a half-size window for clips that
        /// fit one: a single extra window size is set up once, and it roughly halves the cost of the retry.
        /// </summary>
        public WhisperProcessor Hindi(int sampleCount) => Get(sampleCount, _hindi, HinglishPrompt, hindi: true);

        WhisperProcessor Get(int sampleCount, Dictionary<int, WhisperProcessor> cache, string? prompt, bool hindi = false)
        {
            int context = 1500;
            // Whisper normally encodes a full 30 s window (1500 frames) even for a 3 s query. Sizing the
            // window to the clip (50 frames per second, plus headroom) cuts CPU time several-fold.
            if (!_model.FullWindow && _gpu < 0) context = ClipContext(sampleCount);
            else if (hindi && sampleCount <= MicRecorder.SampleRate * 14) context = 768;
            if (cache.TryGetValue(context, out var p)) return p;
            var builder = _factory.CreateBuilder()
                .WithLanguage(hindi ? "hi" : _owner.Language)
                .WithThreads(_owner._threads)
                .WithNoContext()
                .WithSingleSegment()
                .WithProbabilities();
            if (prompt is not null) builder = builder.WithPrompt(prompt);
            if (context < 1500) builder = builder.WithAudioContextSize(context);
            // A retry gets one try: temperature fallbacks made a single Hindi pass take up to 8 s.
            if (hindi) builder = builder.WithTemperatureInc(0).WithMaxTokensPerSegment(96);
            return cache[context] = builder.Build();
        }

        public void Reset()
        {
            foreach (var p in _byContext.Values.Concat(_unprimed.Values).Concat(_hindi.Values)) p.Dispose();
            _byContext.Clear();
            _unprimed.Clear();
            _hindi.Clear();
            _prompt = _owner.Prompt();
        }

        public void Dispose()
        {
            Reset();
            _factory.Dispose();
        }
    }
}
