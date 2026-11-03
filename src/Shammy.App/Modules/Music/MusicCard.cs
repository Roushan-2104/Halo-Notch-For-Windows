using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Core.Cards;
using Shammy.Core.Music;
using Shammy.Infrastructure;
using Shammy.Playback;
using Shammy.Views;
using Shammy.Views.Cards;

namespace Shammy.Modules.Music;

/// <summary>
/// The v2 music card (Music.dc.html): album art softly glowing in its own colour, title and artist, a live
/// waveform from the speakers, a scrubber when the app reports a timeline, and round prev/play/next. Its
/// detail adds big art, the synced lyrics (the same <see cref="LyricsPanel"/> the old Lyrics tab used) and
/// a volume slider. YouTube Music and most players don't expose their queue to Windows, so there's no
/// "Up next" list rather than a made-up one.
/// </summary>
internal sealed class MusicCard
{
    const double Width = 340;

    readonly ShammyServices _services;
    readonly LyricsPanel _lyrics;
    readonly Brush _accent = CardKit.Res("MusicAccent");
    readonly Color _accentColor = CardKit.Color(NotchPalette.Music);

    // Compact parts, built once and updated in place.
    readonly Grid _compact;
    readonly Canvas _glowLayer = new() { IsHitTestVisible = false };
    readonly Border _art = new() { Width = 64, Height = 64, CornerRadius = new CornerRadius(12) };
    readonly TextBlock _status, _title, _artist, _elapsed, _total;
    readonly VisualizerBars _bars;
    readonly CardTrack _scrub = new() { TrackHeight = 4, KnobSize = 8, Interactive = false };
    readonly Grid _scrubRow = new();
    readonly ContentControl _playSlot = new();

    // The open detail's live parts (null while it's closed).
    Detail? _detail;

    public MusicCard(ShammyServices services, LyricsPanel lyrics)
    {
        _services = services;
        _lyrics = lyrics;
        _scrub.Fill = _accent;

        _status = CardKit.Label("Now playing", _accent);
        _title = CardKit.Text("", 17, FontWeights.Bold);
        _artist = CardKit.Text("", 13, FontWeights.Medium, secondary: true);
        _elapsed = CardKit.Text("0:00", 11, FontWeights.Medium, secondary: true);
        _total = CardKit.Text("0:00", 11, FontWeights.Medium, secondary: true);
        _bars = Waveform(28);

        // Top: art, words, waveform.
        var words = CardKit.Column(3, _status, _title, _artist);
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(12, 0, 8, 0);
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_art, Dock.Left);
        top.Children.Add(_art);
        _bars.VerticalAlignment = VerticalAlignment.Top;
        _bars.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(_bars, Dock.Right);
        top.Children.Add(_bars);
        top.Children.Add(words);

        // Middle: elapsed, scrubber, total.
        _scrubRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _scrubRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _scrubRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _scrub.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(_scrub, 1);
        Grid.SetColumn(_total, 2);
        _scrubRow.Children.Add(_elapsed);
        _scrubRow.Children.Add(_scrub);
        _scrubRow.Children.Add(_total);
        _scrubRow.VerticalAlignment = VerticalAlignment.Center;

        // Bottom: prev, play/pause, next.
        var controls = CardKit.Row(16,
            CardKit.RoundButton("Previous track", CardKit.Icon(CardKit.Paths.Previous, CardKit.Res("CardText")), () => Media.Previous()),
            _playSlot,
            CardKit.RoundButton("Next track", CardKit.Icon(CardKit.Paths.Next, CardKit.Res("CardText")), () => Media.Next()));
        controls.HorizontalAlignment = HorizontalAlignment.Center;

        var content = new Grid { Margin = CardKit.CompactPadding };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(top);
        Grid.SetRow(_scrubRow, 1);
        content.Children.Add(_scrubRow);
        Grid.SetRow(controls, 2);
        content.Children.Add(controls);

        _compact = new Grid
        {
            Width = Width,
            Height = NotchPalette.CardHeight,
            ClipToBounds = true,
            Clip = new RectangleGeometry(new Rect(0, 0, Width, NotchPalette.CardHeight), 20, 20),
        };
        _compact.Children.Add(_glowLayer);
        _compact.Children.Add(content);

