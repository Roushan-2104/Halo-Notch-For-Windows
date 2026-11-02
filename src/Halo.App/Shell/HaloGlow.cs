using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Halo.Shell;

/// <summary>
/// The halo: a soft multi-colour light hugging the notch's outline, slowly circling and breathing.
/// It's a few stacked strokes (wide and faint to thin and bright) rather than a blur effect, and it
/// ticks at ~30 fps on a timer instead of the render loop, which keeps it at a percent or so of one core.
/// Colours ease towards a target palette, so the halo can react (album art, charging, errors).
/// </summary>
internal sealed class HaloGlow : FrameworkElement
{
    const int Stops = 4;
    const double DegreesPerSecond = 22;

    static readonly (double Thickness, double Opacity)[] Rings =
    {
        (16, 0.07), (10, 0.13), (5.5, 0.24), (2.6, 0.5), (1.2, 0.95),
    };

    readonly LinearGradientBrush _brush;
    readonly RotateTransform _rotation = new() { CenterX = 0.5, CenterY = 0.5 };
    readonly Color[] _current = new Color[Stops];
    readonly Color[] _target = new Color[Stops];
    readonly DispatcherTimer _tick;
    Geometry _outline = Geometry.Empty;
    double _time, _strength = 0.7, _targetStrength = 0.7;
    bool _enabled = true, _shown = true;

    public HaloGlow()
    {
        IsHitTestVisible = false;
        _brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), RelativeTransform = _rotation };
        for (int i = 0; i <= Stops; i++) _brush.GradientStops.Add(new GradientStop(Colors.White, (double)i / Stops));
        SetPalette(Aurora(Theme.Accent.Current), immediate: true);
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Render, (s, _) => Advance(((DispatcherTimer)s!).Interval.TotalSeconds), Dispatcher);
        _tick.Start();
    }

    public double CenterX { get; set; }

    /// <summary>Default palette: violet, cyan, pink and the user's Windows accent.</summary>
    public static Color[] Aurora(Color accent) =>
        new[] { Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0xF4, 0x72, 0xB6), accent };

    /// <summary>A palette built around one colour (album art, charging green, error red).</summary>
    public static Color[] Around(Color c) =>
        new[] { c, Shift(c, 1.35), Shift(c, 0.75), Mix(c, Colors.White, 0.35) };

    public void SetPalette(Color[] palette, bool immediate = false)
    {
        for (int i = 0; i < Stops; i++)
        {
            _target[i] = palette[i % palette.Length];
            if (immediate) _current[i] = _target[i];
        }
        ApplyStops();
    }

    /// <summary>How present the halo is: subtle when idle, brighter while something is happening.</summary>
    public void SetStrength(double strength) => _targetStrength = Math.Clamp(strength, 0, 1);

    /// <summary>
    /// Every repaint of the transparent notch window means Windows re-copies its whole surface, so the
    /// idle halo ticks slowly (its slow drift hides the lower rate) and only runs at 30 fps while active.
    /// </summary>
    public void SetLively(bool lively) => _tick.Interval = TimeSpan.FromMilliseconds(lively ? 33 : 90);

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            UpdateTimer();
            InvalidateVisual();
        }
    }

    /// <summary>Hidden while the notch itself is hidden (fullscreen apps), so it costs nothing then.</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            UpdateTimer();
            InvalidateVisual();
        }
    }

    void UpdateTimer()
    {
        if (_enabled && _shown) _tick.Start();
        else _tick.Stop();
    }

    public void UpdateShape(double w, double h, double r)
    {
        _outline = NotchShape.BuildOutline(CenterX, w, h, r);
        InvalidateVisual();
    }

    void Advance(double dt)
    {
        _time += dt;
        _rotation.Angle = (_time * DegreesPerSecond) % 360;
        _strength += (_targetStrength - _strength) * Math.Min(1, dt * 3);
        for (int i = 0; i < Stops; i++) _current[i] = Mix(_current[i], _target[i], Math.Min(1, dt * 2.5));
        ApplyStops();
        InvalidateVisual();
    }

    void ApplyStops()
    {
        for (int i = 0; i <= Stops; i++) _brush.GradientStops[i].Color = _current[i % Stops];
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_enabled || !_shown || _outline.IsEmpty()) return;
        // Slow breathing so the light feels alive rather than painted on.
        double breathe = 0.82 + 0.18 * Math.Sin(_time * 1.3);
        foreach (var (thickness, opacity) in Rings)
        {
            dc.PushOpacity(opacity * _strength * breathe);
            dc.DrawGeometry(null, new Pen(_brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, _outline);
            dc.Pop();
        }
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static Color Shift(Color c, double k) => Color.FromRgb(
        (byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255));
}
