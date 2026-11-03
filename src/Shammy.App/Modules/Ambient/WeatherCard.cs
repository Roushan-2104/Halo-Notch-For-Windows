using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shammy.Core.Ambient;
using Shammy.Core.Cards;
using Shammy.Views.Cards;

namespace Shammy.Modules.Ambient;

/// <summary>
/// The v2 weather card (Weather.dc.html): city, a big temperature, the condition icon and six mini hourly
/// bars. Its detail shows now (feels like, humidity, wind), the next 12 hours as bars and a 5-day range list,
/// all from the Open-Meteo forecast the weather feature already fetches; anything the forecast lacks is left out.
/// </summary>
internal sealed class WeatherCard
{
    const int MiniHours = 6, ChartHours = 12, DaysShown = 5;

    readonly Brush _accent = CardKit.Res("WeatherAccent");
    readonly Grid _compact = new() { Width = 176, Height = NotchPalette.CardHeight };
    readonly Grid _content = new() { Margin = CardKit.CompactPadding };
    readonly TextBlock _city = CardKit.Label("");
    readonly TextBlock _temp = CardKit.Big("", 34);
    readonly TextBlock _condition = CardKit.Text("", 12, FontWeights.Medium, secondary: true);
    readonly TextBlock _status = CardKit.Text("Finding the weather\u2026", 12, FontWeights.Medium, secondary: true);
    readonly ContentControl _icon = new() { Width = 24, Height = 24 };
    readonly UniformGrid _mini = new() { Rows = 1, Columns = MiniHours };

    WeatherReport? _report;
    string _cityName = "";

