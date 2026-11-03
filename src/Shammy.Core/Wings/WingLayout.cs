namespace Shammy.Core.Wings;

/// <summary>
/// One wing's icons: the x (left edge, logical px) of each shown item in order, and where the "+N" button
/// goes when they don't all fit (null when everything fits).
/// </summary>
public sealed record WingSide(IReadOnlyList<double> ItemX, int Overflow, double? MoreX)
{
    public int Shown => ItemX.Count;
}

public sealed record WingLayout(WingSide Left, WingSide Right);

/// <summary>
/// Where icons go on a strip. Both wings hug the centre: pins grow leftwards from the notch's left side
/// (first pin furthest left), open apps rightwards from its right side (first opened nearest the notch).
/// A wing that doesn't fit keeps its last slot for "+N" at the outer end. All sizes are logical (96-dpi) px.
/// </summary>
public static class WingLayoutMath
{
    public const double StripHeight = 36, Slot = 34, IconSize = 22, EdgePad = 8;

    /// <summary>
    /// Room kept free around the notch on the primary screen: as wide as the open notch card (and its
    /// shadow mostly), so the notch never sits on top of an icon until it opens wider than a card.
    /// </summary>
    public const double NotchGap = 480;

    /// <summary>Screens without the notch still keep pins and open apps apart.</summary>
    public const double SeamGap = 16;

    /// <summary>The strip's height in physical pixels at a monitor's DPI (36 px at 100%).</summary>
    public static int StripHeightPx(uint dpi) => (int)Math.Round(StripHeight * (dpi == 0 ? 96 : dpi) / 96.0);

    public static double ToLogical(int px, uint dpi) => px * 96.0 / (dpi == 0 ? 96 : dpi);

    /// <param name="width">Strip width.</param>
    /// <param name="center">Where the notch's centre is, measured from the strip's left edge.</param>
    /// <param name="gap">Space kept free around the centre.</param>
    public static WingLayout Compute(double width, double center, double gap, int left, int right,
        double slot = Slot, double pad = EdgePad)
    {
        double leftInner = center - gap / 2, rightInner = center + gap / 2;
        int leftCap = Capacity(leftInner - pad, slot);
        int rightCap = Capacity(width - pad - rightInner, slot);

        var (lShown, lMore, lOverflow) = Fit(left, leftCap);
        int lSlots = lShown + (lMore ? 1 : 0);
        double lStart = leftInner - lSlots * slot;
        var lx = Enumerable.Range(0, lShown).Select(i => lStart + (i + (lMore ? 1 : 0)) * slot).ToList();
        var leftSide = new WingSide(lx, lOverflow, lMore ? lStart : null);

        var (rShown, rMore, rOverflow) = Fit(right, rightCap);
        var rx = Enumerable.Range(0, rShown).Select(i => rightInner + i * slot).ToList();
        var rightSide = new WingSide(rx, rOverflow, rMore ? rightInner + rShown * slot : null);
        return new WingLayout(leftSide, rightSide);
    }

    static int Capacity(double room, double slot) => room <= 0 ? 0 : (int)Math.Floor(room / slot + 1e-9);

    static (int Shown, bool More, int Overflow) Fit(int count, int capacity)
    {
        if (count <= capacity) return (count, false, 0);
        // Not even room for "+N": nothing shows (a very narrow screen at a very high scale).
        if (capacity <= 0) return (0, false, count);
        int shown = capacity - 1;
        return (shown, true, count - shown);
    }

    /// <summary>
    /// Drag-to-reorder: the index a dragged icon lands on, from the x of its centre and the shown slots'
    /// left edges. Before the first slot is 0, past the last is the last index.
    /// </summary>
    public static int DropIndex(double centerX, IReadOnlyList<double> itemX, double slot = Slot)
    {
        if (itemX.Count == 0) return 0;
        for (int i = 0; i < itemX.Count; i++)
            if (centerX < itemX[i] + slot) return i;
        return itemX.Count - 1;
    }
}
