using System.Windows;
using System.Windows.Media;

namespace Shammy.Shell;

/// <summary>
/// Draws the notch: flush with the top edge of the screen, with concave "shoulders" where it meets the
/// edge and continuous rounded corners at the bottom, like the MacBook cut-out it imitates.
/// </summary>
internal sealed class NotchShape : FrameworkElement
{
    const double ShoulderRadius = 7;

    Geometry _geometry = Geometry.Empty;

    public double CenterX { get; set; }
    public Brush Fill { get; set; } = Brushes.Black;
    public Pen? Stroke { get; set; }
    public Geometry Geometry => _geometry;

    public void Update(double width, double height, double radius)
    {
        _geometry = Build(CenterX, width, height, radius);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawGeometry(Fill, Stroke, _geometry);

    /// <summary>The same outline without the top edge (it sits against the screen or taskbar), for the shammy.</summary>
    public static Geometry BuildOutline(double cx, double w, double h, double r)
    {
        if (w < 1 || h < 0.5) return Geometry.Empty;

        double rs = Math.Min(ShoulderRadius, h / 2);
        r = Math.Clamp(r, 0, Math.Min(h - rs, w / 2));
        double x0 = cx - w / 2, x1 = cx + w / 2;

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x0 - rs, 0), false, false);
            ctx.QuadraticBezierTo(new Point(x0, 0), new Point(x0, rs), true, true);
            ctx.LineTo(new Point(x0, h - r), true, true);
            if (r > 0) ctx.ArcTo(new Point(x0 + r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x1 - r, h), true, true);
            if (r > 0) ctx.ArcTo(new Point(x1, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x1, rs), true, true);
            ctx.QuadraticBezierTo(new Point(x1, 0), new Point(x1 + rs, 0), true, true);
        }
        g.Freeze();
        return g;
    }

    public static Geometry Build(double cx, double w, double h, double r)
    {
        if (w < 1 || h < 0.5) return Geometry.Empty;

        double rs = Math.Min(ShoulderRadius, h / 2);
        r = Math.Clamp(r, 0, Math.Min(h - rs, w / 2));
        double x0 = cx - w / 2, x1 = cx + w / 2;

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x0 - rs, 0), true, true);
            ctx.QuadraticBezierTo(new Point(x0, 0), new Point(x0, rs), true, true);
            ctx.LineTo(new Point(x0, h - r), true, true);
            if (r > 0) ctx.ArcTo(new Point(x0 + r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x1 - r, h), true, true);
            if (r > 0) ctx.ArcTo(new Point(x1, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x1, rs), true, true);
            ctx.QuadraticBezierTo(new Point(x1, 0), new Point(x1 + rs, 0), true, true);
        }
        g.Freeze();
        return g;
    }
}
