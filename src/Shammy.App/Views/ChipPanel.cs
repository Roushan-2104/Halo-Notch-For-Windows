using System.Windows;
using System.Windows.Controls;

namespace Shammy.Views;

/// <summary>
/// A row of chips with a gap between them that shows only the chips that fit whole: one cut in half
/// looks broken, and every view a hidden chip opens is still reachable (by its card, a command or search).
/// </summary>
public sealed class ChipPanel : Panel
{
    public ChipPanel() => ClipToBounds = true;

    public double Gap { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, h = 0;
        bool full = false;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            var d = child.DesiredSize;
            double next = x == 0 ? d.Width : x + Gap + d.Width;
            if (full || next > available.Width)
            {
                full = true;
                continue;
            }
            x = next;
            h = Math.Max(h, d.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? x : Math.Min(x, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        bool full = false;
        foreach (UIElement child in InternalChildren)
        {
            var d = child.DesiredSize;
            double left = x == 0 ? 0 : x + Gap;
            if (full || left + d.Width > final.Width + 0.5)
            {
                // Parked outside the clip rather than hidden: toggling Visibility from inside layout can loop.
                full = true;
                child.Arrange(new Rect(final.Width + 10000, 0, d.Width, d.Height));
                continue;
            }
            child.Arrange(new Rect(left, (final.Height - d.Height) / 2, d.Width, d.Height));
            x = left + d.Width;
        }
        return final;
    }
}
