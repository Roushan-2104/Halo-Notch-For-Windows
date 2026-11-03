using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Shammy.Views.Cards;

/// <summary>
/// Building blocks shared by the v2 notch cards (see the mockup's BRIEF: #121214 surfaces, one accent per
/// activity, big bold numbers under tiny uppercase labels, 36 px round controls). Everything is plain WPF
/// drawn from code, so the cards cost nothing while they aren't on screen.
/// </summary>
internal static class CardKit
{
    /// <summary>Compact cards are 176 tall; the content keeps the mockup's 14 px padding inside the notch's frame.</summary>
    public static readonly Thickness CompactPadding = new(14);

    /// <summary>
    /// The notch frames a detail with its own Back/title band and inner padding, so details add none.
    /// </summary>
    public static readonly Thickness DetailPadding = new(0);

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly Brush ControlBg = Hex("#232326"), ControlHover = Hex("#2E2E33"), TrackBg = Hex("#2A2A2E"),
        RingTrack = Hex("#232326"), RowBg = Hex("#18181B"), Divider = Hex("#1F1F23");

    static FontFamily Font => (FontFamily)Application.Current.Resources["ShammyFont"];
    static FontFamily Display => (FontFamily)Application.Current.Resources["ShammyDisplayFont"];

    /// <summary>Body text in the card colours (CardText / CardText2).</summary>
    public static TextBlock Text(string text, double size, FontWeight weight, bool secondary = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = size,
            FontWeight = weight,
            Foreground = Res(secondary ? "CardText2" : "CardText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>The tiny uppercase label (11 px SemiBold) that sits above a big number.</summary>
    public static TextBlock Label(string text, Brush? brush = null)
    {
        var t = Text(text.ToUpperInvariant(), 11, FontWeights.SemiBold, secondary: true);
        if (brush is not null) t.Foreground = brush;
        return t;
    }

    /// <summary>A big confident number ("31°", "4:32", "81%").</summary>
    public static TextBlock Big(string text, double size)
    {
        var t = Text(text, size, FontWeights.Bold);
        t.FontFamily = Display;
        t.TextTrimming = TextTrimming.None;
        // Tight like the mockup's line-height 1: digits and ° have no descenders to clip, and the default
        // line box would make a 48 px number take ~64 px.
        t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        t.LineHeight = Math.Round(size * 1.15);
        return t;
    }

    /// <summary>A rounded #121214 panel inside a detail view (the mockup's 16 px-radius sections).</summary>
    public static Border Section(UIElement child, double padding = 14) => new()
    {
        Background = Res("CardBg"),
        BorderBrush = Res("CardStroke"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(padding),
        Child = child,
    };

    /// <summary>A whole detail drawn as one card (timer, system), with the Back button's room on the left.</summary>
    public static Border DetailPanel(UIElement child) => new()
    {
        Width = Shammy.Modules.NotchPalette.DetailWidth,
        Height = Shammy.Modules.NotchPalette.DetailHeight,
        Background = Res("CardBg"),
        BorderBrush = Res("CardStroke"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(20),
        Padding = DetailPadding,
        Child = child,
    };

    /// <summary>A hairline column divider with the mockup's 18 px gutter after it.</summary>
    public static Border VerticalDivider() => new() { Width = 1, Background = Divider, Margin = new Thickness(18, 0, 18, 0) };

    /// <summary>A rounded bar (charts, meters).</summary>
    public static Border Bar(Brush fill, double width, double height, double radius) => new()
    {
        Width = width,
        Height = height,
        CornerRadius = new CornerRadius(radius),
        Background = fill,
    };

    /// <summary>A stroked 24×24 icon (the mockup's inline SVGs), scaled to <paramref name="size"/>.</summary>
    public static FrameworkElement Icon(string data, Brush stroke, double size = 16, double strokeWidth = 2, Brush? fill = null)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = strokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = fill,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>The mockup's icon paths (24×24 viewBox).</summary>
    public static class Paths
    {
        public const string Previous = "M19 5 9 12l10 7V5z M5 5v14";
        public const string Next = "M5 5l10 7-10 7V5z M19 5v14";
        public const string Play = "M8 5.5v13l11-6.5-11-6.5z";
        public const string Pause = "M9 5v14 M15 5v14";
        public const string Stop = "M8 5h8a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3V8a3 3 0 0 1 3-3z";
        public const string Plus = "M12 5v14 M5 12h14";
        public const string Volume = "M11 5 6 9H3v6h3l5 4V5z M16 9.5a3.5 3.5 0 0 1 0 5";
        public const string Bolt = "M13 2L4 14h7l-1 8 9-12h-7z";
        public const string Laptop = "M4 5h16v11H4z M2 19h20";
        public const string Bluetooth = "M7 7l10 10-5 5V2l5 5L7 17";
        public const string Stopwatch = "M12 7a7 7 0 1 1 0 14a7 7 0 1 1 0-14z M12 11v3 M10 3h4 M18 6l1.5-1.5";
        public const string Timer = "M12 5a8 8 0 1 1 0 16a8 8 0 1 1 0-16z M12 9v4l2.5 2 M9 2h6";
        public const string Chip = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z M12 8.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7z";
    }

    /// <summary>
    /// A 36 px round control: #232326 with a lighter hover, or the activity's accent for the primary one
    /// (play, resume) with a black glyph. Real buttons, named for screen readers.
    /// </summary>
    public static Button RoundButton(string name, FrameworkElement icon, Action onClick, Brush? accent = null, double size = 36)
    {
        var bg = accent ?? ControlBg;
        var face = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = bg,
            Child = icon,
        };
        var button = new Button
        {
            Content = face,
            Width = size,
            Height = size,
            Focusable = false,
            Cursor = Cursors.Hand,
            Template = BareTemplate,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.MouseEnter += (_, _) => face.Background = accent is null ? ControlHover : Lighter(accent);
        button.MouseLeave += (_, _) => face.Background = bg;
        button.PreviewMouseLeftButtonDown += (_, _) => ((ScaleTransform)button.RenderTransform).ScaleX = ((ScaleTransform)button.RenderTransform).ScaleY = 0.94;
        button.PreviewMouseLeftButtonUp += (_, _) => ((ScaleTransform)button.RenderTransform).ScaleX = ((ScaleTransform)button.RenderTransform).ScaleY = 1;
        button.MouseLeave += (_, _) => ((ScaleTransform)button.RenderTransform).ScaleX = ((ScaleTransform)button.RenderTransform).ScaleY = 1;
        button.Click += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return button;
    }

    /// <summary>A text chip ("5m", "+1 min") in the round-control style.</summary>
    public static Button Chip(string text, string name, Action onClick, double height = 40, double fontSize = 15, double radius = 14)
    {
        var label = Text(text, fontSize, FontWeights.Bold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var face = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = ControlBg,
            Padding = new Thickness(12, 0, 12, 0),
            Child = label,
        };
        var button = new Button { Content = face, Focusable = false, Cursor = Cursors.Hand, Template = BareTemplate, ToolTip = name };
        AutomationProperties.SetName(button, name);
        button.MouseEnter += (_, _) => face.Background = ControlHover;
        button.MouseLeave += (_, _) => face.Background = ControlBg;
        button.Click += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return button;
    }

    static Brush Lighter(Brush accent)
    {
        if (accent is not SolidColorBrush s) return accent;
        var c = s.Color;
        static byte Up(byte v) => (byte)Math.Min(255, v + (255 - v) * 0.18);
        var b = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, Up(c.R), Up(c.G), Up(c.B)));
        b.Freeze();
        return b;
    }

    /// <summary>A button template that is just its content (the face draws hover itself).</summary>
    static readonly ControlTemplate BareTemplate = MakeBareTemplate();

    static ControlTemplate MakeBareTemplate()
    {
        var t = new ControlTemplate(typeof(ButtonBase));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = presenter;
        t.Seal();
        return t;
    }

    /// <summary>A soft, static glow in a colour (album art, rings): a radial gradient, no blur effect to render.</summary>
    public static Ellipse Glow(Color color, double size, double strength)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)(255 * strength), color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)(255 * strength * 0.33), color.R, color.G, color.B), 0.45));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 0.7));
        brush.Freeze();
        return new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false };
    }

    /// <summary>A gentle glow under a ring or art tile in its accent colour (static; not animated).</summary>
    public static Effect Shadow(Color color, double blur, double opacity) =>
        new DropShadowEffect { Color = color, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, RenderingBias = RenderingBias.Performance };

    /// <summary>Lays children out left to right with a fixed gap.</summary>
    public static StackPanel Row(double gap, params UIElement[] children) => Stack(Orientation.Horizontal, gap, children);

    /// <summary>Lays children out top to bottom with a fixed gap.</summary>
    public static StackPanel Column(double gap, params UIElement[] children) => Stack(Orientation.Vertical, gap, children);

    static StackPanel Stack(Orientation orientation, double gap, UIElement[] children)
    {
        var panel = new StackPanel { Orientation = orientation };
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement fe)
            {
                var m = fe.Margin;
                fe.Margin = orientation == Orientation.Horizontal ? new Thickness(m.Left + gap, m.Top, m.Right, m.Bottom) : new Thickness(m.Left, m.Top + gap, m.Right, m.Bottom);
            }
            panel.Children.Add(children[i]);
        }
        return panel;
    }

    /// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> while <paramref name="element"/> is on screen, and never otherwise.</summary>
    public static void TickWhileVisible(FrameworkElement element, TimeSpan interval, Action tick)
    {
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = interval };
        timer.Tick += (_, _) => tick();
        element.IsVisibleChanged += (_, _) =>
        {
            if (element.IsVisible)
            {
                tick();
                timer.Start();
            }
            else timer.Stop();
        };
        element.Unloaded += (_, _) => timer.Stop();
    }
}

