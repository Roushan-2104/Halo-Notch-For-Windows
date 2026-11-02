using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Core.Clipboard;
using Halo.Infrastructure;
using Halo.Shell;
using WinClipboard = System.Windows.Clipboard;

namespace Halo.Modules.ClipboardHistory;

/// <summary>Saved history: text and file lists only (images stay in memory).</summary>
internal sealed class ClipStore
{
    public List<ClipEntry> Items { get; set; } = new();
}

/// <summary>
/// Clipboard history, a short "copied" preview in the notch, and a screenshot catcher with a card to
/// copy / shelve / open / drag the capture. Everything is event driven: a clipboard listener window,
/// a folder watcher and the snip hotkeys; nothing polls.
/// </summary>
/// <remarks>
/// Namespace is ClipboardHistory, not Clipboard, so that "Clipboard.SetText" in sibling modules keeps
/// resolving to System.Windows.Clipboard instead of this namespace.
/// </remarks>
internal sealed class ClipboardModule : IHaloModule
{
    const int PreviewPriority = 35, ScreenshotPriority = 55;
    const string PreviewId = "clipboard.preview", ScreenshotId = "clipboard.screenshot";
    const int MaxSavedText = 20_000;
    const int MaxHistoryText = 1_000_000;
    // Unpinned history is forgotten after a day so old passwords and tokens don't pile up.
    static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    readonly ClipHistory _history = new();
    readonly CopyCoalescer _coalescer = new(TimeSpan.FromMilliseconds(300));
    ModuleContext _ctx = null!;
    ClipboardPanel _panel = null!;
    ClipboardListener? _listener;
    ScreenshotWatcher? _screens;
    DispatcherTimer? _readDelay, _saveDelay, _expiry;
    CopyPreview? _preview;
    Capture? _capture;
    ScreenshotCard? _card;
    DateTime _ignoreUntil, _simulateUntil;
    int _readGeneration;

    public string Name => "clipboard";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _readDelay = OneShot(TimeSpan.FromMilliseconds(60), () => _ = ReadClipboardAsync());
        _saveDelay = OneShot(TimeSpan.FromSeconds(1), Persist);

        _history.Load(ctx.Load<ClipStore>().Items.Where(e => e is { Kind: not ClipKind.Image, Text: not null, Files: not null }));
        _history.Changed += () => Restart(_saveDelay);
        if (_history.Expire(DateTime.Now - KeepFor) > 0) Persist();
        _expiry = new DispatcherTimer(TimeSpan.FromMinutes(10), DispatcherPriority.Background,
            (_, _) => _history.Expire(DateTime.Now - KeepFor), ctx.Ui);

        _panel = new ClipboardPanel(_history, CopyBack);
        ctx.AddTab("", "Clipboard", _panel);
        ctx.AddCommand(new ClipboardCommand(this));
        ctx.AddPipeCommand("clipboard-copy", PipeCopy);
        ctx.AddPipeCommand("clipboard-screenshot", PipeScreenshot);
        ctx.AddPipeCommand("clipboard-state", _ => State());
        ctx.AddPipeCommand("clipboard-open", _ =>
        {
            OpenTabHeld();
            return "ok";
        });

        try
        {
            _listener = new ClipboardListener();
            _listener.Changed += () => Restart(_readDelay);
            if (!_listener.IsListening) ctx.Warn("AddClipboardFormatListener failed; clipboard history is off");
        }
        catch (Exception ex)
        {
            ctx.Warn("Clipboard listener failed to start", ex);
        }

