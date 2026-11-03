using System.Globalization;
using System.Text.RegularExpressions;

namespace Shammy.Core.Music;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Parses LRC lyrics ("[01:02.34]words") into time-sorted lines.</summary>
public static partial class Lrc
{
    // [mm:ss], [mm:ss.x], [mm:ss.xx], [mm:ss.xxx] and the [mm:ss:xx] variant some editors write.
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    // Enhanced LRC puts per-word times inline ("<00:01.20>word"); we only sync whole lines.
    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordTag();

    public static List<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return lines;

        double offsetMs = 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var offset = OffsetTag().Match(line);
            if (offset.Success)
            {
                offsetMs = double.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            // Time tags must lead the line; one line can carry several (a repeated chorus).
            var times = new List<TimeSpan>();
            int pos = 0;
            while (pos < line.Length)
            {
                var m = TimeTag().Match(line, pos);
                if (!m.Success || m.Index != pos) break;
                times.Add(ToTime(m));
                pos = m.Index + m.Length;
            }
            if (times.Count == 0) continue; // metadata such as [ar:...] or stray text

            var text = WordTag().Replace(line[pos..], "").Trim();
            foreach (var t in times) lines.Add(new LyricLine(t, text));
        }

        // A positive offset means the lyrics should appear sooner.
        if (offsetMs != 0)
        {
            var shift = TimeSpan.FromMilliseconds(offsetMs);
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Time - shift;
                lines[i] = lines[i] with { Time = t < TimeSpan.Zero ? TimeSpan.Zero : t };
            }
        }

        // Stable sort so lines sharing a timestamp keep their written order.
        return lines.Select((l, i) => (l, i)).OrderBy(x => x.l.Time).ThenBy(x => x.i).Select(x => x.l).ToList();
    }

    static TimeSpan ToTime(Match m)
    {
        int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double frac = 0;
        if (m.Groups[3].Success)
        {
            var f = m.Groups[3].Value;
            frac = int.Parse(f, CultureInfo.InvariantCulture) / Math.Pow(10, f.Length);
        }
        return TimeSpan.FromSeconds(min * 60 + sec + frac);
    }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}
