using System.Globalization;
using System.Text;

namespace Shammy.Core.Calendar;

/// <summary>How an iCalendar date-time is anchored (RFC 5545 §3.3.5): UTC, a named zone, the viewer's zone, or a whole day.</summary>
public enum IcsTimeKind { Utc, Zoned, Floating, Date }

/// <summary>A DTSTART/DTEND/EXDATE/RECURRENCE-ID value as written: wall-clock (or UTC) time plus how to read it.</summary>
public readonly record struct IcsTime(DateTime Value, IcsTimeKind Kind, string? TzId = null)
{
    public bool IsDate => Kind == IcsTimeKind.Date;

    /// <summary>The zone the wall-clock value is in (the event's TZID, else the viewer's zone).</summary>
    public TimeZoneInfo Zone(TimeZoneInfo local) =>
        Kind == IcsTimeKind.Utc ? TimeZoneInfo.Utc
        : Kind == IcsTimeKind.Zoned && IcsZones.Find(TzId) is { } z ? z
        : local;

    /// <summary>The instant this value names; a whole day starts at midnight in <paramref name="local"/>.</summary>
    public DateTime ToUtc(TimeZoneInfo local) =>
        Kind == IcsTimeKind.Utc ? DateTime.SpecifyKind(Value, DateTimeKind.Utc) : IcsZones.ToUtc(Value, Zone(local));
}

/// <summary>One VEVENT, as written (a recurring one is expanded later by <see cref="CalendarExpander"/>).</summary>
public sealed class IcsEvent
{
    public string Uid { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>X-GOOGLE-CONFERENCE: the Meet link Google attaches to an event.</summary>
    public string Conference { get; set; } = "";
    public string Status { get; set; } = "";
    public IcsTime? Start { get; set; }
    public IcsTime? End { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? RRule { get; set; }
    public List<IcsTime> ExDates { get; } = new();
    public IcsTime? RecurrenceId { get; set; }

    public bool Cancelled => Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A small RFC 5545 reader: enough for Google Calendar's "secret address in iCal format" feed. Unfolds lines,
/// unescapes text, reads VEVENTs (and ignores VTIMEZONE/VALARM, since zones come from the TZID names).
/// </summary>
public static class IcsParser
{
    public static List<IcsEvent> Parse(string ics)
    {
        var events = new List<IcsEvent>();
        IcsEvent? current = null;
        int depth = 0; // nesting inside the VEVENT (a VALARM's properties mustn't overwrite the event's)
        foreach (var line in Unfold(ics))
        {
            if (!TrySplit(line, out var name, out var parameters, out var value)) continue;
            if (name == "BEGIN")
            {
                if (current is not null) depth++;
                else if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) current = new IcsEvent();
                continue;
            }
            if (name == "END")
            {
                if (current is null) continue;
                if (depth > 0) depth--;
                else
                {
                    if (current.Start is not null) events.Add(current);
                    current = null;
                }
                continue;
            }
            if (current is null || depth > 0) continue;
            Apply(current, name, parameters, value);
        }
        return events;
    }

    static void Apply(IcsEvent e, string name, Dictionary<string, string> p, string value)
    {
        switch (name)
        {
            case "UID": e.Uid = value.Trim(); break;
            case "SUMMARY": e.Summary = Unescape(value).Trim(); break;
            case "LOCATION": e.Location = Unescape(value).Trim(); break;
            case "DESCRIPTION": e.Description = Unescape(value); break;
            case "X-GOOGLE-CONFERENCE": e.Conference = Unescape(value).Trim(); break;
            case "STATUS": e.Status = value.Trim(); break;
            case "DTSTART": e.Start = ParseTime(value, p); break;
            case "DTEND": e.End = ParseTime(value, p); break;
            case "DURATION": e.Duration = ParseDuration(value); break;
            case "RRULE": e.RRule = value.Trim(); break;
            case "RECURRENCE-ID": e.RecurrenceId = ParseTime(value, p); break;
            case "EXDATE":
                foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (ParseTime(part, p) is { } t) e.ExDates.Add(t);
                break;
        }
    }

    /// <summary>Joins folded lines: a line starting with a space or tab continues the previous one (§3.1).</summary>
    public static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        bool any = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                sb.Append(line, 1, line.Length - 1);
                continue;
            }
            if (any) yield return sb.ToString();
            sb.Clear();
            sb.Append(line);
            any = true;
        }
        if (any && sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>"DTSTART;TZID=Asia/Kolkata:20261103T103000" → name, parameters, value. Colons inside quoted parameters don't split.</summary>
    public static bool TrySplit(string line, out string name, out Dictionary<string, string> parameters, out string value)
    {
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        name = value = "";
        int colon = -1;
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon <= 0) return false;
        value = line[(colon + 1)..];
        var head = line[..colon].Split(';');
        name = head[0].Trim().ToUpperInvariant();
        foreach (var param in head.Skip(1))
        {
            int eq = param.IndexOf('=');
            if (eq > 0) parameters[param[..eq].Trim()] = param[(eq + 1)..].Trim().Trim('"');
        }
        return name.Length > 0;
    }

