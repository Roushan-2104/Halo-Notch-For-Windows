using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Shammy.Modules.Mirror;

/// <summary>Why the camera isn't showing.</summary>
internal enum MirrorProblem { None, NoCamera, Denied, InUse, Failed }

/// <summary>
/// The webcam as a stream of small BGRA frames for the mirror: MediaCapture + MediaFrameReader, scaled by the
/// reader to about 640 px wide, at most 30 frames a second, copied into one WriteableBitmap on the UI thread.
/// Nothing is recorded or kept; frames that arrive while the UI is still drawing the last one are dropped.
/// </summary>
internal sealed class MirrorCamera
{
    const int TargetWidth = 640;
    static readonly TimeSpan MinFrameGap = TimeSpan.FromMilliseconds(1000.0 / 30 - 2);

    readonly Dispatcher _ui;
    readonly SemaphoreSlim _gate = new(1, 1);
    MediaCapture? _capture;
    MediaFrameReader? _reader;
    byte[]? _pixels;
    readonly byte[] _lut = new byte[256];
    double _lutFactor = -1;
    int _pending, _width, _height;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    TimeSpan _lastFrame = TimeSpan.MinValue;
    int _generation;

    public MirrorCamera(Dispatcher ui) => _ui = ui;

    /// <summary>The bitmap frames land in; replaced (and announced) when the frame size changes.</summary>
    public WriteableBitmap? Bitmap { get; private set; }

    public event Action<WriteableBitmap>? BitmapChanged;

    public bool Running => _reader is not null;
    public long Frames { get; private set; }
    public string Size => _width > 0 ? $"{_width}x{_height}" : "-";

    /// <summary>Brightness multiplier (0.6–1.4); 1 leaves frames untouched. Read on the capture thread.</summary>
    public double Brightness { get; set; } = 1;

