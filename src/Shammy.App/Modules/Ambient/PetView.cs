using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Shammy.Core.Ambient;
using Shammy.Motion;

namespace Shammy.Modules.Ambient;

/// <summary>
/// A small round creature drawn with plain geometry. It bobs and blinks while awake, bounces faster
/// when excited, hops when woken, and when asleep it closes its eyes, settles, and stops requesting
/// frames entirely, so a sleeping pet costs nothing.
/// </summary>
internal sealed class PetView : FrameworkElement
{
    public const double EarWidth = 32;
    const double FrameInterval = 1.0 / 30; // the pet doesn't need more than 30 redraws a second
    const double HopDuration = 0.42;

    static readonly Brush Body = Frozen(Color.FromRgb(0xEE, 0xEA, 0xFF));
    static readonly Brush Shade = Frozen(Color.FromRgb(0xC9, 0xC2, 0xEE));
    static readonly Brush Cheek = Frozen(Color.FromArgb(0x8C, 0xFF, 0x8F, 0xA8));
    static readonly Brush Ink = Frozen(Color.FromRgb(0x1A, 0x18, 0x24));
    static readonly Pen InkPen = FrozenPen(Ink, 1.1);

    readonly Random _rng = new();
    PetMood _mood = PetMood.Awake;
    double _phase, _amp, _sleepiness, _hop = -1, _blinkIn = 2.5, _blinkLeft, _sinceDraw;
    IDisposable? _frames;

    public PetView()
    {
        Width = EarWidth;
        Height = 28;
        VerticalAlignment = VerticalAlignment.Center;
        IsVisibleChanged += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Stop();
    }

    public bool IsAnimating => _frames is not null;

    /// <summary>False for the still snapshot in the detached bubble, which may stay attached while faded out.</summary>
    public bool Animated { get; init; } = true;

    public void SetMood(PetMood mood, bool woke)
    {
        _mood = mood;
        if (!Animated) _sleepiness = mood == PetMood.Asleep ? 1 : 0;
        else if (woke) _hop = 0;
        UpdateSubscription();
        InvalidateVisual();
    }

    bool AtRest => _mood == PetMood.Asleep && _sleepiness >= 1 && _amp < 0.05 && _hop < 0;

    void UpdateSubscription()
    {
        if (Animated && IsVisible && !AtRest) _frames ??= Animator.Subscribe(OnFrame);
        else Stop();
    }

    public void Stop()
    {
        _frames?.Dispose();
        _frames = null;
    }

