using System.Globalization;
using System.Text.RegularExpressions;

namespace Halo.Core.Conversions;

/// <summary>A place or abbreviation people say ("tokyo", "ist") and the Windows time zone behind it.</summary>
public sealed record Zone(string Label, string WindowsId)
{
    public TimeZoneInfo Info => TimeZoneInfo.FindSystemTimeZoneById(WindowsId);
}

public abstract record TimeQuery;

/// <summary>"time in tokyo", "what time is it in london".</summary>
public sealed record ClockQuery(Zone Zone) : TimeQuery;

/// <summary>"5pm ist in pst", "3:30 pm est to ist", "noon in london" (no source zone = local time).</summary>
public sealed record ClockConversionQuery(int Hour, int Minute, Zone? From, Zone To) : TimeQuery;

public sealed record TimeAnswer(string Title, string Detail);

/// <summary>
/// World clock and time-zone conversion over Windows time zones, so daylight saving comes from the OS.
/// Abbreviations name the region, not the exact offset: "5pm EST" in July means 5pm New York clock time.
/// </summary>
public static class WorldClock
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, Zone> Zones = BuildZones();

    static readonly Regex ClockLead = new(
        @"^(?:what time is it|what(?:'s| is|s) the (?:current |local )?time|(?:the )?current time|(?:the )?local time|time now|time right now|(?:the )?time) (?:in|at) (?<z>.+?)(?: (?:now|right now))?$");
    static readonly Regex ClockTail = new(@"^(?:current |local )?(?<z>[a-z][a-z .'-]*?) time(?: now| right now)?$");
    static readonly Regex TimeAndZone = new(
        @"^(?:(?<h>\d{1,2})(?::(?<m>\d{2}))? ?(?<ap>a\.?m\.?|p\.?m\.?)?|(?<word>noon|midnight))(?: (?<z>[a-z][a-z .'-]*?))?(?: time)?$");

    static Dictionary<string, Zone> BuildZones()
    {
        var map = new Dictionary<string, Zone>(StringComparer.Ordinal);
        void Abbr(string id, params string[] names)
        {
            foreach (var n in names) map[n] = new Zone(n.ToUpperInvariant(), id);
        }
        void City(string id, params string[] names)
        {
            foreach (var n in names) map[n] = new Zone(Inv.TextInfo.ToTitleCase(n), id);
        }

        Abbr("India Standard Time", "ist");
        Abbr("Pacific Standard Time", "pst", "pdt", "pt");
        Abbr("Mountain Standard Time", "mst", "mdt", "mt");
        Abbr("Central Standard Time", "cst", "cdt", "ct");
        Abbr("Eastern Standard Time", "est", "edt", "et");
        Abbr("Alaskan Standard Time", "akst", "akdt");
        Abbr("Hawaiian Standard Time", "hst");
        Abbr("Atlantic Standard Time", "ast", "adt");
        Abbr("Newfoundland Standard Time", "nst", "ndt");
        Abbr("UTC", "utc", "gmt");
        Abbr("GMT Standard Time", "bst");
        Abbr("W. Europe Standard Time", "cet", "cest");
        Abbr("GTB Standard Time", "eet", "eest");
        Abbr("Russian Standard Time", "msk");
        Abbr("Arabian Standard Time", "gst");
        Abbr("Pakistan Standard Time", "pkt");
        Abbr("Nepal Standard Time", "npt");
        Abbr("SE Asia Standard Time", "ict", "wib");
        Abbr("Singapore Standard Time", "sgt", "myt", "pht");
        Abbr("China Standard Time", "hkt");
        Abbr("Tokyo Standard Time", "jst");
        Abbr("Korea Standard Time", "kst");
        Abbr("W. Australia Standard Time", "awst");
        Abbr("Cen. Australia Standard Time", "acst", "acdt");
        Abbr("AUS Eastern Standard Time", "aest", "aedt");
        Abbr("New Zealand Standard Time", "nzst", "nzdt");
        Abbr("South Africa Standard Time", "sast");
        Abbr("E. South America Standard Time", "brt");

        City("Eastern Standard Time", "new york", "nyc", "boston", "washington", "washington dc", "miami", "atlanta", "toronto", "montreal",
            "philadelphia", "detroit", "florida");
        City("Central Standard Time", "chicago", "dallas", "houston", "austin", "texas");
        City("Central Standard Time (Mexico)", "mexico city", "mexico");
        City("Mountain Standard Time", "denver", "calgary");
        City("US Mountain Standard Time", "phoenix", "arizona");
        City("Pacific Standard Time", "los angeles", "la", "san francisco", "sf", "seattle", "vancouver", "las vegas", "san diego",
            "silicon valley", "california");
        City("Alaskan Standard Time", "anchorage", "alaska");
        City("Hawaiian Standard Time", "honolulu", "hawaii");
        City("GMT Standard Time", "london", "uk", "england", "britain", "dublin", "ireland", "lisbon", "portugal", "edinburgh");
        City("Greenwich Standard Time", "reykjavik", "iceland");
        City("W. Europe Standard Time", "berlin", "germany", "amsterdam", "netherlands", "rome", "italy", "vienna", "austria",
            "stockholm", "sweden", "oslo", "norway", "zurich", "geneva", "switzerland", "munich", "frankfurt", "milan");
        City("Romance Standard Time", "paris", "france", "madrid", "spain", "barcelona", "brussels", "belgium", "copenhagen", "denmark");
        City("Central Europe Standard Time", "prague", "budapest", "belgrade");
        City("Central European Standard Time", "warsaw", "poland");
        City("GTB Standard Time", "athens", "greece", "bucharest", "romania");
        City("FLE Standard Time", "helsinki", "finland", "kyiv", "kiev", "ukraine");
        City("Turkey Standard Time", "istanbul", "turkey", "ankara");
        City("Israel Standard Time", "jerusalem", "tel aviv", "israel");
        City("Egypt Standard Time", "cairo", "egypt");
        City("South Africa Standard Time", "johannesburg", "cape town", "south africa");
        City("E. Africa Standard Time", "nairobi", "kenya");
        City("W. Central Africa Standard Time", "lagos", "nigeria");
        City("Russian Standard Time", "moscow", "russia");
        City("Arabian Standard Time", "dubai", "abu dhabi", "uae");
        City("Arab Standard Time", "riyadh", "saudi arabia", "doha", "qatar", "kuwait");
        City("Iran Standard Time", "tehran", "iran");
        City("Pakistan Standard Time", "karachi", "lahore", "islamabad", "pakistan");
        City("India Standard Time", "india", "delhi", "new delhi", "mumbai", "bombay", "bangalore", "bengaluru", "chennai", "kolkata",
            "hyderabad", "pune", "ahmedabad", "jaipur", "noida", "gurgaon", "gurugram");
        City("Sri Lanka Standard Time", "colombo", "sri lanka");
        City("Nepal Standard Time", "kathmandu", "nepal");
        City("Bangladesh Standard Time", "dhaka", "bangladesh");
        City("SE Asia Standard Time", "bangkok", "thailand", "jakarta", "indonesia", "hanoi", "ho chi minh city", "vietnam");
        City("Singapore Standard Time", "singapore", "kuala lumpur", "malaysia", "manila", "philippines");
        City("China Standard Time", "beijing", "shanghai", "shenzhen", "china", "hong kong");
        City("Taipei Standard Time", "taipei", "taiwan");
        City("Korea Standard Time", "seoul", "south korea", "korea");
        City("Tokyo Standard Time", "tokyo", "osaka", "kyoto", "japan");
        City("AUS Eastern Standard Time", "sydney", "melbourne", "canberra", "australia");
        City("E. Australia Standard Time", "brisbane");
        City("Cen. Australia Standard Time", "adelaide");
        City("W. Australia Standard Time", "perth");
        City("New Zealand Standard Time", "auckland", "wellington", "new zealand");
        City("E. South America Standard Time", "sao paulo", "rio", "rio de janeiro", "brazil");
        City("Argentina Standard Time", "buenos aires", "argentina");
        City("SA Pacific Standard Time", "bogota", "colombia", "lima", "peru");
        City("Pacific SA Standard Time", "santiago", "chile");

        // Short names read better upper-cased.
        foreach (var k in new[] { "nyc", "la", "sf", "uk", "uae" }) map[k] = map[k] with { Label = k.ToUpperInvariant() };
        map["washington dc"] = map["washington dc"] with { Label = "Washington DC" };
        return map;
    }

    /// <summary>Every name the clock understands, for tests and diagnostics.</summary>
    public static IReadOnlyDictionary<string, Zone> KnownZones => Zones;

    public static bool TryFindZone(string text, out Zone zone)
    {
        var key = Phrase.Normalize(text).Replace(".", "");
        if (key.StartsWith("the ")) key = key[4..];
        return Zones.TryGetValue(key, out zone!);
    }

    /// <summary>"time in tokyo", "what time is it in london", "tokyo time", "5pm ist in pst", "3:30 pm est to ist".</summary>
    public static bool TryParse(string text, out TimeQuery query)
    {
        query = null!;
        var s = Phrase.Normalize(text);
        if (s.Length == 0 || s.Length > 60) return false;
        if (s.StartsWith("convert ")) s = s[8..];

        var m = ClockLead.Match(s);
        if (!m.Success) m = ClockTail.Match(s);
        if (m.Success && TryFindZone(m.Groups["z"].Value, out var here))
        {
            query = new ClockQuery(here);
            return true;
        }

        foreach (var (left, right) in Phrase.Splits(s, "in", "to", "into"))
        {
            var t = TimeAndZone.Match(left);
            if (!t.Success || !TryFindZone(right.EndsWith(" time") ? right[..^5] : right, out var to)) continue;
            Zone? from = null;
            if (t.Groups["z"].Success && !TryFindZone(t.Groups["z"].Value, out from!)) continue;
            if (!TryReadTime(t, out int hour, out int minute)) continue;
            // A bare number with no zone on the left ("5 in tokyo") is too vague to be a time.
            bool clearlyTime = t.Groups["ap"].Success || t.Groups["m"].Success || t.Groups["word"].Success || from is not null;
            if (!clearlyTime) continue;
            query = new ClockConversionQuery(hour, minute, from, to);
            return true;
        }
        return false;
    }

    static bool TryReadTime(Match t, out int hour, out int minute)
    {
        hour = minute = 0;
        if (t.Groups["word"].Success)
        {
            hour = t.Groups["word"].Value == "noon" ? 12 : 0;
            return true;
        }
        hour = int.Parse(t.Groups["h"].Value, Inv);
        minute = t.Groups["m"].Success ? int.Parse(t.Groups["m"].Value, Inv) : 0;
        if (minute > 59) return false;
        if (t.Groups["ap"].Success)
        {
            if (hour is < 1 or > 12) return false;
            bool pm = t.Groups["ap"].Value.StartsWith('p');
            hour = hour % 12 + (pm ? 12 : 0);
        }
        return hour <= 23;
    }

    public static TimeAnswer Answer(TimeQuery query, DateTime utcNow, TimeZoneInfo local)
    {
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        switch (query)
        {
            case ClockQuery c:
            {
                var tz = c.Zone.Info;
                var there = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                var diff = tz.GetUtcOffset(utcNow) - local.GetUtcOffset(utcNow);
                return new TimeAnswer(Clock(there),
                    $"{c.Zone.Label} · {there.ToString("ddd d MMM", Inv)} · {Offset(tz.GetUtcOffset(utcNow))} · {Relative(diff)}");
            }
            case ClockConversionQuery q:
            {
                var fromTz = q.From?.Info ?? local;
                var toTz = q.To.Info;
                var day = TimeZoneInfo.ConvertTimeFromUtc(utcNow, fromTz).Date;
                var source = DateTime.SpecifyKind(day.AddHours(q.Hour).AddMinutes(q.Minute), DateTimeKind.Unspecified);
                if (fromTz.IsInvalidTime(source)) source = source.AddHours(1); // skipped by a DST jump
                var target = TimeZoneInfo.ConvertTime(source, fromTz, toTz);
                int days = (target.Date - source.Date).Days;
                var fromLabel = q.From?.Label ?? "your time";
                var dayNote = days switch
                {
                    > 0 => $" · next day in {q.To.Label}",
                    < 0 => $" · previous day in {q.To.Label}",
                    _ => "",
                };
                return new TimeAnswer($"{Clock(target)} {q.To.Label}", $"{Clock(source)} {fromLabel}{dayNote}");
            }
            default:
                throw new ArgumentException("Unknown time query", nameof(query));
        }
    }

    static string Clock(DateTime t) => t.ToString("h:mm tt", Inv);

    static string Offset(TimeSpan o)
    {
        var sign = o < TimeSpan.Zero ? "-" : "+";
        o = o.Duration();
        return o.Minutes == 0 ? $"UTC{sign}{o.Hours}" : $"UTC{sign}{o.Hours}:{o.Minutes:00}";
    }

    static string Relative(TimeSpan diff)
    {
        if (diff == TimeSpan.Zero) return "same as you";
        var d = diff.Duration();
        var text = d.Minutes == 0 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalHours}h {d.Minutes}m";
        return diff > TimeSpan.Zero ? $"{text} ahead" : $"{text} behind";
    }
}
