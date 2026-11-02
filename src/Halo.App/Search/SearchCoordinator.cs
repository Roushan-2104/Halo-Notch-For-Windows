using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Halo.Core.Search;
using Halo.Infrastructure;
using Halo.Interop;
using Halo.Shell;

namespace Halo.Search;

/// <summary>
/// The whole "hold Right Alt" flow:
/// <list type="bullet">
/// <item>tap (under 180 ms): a type-to-search box drops from the notch;</item>
/// <item>hold: the screen freezes, the mic listens, and you can circle anything; release to search;</item>
/// <item>hold and release without speaking or circling: the frozen screen stays so you can circle at leisure.</item>
/// </list>
/// Results come from Google AI Mode, the plain Web tab, or Lens for images, rendered in the notch.
/// </summary>
internal sealed class SearchCoordinator
{
    enum Phase { Idle, Pending, Holding, Circling, Processing, Typing, Showing }

    static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(180);

    readonly NotchWindow _notch;
    readonly GlobalKeys _keys;
    readonly WhisperService _whisper;
    readonly AppSettings _settings;
    readonly Modules.ModuleRegistry _modules;
    readonly MicRecorder _mic = new();
    readonly DispatcherTimer _holdTimer;

    Phase _phase;
    int _session;
    bool _micOk;
    string? _micError;
    ScreenShot? _shot;
    SearchOverlay? _overlay;
    Task<IReadOnlyList<OcrWord>>? _ocr;
    Context? _ctx;

    sealed record Context(string Query, string DisplayQuery, SearchMode Mode, SearchFilter Filters, byte[]? Png, BitmapSource? Thumb)
    {
        public Modules.SelectionInfo? Selection { get; init; }
    }

    public SearchCoordinator(NotchWindow notch, GlobalKeys keys, WhisperService whisper, AppSettings settings, Modules.ModuleRegistry modules)
    {
        _notch = notch;
        _keys = keys;
        _whisper = whisper;
        _settings = settings;
        _modules = modules;
        _holdTimer = new DispatcherTimer(HoldThreshold, DispatcherPriority.Input, (_, _) => OnHoldElapsed(), notch.Dispatcher) { IsEnabled = false };

        keys.SearchPressed += OnKeyDown;
        keys.SearchReleased += OnKeyUp;
        keys.EscapePressed += Cancel;
        notch.InteractiveDeactivated += () =>
        {
            if (_phase is Phase.Showing or Phase.Typing) Cancel();
        };
        notch.SearchBarView.Submitted += text => _ = ProcessTextAsync(text);
        notch.ResultView.CloseRequested += Cancel;
        notch.ResultView.ModeChanged += mode =>
        {
            if (_ctx is null) return;
            _ctx = _ctx with { Mode = mode };
            _ = NavigateAsync(_ctx);
        };
        notch.ResultView.FiltersChanged += filters =>
        {
            if (_ctx is null) return;
            _ctx = _ctx with { Filters = filters, Mode = _ctx.Mode == SearchMode.Lens ? _settings.DefaultSearchMode : _ctx.Mode };
            _ = NavigateAsync(_ctx);
        };
        notch.AnswerView.SearchWebRequested += query => _ = ShowResultAsync(new Context(query, query, _settings.DefaultSearchMode, SearchFilter.None, null, null));
    }

    public bool IsActive => _phase != Phase.Idle;

    // ---- Hotkey ------------------------------------------------------------------------------

    public void OnKeyDown()
    {
        if (_phase == Phase.Processing) return;
        // Start collapsing an open card right away so it's out of the way before the screen freezes.
        if (_phase != Phase.Idle) Reset(keepNotch: false);

        _session++;
        _phase = Phase.Pending;
        _keys.InterceptEscape = true;
        _micOk = _mic.Start(out _micError);
        _holdTimer.Start();
    }

    void OnHoldElapsed()
    {
        _holdTimer.Stop();
        if (_phase != Phase.Pending) return;
        _phase = Phase.Holding;

        _notch.SetSearchMode(NotchMode.Listening);
        if (_micOk)
        {
            var hint = _whisper.IsReady ? "Speak, circle anything, then let go"
                : _whisper.IsDownloading ? $"Voice model downloading ({_whisper.DownloadProgress:P0}) · circle instead"
                : "Voice unavailable · circle anything instead";
            _notch.SearchBarView.ShowListening(() => _mic.Level, "Listening…", hint);
        }
        else
        {
            _notch.SearchBarView.ShowCircling();
            _notch.SearchBarView.ShowMessage("", "Circle anything", $"{_micError} · voice is off");
        }
        BeginFreeze();
    }