    /// <summary>TEXT unescaping (§3.3.11): \n, \, \; and \\.</summary>
    public static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char n = value[++i];
                sb.Append(n is 'n' or 'N' ? '\n' : n);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static IcsTime? ParseTime(string value, IReadOnlyDictionary<string, string> parameters)
    {
        value = value.Trim();
        bool date = parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase)
                    || value.Length == 8;
        if (date)
        {
            return DateTime.TryParseExact(value.Length >= 8 ? value[..8] : value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)
                ? new IcsTime(d, IcsTimeKind.Date)
                : null;
        }
        bool utc = value.EndsWith('Z') || value.EndsWith('z');
        var body = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(body, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t))
            return null;
        if (utc) return new IcsTime(DateTime.SpecifyKind(t, DateTimeKind.Utc), IcsTimeKind.Utc);
        if (parameters.TryGetValue("TZID", out var tz) && tz.Length > 0) return new IcsTime(t, IcsTimeKind.Zoned, tz);
        return new IcsTime(t, IcsTimeKind.Floating);
    }

    /// <summary>"PT1H30M", "P1D", "-PT15M", "P2W" (§3.3.6).</summary>
    public static TimeSpan? ParseDuration(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length < 2) return null;
        int sign = 1, i = 0;
        if (value[0] is '+' or '-')
        {
            sign = value[0] == '-' ? -1 : 1;
            i = 1;
        }
        if (i >= value.Length || value[i] != 'P') return null;
        i++;
        var total = TimeSpan.Zero;
        bool time = false, any = false;
        int number = 0;
        bool digits = false;
        for (; i < value.Length; i++)
        {
            char c = value[i];
            if (c == 'T')
            {
                time = true;
                continue;
            }
            if (char.IsDigit(c))
            {
                number = number * 10 + (c - '0');
                digits = true;
                continue;
            }
            if (!digits) return null;
            total += c switch
            {
                'W' when !time => TimeSpan.FromDays(7 * number),
                'D' when !time => TimeSpan.FromDays(number),
                'H' when time => TimeSpan.FromHours(number),
                'M' when time => TimeSpan.FromMinutes(number),
                'S' when time => TimeSpan.FromSeconds(number),
                _ => TimeSpan.MinValue,
            };
            if (total < TimeSpan.Zero) return null;
            number = 0;
            digits = false;
            any = true;
        }
        return any && !digits ? sign * total : null;
    }
}

/// <summary>
/// TZID names to Windows zones. Google writes IANA names ("Asia/Kolkata"); Outlook writes Windows names.
/// .NET 8 on Windows resolves both through ICU; the small table covers machines where that isn't available.
/// </summary>
public static class IcsZones
{
    static readonly Dictionary<string, TimeZoneInfo?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();

    static readonly Dictionary<string, string> Fallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Kolkata"] = "India Standard Time",
        ["Asia/Calcutta"] = "India Standard Time",
        ["Asia/Dubai"] = "Arabian Standard Time",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Asia/Shanghai"] = "China Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Europe/Amsterdam"] = "W. Europe Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["UTC"] = "UTC",
        ["Etc/UTC"] = "UTC",
        ["Etc/GMT"] = "UTC",
        ["GMT"] = "UTC",
    };

    /// <summary>The zone for a TZID, or null when it isn't known (callers then use the viewer's zone).</summary>
    public static TimeZoneInfo? Find(string? tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid)) return null;
        tzid = tzid.Trim().Trim('"');
        lock (Gate)
        {
            if (Cache.TryGetValue(tzid, out var cached)) return cached;
            var zone = Resolve(tzid);
            Cache[tzid] = zone;
            return zone;
        }
    }

    static TimeZoneInfo? Resolve(string tzid)
    {
        // Some exporters prefix a namespace: "/mozilla.org/20050126_1/Europe/Berlin".
        var candidates = new List<string> { tzid };
        int slash = tzid.LastIndexOf('/');
        if (tzid.StartsWith('/') && slash > 0)
        {
            var parts = tzid.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) candidates.Add($"{parts[^2]}/{parts[^1]}");
        }
        foreach (var id in candidates)
        {
            if (TryFind(id) is { } z) return z;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) && TryFind(win) is { } w) return w;
            if (Fallback.TryGetValue(id, out var fb) && TryFind(fb) is { } f) return f;
        }
        return null;
    }

    static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Wall-clock time in a zone to UTC. A time skipped by a spring-forward jump moves forward by the gap
    /// (RFC 5545 §3.3.5); a repeated autumn hour means its first (daylight) occurrence.
    /// </summary>
    public static DateTime ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall))
        {
            var before = zone.GetUtcOffset(wall.AddHours(-3));
            return DateTime.SpecifyKind(wall - before, DateTimeKind.Utc);
        }
        if (zone.IsAmbiguousTime(wall))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wall);
            return DateTime.SpecifyKind(wall - offsets.Max(), DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }
}
