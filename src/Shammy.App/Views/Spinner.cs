using System.Windows;
using System.Windows.Media;
using Shammy.Motion;

namespace Shammy.Views;

/// <summary>A Fluent-style indeterminate ring: an arc that rotates while it grows and shrinks.</summary>
internal sealed class Spinner : FrameworkElement
{
    IDisposable? _frames;
    double _t;

    public Spinner()
    {
        Width = Height = 18;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _frames ??= Animator.Subscribe(dt => { _t += dt; InvalidateVisual(); });
            else
            {
                _frames?.Dispose();
                _frames = null;
            }
        };
    }

    public Brush Stroke { get; set; } = Brushes.White;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(Width, Height), thickness = Math.Max(2, size / 8), r = (size - thickness) / 2;
        var c = new Point(Width / 2, Height / 2);
        double sweep = 60 + 200 * (0.5 + 0.5 * Math.Sin(_t * 3.2));
        double start = (_t * 420) % 360;

        var pen = new Pen(Stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var a0 = start * Math.PI / 180;
        var a1 = (start + sweep) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, g);
    }
}
