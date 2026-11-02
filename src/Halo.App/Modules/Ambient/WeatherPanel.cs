using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Halo.Core.Ambient;

namespace Halo.Modules.Ambient;

/// <summary>The Weather tab (~436×150): now on the left, the next six hours on the right.</summary>
internal sealed class WeatherPanel : Grid
{
    const int HourCount = 6;

    readonly TextBlock _city = Text(12, "Fg2");
    readonly TextBlock _temp = new()
    {
        FontSize = 44,
        FontWeight = FontWeights.Light,
        Foreground = Brush("Fg"),
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly WeatherIcon _icon = new() { Width = 30, Height = 30, Margin = new Thickness(10, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _condition = Text(13, "Fg", FontWeights.SemiBold);
    readonly TextBlock _details = Text(11.5, "Fg2");
    readonly TextBlock _status = Text(12.5, "Fg2");
    readonly UniformGrid _hours = new() { Rows = 1, Columns = HourCount, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid _content = new();

    public WeatherPanel()
    {
        Margin = new Thickness(0, 8, 0, 0);
        _temp.FontFamily = (FontFamily)Application.Current.Resources["HaloDisplayFont"];
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;

        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        now.Children.Add(_city);
        var tempRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-2, -4, 0, -2) };
        tempRow.Children.Add(_temp);
        tempRow.Children.Add(_icon);
        now.Children.Add(tempRow);
        now.Children.Add(_condition);
        _details.Margin = new Thickness(0, 2, 0, 0);
        now.Children.Add(_details);
        _content.Children.Add(now);

        // A hairline between "now" and the hourly strip, like the media card's separators.
        var divider = new Border { Width = 1, Background = Brush("Fill2"), Margin = new Thickness(0, 14, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        SetColumn(divider, 1);
        _content.Children.Add(divider);
        SetColumn(_hours, 2);
        _content.Children.Add(_hours);
        for (int i = 0; i < HourCount; i++) _hours.Children.Add(new HourCell());

        Children.Add(_content);
        Children.Add(_status);
        ShowStatus("");
    }

    public void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
        _content.Visibility = Visibility.Collapsed;
    }

    public void Show(WeatherReport r, string city, DateTime nowUtc)
    {
        _status.Visibility = Visibility.Collapsed;
        _content.Visibility = Visibility.Visible;
        _city.Text = city;
        _temp.Text = WeatherText.Temp(r.Temperature);
        _icon.Set(r.Code, r.IsDay);
        _condition.Text = WeatherCodes.Describe(r.Code);
        _details.Text = $"Feels {WeatherText.Temp(r.FeelsLike)} \u00B7 H {WeatherText.Temp(r.TodayHigh)}  L {WeatherText.Temp(r.TodayLow)}";
        ToolTip = $"Updated {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)} \u00B7 Open-Meteo";

        var next = r.NextHours(r.LocalNow(nowUtc), HourCount);
        for (int i = 0; i < HourCount; i++)
        {
            var cell = (HourCell)_hours.Children[i];
            if (i < next.Count) cell.Set(next[i]);
            else cell.Visibility = Visibility.Hidden;
        }
    }

    static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    static TextBlock Text(double size, string brush, FontWeight? weight = null) => new()
    {
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = Brush(brush),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Time, icon, temperature and rain chance for one hour.</summary>
    sealed class HourCell : StackPanel
    {
        readonly TextBlock _time = Text(10.5, "Fg2");
        readonly WeatherIcon _icon = new() { Width = 22, Height = 22, Margin = new Thickness(0, 6, 0, 6) };
        readonly TextBlock _temp = Text(12.5, "Fg", FontWeights.SemiBold);
        readonly TextBlock _rain = Text(10.5, "Fg3");

        public HourCell()
        {
            VerticalAlignment = VerticalAlignment.Center;
            foreach (var t in new[] { _time, _temp, _rain }) t.HorizontalAlignment = HorizontalAlignment.Center;
            _rain.Margin = new Thickness(0, 2, 0, 0);
            Children.Add(_time);
            Children.Add(_icon);
            Children.Add(_temp);
            Children.Add(_rain);
        }

        public void Set(HourForecast h)
        {
            Visibility = Visibility.Visible;
            _time.Text = WeatherText.HourLabel(h.Time);
            // Hourly is_day isn't requested; 6 am to 6 pm is close enough for an icon.
            _icon.Set(h.Code, h.Time.Hour is >= 6 and < 18);
            _temp.Text = WeatherText.Temp(h.Temperature);
            int chance = h.RainChance ?? 0;
            _rain.Text = h.RainChance is null ? " " : $"{chance}%";
            _rain.Foreground = Brush(chance >= 30 ? "AccentBrush" : "Fg3");
        }
    }
}

/// <summary>
/// A weather icon composed from Segoe Fluent glyphs (sun, moon, cloud, bolt, fog, snow) plus drawn
/// drops, since the icon font has no rain or snow cloud glyphs. The cloud gets a black backing so a sun
/// behind it is hidden, which only works because the notch is pure black.
/// </summary>
internal sealed class WeatherIcon : FrameworkElement
{
    static readonly Dictionary<string, (Geometry Shape, Geometry Backing)> Cache = new();

    int _code;
    bool _isDay = true;
    bool _set;

    public void Set(int code, bool isDay)
    {
        if (_set && code == _code && isDay == _isDay) return;
        _code = code;
        _isDay = isDay;
        _set = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_set) return;
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        var fg = (Brush)Application.Current.Resources["Fg"];
        var amber = (Brush)Application.Current.Resources["Amber"];
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var sky = _isDay ? WeatherCodes.SunGlyph : WeatherCodes.MoonGlyph;
        var skyBrush = _isDay ? amber : fg;

        switch (WeatherCodes.Kind(_code))
        {
            case WeatherKind.Clear when _code == 0:
                Glyph(dc, sky, new Rect(0, 0, s, s), skyBrush);
                break;
            case WeatherKind.Clear or WeatherKind.PartlyCloudy:
                // Mostly clear shows more sky than partly cloudy.
                double sun = _code == 1 ? 0.7 : 0.62;
                Glyph(dc, sky, new Rect(s * (1 - sun), 0, s * sun, s * sun), skyBrush);
                Glyph(dc, WeatherCodes.CloudGlyph, new Rect(0, s * 0.3, s * 0.82, s * 0.7), fg, backing: true);
                break;
            case WeatherKind.Fog:
                Glyph(dc, WeatherCodes.FogGlyph, new Rect(0, s * 0.1, s, s * 0.8), fg);
                break;
            case WeatherKind.Drizzle or WeatherKind.Rain:
                Glyph(dc, WeatherCodes.CloudGlyph, new Rect(0, 0, s, s * 0.66), fg);
                Drops(dc, s, accent, WeatherCodes.Kind(_code) == WeatherKind.Drizzle ? 1 : Math.Max(2, WeatherCodes.Intensity(_code)));
                break;
            case WeatherKind.Snow:
                Glyph(dc, WeatherCodes.CloudGlyph, new Rect(0, 0, s, s * 0.66), fg);
                Flakes(dc, s, fg);
                break;
            case WeatherKind.Thunderstorm:
                Glyph(dc, WeatherCodes.CloudGlyph, new Rect(0, 0, s, s * 0.66), fg);
                Glyph(dc, WeatherCodes.BoltGlyph, new Rect(s * 0.34, s * 0.52, s * 0.34, s * 0.48), amber, backing: true);
                break;
            default:
                Glyph(dc, WeatherCodes.CloudGlyph, new Rect(0, s * 0.1, s, s * 0.8), fg);
                break;
        }
    }

    static void Drops(DrawingContext dc, double s, Brush brush, int count)
    {
        var pen = new Pen(brush, Math.Max(1.2, s * 0.07)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double len = count == 1 ? s * 0.12 : s * 0.2;
        int n = Math.Min(3, count + 1);
        for (int i = 0; i < n; i++)
        {
            double x = s * (0.32 + i * 0.2 - (n - 2) * 0.05);
            double y = s * 0.72;
            dc.DrawLine(pen, new Point(x + len * 0.35, y), new Point(x, y + len));
        }
    }

    static void Flakes(DrawingContext dc, double s, Brush brush)
    {
        double r = Math.Max(1, s * 0.055);
        foreach (var (x, y) in new[] { (0.3, 0.8), (0.5, 0.9), (0.7, 0.8) })
            dc.DrawEllipse(brush, null, new Point(s * x, s * y), r, r);
    }

    /// <summary>Draw a glyph scaled to fit <paramref name="box"/>, optionally over a black silhouette.</summary>
    void Glyph(DrawingContext dc, string glyph, Rect box, Brush brush, bool backing = false)
    {
        var (shape, back) = Shapes(glyph);
        var b = shape.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return;
        double k = Math.Min(box.Width / b.Width, box.Height / b.Height);
        var m = new Matrix();
        m.Translate(-b.X, -b.Y);
        m.Scale(k, k);
        m.Translate(box.X + (box.Width - b.Width * k) / 2, box.Y + (box.Height - b.Height * k) / 2);
        dc.PushTransform(new MatrixTransform(m));
        if (backing)
        {
            // Slightly fattened so whatever sits behind doesn't touch the outline.
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2.2 / k), back);
        }
        dc.DrawGeometry(brush, null, shape);
        dc.Pop();
    }

    (Geometry Shape, Geometry Backing) Shapes(string glyph)
    {
        if (Cache.TryGetValue(glyph, out var cached)) return cached;
        var typeface = new Typeface((FontFamily)Application.Current.Resources["IconFont"], FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 100, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var shape = text.BuildGeometry(new Point(0, 0));
        shape.Freeze();

        // The outer contour alone, filled, is the glyph's silhouette.
        var flat = shape.GetFlattenedPathGeometry();
        var outer = flat.Figures
            .Select(f => new PathGeometry(new[] { f.Clone() }))
            .OrderByDescending(g => g.Bounds.IsEmpty ? 0 : g.Bounds.Width * g.Bounds.Height)
            .FirstOrDefault();
        Geometry backing = outer is null ? Geometry.Empty : MakeSolid(outer);
        backing.Freeze();
        return Cache[glyph] = (shape, backing);
    }

    static Geometry MakeSolid(PathGeometry outer)
    {
        foreach (var f in outer.Figures) f.IsFilled = true;
        outer.FillRule = FillRule.Nonzero;
        return outer;
    }
}
