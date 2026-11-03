using Shammy.Core.Launcher;

namespace Shammy.Core.Palette;

/// <summary>
/// What the user picks in the palette, so repeated picks rise: a use count that halves every
/// <see cref="HalfLife"/>, plus the last few texts it was picked for. Picking Spotify after typing "s"
/// makes "s" mean Spotify next time; picking it often lifts it for any text that matches it.
/// </summary>
public sealed class FrecencyStore
{
    public const int MaxEntries = 400;
    public const int QueriesPerEntry = 5;
    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(10);

    /// <summary>One remembered row. Public setters so it round-trips through System.Text.Json.</summary>
    public sealed class Entry
    {
        public string Key { get; set; } = "";
        public double Score { get; set; }
        public DateTime LastUsed { get; set; }
        public List<string> Queries { get; set; } = new();
    }

    readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public FrecencyStore(IEnumerable<Entry>? entries = null)
    {
        foreach (var e in entries ?? Array.Empty<Entry>())
            if (!string.IsNullOrEmpty(e.Key)) _entries[e.Key] = e;
    }

    public IReadOnlyCollection<Entry> Entries => _entries.Values;

    public void Record(string key, string query, DateTime now)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry { Key = key };
        e.Score = Decayed(e, now) + 1;
        e.LastUsed = now;
        var q = Normalize(query);
        if (q.Length > 0)
        {
            e.Queries.Remove(q);
            e.Queries.Insert(0, q);
            if (e.Queries.Count > QueriesPerEntry) e.Queries.RemoveRange(QueriesPerEntry, e.Queries.Count - QueriesPerEntry);
        }
        if (_entries.Count > MaxEntries)
        {
            var weakest = _entries.Values.Where(x => x.Key != key).OrderBy(x => Decayed(x, now)).First();
            _entries.Remove(weakest.Key);
        }
    }

    /// <summary>
    /// 0–<see cref="PaletteRanker.MaxBoost"/>. Habit alone tops out at 70; the same text picked before adds
    /// 80 (so it reaches <see cref="PaletteRanker.LearnedBoost"/>), a longer or shorter version of it adds 40.
    /// </summary>
    public int Boost(string key, string query, DateTime now)
    {
        if (!_entries.TryGetValue(key, out var e)) return 0;
        double used = Decayed(e, now);
        int habit = (int)Math.Min(70, 40 * Math.Log2(1 + used));
        var q = Normalize(query);
        int bonus = 0;
        if (q.Length > 0 && used > 0.05)
        {
            if (e.Queries.Contains(q)) bonus = 80;
            else if (q.Length >= 2 && e.Queries.Any(s => s.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(s, StringComparison.Ordinal)))
                bonus = 40;
        }
        return Math.Min(PaletteRanker.MaxBoost, habit + bonus);
    }

    /// <summary>
    /// The strongest picks right now (decayed score, then most recent), for the empty palette's suggestions.
    /// <paramref name="include"/> filters keys out (rows that can't be rebuilt, or private ones).
    /// </summary>
    public IReadOnlyList<Entry> Top(int count, DateTime now, Func<string, bool>? include = null) =>
        _entries.Values
            .Where(e => include?.Invoke(e.Key) ?? true)
            .Where(e => Decayed(e, now) > 0.05)
            .OrderByDescending(e => Decayed(e, now))
            .ThenByDescending(e => e.LastUsed)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, count))
            .ToList();

    static double Decayed(Entry e, DateTime now)
    {
        var age = now - e.LastUsed;
        if (age <= TimeSpan.Zero) return e.Score;
        return e.Score * Math.Pow(0.5, age.TotalDays / HalfLife.TotalDays);
    }

    public static string Normalize(string query) => AppMatcher.Normalize(query ?? "");
}
