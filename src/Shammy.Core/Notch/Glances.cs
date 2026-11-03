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
}

/// <summary>
/// Steps through the glance items one at a time. Items are asked for fresh each step and may come and go,
/// so it remembers the id it last showed and moves to whatever follows it now; if that one disappeared it
/// starts again from the top (the most important item).
/// </summary>
public sealed class GlanceRotator
{
    public string? CurrentId { get; private set; }

    /// <returns>The index in <paramref name="ids"/> to show next, or -1 when there is nothing.</returns>
    public int Advance(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            CurrentId = null;
            return -1;
        }
        int at = CurrentId is null ? -1 : IndexOf(ids, CurrentId);
        int next = at < 0 ? 0 : (at + 1) % ids.Count;
        CurrentId = ids[next];
        return next;
    }

    /// <summary>Where the current item is now (to refresh its text without moving on), or -1.</summary>
    public int Locate(IReadOnlyList<string> ids) => CurrentId is null ? -1 : IndexOf(ids, CurrentId);

    static int IndexOf(IReadOnlyList<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
            if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
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
    public static bool Uses24Hour(CultureInfo culture) => culture.DateTimeFormat.ShortTimePattern.Contains('H');

    /// <summary>"Charging 64%" or "28% battery"; null when the battery isn't worth mentioning.</summary>
    public static string? Battery(bool hasBattery, int percent, bool charging)
    {
        if (!hasBattery) return null;
        if (charging) return $"Charging {percent}%";
        return percent < LowBattery ? $"{percent}% battery" : null;
    }
}
