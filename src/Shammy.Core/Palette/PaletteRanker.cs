namespace Shammy.Core.Palette;

/// <summary>Where a palette row came from; decides its band in the ranking.</summary>
public enum PaletteKind { Command, Setting, App, File, Music, Media, Web }

/// <summary>
/// One palette row as the ranker sees it. <see cref="Key"/> stays the same across keystrokes
/// ("app:&lt;target&gt;", "file:&lt;path&gt;") so learning and the selection follow the thing, not its position.
/// <see cref="Match"/> (0–100) is how well it matches the text within its own source.
/// </summary>
public sealed record PaletteItem(string Key, PaletteKind Kind, string Title, string Subtitle, int Match)
{
    /// <summary>Right-side hint ("App", "File", "Enter").</summary>
    public string Hint { get; init; } = "";

    /// <summary>Strong enough to be what Enter does without the user picking it.</summary>
    public bool Confident { get; init; }

    /// <summary>Whatever the app needs to run the row; the ranker never looks at it.</summary>
    public object? Tag { get; init; }
}

/// <summary>Ranked rows and which one Enter runs; -1 means none (Enter does what it always did).</summary>
public sealed record RankedPalette(IReadOnlyList<PaletteItem> Items, int Selected)
{
    public static readonly RankedPalette Empty = new(Array.Empty<PaletteItem>(), -1);

    public PaletteItem? SelectedItem => Selected >= 0 && Selected < Items.Count ? Items[Selected] : null;
}

/// <summary>
/// Merges every source into one list: an exact app match first, then commands, settings, apps, files,
/// music, and "Search Google" always last. Learning (frecency) nudges rows within and across nearby
/// bands but is capped, so a habit never buries a command the user is typing right now.
/// </summary>
public static class PaletteRanker
{
    public const int MaxRows = 7;

    /// <summary>"spotify" for Spotify (or its exe name): outranks commands.</summary>
    public const int ExactAppMatch = 95;

    /// <summary>A prefix of the app's name ("spot"): good enough for Enter to launch it.</summary>
    public const int ConfidentAppMatch = 85;

    /// <summary>A learned boost this big means the user picked this row for this same text before.</summary>
    public const int LearnedBoost = 100;

    public const int MaxBoost = 150;

    public static int Band(PaletteItem item) => item.Kind switch
    {
        PaletteKind.App when item.Match >= ExactAppMatch => 1000,
        PaletteKind.Command => 900,
        PaletteKind.Setting => 800,
        PaletteKind.App => 700,
        PaletteKind.File => 600,
        PaletteKind.Media => 520,
        PaletteKind.Music => 500,
        _ => 0,
    };

    /// <summary>
    /// Dedupes by key (keeping the best match), ranks, keeps at most <paramref name="max"/> rows with the web
    /// row last, and picks the default: the top row when it is confident or learned, otherwise none.
    /// Equal scores keep the order the sources produced them in, so ranking is deterministic.
    /// </summary>
    public static RankedPalette Rank(IEnumerable<PaletteItem> items, Func<PaletteItem, int>? boost = null, int max = MaxRows)
    {
        var best = new Dictionary<string, (PaletteItem Item, int Order)>();
        int order = 0;
        foreach (var item in items)
        {
            if (!best.TryGetValue(item.Key, out var seen)) best[item.Key] = (item, order++);
            else if (item.Match > seen.Item.Match) best[item.Key] = (item, seen.Order);
        }

        var web = best.Values.Where(b => b.Item.Kind == PaletteKind.Web).OrderBy(b => b.Order).Select(b => b.Item).FirstOrDefault();
        int room = Math.Max(0, max - (web is null ? 0 : 1));
        var boosts = new Dictionary<string, int>();
        var ranked = best.Values
            .Where(b => b.Item.Kind != PaletteKind.Web)
            .Select(b =>
            {
                int extra = Math.Clamp(boost?.Invoke(b.Item) ?? 0, 0, MaxBoost);
                boosts[b.Item.Key] = extra;
                return (b.Item, b.Order, Score: Band(b.Item) + Math.Clamp(b.Item.Match, 0, 100) + extra);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Order)
            .Take(room)
            .Select(r => r.Item)
            .ToList();
        if (web is not null) ranked.Add(web);

        int selected = -1;
        if (ranked.Count > 0 && ranked[0].Kind != PaletteKind.Web &&
            (ranked[0].Confident || boosts.GetValueOrDefault(ranked[0].Key) >= LearnedBoost))
            selected = 0;
        return new RankedPalette(ranked, selected);
    }

    /// <summary>
    /// Late results (files from the index) arrived for the same text. Everything from the top down to the
    /// row the user has moved to stays exactly where it is, so the row under their finger never jumps;
    /// the rest is re-ranked from <paramref name="fresh"/>. The selection follows its key.
    /// </summary>
    public static RankedPalette Stabilize(RankedPalette shown, RankedPalette fresh, int max = MaxRows)
    {
        if (shown.Selected < 0 || shown.Items.Count == 0) return fresh;
        var freshKeys = fresh.Items.Select(i => i.Key).ToHashSet();
        var selectedKey = shown.Items[shown.Selected].Key;

        // The web row isn't pinned in place; it is always last.
        var pinned = shown.Items.Take(shown.Selected + 1).Where(i => i.Kind != PaletteKind.Web).ToList();
        if (pinned.Any(i => !freshKeys.Contains(i.Key))) return fresh;

        var pinnedKeys = pinned.Select(i => i.Key).ToHashSet();
        var web = fresh.Items.FirstOrDefault(i => i.Kind == PaletteKind.Web);
        int room = Math.Max(pinned.Count, max - (web is null ? 0 : 1));
        var merged = pinned
            .Concat(fresh.Items.Where(i => i.Kind != PaletteKind.Web && !pinnedKeys.Contains(i.Key)))
            .Take(room)
            .ToList();
        if (web is not null) merged.Add(web);
        int selected = merged.FindIndex(i => i.Key == selectedKey);
        return new RankedPalette(merged, selected);
    }
}
