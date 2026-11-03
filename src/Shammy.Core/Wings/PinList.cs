using System.Text;
using System.Text.RegularExpressions;

namespace Shammy.Core.Wings;

/// <summary>Edits to the pinned list. Every operation keeps one pin per app (by <see cref="PinnedApp.Key"/>).</summary>
public static class PinList
{
    /// <summary>Appends the candidates that aren't pinned yet (or repeated among themselves); returns how many were added.</summary>
    public static int Import(List<PinnedApp> pins, IEnumerable<PinnedApp> candidates)
    {
        var seen = pins.Select(p => p.Key).ToHashSet();
        int added = 0;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.Launch) || !seen.Add(c.Key)) continue;
            // The same app reached through an exe and through its app ID is still one pin.
            if (pins.Any(p => SameApp(p, c))) continue;
            pins.Add(c);
            added++;
        }
        return added;
    }

    /// <summary>True when the two pins would claim the same windows.</summary>
    public static bool SameApp(PinnedApp a, PinnedApp b)
    {
        if (!string.IsNullOrEmpty(a.Aumid) && !string.IsNullOrEmpty(b.Aumid))
            return string.Equals(a.Aumid, b.Aumid, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(a.ExePath) && !string.IsNullOrEmpty(b.ExePath))
            return string.Equals(a.ExePath, b.ExePath, StringComparison.OrdinalIgnoreCase);
        return string.Equals(a.Launch, b.Launch, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pins at the end unless it's already pinned. Returns false when nothing changed.</summary>
    public static bool Pin(List<PinnedApp> pins, PinnedApp app) => Import(pins, new[] { app }) > 0;

    public static PinnedApp? Unpin(List<PinnedApp> pins, string key)
    {
        int i = pins.FindIndex(p => p.Key == key);
        if (i < 0) return null;
        var removed = pins[i];
        pins.RemoveAt(i);
        return removed;
    }

    /// <summary>Drag-to-reorder: the pin at <paramref name="from"/> ends up at index <paramref name="to"/> (clamped).</summary>
    public static bool Move(List<PinnedApp> pins, int from, int to)
    {
        if (from < 0 || from >= pins.Count) return false;
        to = Math.Clamp(to, 0, pins.Count - 1);
        if (from == to) return false;
        var p = pins[from];
        pins.RemoveAt(from);
        pins.Insert(to, p);
        return true;
    }
}

/// <summary>
/// Reads the taskbar's own pin list (HKCU\...\Explorer\Taskband "Favorites"), an undocumented blob of shell
/// item IDs. It isn't parsed; it's only searched for the two things that identify pins in it: the names of
/// the pinned shortcuts (for their order) and Store app IDs, which have no shortcut file at all.
/// </summary>
public static class TaskbandPins
{
    // PackageFamilyName (name_publisherId, the id is 13 base-32 characters) + "!" + application id.
    static readonly Regex PackagedAumid = new(@"[A-Za-z0-9][A-Za-z0-9.\-]{1,100}_[a-z0-9]{13}![A-Za-z][A-Za-z0-9.\-_]{0,100}", RegexOptions.CultureInvariant);

    /// <summary>The blob as UTF-16 text read from both even and odd offsets (strings in it aren't always aligned).</summary>
    static string[] Texts(byte[] blob) =>
        blob.Length < 2 ? Array.Empty<string>() : new[] { Encoding.Unicode.GetString(blob), Encoding.Unicode.GetString(blob, 1, blob.Length - 1) };

    /// <summary>Byte offset of the first mention of <paramref name="text"/>, or -1.</summary>
    static long Position(string[] texts, string text)
    {
        long best = -1;
        for (int k = 0; k < texts.Length; k++)
        {
            int i = texts[k].IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            long at = i * 2L + k;
            if (best < 0 || at < best) best = at;
        }
        return best;
    }

    /// <summary>
    /// Shortcut file names (and Store app IDs) in taskbar order. Anything the blob doesn't mention keeps its
    /// given order after the ones it does.
    /// </summary>
    public static List<string> Order(byte[]? blob, IReadOnlyList<string> names)
    {
        var texts = Texts(blob ?? Array.Empty<byte>());
        return names
            .Select((n, i) => (n, i, at: texts.Length == 0 ? -1 : Position(texts, n)))
            .OrderBy(x => x.at < 0 ? 1 : 0)
            .ThenBy(x => x.at)
            .ThenBy(x => x.i)
            .Select(x => x.n)
            .ToList();
    }

    /// <summary>Store app IDs pinned to the taskbar, in the order they first appear, without repeats.</summary>
    public static List<string> PackagedAppIds(byte[]? blob)
    {
        var found = new List<(string Id, long At)>();
        var texts = Texts(blob ?? Array.Empty<byte>());
        for (int k = 0; k < texts.Length; k++)
            foreach (Match m in PackagedAumid.Matches(texts[k]))
                found.Add((m.Value, m.Index * 2L + k));
        return found.OrderBy(f => f.At)
            .Select(f => f.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" → "Windows Calculator"-ish, when the shell can't name it.</summary>
    public static string FallbackName(string aumid)
    {
        var family = aumid.Split('!')[0];
        var name = family.Split('_')[0];
        var last = name.Split('.').Last();
        return Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
