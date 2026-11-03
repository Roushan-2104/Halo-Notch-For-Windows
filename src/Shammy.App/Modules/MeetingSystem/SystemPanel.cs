using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Shammy.Core.MeetingSystem;

namespace Shammy.Modules.MeetingSystem;

/// <summary>The "System" tab: CPU with a 60 s sparkline and the busiest app, memory, and battery.</summary>
internal sealed class SystemPanel : Grid
{
    readonly TextBlock _cpuValue = Value();
    readonly Sparkline _spark = new() { Margin = new Thickness(0, 4, 0, 5) };
    readonly Run _topName = new();
    readonly Run _topPercent = new();
    readonly TextBlock _memValue = Value();
    readonly TextBlock _memDetail = Detail();
    readonly Border _memFill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    readonly Border _memTrack;
    readonly TextBlock _batValue = Value();
    readonly TextBlock _batRate = Detail();
    readonly TextBlock _batLeft = Detail();

    public SystemPanel()
    {
        Margin = new Thickness(0, 8, 0, 0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition());

        // CPU
        var top = Detail();
        top.Inlines.Add(_topName);
        top.Inlines.Add(_topPercent);
        _topName.Foreground = Res("Fg");
        var cpu = new Grid();
        cpu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cpu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cpu.RowDefinitions.Add(new RowDefinition());
        cpu.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(cpu, Header("CPU"), 0);
        Add(cpu, _cpuValue, 1);
        Add(cpu, _spark, 2);
        Add(cpu, top, 3);
        Tile(cpu, 0);

        // Memory: the bar sits at the bottom, level with the CPU footer.
        _memTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Res("Fill2"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 5),
            Child = _memFill,
        };
        var mem = new Grid();
        mem.Children.Add(Stack(Header("Memory"), _memValue, _memDetail));
        mem.Children.Add(_memTrack);
        Tile(mem, 2);

        // Battery
        Tile(Stack(Header("Battery"), _batValue, _batRate, _batLeft), 4);

        _memTrack.SizeChanged += (_, _) => UpdateMemoryBar();
    }

    double _memLoad;

    public void Show(SystemSample s, IReadOnlyList<(DateTime At, double Value)> cpuHistory, double? drainPerHour)
    {
        _cpuValue.Text = SystemFormat.Percent(s.Cpu);
        _cpuValue.Foreground = Res(s.Cpu >= 90 ? "Red" : s.Cpu >= 75 ? "Amber" : "Fg");
        _spark.Set(cpuHistory);
        if (s.Top is { } t)
        {
            _topName.Text = AppNames.Friendly(t.Name);
            _topPercent.Text = "  " + SystemFormat.Percent(t.Percent);
        }
        else
        {
            _topName.Text = "";
            _topPercent.Text = "Finding the busiest app…";
        }

        _memLoad = s.MemoryLoad;
        _memValue.Text = $"{s.MemoryLoad}%";
        _memValue.Foreground = Res(s.MemoryLoad >= 90 ? "Red" : s.MemoryLoad >= 80 ? "Amber" : "Fg");
        _memDetail.Text = $"{SystemFormat.Gigabytes(s.UsedMemory)} of {SystemFormat.Gigabytes(s.TotalMemory)} GB";
        UpdateMemoryBar();

        if (!s.HasBattery)
        {
            _batValue.Text = "AC";
            _batValue.Foreground = Res("Fg");
            _batRate.Text = "No battery";
            _batLeft.Text = "";
            return;
        }
        _batValue.Text = $"{s.BatteryPercent}%";
        _batValue.Foreground = Res(!s.PluggedIn && s.BatteryPercent <= 20 ? "Red" : s.Charging ? "Green" : "Fg");
        if (s.PluggedIn)
        {
            _batRate.Text = s.Charging ? "Charging" : "Plugged in";
            _batLeft.Text = "";
        }
        else
        {
            _batRate.Text = drainPerHour is double r ? $"−{SystemFormat.Rate(Math.Max(0, r))}" : "On battery";
            _batLeft.Text = DrainEstimator.Remaining(s.BatteryPercent, drainPerHour) is TimeSpan left
                ? $"{SystemFormat.Duration(left)} left"
                : "Estimating…";
        }
    }

    void UpdateMemoryBar()
    {
        _memFill.Width = Math.Max(0, _memTrack.ActualWidth * Math.Clamp(_memLoad / 100, 0, 1));
        _memFill.Background = Res(_memLoad >= 90 ? "Red" : _memLoad >= 80 ? "Amber" : "Fg");
    }

    void Tile(UIElement content, int column)
    {
        var tile = new Border
        {
            Background = Res("Fill1"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 9, 12, 8),
            Child = content,
        };
        SetColumn(tile, column);
        Children.Add(tile);
    }

    static void Add(Grid grid, UIElement child, int row)
    {
        SetRow(child, row);
        grid.Children.Add(child);
    }

    static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var c in children) stack.Children.Add(c);
        return stack;
    }

    static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Res("Fg2"),
    };

    static TextBlock Value() => new()
    {
        FontFamily = (FontFamily)Application.Current.Resources["ShammyDisplayFont"],
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        Foreground = Res("Fg"),
        Margin = new Thickness(0, -1, 0, 0),
    };

    static TextBlock Detail() => new()
    {
        FontSize = 11,
        Foreground = Res("Fg2"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 1, 0, 0),
    };

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>CPU over the last minute: an accent line with a soft fill, newest on the right.</summary>
internal sealed class Sparkline : FrameworkElement
{
    static readonly TimeSpan Span = TimeSpan.FromSeconds(60);

    IReadOnlyList<(DateTime At, double Value)> _points = Array.Empty<(DateTime, double)>();

    public Sparkline() => ClipToBounds = true;

    public void Set(IReadOnlyList<(DateTime At, double Value)> points)
    {
        _points = points;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawLine(new Pen((Brush)Application.Current.Resources["Fill2"], 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (_points.Count < 2) return;

        var end = _points[^1].At;
        Point At(int i) => new(
            w - (end - _points[i].At).TotalSeconds / Span.TotalSeconds * w,
            h - 1 - Math.Clamp(_points[i].Value, 0, 100) / 100 * (h - 3));

        var accent = (Application.Current.Resources["AccentBrush"] as SolidColorBrush)?.Color ?? Colors.White;
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            var first = At(0);
            l.BeginFigure(first, false, false);
            a.BeginFigure(new Point(first.X, h), true, true);
            a.LineTo(first, false, false);
            for (int i = 1; i < _points.Count; i++)
            {
                var p = At(i);
                l.LineTo(p, true, true);
                a.LineTo(p, false, false);
            }
            a.LineTo(new Point(w, h), false, false);
        }
        line.Freeze();
        area.Freeze();
        var fill = new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B));
        fill.Freeze();
        var pen = new Pen(new SolidColorBrush(accent), 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
    }
}
