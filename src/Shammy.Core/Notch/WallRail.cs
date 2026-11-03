namespace Shammy.Core.Notch;

/// <summary>A rail candidate: what the Focus Wall needs to know about a card to place and focus it.</summary>
/// <param name="Live">How live it is right now (0 = idle; a ringing alarm or a meeting about to start is higher
/// than a song playing, which is higher than a running timer). The most live tile is focused when the notch opens.</param>
public readonly record struct WallCard(string Id, int Order, bool Relevant, int Live = 0);

/// <summary>
/// The open notch's Focus Wall rail (Wall.dc.html): one line of fixed 6:5 tiles, one of them focused (scaled
/// 1.15×, outlined, its words shown) while its neighbours step aside and dim. Pure geometry and choice, so the
/// view only animates what this decides. When the tiles don't fit, the rail scrolls to keep the focused one in view.
/// </summary>
public static class WallRail
{
    /// <summary>The tile (6:5), the gap between tiles, the focused tile's scale and how far neighbours step aside.</summary>
    public const double TileWidth = 168, TileHeight = 140, Gap = 14, FocusScale = 1.15, Shift = 10;

    /// <summary>The open notch's rail width (1012 minus 20 px padding each side).</summary>
    public const double RailWidth = 972;

    /// <summary>The relevant cards, left to right by Order (ties keep registration order).</summary>
    public static IReadOnlyList<string> Tiles(IEnumerable<WallCard> cards) =>
        cards.Where(c => c.Relevant).OrderBy(c => c.Order).Select(c => c.Id).ToList();

    /// <summary>
    /// The tile to focus when the rail appears: the most live one (the first of equals), else the first tile.
    /// -1 for an empty rail.
    /// </summary>
    public static int DefaultFocus(IReadOnlyList<int> live)
    {
        if (live.Count == 0) return -1;
        int best = 0;
        for (int i = 1; i < live.Count; i++)
            if (live[i] > live[best]) best = i;
        return best;
    }

    /// <summary>Keeps a focus on the same card when the rail changes; falls back to the default when it left.</summary>
    public static int Keep(IReadOnlyList<string> tiles, string? focusedId, IReadOnlyList<int> live)
    {
        if (focusedId is not null)
        {
            for (int i = 0; i < tiles.Count; i++)
                if (string.Equals(tiles[i], focusedId, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return DefaultFocus(live);
    }

    /// <summary>Moves the focus by <paramref name="delta"/>, wrapping around the ends (← on the first goes to the last).</summary>
    public static int Step(int focus, int delta, int count)
    {
        if (count <= 0) return -1;
        if (focus < 0) return delta < 0 ? count - 1 : 0;
        return ((focus + delta) % count + count) % count;
    }

    /// <summary>The rail's total width for <paramref name="count"/> tiles at rest.</summary>
    public static double Width(int count) => count <= 0 ? 0 : count * TileWidth + (count - 1) * Gap;

    /// <summary>
    /// Where the first tile's left edge sits in the rail: centred when everything fits, otherwise scrolled so the
    /// focused tile (with room for its scale and the neighbours' step) stays fully in view, never past the ends.
    /// </summary>
    public static double Start(int count, int focus, double railWidth = RailWidth)
    {
        double total = Width(count);
        if (total <= railWidth) return (railWidth - total) / 2;
        // Room on each side of the focused tile for its growth plus a neighbour's sliver.
        double grow = TileWidth * (FocusScale - 1) / 2 + Gap;
        double center = Math.Clamp(focus, 0, count - 1) * (TileWidth + Gap) + TileWidth / 2;
        double start = railWidth / 2 - center;
        double min = railWidth - total - grow, max = grow;
        return Math.Clamp(start, min, max);
    }

    /// <summary>A tile's horizontal step: 0 for the focused one, −Shift left of it, +Shift right of it.</summary>
    public static double Offset(int index, int focus) => focus < 0 || index == focus ? 0 : index < focus ? -Shift : Shift;
}