        try
        {
            _screens = new ScreenshotWatcher(ctx.Ui, ctx.Warn);
            _screens.FileArrived += path => _ = OnScreenshotFileAsync(path, simulated: false);
        }
        catch (Exception ex)
        {
            ctx.Warn("Screenshot watcher failed to start", ex);
        }
    }

    // ---- Reading the clipboard ---------------------------------------------------------------

    async Task ReadClipboardAsync()
    {
        int generation = ++_readGeneration;
        var now = DateTime.Now;
        if (now < _ignoreUntil) return;
        bool simulated = now < _simulateUntil;
        _simulateUntil = default;

        // Password managers mark their copies; checking the format needs no clipboard lock.
        if (ClipNative.HasFormat(ClipPrivacy.ExcludeFormat)) return;
        uint owner = ClipNative.OwnerProcessId();
        if (!simulated && owner == Environment.ProcessId) return;

        IDataObject? data = null;
        for (int attempt = 0; data is null && attempt < 4; attempt++)
        {
            try
            {
                data = WinClipboard.GetDataObject();
            }
            catch (ExternalException)
            {
                // Right after a copy the source app often still holds the clipboard open.
            }
            if (data is not null) break;
            await Task.Delay(40 << attempt);
            if (generation != _readGeneration) return;
        }
        if (data is null)
        {
            _ctx.Warn("Clipboard stayed locked; skipped a copy");
            return;
        }

        try
        {
            if (ClipPrivacy.ShouldSkip(false, Flag(data, ClipPrivacy.HistoryFormat), Flag(data, ClipPrivacy.CloudFormat))) return;

            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                _history.Add(ClipEntry.FromFiles(files, now));
            }
            // Text before image: Office and browsers put a picture of the selection next to the text.
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text
                     && !string.IsNullOrWhiteSpace(text))
            {
                // A copied 100 MB log mustn't sit in memory for a day or be re-split on every panel refresh;
                // the preview only ever needs the start.
                if (text.Length <= MaxHistoryText) _history.Add(ClipEntry.FromText(text, now));
                ShowPreview(ClipText.Preview(text.Length > 4096 ? text[..4096] : text), null);
            }
            else if (ClipImages.FromDataObject(data) is { } image)
            {
                await OnImageAsync(image, ClipNative.ProcessName(owner), now);
            }
        }
        catch (Exception ex)
        {
            _ctx.Warn("Reading the clipboard failed", ex);
        }
    }

    /// <summary>The DWORD behind an opt-out format, or null when it's absent.</summary>
    static uint? Flag(IDataObject data, string format)
    {
        if (!ClipNative.HasFormat(format)) return null;
        try
        {
            return data.GetData(format) switch
            {
                MemoryStream ms => ClipPrivacy.ReadDword(ms.ToArray()),
                byte[] bytes => ClipPrivacy.ReadDword(bytes),
                int i => (uint)i,
                _ => 0,
            };
        }
        catch (Exception)
        {
            return 0;
        }
    }

    async Task OnImageAsync(BitmapSource image, string? ownerProcess, DateTime now)
    {
        bool screenshot = _screens?.Snip.IsScreenshot(now, ownerProcess) ?? SnipDetector.IsSnippingProcess(ownerProcess);
        if (screenshot) OnClipboardCapture(image, now);
        else ShowPreview($"Image · {ClipText.Dimensions(image.PixelWidth, image.PixelHeight)}", image);

        try
        {
            // The clipboard's bitmap belongs to the UI thread; it must be frozen (or copied) before a
            // background thread may read it, or every copied image fails to reach the history.
            var shared = image;
            if (!shared.IsFrozen)
            {
                if (shared.CanFreeze) shared.Freeze();
                else
                {
                    shared = new WriteableBitmap(shared);
                    shared.Freeze();
                }
            }
            var (hash, clip) = await Task.Run(() => ClipImages.Process(shared));
            _history.Add(ClipEntry.FromImage(hash, image.PixelWidth, image.PixelHeight, now, clip));
            if (_capture is { } c && ReferenceEquals(c.Full, image)) c.Png ??= clip.Png;
        }
        catch (Exception ex)
        {
            _ctx.Warn("Processing a copied image failed", ex);
        }
    }

    void ShowPreview(string text, ImageSource? thumb)
    {
        var now = DateTime.Now;
        bool fresh = _coalescer.ShouldAnnounce(now) || _preview is null || _ctx.Get(PreviewId) is null;
        if (fresh) _preview = new CopyPreview(text, thumb);
        else _preview!.Set(text, thumb);
        _ctx.Post(new Activity(PreviewId, ActivityKind.Custom, PreviewPriority, _preview, now.AddSeconds(2))
        {
            Click = () => _ctx.OpenTab(_panel),
        });
    }

    // ---- Writing the clipboard ---------------------------------------------------------------

    void CopyBack(ClipEntry entry)
    {
        var data = new DataObject();
        switch (entry.Kind)
        {
            case ClipKind.Text:
                data.SetText(entry.Text);
                break;
            case ClipKind.Files:
                data.SetData(DataFormats.FileDrop, entry.Files);
                break;
            case ClipKind.Image when entry.Tag is ClipImage img:
                SetImage(data, ClipImages.Decode(img.Png), img.Png);
                break;
            default:
                return;
        }
        if (!SetClipboard(data, echo: false)) return;
        _history.Touch(entry, DateTime.Now);
        _panel.Flash("Copied");
        _ctx.ShowMessage("", "Copied", seconds: 1.5);
    }

    static void SetImage(DataObject data, BitmapSource image, byte[]? png)
    {
        data.SetImage(image);
        // Apps that understand PNG keep transparency; the bitmap is for everything else.
        if (png is not null) data.SetData("PNG", new MemoryStream(png));
    }

    /// <summary>Sets the clipboard; unless <paramref name="echo"/>, the update this causes is ignored.</summary>
    bool SetClipboard(DataObject data, bool echo)
    {
        if (!echo) _ignoreUntil = DateTime.Now.AddMilliseconds(600);
        try
        {
            WinClipboard.SetDataObject(data, true);
            return true;
        }
        catch (ExternalException ex)
        {
            _ignoreUntil = default;
            _ctx.Warn("Setting the clipboard failed", ex);
            _ctx.ShowMessage("", "Clipboard is busy", error: true);
            return false;
        }
    }

    // ---- Screenshots -------------------------------------------------------------------------

    void OnClipboardCapture(BitmapSource image, DateTime now)
    {
        // The Snipping Tool also auto-saves; if its file already arrived, this is the same capture.
        if (_capture is { FromClipboard: false } c && SnipDetector.IsSameCapture(c.At, now)) return;
        ShowCapture(new Capture(image, image.PixelWidth, image.PixelHeight, now) { Full = image });
    }

    async Task OnScreenshotFileAsync(string path, bool simulated)
    {
        var now = DateTime.Now;
        if (!simulated && _capture is { FromClipboard: true, Path: null } snip && SnipDetector.IsSameCapture(snip.At, now))
        {
            // The snip we already showed was saved by the Snipping Tool: use its file instead of writing our own.
            snip.Path = path;
            snip.Owned = false;
            _card?.Refresh();
            return;
        }

        (BitmapSource Preview, int Width, int Height, long Bytes)? loaded = null;
        for (int attempt = 0; loaded is null && attempt < 8; attempt++)
        {
            try
            {
                loaded = await Task.Run(() => ClipImages.LoadFile(path, 900));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                           or ArgumentException or ExternalException or InvalidOperationException)
            {
                // Still being written (or locked by the capture tool); try again shortly.
                if (!File.Exists(path)) return;
                await Task.Delay(150 * (attempt + 1));
            }
        }
        if (loaded is not { } l)
        {
            _ctx.Warn($"Couldn't read new screenshot {path}");
            return;
        }
        ShowCapture(new Capture(l.Preview, l.Width, l.Height, now) { Path = path, Bytes = l.Bytes });
    }

    void ShowCapture(Capture capture)
    {
        _capture = capture;
        _ctx.Remove(PreviewId);
        var size = ClipText.Dimensions(capture.Width, capture.Height);
        _ctx.Post(new Activity(ScreenshotId, ActivityKind.Custom, ScreenshotPriority, new ScreenshotPreview(capture.Preview, size),
            DateTime.Now.AddSeconds(6))
        {
            Click = () => OpenCard(capture),
        });
    }

    void OpenCard(Capture capture)
    {
        _ctx.Remove(ScreenshotId);
        _card = new ScreenshotCard(this, capture);
        _ctx.ShowCard(_card, ScreenshotCard.CardWidth, ScreenshotCard.CardHeight, interactive: true);
    }

    internal void CloseCard()
    {
        _card = null;
        _ctx.CloseCard();
    }

    /// <summary>The capture as a file, writing a PNG under %LOCALAPPDATA%\Halo\screenshots for clipboard-only snips.</summary>
    internal string? EnsureFile(Capture c)
    {
        if (c.HasFile) return c.Path;
        // The screenshot's own file was moved or deleted and only the small preview is left: say so rather
        // than quietly handing out a low-resolution copy as if it were the screenshot.
        if (c.Full is null && c.Path is not null)
        {
            _ctx.ShowMessage("", "That screenshot's file is gone", error: true);
            return null;
        }
        try
        {
            var dir = Path.Combine(Paths.Root, "screenshots");
            Directory.CreateDirectory(dir);
            var stem = $"Screenshot {c.At:yyyy-MM-dd HHmmss}";
            var path = Path.Combine(dir, stem + ".png");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{stem} ({n}).png");
            c.Png ??= ClipImages.EncodePng(c.Full ?? c.Preview);
            File.WriteAllBytes(path, c.Png);
            c.Path = path;
            c.Owned = true;
            c.Bytes = c.Png.Length;
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Warn("Saving the screenshot failed", ex);
            return null;
        }
    }

    internal bool CopyCapture(Capture c)
    {
        try
        {
            BitmapSource image;
            byte[]? png = c.Png;
            if (c.Full is not null)
            {
                image = c.Full;
            }
            else if (c.HasFile && c.Path is { } file)
            {
                var bytes = File.ReadAllBytes(file);
                image = ClipImages.Decode(bytes);
                if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)) png = bytes;
            }
            else
            {
                return false;
            }
            var data = new DataObject();
            SetImage(data, image, png);
            return SetClipboard(data, echo: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            _ctx.Warn("Copying the screenshot failed", ex);
            return false;
        }
    }

    internal bool AddCaptureToShelf(Capture c)
    {
        if (EnsureFile(c) is not { } path) return false;
        _ctx.Services.Shelf.Add(new[] { path });
        _card?.Refresh();
        return true;
    }

    internal bool OpenCapture(Capture c)
    {
        if (EnsureFile(c) is not { } path) return false;
        CloseCard();
        _ctx.Open(path);
        return true;
    }

    internal bool ShowCaptureInFolder(Capture c)
    {
        if (!c.HasFile) return false;
        CloseCard();
        _ctx.Open(Halo.Interop.Native.ExplorerPath, $"/select,\"{c.Path}\"");
        return true;
    }

    /// <summary>Only for files Halo wrote itself; the user's Screenshots folder is never touched.</summary>
    internal bool DeleteCapture(Capture c)
    {
        if (!c.Owned || c.Path is not { } path) return false;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Warn("Deleting the screenshot failed", ex);
            return false;
        }
        var shelved = _ctx.Services.Shelf.Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (shelved is not null) _ctx.Services.Shelf.Remove(shelved);
        c.Path = null;
        c.Owned = false;
        CloseCard();
        _ctx.ShowMessage("", "Screenshot deleted", seconds: 2);
        return true;
    }

    // ---- Commands ------------------------------------------------------------------------------

    internal CommandResult Run(ClipIntent intent)
    {
        if (intent == ClipIntent.Clear)
        {
            int removed = _history.Clear();
            int pinned = _history.PinnedCount;
            return new CommandResult("", "Clipboard history cleared",
                pinned > 0 ? $"{removed} removed · {pinned} pinned kept" : $"{removed} removed");
        }
        OpenTabHeld();
        return new CommandResult("", "Clipboard", CountText());
    }

    /// <summary>OpenTab waits for a command's confirmation card and holds the tab until it's been looked at.</summary>
    void OpenTabHeld() => _ctx.OpenTab(_panel);

    string CountText()
    {
        int n = _history.Items.Count, pinned = _history.PinnedCount;
        return (n == 1 ? "1 item" : $"{n} items") + (pinned > 0 ? $" · {pinned} pinned" : "");
    }

    // ---- Test pipe -----------------------------------------------------------------------------

    /// <summary>Puts text on the clipboard as if another app copied it, so it goes through the normal listener path.</summary>
    string PipeCopy(string text)
    {
        if (text.Length == 0) return "error: usage clipboard-copy <text>";
        if (_listener is not { IsListening: true }) return "error: clipboard listener isn't running";
        _simulateUntil = DateTime.Now.AddSeconds(2);
        var data = new DataObject();
        data.SetText(text);
        return SetClipboard(data, echo: true) ? "ok" : "error: clipboard busy";
    }

    string PipeScreenshot(string arg)
    {
        var path = arg.Trim().Trim('"');
        if (path.Length == 0 || !File.Exists(path)) return "error: file not found";
        if (!ScreenshotWatcher.IsImage(path)) return "error: not a .png/.jpg";
        _ = OnScreenshotFileAsync(Path.GetFullPath(path), simulated: true);
        return "ok";
    }

    string State()
    {
        var last = _history.Items.MaxBy(e => e.Time);
        string lastText = last is null ? "none" : last.Kind switch
        {
            ClipKind.Text => $"text \"{ClipText.Preview(last.Text, 40)}\"",
            ClipKind.Image => $"image {ClipText.Dimensions(last.Width, last.Height)}",
            _ => $"files \"{ClipText.FilesTitle(last.Files)}\"",
        };
        int images = _history.Items.Count(e => e.Kind == ClipKind.Image);
        string screenshots = _screens is null ? "off" : _screens.IsWatching ? _screens.Folder : $"{_screens.Folder} (not watched)";
        string capture = _capture is null ? "none" : $"{ClipText.Dimensions(_capture.Width, _capture.Height)} {(_capture.Path ?? "unsaved")}";
        return $"count={_history.Items.Count} pinned={_history.PinnedCount} images={images} last={lastText} " +
               $"listener={(_listener?.IsListening == true ? "on" : "off")} screenshots={screenshots} capture={capture}";
    }

    // ---- Plumbing --------------------------------------------------------------------------------

    void Persist() => _ctx.Save(new ClipStore
    {
        Items = _history.Items.Where(e => e.Kind != ClipKind.Image && e.Text.Length <= MaxSavedText).ToList(),
    });

    DispatcherTimer OneShot(TimeSpan delay, Action run)
    {
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(delay, DispatcherPriority.Background, (_, _) =>
        {
            timer!.Stop();
            run();
        }, _ctx.Ui) { IsEnabled = false };
        return timer;
    }

    static void Restart(DispatcherTimer? timer)
    {
        if (timer is null) return;
        timer.Stop();
        timer.Start();
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _screens?.Dispose();
        _readDelay?.Stop();
        _expiry?.Stop();
        if (_saveDelay?.IsEnabled == true)
        {
            _saveDelay.Stop();
            Persist();
        }
    }

    sealed class ClipboardCommand(ClipboardModule module) : ITextCommand
    {
        public bool CanHandle(string text) => ClipText.Parse(text) != ClipIntent.None;

        public Task<CommandResult> RunAsync(string text) => Task.FromResult(module.Run(ClipText.Parse(text)));
    }
}