/// <summary>
/// A progress ring: a #232326 track and a round-capped arc from 12 o'clock, clockwise. Drawn directly, so a
/// change of <see cref="Value"/> is one cheap re-render.
/// </summary>
internal sealed class Ring : FrameworkElement
{
    double _value;
    Brush _fill = Brushes.White;
    double _thickness = 7;

    public double Value
    {
        get => _value;
        set
        {
            var v = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _value) < 0.0005) return;
            _value = v;
            InvalidateVisual();
        }
    }

    public Brush Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            InvalidateVisual();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            _thickness = value;
            InvalidateVisual();
        }
    }

    public Brush Track { get; set; } = CardKit.RingTrack;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double r = (size - _thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Track, _thickness), c, r, r);
        if (_value <= 0) return;
        var pen = new Pen(_fill, _thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (_value >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        double a = -Math.PI / 2 + _value * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, _value > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>
/// A progress track with a fill and a white knob (the music scrubber, the volume slider). Click or drag to
/// set; <see cref="Changed"/> reports the new fraction.
/// </summary>
internal sealed class CardTrack : FrameworkElement
{
    double _value;
    bool _dragging;

    public CardTrack()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    public double TrackHeight { get; init; } = 4;
    public double KnobSize { get; init; } = 8;
    public Brush Fill { get; set; } = Brushes.White;
    public Brush KnobHalo { get; set; } = Brushes.Transparent;
    public bool Interactive { get; init; } = true;

    /// <summary>Raised while dragging and on click with the new value, 0..1.</summary>
    public event Action<double>? Changed;

    /// <summary>Raised when a drag or click ends with the final value, 0..1.</summary>
    public event Action<double>? Committed;

    public bool IsDragging => _dragging;

    public double Value
    {
        get => _value;
        set
        {
            if (_dragging) return; // the pointer owns it until release
            var v = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _value) < 0.0005) return;
            _value = v;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size available) => new(0, Math.Max(TrackHeight, KnobSize) + 8);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        // Transparent hit area over the full height, so the thin track is easy to grab.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double y = (h - TrackHeight) / 2, rad = TrackHeight / 2;
        dc.DrawRoundedRectangle(CardKit.TrackBg, null, new Rect(0, y, w, TrackHeight), rad, rad);
        double x = w * _value;
        if (x > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, Math.Max(TrackHeight, x), TrackHeight), rad, rad);
        if (KnobSize > 0)
        {
            var c = new Point(Math.Clamp(x, KnobSize / 2, w - KnobSize / 2), h / 2);
            if (KnobHalo != Brushes.Transparent) dc.DrawEllipse(KnobHalo, null, c, KnobSize / 2 + 3, KnobSize / 2 + 3);
            dc.DrawEllipse(CardKit.Res("CardText"), null, c, KnobSize / 2, KnobSize / 2);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Interactive) return;
        _dragging = CaptureMouse();
        Set(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) Set(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Committed?.Invoke(_value);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Committed?.Invoke(_value);
    }

    void Set(double x)
    {
        _value = ActualWidth > 0 ? Math.Clamp(x / ActualWidth, 0, 1) : 0;
        InvalidateVisual();
        Changed?.Invoke(_value);
    }
}