    /// <summary>Starts the camera; returns what went wrong, or <see cref="MirrorProblem.None"/>.</summary>
    public async Task<MirrorProblem> StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_reader is not null) return MirrorProblem.None;
            int generation = ++_generation;

            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var pick = groups
                .SelectMany(g => g.SourceInfos.Select(s => (Group: g, Info: s)))
                .Where(x => x.Info.SourceKind == MediaFrameSourceKind.Color
                            && x.Info.MediaStreamType is Windows.Media.Capture.MediaStreamType.VideoPreview or Windows.Media.Capture.MediaStreamType.VideoRecord)
                // The front camera first; then preview streams, which are meant for showing.
                .OrderBy(x => x.Info.DeviceInformation?.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Front ? 0 : 1)
                .ThenBy(x => x.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoPreview ? 0 : 1)
                .Select(x => ((MediaFrameSourceGroup Group, MediaFrameSourceInfo Info)?)x)
                .FirstOrDefault();
            if (pick is not { } chosen) return MirrorProblem.NoCamera;

            var capture = new MediaCapture();
            bool exclusive = true;
            var problem = await TryInit(capture, chosen.Group, MediaCaptureSharingMode.ExclusiveControl);
            if (problem == MirrorProblem.InUse)
            {
                // Another app (a meeting) has it; Windows can still share frames read-only.
                capture.Dispose();
                capture = new MediaCapture();
                exclusive = false;
                problem = await TryInit(capture, chosen.Group, MediaCaptureSharingMode.SharedReadOnly);
            }
            if (problem != MirrorProblem.None)
            {
                capture.Dispose();
                return problem;
            }

            if (!capture.FrameSources.TryGetValue(chosen.Info.Id, out var source))
            {
                capture.Dispose();
                return MirrorProblem.Failed;
            }
            if (exclusive) await PickSmallFormat(source);

            var fmt = source.CurrentFormat?.VideoFormat;
            uint w = TargetWidth, h = 360;
            if (fmt is { Width: > 0, Height: > 0 })
            {
                w = Math.Min(TargetWidth, fmt.Width);
                h = (uint)Math.Max(2, Math.Round(w * (double)fmt.Height / fmt.Width) / 2 * 2);
            }
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, new BitmapSize { Width = w, Height = h });
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrameArrived;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success || generation != _generation)
            {
                reader.FrameArrived -= OnFrameArrived;
                reader.Dispose();
                capture.Dispose();
                return status == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable ? MirrorProblem.InUse
                    : status == MediaFrameReaderStartStatus.DeviceNotAvailable ? MirrorProblem.NoCamera
                    : generation != _generation ? MirrorProblem.None
                    : MirrorProblem.Failed;
            }
            _capture = capture;
            _reader = reader;
            return MirrorProblem.None;
        }
        catch (UnauthorizedAccessException)
        {
            return MirrorProblem.Denied;
        }
        catch (Exception ex)
        {
            Shammy.Infrastructure.Log.Warn("Mirror: starting the camera failed", ex);
            return MirrorProblem.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    static async Task<MirrorProblem> TryInit(MediaCapture capture, MediaFrameSourceGroup group, MediaCaptureSharingMode mode)
    {
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = mode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            return MirrorProblem.None;
        }
        catch (UnauthorizedAccessException)
        {
            // "Let desktop apps access your camera" is off (Settings › Privacy & security › Camera).
            return MirrorProblem.Denied;
        }
        catch (Exception ex) when (unchecked((uint)ex.HResult) is 0xC00D3704 or 0xC00DABE4 or 0x80070020 or 0xC00D36B4 or 0x800700AA)
        {
            // Device busy / locked by another app.
            return MirrorProblem.InUse;
        }
        catch (Exception ex) when (unchecked((uint)ex.HResult) is 0xC00DABE0 or 0x80070490 or 0xC00D36D5)
        {
            return MirrorProblem.NoCamera;
        }
        catch (Exception ex)
        {
            Shammy.Infrastructure.Log.Warn($"Mirror: camera init failed (0x{ex.HResult:X8})", ex);
            return mode == MediaCaptureSharingMode.ExclusiveControl ? MirrorProblem.InUse : MirrorProblem.Failed;
        }
    }

    /// <summary>The smallest format at least 640 wide (or the closest), so the camera does less work.</summary>
    static async Task PickSmallFormat(MediaFrameSource source)
    {
        try
        {
            var best = source.SupportedFormats
                .Where(f => f.VideoFormat is { Width: > 0 } && FrameRate(f) >= 14)
                .OrderBy(f => f.VideoFormat.Width >= TargetWidth ? 0 : 1)
                .ThenBy(f => Math.Abs((int)f.VideoFormat.Width - TargetWidth))
                .ThenBy(f => Math.Abs(f.VideoFormat.Width / (double)f.VideoFormat.Height - 16.0 / 9))
                .ThenByDescending(FrameRate)
                .FirstOrDefault();
            if (best is not null) await source.SetFormatAsync(best);
        }
        catch (Exception ex)
        {
            Shammy.Infrastructure.Log.Warn("Mirror: choosing a camera format failed; using the default", ex);
        }
    }

    static double FrameRate(MediaFrameFormat f) =>
        f.FrameRate is { Denominator: > 0 } r ? r.Numerator / (double)r.Denominator : 0;

    /// <summary>Stops and releases the camera (its light goes off). Safe to call repeatedly.</summary>
    public async Task StopAsync()
    {
        _generation++;
        await _gate.WaitAsync();
        try
        {
            var reader = _reader;
            var capture = _capture;
            _reader = null;
            _capture = null;
            if (reader is not null)
            {
                reader.FrameArrived -= OnFrameArrived;
                try
                {
                    await reader.StopAsync();
                }
                catch (Exception ex)
                {
                    Shammy.Infrastructure.Log.Warn("Mirror: stopping the frame reader failed", ex);
                }
                reader.Dispose();
            }
            capture?.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Runs on a capture thread.
    void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null) return;
        var now = _clock.Elapsed;
        if (now - _lastFrame < MinFrameGap || Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            bitmap.Dispose();
            return;
        }
        _lastFrame = now;

        SoftwareBitmap? converted = null;
        try
        {
            var src = bitmap;
            if (src.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                src = converted = SoftwareBitmap.Convert(src, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            int w = src.PixelWidth, h = src.PixelHeight, size = w * h * 4;
            if (_pixels is null || _pixels.Length != size) _pixels = new byte[size];
            src.CopyToBuffer(_pixels.AsBuffer());
            ApplyBrightness(_pixels);
            _ui.BeginInvoke(DispatcherPriority.Render, () => Present(w, h));
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _pending, 0);
        }
        finally
        {
            converted?.Dispose();
            bitmap.Dispose();
        }
    }

    void ApplyBrightness(byte[] px)
    {
        double f = Brightness;
        if (Math.Abs(f - 1) < 0.01) return;
        if (f != _lutFactor)
        {
            for (int i = 0; i < 256; i++) _lut[i] = (byte)Math.Clamp((int)Math.Round(i * f), 0, 255);
            _lutFactor = f;
        }
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = _lut[px[i]];
            px[i + 1] = _lut[px[i + 1]];
            px[i + 2] = _lut[px[i + 2]];
            px[i + 3] = 255;
        }
    }

    void Present(int w, int h)
    {
        try
        {
            if (_reader is null || _pixels is null || _pixels.Length != w * h * 4) return;
            if (Bitmap is null || Bitmap.PixelWidth != w || Bitmap.PixelHeight != h)
            {
                Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                _width = w;
                _height = h;
                BitmapChanged?.Invoke(Bitmap);
            }
            Bitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixels, w * 4, 0);
            Frames++;
        }
        finally
        {
            Interlocked.Exchange(ref _pending, 0);
        }
    }
}