    public void OnKeyUp()
    {
        if (_phase == Phase.Pending)
        {
            _holdTimer.Stop();
            _mic.Stop();
            ShowTyping();
            return;
        }
        if (_phase != Phase.Holding) return;

        var samples = _mic.Stop();
        bool silentBlock = _micOk && MicRecorder.IsSilentBlock(samples);
        bool speech = _micOk && !silentBlock && MicRecorder.HasSpeech(samples);
        bool circled = _overlay?.HasSelection == true;

        if (!speech && !circled)
        {
            if (_overlay is null)
            {
                // Nothing to circle on (capture failed) and nothing said: fall back to typing.
                Reset(keepNotch: true);
                ShowTyping();
                return;
            }
            _phase = Phase.Circling;
            _notch.SetSearchMode(NotchMode.Circling);
            _notch.SearchBarView.ShowCircling();
            if (silentBlock)
                _notch.SearchBarView.ShowMessage("", "Microphone is blocked", "Allow desktop apps in Settings › Privacy › Microphone");
            _overlay.SetHint("Circle anything to search · Esc to cancel");
            return;
        }

        _ = ProcessAsync(speech ? samples : null, circled ? _overlay!.SelectionPixels : null);
    }

    void BeginFreeze()
    {
        try
        {
            _notch.ExcludeFromCapture(true);
            _shot = ScreenCapture.CaptureMonitorAtCursor();
        }
        catch (Exception ex)
        {
            Log.Error("Screen capture failed", ex);
            _shot = null;
            return;
        }
        finally
        {
            _notch.ExcludeFromCapture(false);
        }
        _ocr = OcrService.RecognizeAsync(_shot);
        _overlay = new SearchOverlay(_shot);
        _overlay.SetHint("Keep holding Right Alt · speak or circle · let go to search");
        _overlay.SelectionMade += OnSelectionMade;
        _overlay.Cancelled += Cancel;
        _overlay.FullScreenshotRequested += () => SaveFromFreeze(null);
        _overlay.SnipMade += region => SaveFromFreeze(region);
        var shownAt = Environment.TickCount64;
        _overlay.AppearedOnScreen += () =>
        {
            _notch.BringToFront();
            Log.Info($"Freeze overlay on screen after {Environment.TickCount64 - shownAt} ms");
        };
        _overlay.Show();
    }

    /// <summary>The overlay's Screenshot / Snip tools: save what's frozen and end the search.</summary>
    void SaveFromFreeze(Box? region)
    {
        var shot = _shot;
        if (shot is null) return;
        var image = region is { } r ? shot.Crop(r) : shot.Image;
        Reset(keepNotch: false);
        ScreenshotSaver.Save(image, region is null ? "screenshot" : "snip");
    }

    void OnSelectionMade(IReadOnlyList<Pt> stroke)
    {
        // While the key is held the selection waits for release (so speech can join it).
        if (_phase == Phase.Circling) _ = ProcessAsync(null, stroke);
    }

    // ---- Processing ----------------------------------------------------------------------------

