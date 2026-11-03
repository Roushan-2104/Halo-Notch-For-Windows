using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shammy.Core.Notch;
using Shammy.Infrastructure;
using Shammy.Modules;

namespace Shammy.Views;

/// <summary>
/// The hover peek (440×68, v2 design): what's playing (album art with a soft glow in its colour, title and
/// artist) and, on the right, the single most important glance as a pill you can click (join the meeting,
/// open the card). With nothing playing the left side shows the time and date instead.
/// </summary>
internal sealed class PeekView : Grid
{
    public const double PeekWidth = 440, PeekHeight = 68;

    readonly ShammyServices _services;
    readonly GlanceFeed _feed;
    readonly Border _glow, _art, _pill;
    readonly TextBlock _title, _subtitle, _pillText;
    GlanceEntry? _top;

    public PeekView(ShammyServices services, GlanceFeed feed)
    {
        _services = services;
        _feed = feed;
        Width = PeekWidth;
        Height = PeekHeight;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _glow = new Border { Margin = new Thickness(-6), CornerRadius = new CornerRadius(14), Opacity = 0.35, Effect = new BlurEffect { Radius = 14 } };
        _art = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true };
        var art = new Grid { Width = 40, Height = 40, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        art.Children.Add(_glow);
        art.Children.Add(_art);
        Children.Add(art);

        _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("CardText"), TextTrimming = TextTrimming.CharacterEllipsis };
        _subtitle = new TextBlock { FontSize = 12, Foreground = Res("CardText2"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(_title);
        text.Children.Add(_subtitle);
        SetColumn(text, 1);
        Children.Add(text);

        _pillText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
        _pill = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _pillText,
            Cursor = Cursors.Hand,
        };
        _pill.MouseEnter += (_, _) => _pill.Opacity = 0.85;
        _pill.MouseLeave += (_, _) => _pill.Opacity = 1;
        _pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            GlanceClicked?.Invoke(_top?.Item);
        };
        SetColumn(_pill, 2);
        Children.Add(_pill);
    }

    /// <summary>The pill was clicked, with its glance (null when it had none to show).</summary>
    public event Action<GlanceItem?>? GlanceClicked;

    public string Describe() =>
        $"title={_title.Text} subtitle={_subtitle.Text} pill={(_pill.Visibility == Visibility.Visible ? _pillText.Text : "-")}";

    /// <summary>Fresh content each time the peek opens (and when the song changes while it's open).</summary>
    public void Refresh()
    {
        var media = _services.Media;
        if (media.HasSession)
        {
            CompactView.ApplyArt(_art, media, 12);
            if (media.Art is null) _art.Background = new SolidColorBrush(RestView.ParseColor(NotchPalette.Music));
            var glow = media.Art is null ? RestView.ParseColor(NotchPalette.Music) : media.ArtColor;
            _glow.Background = new SolidColorBrush(glow);
            _glow.Visibility = Visibility.Visible;
            _title.Text = media.Title;
            _subtitle.Text = media.Artist.Length > 0 ? media.Artist : media.IsPlaying ? "Playing" : "Paused";
        }
        else
        {
            var accent = (Brush)Application.Current.Resources["AccentBrush"];
            _art.Background = accent;
            _art.Child = CompactView.Glyph("", 18, Brushes.Black);
            _glow.Visibility = Visibility.Collapsed;
            var now = DateTime.Now;
            _title.Text = BuiltInGlances.Time(now, BuiltInGlances.Uses24Hour(CultureInfo.CurrentCulture)).Time;
            _subtitle.Text = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        }

        // Only a song title can repeat in words; the clock never should (a "Standup 10:30" glance at 10:30 is news).
        _top = _feed.Top(media.HasSession ? media.Title : "");
        if (_top is null)
        {
            _pill.Visibility = Visibility.Collapsed;
            return;
        }
        var color = RestView.ParseColor(_top.Item.AccentHex);
        _pill.Visibility = Visibility.Visible;
        _pill.Background = new SolidColorBrush(Mix(color, Colors.Black, 0.8));
        _pillText.Foreground = new SolidColorBrush(Mix(color, Colors.White, 0.45));
        _pillText.Text = _top.Item.Text;
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