        // The scrubber only moves while someone can see it.
        CardKit.TickWhileVisible(_compact, TimeSpan.FromSeconds(1), UpdateTime);
        Media.Changed += Refresh;
        Refresh();
    }

    MediaService Media => _services.Media;

    public FrameworkElement Compact => _compact;

    public bool IsRelevant => Media.HasSession;

    Color GlowColor => Media.Art is not null ? Media.ArtColor : _accentColor;

    void Refresh()
    {
        var m = Media;
        _status.Text = (m.IsPlaying ? "Now playing" : m.HasSession ? "Paused" : "Nothing playing").ToUpperInvariant();
        _title.Text = m.HasSession ? m.Title : "Nothing playing";
        _artist.Text = m.Artist;
        _art.Background = ArtBrush(m);
        _art.Child = m.Art is null ? NoteGlyph(26) : null;
        _art.Effect = CardKit.Shadow(GlowColor, 18, 0.35);
        _glowLayer.Children.Clear();
        var glow = CardKit.Glow(GlowColor, 250, 0.32);
        Canvas.SetLeft(glow, -70);
        Canvas.SetTop(glow, -80);
        _glowLayer.Children.Add(glow);
        _bars.Active = m.IsPlaying;
        _playSlot.Content = PlayButton(36, 16);
        _scrubRow.Visibility = MediaTime.HasTimeline(m.Duration) ? Visibility.Visible : Visibility.Hidden;
        UpdateTime();
        _detail?.Refresh();
    }

    void UpdateTime()
    {
        var m = Media;
        if (!MediaTime.HasTimeline(m.Duration)) return;
        var pos = m.Position;
        _elapsed.Text = MediaTime.Clock(pos);
        _total.Text = MediaTime.Clock(m.Duration);
        _scrub.Value = CardMath.Fraction(pos.TotalSeconds, m.Duration.TotalSeconds);
    }

    Button PlayButton(double size, double icon)
    {
        bool playing = Media.IsPlaying;
        var glyph = playing
            ? CardKit.Icon(CardKit.Paths.Pause, Brushes.Black, icon, 3)
            : CardKit.Icon(CardKit.Paths.Play, Brushes.Black, icon, 2, Brushes.Black);
        return CardKit.RoundButton(playing ? "Pause" : "Play", glyph, () => Media.TogglePlayPause(), _accent, size);
    }

    VisualizerBars Waveform(double height)
    {
        // Ten bars mirrored over the visualizer's five bands, low frequencies in the middle.
        var bands = AudioVisualizer.Bands;
        return new VisualizerBars
        {
            BarCount = 10,
            Height = height,
            Color = _accentColor,
            Source = i => _services.Visualizer.Level(i < bands ? bands - 1 - i : i - bands),
        };
    }

    static Brush ArtBrush(MediaService m)
    {
        if (m.Art is { } art)
        {
            var b = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            b.Freeze();
            return b;
        }
        // No artwork (some browser players): the accent's own gradient, like the mockup's placeholder art.
        var g = new LinearGradientBrush(CardKit.Color("#FFB547"), CardKit.Color("#9C2D0B"), 50);
        g.Freeze();
        return g;
    }

    static FrameworkElement NoteGlyph(double size) => new TextBlock
    {
        Text = "\uE8D6",
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = size,
        Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xF1, 0xD6)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- Detail ---------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var detail = new Detail(this);
        _detail = detail;
        detail.Root.Loaded += (_, _) => _detail = detail;
        detail.Root.Unloaded += (_, _) =>
        {
            if (_detail == detail) _detail = null;
            detail.Close();
        };
        return detail.Root;
    }

    /// <summary>One open detail view. Its lyrics panel is the module's single instance, moved in on open.</summary>
    sealed class Detail
    {
        readonly MusicCard _card;
        readonly Border _art = new() { Width = 196, Height = 196, CornerRadius = new CornerRadius(18) };
        readonly Canvas _glow = new() { IsHitTestVisible = false };
        readonly TextBlock _status, _title, _artist, _elapsed, _left, _synced;
        readonly CardTrack _scrub = new() { TrackHeight = 6, KnobSize = 12 };
        readonly CardTrack _volume = new() { TrackHeight = 4, KnobSize = 0 };
        readonly StackPanel _timeline;
        readonly ContentControl _playSlot = new();
        readonly VisualizerBars _bars;
        readonly Border _lyricsHost = new();

        public Detail(MusicCard card)
        {
            _card = card;
            var accent = card._accent;
            _status = CardKit.Label("Now playing", accent);
            _title = CardKit.Text("", 28, FontWeights.Bold);
            _artist = CardKit.Text("", 14, FontWeights.Medium);
            _elapsed = CardKit.Text("", 11, FontWeights.Medium, secondary: true);
            _left = CardKit.Text("", 11, FontWeights.Medium, secondary: true);
            _bars = card.Waveform(18);
            _scrub.Fill = accent;
            _scrub.KnobHalo = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x8A, 0x3D));
            _scrub.Committed += f =>
            {
                var m = card.Media;
                if (m.CanSeek && MediaTime.HasTimeline(m.Duration)) m.Seek(TimeSpan.FromSeconds(f * m.Duration.TotalSeconds));
            };
            _volume.Fill = CardKit.Res("CardText");
            _volume.Changed += v => card._services.Volume?.SetLevel((float)v);
            _volume.Width = 64;
            _volume.VerticalAlignment = VerticalAlignment.Center;

            // Info column: status + waveform, title, artist / scrubber / controls + volume.
            var statusRow = new DockPanel();
            DockPanel.SetDock(_bars, Dock.Right);
            statusRow.Children.Add(_bars);
            statusRow.Children.Add(_status);
            var words = CardKit.Column(4, statusRow, _title, _artist);

            var times = new DockPanel();
            DockPanel.SetDock(_left, Dock.Right);
            times.Children.Add(_left);
            times.Children.Add(_elapsed);
            _timeline = CardKit.Column(2, _scrub, times);

            var transport = CardKit.Row(12,
                CardKit.RoundButton("Previous track", CardKit.Icon(CardKit.Paths.Previous, CardKit.Res("CardText")), () => card.Media.Previous()),
                _playSlot,
                CardKit.RoundButton("Next track", CardKit.Icon(CardKit.Paths.Next, CardKit.Res("CardText")), () => card.Media.Next()));
            transport.VerticalAlignment = VerticalAlignment.Center;
            var volume = CardKit.Row(6, CardKit.Icon(CardKit.Paths.Volume, CardKit.Res("CardText2"), 14), _volume);
            volume.VerticalAlignment = VerticalAlignment.Center;
            volume.ToolTip = "Volume";
            var controls = new DockPanel();
            DockPanel.SetDock(volume, Dock.Right);
            controls.Children.Add(volume);
            controls.Children.Add(transport);

            var info = new Grid { Width = 236, Margin = new Thickness(20, 0, 16, 0) };
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            info.Children.Add(words);
            _timeline.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_timeline, 1);
            info.Children.Add(_timeline);
            Grid.SetRow(controls, 2);
            info.Children.Add(controls);

            // Lyrics: the module's own panel, synced and clickable to seek, in a section of its own.
            _synced = CardKit.Text("SYNCED", 10, FontWeights.SemiBold);
            _synced.Foreground = accent;
            var badge = new Border
            {
                Child = _synced,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0x8A, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(7, 1, 7, 2),
            };
            var lyricsHead = new DockPanel();
            DockPanel.SetDock(badge, Dock.Right);
            lyricsHead.Children.Add(badge);
            lyricsHead.Children.Add(CardKit.Label("Lyrics"));
            var lyricsBody = new DockPanel();
            DockPanel.SetDock(lyricsHead, Dock.Top);
            lyricsBody.Children.Add(lyricsHead);
            lyricsBody.Children.Add(_lyricsHost);
            badge.Visibility = Visibility.Collapsed;
            _badge = badge;
            Attach();

            var row = new DockPanel { Margin = CardKit.DetailPadding, LastChildFill = true };
            _art.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(_art, Dock.Left);
            row.Children.Add(_art);
            DockPanel.SetDock(info, Dock.Left);
            row.Children.Add(info);
            row.Children.Add(CardKit.Section(lyricsBody));

            Root = new Grid { ClipToBounds = true };
            Root.Children.Add(_glow);
            Root.Children.Add(row);

            CardKit.TickWhileVisible(Root, TimeSpan.FromSeconds(1), Tick);
            Root.Loaded += (_, _) => Attach();
            Refresh();
        }

        readonly Border _badge;

        public Grid Root { get; }

        public void Refresh()
        {
            var m = _card.Media;
            _status.Text = (m.IsPlaying ? "Now playing" : m.HasSession ? "Paused" : "Nothing playing").ToUpperInvariant();
            _title.Text = m.HasSession ? m.Title : "Nothing playing";
            _artist.Text = m.Artist;
            _art.Background = ArtBrush(m);
            _art.Child = m.Art is null ? NoteGlyph(64) : null;
            _art.Effect = CardKit.Shadow(_card.GlowColor, 40, 0.4);
            _glow.Children.Clear();
            var glow = CardKit.Glow(_card.GlowColor, 420, 0.30);
            Canvas.SetLeft(glow, 50);
            Canvas.SetTop(glow, -90);
            _glow.Children.Add(glow);
            _bars.Active = m.IsPlaying;
            _playSlot.Content = _card.PlayButton(44, 18);
            _timeline.Visibility = MediaTime.HasTimeline(m.Duration) ? Visibility.Visible : Visibility.Hidden;
            _scrub.IsHitTestVisible = m.CanSeek;
            _scrub.Cursor = m.CanSeek ? System.Windows.Input.Cursors.Hand : null;
            Tick();
        }

        void Tick()
        {
            var m = _card.Media;
            if (MediaTime.HasTimeline(m.Duration))
            {
                var pos = m.Position;
                _elapsed.Text = MediaTime.Clock(pos);
                _left.Text = MediaTime.Remaining(pos, m.Duration);
                _scrub.Value = CardMath.Fraction(pos.TotalSeconds, m.Duration.TotalSeconds);
            }
            if (_card._services.Volume is { } v && !_volume.IsDragging) _volume.Value = v.Muted ? 0 : v.Level;
        }

        void OnVolume(float level, bool muted)
        {
            if (!_volume.IsDragging) _volume.Value = muted ? 0 : level;
        }

        void OnLyricsChanged() => _badge.Visibility = _card._lyrics.Kind == LyricsKind.Synced ? Visibility.Visible : Visibility.Collapsed;

        bool _attached;

        /// <summary>Moves the lyrics panel in and listens for lyrics and volume; undone by <see cref="Close"/>.</summary>
        void Attach()
        {
            if (_attached) return;
            _attached = true;
            Detach(_card._lyrics);
            _card._lyrics.ShowTrack = false;
            _lyricsHost.Child = _card._lyrics;
            _card._lyrics.LyricsChanged += OnLyricsChanged;
            if (_card._services.Volume is { } v) v.Changed += OnVolume;
            OnLyricsChanged();
        }

        public void Close()
        {
            if (!_attached) return;
            _attached = false;
            _card._lyrics.LyricsChanged -= OnLyricsChanged;
            if (_card._services.Volume is { } v) v.Changed -= OnVolume;
            // Let the next detail (or nothing) own the panel; it stops ticking once it's off screen.
            if (_lyricsHost.Child == _card._lyrics) _lyricsHost.Child = null;
        }
    }

    /// <summary>Takes an element out of whatever it was in, so it can be shown somewhere else.</summary>
    static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel p: p.Children.Remove(element); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        var m = Media;
        if (!m.HasSession) return "music: nothing playing (card not relevant)";
        var timeline = MediaTime.HasTimeline(m.Duration) ? $"{MediaTime.Clock(m.Position)}/{MediaTime.Clock(m.Duration)}" : "hidden";
        var glow = GlowColor;
        return $"music: status={(m.IsPlaying ? "playing" : "paused")} title={m.Title} artist={m.Artist} art={(m.Art is null ? "none" : "yes")} " +
               $"glow=#{glow.R:X2}{glow.G:X2}{glow.B:X2} waveform={(_bars.Active ? "live" : "idle")} scrubber={timeline} " +
               $"buttons=prev,{(m.IsPlaying ? "pause" : "play")},next visible={_compact.IsVisible}";
    }

    public string DescribeDetail()
    {
        var m = Media;
        if (!m.HasSession) return "music detail: nothing playing";
        var timeline = MediaTime.HasTimeline(m.Duration) ? $"{MediaTime.Clock(m.Position)} {MediaTime.Remaining(m.Position, m.Duration)} seek={m.CanSeek}" : "hidden";
        var volume = _services.Volume is { } v ? $"{(int)Math.Round(v.Level * 100)}%{(v.Muted ? " muted" : "")}" : "unavailable";
        var lyric = _lyrics.Kind == LyricsKind.Synced ? $" now=\"{_lyrics.CurrentLine}\"" : "";
        return $"music detail: title={m.Title} artist={m.Artist} timeline={timeline} lyrics={_lyrics.Kind} lines={_lyrics.LineCount}{lyric} " +
               $"upNext=omitted(no queue from Windows) volume={volume} open={(_detail is null ? "no" : "yes")}";
    }
}