    async Task ProcessAsync(float[]? speechSamples, IReadOnlyList<Pt>? stroke)
    {
        int session = _session;
        _phase = Phase.Processing;
        _notch.SetSearchMode(NotchMode.Thinking);

        string spoken = "";
        if (speechSamples is not null)
        {
            if (_whisper.IsReady)
            {
                _notch.SearchBarView.ShowThinking("Transcribing…");
                try { spoken = await _whisper.TranscribeAsync(speechSamples); }
                catch (Exception ex) { Log.Error("Transcription failed", ex); }
                if (session != _session) return;
                Log.Info($"Heard: \"{spoken}\"");
            }
            else if (stroke is null)
            {
                await ShowMessageThenCancel(session, "",
                    _whisper.IsDownloading ? "Voice model is still downloading" : "Voice isn't available",
                    _whisper.IsDownloading ? $"{_whisper.DownloadProgress:P0} done · tap Right Alt to type instead" : _whisper.Failure ?? "Tap Right Alt to type instead");
                return;
            }
        }

        var (query, filters) = QueryBuilder.ExtractSpokenFilters(spoken);

        Selection? selection = null;
        if (stroke is { Count: > 0 } && _shot is not null)
        {
            _notch.SearchBarView.ShowThinking(query.Length > 0 ? $"“{query}”" : "Reading the selection…");
            var words = _ocr is null ? Array.Empty<OcrWord>() : await _ocr;
            if (session != _session) return;
            selection = SelectionAnalyzer.Analyze(words, stroke);
            selection = selection with { Region = selection.Region.Clamp(_shot.Width, _shot.Height) };
            var b = SelectionAnalyzer.Bounds(stroke);
            Log.Info($"Selection: {stroke.Count} pts, bounds {b.X:0},{b.Y:0} {b.W:0}x{b.H:0}, {words.Count} OCR words on screen -> " +
                     $"{selection.Kind} \"{(selection.Text.Length > 80 ? selection.Text[..80] + "…" : selection.Text)}\"");
            if (selection.Region.W < 4 || selection.Region.H < 4) selection = null;
            else _overlay?.ShowSelection(selection.Region);
        }

        // Modules get first look at a circle (a QR code opens its link, an amount converts...).
        Modules.SelectionInfo? info = null;
        if (selection is not null)
        {
            info = new Modules.SelectionInfo(selection.Text, _shot!.EncodePng(selection.Region), _shot.Crop(selection.Region),
                selection.Kind == SelectionKind.Image);
            foreach (var handler in _modules.SelectionHandlers)
            {
                Modules.CommandResult? taken = null;
                try { taken = await handler.TryTakeOverAsync(info); }
                catch (Exception ex) { Log.Warn($"Selection handler {handler.GetType().Name} failed", ex); }
                if (session != _session) return;
                if (taken is null) continue;
                ShowCommandResult(taken);
                return;
            }
        }

        // Spoken commands ("play …", "timer 10 minutes") run instead of a web search.
        if (selection is null && spoken.Length > 0 && await TryRunCommandAsync(spoken)) return;

        if (selection is null && query.Length == 0)
        {
            await ShowMessageThenCancel(session, "", "Didn't catch that", "Try again, or tap Right Alt to type");
            return;
        }

        if (selection is null && Calculator.TryEvaluate(query, out var value, out _))
        {
            ShowAnswer(query, Calculator.DisplayForm(query), Calculator.Format(value));
            return;
        }

        Context ctx;
        if (selection is { Kind: SelectionKind.Image })
        {
            var png = _shot!.EncodePng(selection.Region);
            ctx = new Context(query, query.Length > 0 ? query : "Selected area", SearchMode.Lens, filters, png, _shot.Crop(selection.Region));
        }
        else if (selection is not null)
        {
            var combined = QueryBuilder.Combine(query, selection.Text);
            ctx = new Context(combined, combined, _settings.DefaultSearchMode, filters, null, _shot!.Crop(selection.Region));
        }
        else
        {
            ctx = new Context(query, query, _settings.DefaultSearchMode, filters, null, null);
        }

        // An image selection can also be searched as text if OCR found words in it.
        if (ctx.Mode == SearchMode.Lens && selection is not null && selection.Text.Length > 0)
            ctx = ctx with { Query = QueryBuilder.Combine(query, selection.Text) };

        await ShowResultAsync(ctx with { Selection = info });
    }

    /// <summary>Runs the first module command that recognises the text. Returns false if none did.</summary>
    async Task<bool> TryRunCommandAsync(string text)
    {
        int session = _session;
        var clean = text.Trim().TrimEnd('.', '!', '?').Trim();
        var command = _modules.Commands.FirstOrDefault(c =>
        {
            try { return c.CanHandle(clean); }
            catch (Exception ex)
            {
                Log.Warn($"Command {c.GetType().Name} threw in CanHandle", ex);
                return false;
            }
        });
        if (command is null) return false;

        _phase = Phase.Processing;
        _notch.SetSearchMode(NotchMode.Thinking);
        _notch.SearchBarView.ShowThinking($"“{clean}”");
        Modules.CommandResult result;
        try
        {
            result = await command.RunAsync(clean);
        }
        catch (Exception ex)
        {
            Log.Error($"Command {command.GetType().Name} failed", ex);
            result = new Modules.CommandResult("", "That didn't work", ex.Message, IsError: true);
        }
        if (session == _session) ShowCommandResult(result);
        return true;
    }