    public WeatherCard()
    {
        var top = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Right);
        top.Children.Add(_icon);
        top.Children.Add(_city);
        _temp.Margin = new Thickness(0, 4, 0, 0);
        _condition.Margin = new Thickness(0, 5, 0, 0);
        var now = CardKit.Column(0, top, _temp, _condition);
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _content.Children.Add(now);
        _mini.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(_mini, 1);
        _content.Children.Add(_mini);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = CardKit.CompactPadding;
        _compact.Children.Add(_content);
        _compact.Children.Add(_status);
        Show(null, "");
    }

    public FrameworkElement Compact => _compact;

    public bool IsRelevant => _report is not null;

    /// <summary>"31° Pune" for the resting notch.</summary>
    public GlanceItem? Glance() =>
        _report is { } r ? new GlanceItem(CardMath.Fit(WeatherText.Glance(r, _cityName)), NotchPalette.Weather, 10) : null;

    /// <summary>Shows "Finding the weather…" style text while there's no forecast yet.</summary>
    public void ShowStatus(string text)
    {
        _status.Text = text;
        if (_report is null) Show(null, _cityName);
    }

    public void Show(WeatherReport? report, string city)
    {
        _report = report;
        _cityName = city;
        _content.Visibility = report is null ? Visibility.Hidden : Visibility.Visible;
        _status.Visibility = report is null ? Visibility.Visible : Visibility.Collapsed;
        if (report is not { } r) return;

        _city.Text = city.ToUpperInvariant();
        _temp.Text = WeatherText.Temp(r.Temperature);
        _condition.Text = WeatherCodes.Describe(r.Code);
        _icon.Content = Icon(r.Code, r.IsDay, 24);

        var hours = r.NextHours(r.LocalNow(DateTime.UtcNow), MiniHours);
        var heights = CardMath.BarHeights(hours.Select(h => h.Temperature).ToList());
        _mini.Children.Clear();
        for (int i = 0; i < hours.Count; i++)
        {
            var bar = CardKit.Bar(_accent, 12, 30 * heights[i], 4);
            bar.Opacity = CardMath.BarOpacity(heights[i]);
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            var slot = new Grid { Height = 30 };
            slot.Children.Add(bar);
            var label = CardKit.Text(HourNumber(hours[i].Time), 10, FontWeights.Medium, secondary: true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var column = CardKit.Column(4, slot, label);
            column.ToolTip = $"{WeatherText.HourLabel(hours[i].Time)} \u00B7 {WeatherText.Temp(hours[i].Temperature)}";
            _mini.Children.Add(column);
        }
    }

    static string HourNumber(DateTime t) => (t.Hour % 12 == 0 ? 12 : t.Hour % 12).ToString(CultureInfo.InvariantCulture);

    FrameworkElement Icon(int code, bool isDay, double size)
    {
        var icon = new WeatherIcon { Width = size, Height = size, Backing = CardKit.Res("CardBg"), Sun = _accent };
        icon.Set(code, isDay);
        return icon;
    }

    // ---- Detail ---------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var root = new Grid { ClipToBounds = true };
        var glow = CardKit.Glow(CardKit.Color(NotchPalette.Weather), 320, 0.16);
        glow.HorizontalAlignment = HorizontalAlignment.Left;
        glow.VerticalAlignment = VerticalAlignment.Top;
        glow.Margin = new Thickness(90, -120, 0, 0);
        root.Children.Add(glow);

        if (_report is not { } r)
        {
            var empty = CardKit.Text(_status.Text, 14, FontWeights.Medium, secondary: true);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(empty);
            return root;
        }

        var row = new DockPanel { Margin = CardKit.DetailPadding };
        var now = CardKit.Section(NowBlock(r));
        now.Width = 208;
        DockPanel.SetDock(now, Dock.Left);
        row.Children.Add(now);
        var days = CardKit.Section(DaysBlock(r));
        days.Width = 286;
        days.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(days, Dock.Right);
        row.Children.Add(days);
        var hours = CardKit.Section(HoursBlock(r));
        hours.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(hours);
        root.Children.Add(row);
        return root;
    }

    FrameworkElement NowBlock(WeatherReport r)
    {
        var temp = CardKit.Big(WeatherText.Temp(r.Temperature), 48);
        var tempRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var icon = Icon(r.Code, r.IsDay, 44);
        DockPanel.SetDock(icon, Dock.Right);
        tempRow.Children.Add(icon);
        tempRow.Children.Add(temp);
        var condition = CardKit.Text(WeatherCodes.Describe(r.Code), 13, FontWeights.Medium);
        condition.Margin = new Thickness(0, 2, 0, 0);
        var range = CardKit.Text($"H {WeatherText.Temp(r.TodayHigh)}  \u00B7  L {WeatherText.Temp(r.TodayLow)}", 12, FontWeights.Medium, secondary: true);
        range.Margin = new Thickness(0, 2, 0, 0);
        var head = CardKit.Column(0, CardKit.Label($"{_cityName} \u00B7 Now"), tempRow, condition, range);

        var stats = new StackPanel();
        foreach (var (label, value) in Stats(r))
        {
            var line = new DockPanel { Margin = new Thickness(0, stats.Children.Count == 0 ? 0 : 4, 0, 0) };
            var v = CardKit.Text(value, 13, FontWeights.SemiBold);
            DockPanel.SetDock(v, Dock.Right);
            line.Children.Add(v);
            line.Children.Add(CardKit.Text(label, 12, FontWeights.Medium, secondary: true));
            stats.Children.Add(line);
        }
        var statsBox = new Border
        {
            BorderBrush = CardKit.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = stats,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = stats.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(head);
        Grid.SetRow(statsBox, 1);
        grid.Children.Add(statsBox);
        return grid;
    }

    /// <summary>Only what the forecast actually has: feels-like always, humidity and wind when present.</summary>
    static List<(string Label, string Value)> Stats(WeatherReport r)
    {
        var stats = new List<(string, string)> { ("Feels like", WeatherText.Temp(r.FeelsLike)) };
        if (r.Humidity is { } h) stats.Add(("Humidity", $"{h}%"));
        if (r.WindKmh is { } w) stats.Add(("Wind", WeatherText.Wind(w)));
        return stats;
    }

    /// <summary>"Now" plus the next 11 hours.</summary>
    static List<(string Label, double Temp, bool IsNow)> ChartPoints(WeatherReport r)
    {
        var points = new List<(string, double, bool)> { ("Now", r.Temperature, true) };
        foreach (var h in r.NextHours(r.LocalNow(DateTime.UtcNow), ChartHours - 1))
            points.Add((HourNumber(h.Time), h.Temperature, false));
        return points;
    }

    FrameworkElement HoursBlock(WeatherReport r)
    {
        var points = ChartPoints(r);
        var hours = r.NextHours(r.LocalNow(DateTime.UtcNow), ChartHours - 1);
        var head = new DockPanel();
        if (hours.Count > 0)
        {
            var peak = hours.MaxBy(h => h.Temperature)!;
            var peakText = CardKit.Text($"Peak {WeatherText.Temp(peak.Temperature)} at {WeatherText.HourLabel(peak.Time)}", 11, FontWeights.Medium, secondary: true);
            DockPanel.SetDock(peakText, Dock.Right);
            head.Children.Add(peakText);
        }
        head.Children.Add(CardKit.Label($"Next {ChartHours} hours"));

        var heights = CardMath.BarHeights(points.Select(p => p.Temp).ToList(), floor: 0.18);
        var chart = new UniformGrid { Rows = 1, Columns = Math.Max(1, points.Count), Margin = new Thickness(0, 10, 0, 0) };
        const double maxBar = 104;
        for (int i = 0; i < points.Count; i++)
        {
            var bar = CardKit.Bar(_accent, 14, maxBar * heights[i], 5);
            bar.Opacity = CardMath.BarOpacity(heights[i], 0.18, 0.35);
            var temp = CardKit.Text(WeatherText.Temp(points[i].Temp), 11, FontWeights.SemiBold);
            temp.HorizontalAlignment = HorizontalAlignment.Center;
            var label = CardKit.Text(points[i].Label, 10, points[i].IsNow ? FontWeights.Bold : FontWeights.Medium, secondary: !points[i].IsNow);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 6, 0, 0);
            var column = new DockPanel();
            DockPanel.SetDock(label, Dock.Bottom);
            column.Children.Add(label);
            var stack = CardKit.Column(4, temp, bar);
            stack.VerticalAlignment = VerticalAlignment.Bottom;
            column.Children.Add(stack);
            chart.Children.Add(column);
        }

        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(chart);
        return body;
    }

    FrameworkElement DaysBlock(WeatherReport r)
    {
        var days = r.Days.Take(DaysShown).ToList();
        var list = new UniformGrid { Columns = 1, Rows = Math.Max(1, days.Count), Margin = new Thickness(0, 6, 0, 0) };
        if (days.Count > 0)
        {
            double min = days.Min(d => d.Low), max = days.Max(d => d.High);
            var today = r.LocalNow(DateTime.UtcNow).Date;
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                var line = new DockPanel { Height = 30, VerticalAlignment = VerticalAlignment.Center };
                var name = CardKit.Text(WeatherText.DayLabel(d.Date, today), 13, FontWeights.SemiBold);
                name.Width = 42;
                var icon = Icon(d.Code, true, 20);
                icon.Margin = new Thickness(10, 0, 0, 0);
                var lo = CardKit.Text(WeatherText.Temp(d.Low), 13, FontWeights.Medium, secondary: true);
                lo.Width = 28;
                lo.TextAlignment = TextAlignment.Right;
                lo.Margin = new Thickness(10, 0, 0, 0);
                var hi = CardKit.Text(WeatherText.Temp(d.High), 13, FontWeights.SemiBold);
                hi.Width = 28;
                hi.Margin = new Thickness(10, 0, 0, 0);
                foreach (var e in new FrameworkElement[] { name, icon, lo }) DockPanel.SetDock(e, Dock.Left);
                DockPanel.SetDock(hi, Dock.Right);
                line.Children.Add(name);
                line.Children.Add(icon);
                line.Children.Add(lo);
                line.Children.Add(hi);
                var bar = new RangeBar(_accent)
                {
                    Margin = new Thickness(10, 0, 0, 0),
                    Span = CardMath.RangeSpan(d.Low, d.High, min, max),
                    Strength = 0.6 + 0.4 * CardMath.Fraction(d.High - min, max - min),
                    Dot = d.Date == today ? CardMath.Fraction(r.Temperature - min, max - min) : null,
                };
                line.Children.Add(bar);
                line.ToolTip = $"{WeatherCodes.Describe(d.Code)} \u00B7 low {WeatherText.Temp(d.Low)}, high {WeatherText.Temp(d.High)}" +
                               (d.RainChance is { } p && p > 0 ? $" \u00B7 {p}% rain" : "");
                list.Children.Add(line);
            }
        }
        var body = new DockPanel();
        var head = CardKit.Label($"{days.Count}-day forecast");
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(list);
        return body;
    }

    /// <summary>A day's low–high range on the shared scale, with a dot for the temperature now (today only).</summary>
    sealed class RangeBar(Brush accent) : FrameworkElement
    {
        public (double Left, double Width) Span { get; init; }
        public double Strength { get; init; } = 1;
        public double? Dot { get; init; }

        protected override Size MeasureOverride(Size available) => new(0, 10);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, y = (ActualHeight - 5) / 2;
            if (w <= 0) return;
            dc.DrawRoundedRectangle(CardKit.TrackBg, null, new Rect(0, y, w, 5), 2.5, 2.5);
            dc.PushOpacity(Strength);
            dc.DrawRoundedRectangle(accent, null, new Rect(w * Span.Left, y, Math.Max(5, w * Span.Width), 5), 2.5, 2.5);
            dc.Pop();
            if (Dot is { } d)
            {
                var c = new Point(Math.Clamp(w * d, 4, w - 4), y + 2.5);
                dc.DrawEllipse(CardKit.Res("CardText"), new Pen(CardKit.Res("CardBg"), 1.5), c, 4, 4);
            }
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        if (_report is not { } r) return $"weather: no data ({_status.Text})";
        var hours = r.NextHours(r.LocalNow(DateTime.UtcNow), MiniHours);
        var heights = CardMath.BarHeights(hours.Select(h => h.Temperature).ToList());
        var bars = string.Join(" ", hours.Select((h, i) => $"{HourNumber(h.Time)}:{WeatherText.Temp(h.Temperature)}@{heights[i]:0.00}"));
        return $"weather: city={_cityName} temp={WeatherText.Temp(r.Temperature)} condition={WeatherCodes.Describe(r.Code)} icon={WeatherCodes.Kind(r.Code)} bars=[{bars}]";
    }

    public string DescribeDetail()
    {
        if (_report is not { } r) return $"weather detail: no data ({_status.Text})";
        var stats = string.Join(", ", Stats(r).Select(s => $"{s.Label} {s.Value}"));
        var chart = string.Join(" ", ChartPoints(r).Select(p => $"{p.Label}:{WeatherText.Temp(p.Temp)}"));
        var today = r.LocalNow(DateTime.UtcNow).Date;
        var days = string.Join(" | ", r.Days.Take(DaysShown).Select(d => $"{WeatherText.DayLabel(d.Date, today)} {WeatherText.Temp(d.Low)}-{WeatherText.Temp(d.High)} {WeatherCodes.Describe(d.Code)}"));
        return $"weather detail: now={WeatherText.Temp(r.Temperature)} {WeatherCodes.Describe(r.Code)} H {WeatherText.Temp(r.TodayHigh)} L {WeatherText.Temp(r.TodayLow)} " +
               $"stats=[{stats}] hours=[{chart}] days=[{days}]";
    }
}
