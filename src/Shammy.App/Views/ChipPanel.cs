using System.Windows;
using System.Windows.Controls;
using Shammy.Core.Notch;

namespace Shammy.Views;

/// <summary>
/// A row of chips with a gap between them that shows only the chips that fit whole: one cut in half
/// looks broken. When some don't fit, the <see cref="More"/> chip (the panel's last child) takes the end of
/// the row and lists the rest, so every view stays reachable (see <see cref="ChipFit"/>).
/// </summary>
public sealed class ChipPanel : Panel
{
    int _shown;
    bool _more;

    public ChipPanel() => ClipToBounds = true;

    public double Gap { get; set; } = 8;

    /// <summary>The overflow chip; add it as the last child. Shown only when some chips don't fit.</summary>
    public UIElement? More { get; set; }

    /// <summary>The chips that didn't fit (and are listed under <see cref="More"/>), in order.</summary>
    public IEnumerable<UIElement> Hidden => Chips().Skip(_shown);

    IEnumerable<UIElement> Chips() => InternalChildren.Cast<UIElement>().Where(c => c != More);

    protected override Size MeasureOverride(Size available)
    {
        var chips = Chips().ToList();
        foreach (var child in InternalChildren.Cast<UIElement>()) child.Measure(new Size(double.PositiveInfinity, available.Height));
        var widths = chips.Select(c => c.DesiredSize.Width).ToList();
        double moreWidth = More?.DesiredSize.Width ?? double.PositiveInfinity;
        (_shown, _more) = More is null
            ? (ChipFit.Fit(widths, available.Width, Gap, double.PositiveInfinity).Shown, false)
            : ChipFit.Fit(widths, available.Width, Gap, moreWidth);

        double x = 0, h = 0;
        for (int i = 0; i < _shown; i++)
        {
            x += (i == 0 ? 0 : Gap) + widths[i];
            h = Math.Max(h, chips[i].DesiredSize.Height);
        }
        if (_more && More is not null)
        {
            x += (_shown == 0 ? 0 : Gap) + moreWidth;
            h = Math.Max(h, More.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? x : Math.Min(x, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0;
        int i = 0;
        foreach (var child in Chips())
        {
            var d = child.DesiredSize;
            if (i++ >= _shown)
            {
                // Parked outside the clip rather than hidden: toggling Visibility from inside layout can loop.
                child.Arrange(new Rect(final.Width + 10000, 0, d.Width, d.Height));
                continue;
            }
            double left = x == 0 ? 0 : x + Gap;
            child.Arrange(new Rect(left, (final.Height - d.Height) / 2, d.Width, d.Height));
            x = left + d.Width;
        }
        if (More is { } more)
        {
            var d = more.DesiredSize;
            double left = x == 0 ? 0 : x + Gap;
            more.Arrange(_more ? new Rect(left, (final.Height - d.Height) / 2, d.Width, d.Height) : new Rect(final.Width + 10000, 0, d.Width, d.Height));
        }
        return final;
    }
}