    /// <summary>A short confirmation card that closes by itself.</summary>
    void ShowCommandResult(Modules.CommandResult result)
    {
        DismissOverlay();
        _phase = Phase.Showing;
        _notch.AnswerView.ShowCommand(result);
        _notch.SetInteractive(false);
        _notch.SetSearchMode(NotchMode.Answer);
        Log.Info($"Command result: {result.Title}");
        int session = _session;
        _ = Task.Delay(result.IsError ? 4000 : 2600).ContinueWith(_ =>
        {
            if (session == _session) Cancel();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    async Task ProcessTextAsync(string text)
    {
        if (await TryRunCommandAsync(text)) return;
        if (Calculator.TryEvaluate(text, out var value, out _))
        {
            ShowAnswer(text, Calculator.DisplayForm(text), Calculator.Format(value));
            return;
        }
        var (query, filters) = QueryBuilder.ExtractSpokenFilters(text);
        if (query.Length == 0) query = text;
        await ShowResultAsync(new Context(query, query, _settings.DefaultSearchMode, filters, null, null));
    }

    void ShowAnswer(string query, string expression, string result)
    {
        DismissOverlay();
        _phase = Phase.Showing;
        _notch.AnswerView.Show(query, expression, result);
        _notch.SetSearchMode(NotchMode.Answer);
        _notch.SetInteractive(true);
    }

    async Task ShowResultAsync(Context ctx)
    {
        int session = _session;
        _ctx = ctx;
        _phase = Phase.Showing;
        DismissOverlay();
        _keys.InterceptEscape = true;
        _notch.ResultView.SetContext(ctx.DisplayQuery, ctx.Thumb, ctx.Png is not null, ctx.Mode, ctx.Filters);
        var selection = ctx.Selection;
        _notch.ResultView.SetExtraChips(_modules.ResultChips
            .Where(c =>
            {
                try { return c.IsVisible(selection); }
                catch (Exception) { return false; }
            })
            .Select(c => (c.Label, (Action)(() => c.OnClick(selection, _notch.ResultView))))
            .ToList());
        _notch.SetSearchMode(NotchMode.Result);
        _notch.SetInteractive(true);
        Log.Info($"Search [{ctx.Mode}] {ctx.Query}");
        try
        {
            await NavigateAsync(ctx);
        }
        catch (Exception ex)
        {
            if (session == _session) Log.Error("Showing results failed", ex);
        }
    }

    Task NavigateAsync(Context ctx)
    {
        if (ctx.Mode == SearchMode.Lens && ctx.Png is not null) return _notch.ResultView.NavigateLensAsync(ctx.Png);
        var mode = ctx.Mode == SearchMode.Lens ? SearchMode.Ai : ctx.Mode;
        var q = ctx.Query.Length > 0 ? ctx.Query : ctx.DisplayQuery;
        var url = QueryBuilder.BuildUrl(QueryBuilder.Apply(q, ctx.Filters, mode, DateTime.Today), mode);
        return _notch.ResultView.NavigateAsync(url);
    }

    void ShowTyping()
    {
        _phase = Phase.Typing;
        _keys.InterceptEscape = true;
        _notch.SearchBarView.ShowTyping();
        _notch.SetSearchMode(NotchMode.Typing);
        _notch.SetInteractive(true);
        _notch.Dispatcher.BeginInvoke(_notch.SearchBarView.FocusInput, DispatcherPriority.Input);
    }

    async Task ShowMessageThenCancel(int session, string glyph, string primary, string secondary)
    {
        _notch.SetSearchMode(NotchMode.Thinking);
        _notch.SearchBarView.ShowMessage(glyph, primary, secondary);
        DismissOverlay();
        await Task.Delay(2200);
        if (session == _session) Cancel();
    }

    // ---- Teardown ------------------------------------------------------------------------------

    public void Cancel()
    {
        if (_phase == Phase.Idle) return;
        Reset(keepNotch: false);
    }

    void Reset(bool keepNotch)
    {
        _session++;
        _holdTimer.Stop();
        if (_mic.IsRecording) _mic.Stop();
        DismissOverlay();
        if (_phase == Phase.Showing) _notch.ResultView.Release();
        _shot = null;
        _ocr = null;
        _ctx = null;
        _phase = Phase.Idle;
        _keys.InterceptEscape = false;
        _notch.SetInteractive(false);
        if (!keepNotch) _notch.SetSearchMode(null);
    }

    void DismissOverlay()
    {
        var o = _overlay;
        _overlay = null;
        o?.Dismiss();
    }

    /// <summary>Entry points for the command channel and tests.</summary>
    public Task SearchTextAsync(string text)
    {
        Reset(keepNotch: true);
        _session++;
        return ProcessTextAsync(text);
    }

    public Task SearchImageAsync(byte[] png)
    {
        Reset(keepNotch: true);
        _session++;
        var thumb = new BitmapImage();
        thumb.BeginInit();
        thumb.StreamSource = new MemoryStream(png);
        thumb.CacheOption = BitmapCacheOption.OnLoad;
        thumb.EndInit();
        thumb.Freeze();
        return ShowResultAsync(new Context("", "Image", SearchMode.Lens, SearchFilter.None, png, thumb));
    }

    public void OpenTyping()
    {
        Reset(keepNotch: true);
        ShowTyping();
    }
}
