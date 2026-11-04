using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shammy.Infrastructure;
using Windows.Media.Control;

namespace Shammy.Playback;

/// <summary>
/// Now-playing info for whatever app Windows considers the current media session (Spotify, a browser
/// tab, Apple Music, VLC...), through the same API the Windows media flyout uses.
/// </summary>
internal sealed class MediaService : IDisposable
{
    readonly Dispatcher _ui;
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    int _propertiesVersion;
    TimeSpan _position;
    DateTimeOffset _positionStamp;
    double _rate = 1;

    public MediaService(Dispatcher ui) => _ui = ui;

    public event Action? Changed;

    public bool HasSession => _session is not null && Title.Length > 0;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public ImageSource? Art { get; private set; }
    public Color ArtColor { get; private set; } = Colors.White;
    public bool IsPlaying { get; private set; }
    public TimeSpan Duration { get; private set; }
    public bool CanSeek { get; private set; }
    public bool CanSkipNext { get; private set; }
    public bool CanSkipPrevious { get; private set; }

    /// <summary>Apps only report position occasionally, so it's extrapolated between reports.</summary>
    public TimeSpan Position
    {
        get
        {
            if (!IsPlaying || _positionStamp == default) return Clamp(_position);
            var elapsed = DateTimeOffset.Now - _positionStamp;
            return Clamp(_position + TimeSpan.FromTicks((long)(elapsed.Ticks * _rate)));
        }
    }

    TimeSpan Clamp(TimeSpan t) =>
        t < TimeSpan.Zero ? TimeSpan.Zero : Duration > TimeSpan.Zero && t > Duration ? Duration : t;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => Post(() => Attach(_manager.GetCurrentSession()));
        Attach(_manager.GetCurrentSession());
    }

    void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = session;
        if (session is null)
        {
            Title = Artist = "";
            Art = null;
            IsPlaying = false;
            Changed?.Invoke();
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
        RefreshPlayback();
        RefreshTimeline();
        _ = RefreshPropertiesAsync();
    }

    void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        Post(() => _ = RefreshPropertiesAsync());

    void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        Post(() =>
        {
            RefreshPlayback();
            RefreshTimeline();
            Changed?.Invoke();
        });

    void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
        Post(() =>
        {
            RefreshTimeline();
            Changed?.Invoke();
        });

    async Task RefreshPropertiesAsync()
    {
        var session = _session;
        if (session is null) return;
        int version = ++_propertiesVersion;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _propertiesVersion || props is null) return;

            ImageSource? art = null;
            var color = Colors.White;
            if (props.Thumbnail is not null)
            {
                using var ras = await props.Thumbnail.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // Sharp enough for the 300 px art panel on a 2x screen.
                bmp.DecodePixelWidth = 600;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                art = bmp;
                color = ArtPalette.Vibrant(bmp);
            }
            if (version != _propertiesVersion) return;
            // Title and artwork change together, so nothing sees a new song with the old song's colour.
            Title = props.Title ?? "";
            Artist = string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            Art = art;
            ArtColor = color;
        }
        catch (Exception ex)
        {
            Log.Warn("Reading media properties failed", ex);
        }
        Changed?.Invoke();
    }

    void RefreshPlayback()
    {
        var info = _session?.GetPlaybackInfo();
        if (info is null) return;
        IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        _rate = info.PlaybackRate is > 0 ? info.PlaybackRate.Value : 1;
        CanSeek = info.Controls.IsPlaybackPositionEnabled;
        CanSkipNext = info.Controls.IsNextEnabled;
        CanSkipPrevious = info.Controls.IsPreviousEnabled;
    }

    void RefreshTimeline()
    {
        var t = _session?.GetTimelineProperties();
        if (t is null) return;
        Duration = t.EndTime - t.StartTime;
        _position = t.Position - t.StartTime;
        // Some apps never stamp the position; treat that as "just now" so the bar doesn't jump.
        _positionStamp = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;
    }

    public void TogglePlayPause() => Fire(_session?.TryTogglePlayPauseAsync());
    public void Next() => Fire(_session?.TrySkipNextAsync());
    public void Previous() => Fire(_session?.TrySkipPreviousAsync());

    public void Seek(TimeSpan position)
    {
        var t = _session?.GetTimelineProperties();
        if (t is null || !CanSeek) return;
        _position = position;
        _positionStamp = DateTimeOffset.Now;
        Fire(_session!.TryChangePlaybackPositionAsync((t.StartTime + position).Ticks));
        Changed?.Invoke();
    }

    static async void Fire(Windows.Foundation.IAsyncOperation<bool>? op)
    {
        if (op is null) return;
        try { await op; }
        catch (Exception ex) { Log.Warn("Media command failed", ex); }
    }

    void Post(Action a) => _ui.BeginInvoke(a);

    public void Dispose() => Attach(null);
}