    void OnFrame(double dt)
    {
        var (targetAmp, hz) = _mood switch
        {
            PetMood.Excited => (3.2, 2.2),
            PetMood.Awake => (1.2, 0.55),
            _ => (0.0, 0.3),
        };
        _amp += (targetAmp - _amp) * Math.Min(1, dt * 4);
        _phase = (_phase + dt * Math.PI * hz) % (Math.PI * 2);
        _sleepiness = Math.Clamp(_sleepiness + (_mood == PetMood.Asleep ? dt : -dt * 3) / 0.8, 0, 1);
        if (_hop >= 0 && (_hop += dt) > HopDuration) _hop = -1;

        if (_mood != PetMood.Asleep)
        {
            if (_blinkLeft > 0) _blinkLeft -= dt;
            else if ((_blinkIn -= dt) <= 0)
            {
                _blinkLeft = 0.13;
                _blinkIn = 2.2 + _rng.NextDouble() * 3.8;
            }
        }

        _sinceDraw += dt;
        if (_sinceDraw >= FrameInterval)
        {
            _sinceDraw = 0;
            InvalidateVisual();
        }

        if (AtRest)
        {
            _amp = 0;
            InvalidateVisual();
            Stop();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double cx = 13, ground = 25, w = 20, h = 15;
        double bob = _amp * Math.Abs(Math.Sin(_phase));
        double hop = _hop >= 0 ? 7 * Math.Sin(Math.PI * _hop / HopDuration) : 0;
        double lift = bob + hop;

        // Squash a little on landing (only noticeable when bouncing), and sag while asleep.
        double squash = 1 + 0.09 * (_amp / 3.2) * (1 - Math.Min(1, lift / 1.5));
        double sx = squash * (1 + 0.04 * _sleepiness), sy = (1 / squash) * (1 - 0.1 * _sleepiness);
        double bw = w * sx, bh = h * sy;
        double bottom = ground - lift, top = bottom - bh;

        // Shadow stays on the ground and shrinks as the pet rises.
        double shadow = Math.Max(0.4, 1 - lift / 10);
        dc.DrawEllipse(Frozen(Color.FromArgb((byte)(0x40 * shadow), 0xFF, 0xFF, 0xFF)), null, new Point(cx, ground + 1.2), 7 * shadow, 1.1);

        // Ears, then body over them.
        dc.DrawEllipse(Shade, null, new Point(cx - 5.5, top + 1.5), 2.6, 3.4);
        dc.DrawEllipse(Shade, null, new Point(cx + 5.5, top + 1.5), 2.6, 3.4);
        dc.DrawRoundedRectangle(Body, null, new Rect(cx - bw / 2, top, bw, bh), bh / 2.1, bh / 2.1);
        dc.DrawEllipse(Shade, null, new Point(cx - 4, bottom - 0.3), 2.4, 1.2);
        dc.DrawEllipse(Shade, null, new Point(cx + 4, bottom - 0.3), 2.4, 1.2);

        double eyeY = top + bh * 0.45;
        bool closed = _sleepiness > 0.5 || _blinkLeft > 0;
        foreach (double ex in new[] { cx - 3.8, cx + 3.8 })
        {
            if (!closed)
            {
                double ry = _mood == PetMood.Excited ? 2.3 : 2;
                dc.DrawEllipse(Ink, null, new Point(ex, eyeY), 1.5, ry);
                dc.DrawEllipse(Brushes.White, null, new Point(ex + 0.5, eyeY - 0.8), 0.5, 0.5);
            }
            else if (_sleepiness > 0.5)
            {
                // Sleeping: little downward arcs.
                dc.DrawGeometry(null, InkPen, Arc(ex - 1.6, eyeY, ex + 1.6, eyeY, 1.2));
            }
            else
            {
                dc.DrawLine(InkPen, new Point(ex - 1.5, eyeY), new Point(ex + 1.5, eyeY));
            }
        }
        dc.DrawEllipse(Cheek, null, new Point(cx - 6.6, eyeY + 2.6), 1.7, 1);
        dc.DrawEllipse(Cheek, null, new Point(cx + 6.6, eyeY + 2.6), 1.7, 1);

        double mouthY = eyeY + 3;
        if (_mood == PetMood.Excited && _sleepiness < 0.5) dc.DrawEllipse(Ink, null, new Point(cx, mouthY + 0.4), 1.3, 1.1);
        else if (_sleepiness < 0.5) dc.DrawGeometry(null, InkPen, Arc(cx - 1.3, mouthY, cx + 1.3, mouthY, 0.9));

        if (_sleepiness > 0)
        {
            // Two "z"s drift up as it nods off, then hold still.
            double rise = 3 * _sleepiness;
            Z(dc, "z", 7, cx + 9, top - 1 - rise, _sleepiness);
            Z(dc, "z", 5.5, cx + 13.5, top - 5 - rise, _sleepiness * 0.7);
        }
    }

    void Z(DrawingContext dc, string text, double size, double x, double y, double opacity)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)Application.Current.Resources["ShammyFont"], FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size, (Brush)Application.Current.Resources["Fg2"], VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.PushOpacity(opacity);
        dc.DrawText(ft, new Point(x, y));
        dc.Pop();
    }

    static Geometry Arc(double x1, double y1, double x2, double y2, double depth)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x1, y1), false, false);
            ctx.QuadraticBezierTo(new Point((x1 + x2) / 2, y1 + depth * 2), new Point(x2, y2), true, true);
        }
        g.Freeze();
        return g;
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Pen FrozenPen(Brush b, double thickness)
    {
        var p = new Pen(b, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }
}
