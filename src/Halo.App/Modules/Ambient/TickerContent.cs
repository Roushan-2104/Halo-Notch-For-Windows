using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Halo.Core.Ambient;
using Halo.Motion;
using ShapePath = System.Windows.Shapes.Path;

namespace Halo.Modules.Ambient;

/// <summary>
/// Compact ticker: a provider badge in the left ear, the current line in the right ear. Every 5 s the
/// next line slides up into place on a spring; frames are only requested during that slide, and the
/// rotation timer only runs while the pill is actually on screen.
/// </summary>
internal sealed class TickerContent : ICompactContent, IDisposable
{
    const double EarWidth = 170;
    const double Travel = 13;

    readonly TickerBadge _badge = new();
    readonly Grid _stage = new() { Width = EarWidth, Height = 32, ClipToBounds = true };
    readonly TextBlock _current = Line(), _outgoing = Line();
    readonly TranslateTransform _currentShift = new(), _outgoingShift = new();
    readonly SpringValue _progress = new(1, 0.004);
    readonly DispatcherTimer _rotate = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
    List<TickerItem> _items = new();
    int _index;
    IDisposable? _frames;
    TickerBadge? _bubbleBadge;

    public TickerContent()
    {
        _current.RenderTransform = _currentShift;
        _outgoing.RenderTransform = _outgoingShift;
        _outgoing.Opacity = 0;
        _stage.Children.Add(_outgoing);
        _stage.Children.Add(_current);
        _rotate.Tick += (_, _) => Next();
        _stage.Loaded += (_, _) => UpdateRotation();
        _stage.Unloaded += (_, _) =>
        {
            _rotate.Stop();
            Settle();
        };
    }

    public string Current => _current.Text;

    public double Build(ContentControl left, ContentControl right)
    {
        left.Content = _badge;
        right.Content = _stage;
        return EarWidth;
    }

    /// <summary>Just the badge when media or a download owns the pill; it doesn't rotate.</summary>
    public UIElement? BuildBubble()
    {
        _bubbleBadge ??= new TickerBadge { LayoutTransform = new ScaleTransform(0.85, 0.85) };
        if (_items.Count > 0) _bubbleBadge.Set(_items[Math.Min(_index, _items.Count - 1)]);
        return _bubbleBadge;
    }

    public void SetItems(IReadOnlyList<TickerItem> items)
    {
        _items = items.ToList();
        if (_items.Count == 0)
        {
            _current.Text = "";
            UpdateRotation();
            return;
        }
        _index = Math.Min(_index, _items.Count - 1);
        // Same slot, fresher numbers (a new score or price): update in place without a slide.
        var item = _items[_index];
        _current.Text = item.Text;
        _badge.Set(item);
        _bubbleBadge?.Set(item);
        UpdateRotation();
    }

    void UpdateRotation()
    {
        if (_stage.IsLoaded && _items.Count > 1)
        {
            if (!_rotate.IsEnabled) _rotate.Start();
        }
        else _rotate.Stop();
    }

    void Next()
    {
        if (_items.Count < 2) return;
        _index = (_index + 1) % _items.Count;
        var item = _items[_index];
        _outgoing.Text = _current.Text;
        _current.Text = item.Text;
        _badge.Set(item);

        _progress.Snap(0);
        _progress.AnimateTo(1, 0.5, 0.92);
        Apply();
        _frames ??= Animator.Subscribe(_ =>
        {
            Apply();
            if (_progress.IsSettled) Settle();
        });
    }

    void Apply()
    {
        double p = Math.Clamp(_progress.Value, 0, 1.05);
        _currentShift.Y = (1 - p) * Travel;
        _current.Opacity = Math.Clamp(p, 0, 1);
        _outgoingShift.Y = -p * Travel;
        _outgoing.Opacity = Math.Clamp(1 - p * 1.4, 0, 1);
    }

    /// <summary>Jump to the end of any slide and stop asking for frames.</summary>
    void Settle()
    {
        _frames?.Dispose();
        _frames = null;
        if (!_progress.IsSettled) _progress.Snap(1);
        _currentShift.Y = 0;
        _current.Opacity = 1;
        _outgoing.Opacity = 0;
    }

    static TextBlock Line() => new()
    {
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["Fg"],
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = EarWidth,
    };

    public void Dispose()
    {
        _rotate.Stop();
        Settle();
    }
}

/// <summary>The left-ear badge: a little cricket ball, a trend arrow tinted by direction, or a coin.</summary>
internal sealed class TickerBadge : Grid
{
    readonly TextBlock _glyph = new()
    {
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    readonly Grid _ball = new() { Width = 14, Height = 14 };
    readonly Grid _coin = new() { Width = 14, Height = 14 };
    TickerKind? _kind;
    string? _direction;

    public TickerBadge()
    {
        Width = 18;
        Height = 18;
        VerticalAlignment = VerticalAlignment.Center;

        // Cricket ball: red leather and a curved seam, drawn so it needs no image assets.
        _ball.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x2F)) });
        var seam = new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xF4, 0xE8)), 1.1);
        _ball.Children.Add(new ShapePath { Data = Geometry.Parse("M 4.6,1.4 Q 7.8,7 4.6,12.6"), Stroke = seam.Brush, StrokeThickness = seam.Thickness });
        _ball.Children.Add(new ShapePath { Data = Geometry.Parse("M 9.4,1.4 Q 6.2,7 9.4,12.6"), Stroke = seam.Brush, StrokeThickness = seam.Thickness });

        _coin.Children.Add(new Ellipse { Fill = (Brush)Application.Current.Resources["Amber"] });
        _coin.Children.Add(new TextBlock
        {
            Text = "B",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0.5, 0, 0, 0.5),
        });

        Children.Add(_glyph);
        Children.Add(_ball);
        Children.Add(_coin);
    }

    public void Set(TickerItem item)
    {
        var direction = item.Text.Contains('\u25BC') ? "down" : item.Text.Contains('\u25B2') ? "up" : "flat";
        if (_kind == item.Kind && _direction == direction) return;
        _kind = item.Kind;
        _direction = direction;
        _ball.Visibility = item.Kind == TickerKind.Cricket ? Visibility.Visible : Visibility.Collapsed;
        _coin.Visibility = item.Kind == TickerKind.Crypto ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Visibility = item.Kind == TickerKind.Stocks ? Visibility.Visible : Visibility.Collapsed;
        // "Trending up" glyph, mirrored vertically for a falling price.
        _glyph.Text = "\uEAFC";
        _glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        _glyph.RenderTransform = direction == "down" ? new ScaleTransform(1, -1) : Transform.Identity;
        _glyph.Foreground = (Brush)Application.Current.Resources[direction switch { "down" => "Red", "up" => "Green", _ => "Fg2" }];
    }
}
