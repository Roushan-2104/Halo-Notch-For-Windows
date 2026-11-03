using System.Globalization;

namespace Shammy.Core.Notch;

/// <summary>Ordering and built-in texts for the resting notch's rotating glance.</summary>
public static class GlanceOrder
{
    /// <summary>Highest priority first; equal priorities keep the order they were given in. Nulls (a provider
    /// with nothing to say right now) are dropped.</summary>
    public static List<T> ByPriority<T>(IEnumerable<T?> items, Func<T, int> priority) where T : class =>
        items.OfType<T>()
            .Select((item, index) => (item, index))
            .OrderByDescending(p => priority(p.item))
            .ThenBy(p => p.index)
            .Select(p => p.item)
            .ToList();

    /// <summary>
    /// One item per id, the first one given (Shammy's own items come before the modules', so a module can't
    /// show the song a second time under the built-in's id). Ids compare case-insensitively, as providers do.
    /// </summary>
    public static List<T> Unique<T>(IEnumerable<T?> items, Func<T, string> id) where T : class
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return items.OfType<T>().Where(i => seen.Add(id(i))).ToList();
    }

    /// <summary>
    /// The hover peek's single slot: the first plain item (in priority order) that doesn't repeat what the
    /// peek's left side already shows — the song or the clock, by id, or the same words under another id.
    /// </summary>
    public static T? PeekSlot<T>(IEnumerable<T> ordered, Func<T, (string Id, string Text, bool Plain)> describe,
        IReadOnlyCollection<string> leftIds, string leftText) where T : class
    {
        foreach (var item in ordered)
        {
            var (id, text, plain) = describe(item);
            if (!plain || leftIds.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            if (leftText.Length > 0 && text.Contains(leftText, StringComparison.OrdinalIgnoreCase)) continue;
            return item;
        }
        return null;
    }
}

/// <summary>
/// Steps through the glance items one at a time. Items are asked for fresh each step and may come and go,
/// so it remembers the id it last showed and moves to whatever follows it now; if that one disappeared it
/// starts again from the top (the most important item). It also remembers where that id was, so a list that
/// still repeats an id can't trap it in a loop between the first copy and the item after it.
/// </summary>
public sealed class GlanceRotator
{
    int _index = -1;

    public string? CurrentId { get; private set; }

    /// <returns>The index in <paramref name="ids"/> to show next, or -1 when there is nothing.</returns>
    public int Advance(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            CurrentId = null;
            _index = -1;
            return -1;
        }
        int at = Locate(ids);
        int next = at < 0 ? 0 : (at + 1) % ids.Count;
        CurrentId = ids[next];
        _index = next;
        return next;
    }

    /// <summary>Where the current item is now (to refresh its text without moving on), or -1.</summary>
    public int Locate(IReadOnlyList<string> ids)
    {
        if (CurrentId is null) return -1;
        // The copy at or after where it was last time, else the first one.
        int first = -1;
        for (int i = 0; i < ids.Count; i++)
        {
            if (!string.Equals(ids[i], CurrentId, StringComparison.OrdinalIgnoreCase)) continue;
            if (i >= _index) return _index = i;
            if (first < 0) first = i;
        }
        if (first >= 0) _index = first;
        return first;
    }
}

/// <summary>The glance items Shammy provides itself.</summary>
public static class BuiltInGlances
{
    /// <summary>Battery shows when it's worth a look: below this, or while charging.</summary>
    public const int LowBattery = 30;

    /// <summary>("10:20", "Tue 3"): the time in bold and a short date beside it.</summary>
    public static (string Time, string Date) Time(DateTime local, bool twentyFourHour) =>
        (local.ToString(twentyFourHour ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture),
         local.ToString("ddd d", CultureInfo.InvariantCulture));

    /// <summary>Whether the user's culture writes times on a 24-hour clock.</summary>
    public static bool Uses24Hour(CultureInfo culture) => Cards.ClockFormat.Uses24Hour(culture);

    /// <summary>"Charging 64%" or "28% battery"; null when the battery isn't worth mentioning.</summary>
    public static string? Battery(bool hasBattery, int percent, bool charging)
    {
        if (!hasBattery) return null;
        if (charging) return $"Charging {percent}%";
        return percent < LowBattery ? $"{percent}% battery" : null;
    }
}
