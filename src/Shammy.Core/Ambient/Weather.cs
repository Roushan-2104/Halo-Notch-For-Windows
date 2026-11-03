using System.Globalization;
using System.Text.Json;

namespace Shammy.Core.Ambient;

/// <summary>An approximate position from IP geolocation. Cached for a day because it rarely changes.</summary>
public sealed record GeoLocation(string City, double Latitude, double Longitude, DateTime FetchedUtc)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    public bool IsFresh(DateTime nowUtc) => nowUtc >= FetchedUtc && nowUtc - FetchedUtc < MaxAge;
}

/// <summary>Free, keyless IP geolocation. Both are tried in order; either may rate-limit or be down.</summary>
public static class IpLocation
{
    public const string PrimaryUrl = "https://ipapi.co/json/";
    // HTTPS only: a plain-HTTP lookup would broadcast (and let anyone on the network rewrite) your location.
    public const string FallbackUrl = "https://ipwho.is/?fields=success,city,latitude,longitude";

    /// <summary>ipwho.is: {"success":true,"city":..,"latitude":..,"longitude":..}.</summary>
    public static GeoLocation? ParseIpWhoIs(string json, DateTime nowUtc) =>
        Parse(json, nowUtc, "latitude", "longitude", root => root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True);

    /// <summary>ipapi.co: {"city":..,"latitude":..,"longitude":..} or {"error":true,"reason":..}.</summary>
    public static GeoLocation? ParseIpApiCo(string json, DateTime nowUtc) =>
        Parse(json, nowUtc, "latitude", "longitude", root => !(root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True));


    static GeoLocation? Parse(string json, DateTime nowUtc, string latKey, string lonKey, Func<JsonElement, bool> ok)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !ok(root)) return null;
            if (AmbientJson.Num(root, latKey) is not { } lat || AmbientJson.Num(root, lonKey) is not { } lon) return null;
            if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;
            var city = AmbientJson.Str(root, "city");
            return new GeoLocation(string.IsNullOrWhiteSpace(city) ? "Your area" : city.Trim(), lat, lon, nowUtc);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record HourForecast(DateTime Time, double Temperature, int? RainChance, int Code);

/// <summary>One day of the forecast (local date at the forecast location).</summary>
public sealed record DayForecast(DateTime Date, double High, double Low, int Code, int? RainChance);

/// <summary>One Open-Meteo forecast. Times are local to the forecast location.</summary>
public sealed record WeatherReport(
    DateTime ObservedLocal,
    int UtcOffsetSeconds,
    double Temperature,
    double FeelsLike,
    int Code,
    bool IsDay,
    double TodayHigh,
    double TodayLow,
    double? TomorrowHigh,
    double? TomorrowLow,
    int? TomorrowCode,
    int? TomorrowRainChance,
    IReadOnlyList<HourForecast> Hours)
{
    /// <summary>Relative humidity now (%), when the forecast has it.</summary>
    public int? Humidity { get; init; }

    /// <summary>Wind speed now (km/h), when the forecast has it.</summary>
    public double? WindKmh { get; init; }

    /// <summary>Today first, then the following days the forecast covers.</summary>
    public IReadOnlyList<DayForecast> Days { get; init; } = Array.Empty<DayForecast>();

    /// <summary>Current wall-clock time at the forecast location.</summary>
    public DateTime LocalNow(DateTime nowUtc) => DateTime.SpecifyKind(nowUtc.AddSeconds(UtcOffsetSeconds), DateTimeKind.Unspecified);

    /// <summary>The next <paramref name="count"/> whole hours after <paramref name="localNow"/>.</summary>
    public IReadOnlyList<HourForecast> NextHours(DateTime localNow, int count) =>
        Hours.Where(h => h.Time > localNow).Take(count).ToList();

    public int? ChanceAt(DateTime slot) => Hours.FirstOrDefault(h => h.Time == slot)?.RainChance;
}

public static class OpenMeteo
{
    public static string ForecastUrl(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.####}&longitude={lon:0.####}") +
        "&current=temperature_2m,weather_code,is_day,apparent_temperature,relative_humidity_2m,wind_speed_10m" +
        "&hourly=temperature_2m,precipitation_probability,weather_code" +
        "&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max" +
        "&timezone=auto&forecast_days=5";

    public static WeatherReport? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("current", out var cur) || !root.TryGetProperty("daily", out var daily)) return null;
            if (AmbientJson.Num(cur, "temperature_2m") is not { } temp || AmbientJson.Num(cur, "weather_code") is not { } code) return null;
            if (!TryTime(AmbientJson.Str(cur, "time"), out var observed)) return null;

            var highs = AmbientJson.NumArray(daily, "temperature_2m_max");
            var lows = AmbientJson.NumArray(daily, "temperature_2m_min");
            var codes = AmbientJson.NumArray(daily, "weather_code");
            var rain = AmbientJson.NumArray(daily, "precipitation_probability_max");
            if (highs.Count == 0 || lows.Count == 0 || highs[0] is null || lows[0] is null) return null;

            var hours = new List<HourForecast>();
            if (root.TryGetProperty("hourly", out var hourly) && hourly.TryGetProperty("time", out var times) && times.ValueKind == JsonValueKind.Array)
            {
                var temps = AmbientJson.NumArray(hourly, "temperature_2m");
                var chances = AmbientJson.NumArray(hourly, "precipitation_probability");
                var hourCodes = AmbientJson.NumArray(hourly, "weather_code");
                int i = 0;
                foreach (var t in times.EnumerateArray())
                {
                    if (TryTime(t.GetString(), out var at) && i < temps.Count && temps[i] is { } ht)
                    {
                        int? chance = i < chances.Count && chances[i] is { } c ? (int)Math.Round(c) : null;
                        int hc = i < hourCodes.Count && hourCodes[i] is { } x ? (int)x : (int)code;
                        hours.Add(new HourForecast(at, ht, chance, hc));
                    }
                    i++;
                }
            }

            var days = new List<DayForecast>();
            if (daily.TryGetProperty("time", out var dates) && dates.ValueKind == JsonValueKind.Array)
            {
                int d = 0;
                foreach (var t in dates.EnumerateArray())
                {
                    if (DateTime.TryParseExact(t.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        && At(highs, d) is { } hi && At(lows, d) is { } lo)
                        days.Add(new DayForecast(date, hi, lo, At(codes, d) is { } dc ? (int)dc : (int)code,
                            At(rain, d) is { } dr ? (int)Math.Round(dr) : null));
                    d++;
                }
            }

            return new WeatherReport(
                observed,
                (int)(AmbientJson.Num(root, "utc_offset_seconds") ?? 0),
                temp,
                AmbientJson.Num(cur, "apparent_temperature") ?? temp,
                (int)code,
                (AmbientJson.Num(cur, "is_day") ?? 1) != 0,
                highs[0]!.Value,
                lows[0]!.Value,
                At(highs, 1),
                At(lows, 1),
                At(codes, 1) is { } tc ? (int)tc : null,
                At(rain, 1) is { } tr ? (int)Math.Round(tr) : null,
                hours)
            {
                Humidity = AmbientJson.Num(cur, "relative_humidity_2m") is { } rh ? (int)Math.Round(rh) : null,
                WindKmh = AmbientJson.Num(cur, "wind_speed_10m"),
                Days = days,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    static double? At(IReadOnlyList<double?> list, int i) => i < list.Count ? list[i] : null;

    static bool TryTime(string? s, out DateTime time) =>
        DateTime.TryParseExact(s, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

/// <summary>"Rain likely in ~N min" when the next hour turns wet and the previous forecast didn't think so.</summary>
public static class RainAlert
{
    public const int Threshold = 60;
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(3);

    /// <summary>The upcoming hourly slot and its rain chance, if the forecast has one.</summary>
    public static HourForecast? NextSlot(WeatherReport report, DateTime localNow) =>
        report.Hours.FirstOrDefault(h => h.Time > localNow && h.Time <= localNow.AddHours(1));

    /// <summary>Minutes until rain, or null when no alert is due.</summary>
    public static int? Check(WeatherReport? previous, WeatherReport current, DateTime nowUtc, DateTime? lastAlertUtc)
    {
        var localNow = current.LocalNow(nowUtc);
        if (NextSlot(current, localNow) is not { RainChance: { } chance } slot || chance < Threshold) return null;
        if (previous is null) return null; // nothing to compare against yet, so it isn't news
        var before = previous.ChanceAt(slot.Time) ?? (previous.NextHours(localNow, 1).FirstOrDefault()?.RainChance);
        if (before is null || before >= Threshold) return null;
        if (lastAlertUtc is { } last && nowUtc - last < Cooldown) return null;
        return RoundMinutes(slot.Time - localNow);
    }

    public static int RoundMinutes(TimeSpan until) => Math.Clamp((int)Math.Ceiling(until.TotalMinutes / 5) * 5, 5, 60);

    public static string Message(int minutes) => $"Rain likely in ~{minutes} min";
}

public static class WeatherText
{
    /// <summary>Celsius or Fahrenheit (settings: TemperatureUnit); forecasts arrive in Celsius either way.</summary>
    public static Shammy.Core.Settings.TemperatureUnit Unit { get; set; }

    public static string Temp(double celsius) => Temp(celsius, Unit);

    public static string Temp(double celsius, Shammy.Core.Settings.TemperatureUnit unit)
    {
        double v = unit == Shammy.Core.Settings.TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;
        return $"{(int)Math.Round(v, MidpointRounding.AwayFromZero)}\u00B0";
    }

    public static string HourLabel(DateTime t)
    {
        int h = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return $"{h} {(t.Hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>"31° Pune" for the resting notch's glance.</summary>
    public static string Glance(WeatherReport r, string city) => $"{Temp(r.Temperature)} {city}";

    /// <summary>"9 km/h".</summary>
    public static string Wind(double kmh) => $"{(int)Math.Round(kmh, MidpointRounding.AwayFromZero)} km/h";

    /// <summary>"Today", then "Wed", "Thu"… for the 5-day list.</summary>
    public static string DayLabel(DateTime date, DateTime today) =>
        date.Date == today.Date ? "Today" : date.ToString("ddd", CultureInfo.InvariantCulture);

    /// <summary>"24° Overcast in Pune · feels 30° · H 31° L 22°".</summary>
    public static string Now(WeatherReport r, string city) =>
        $"{Temp(r.Temperature)} {WeatherCodes.Describe(r.Code)} in {city} \u00B7 feels {Temp(r.FeelsLike)} \u00B7 H {Temp(r.TodayHigh)} L {Temp(r.TodayLow)}";

    /// <summary>"Thunderstorm, 30°/24°, 51% chance of rain" or null when the forecast has no tomorrow.</summary>
    public static string? Tomorrow(WeatherReport r)
    {
        if (r.TomorrowHigh is not { } hi || r.TomorrowLow is not { } lo) return null;
        var text = $"{(r.TomorrowCode is { } c ? WeatherCodes.Describe(c) : "Forecast")}, {Temp(hi)}/{Temp(lo)}";
        return r.TomorrowRainChance is { } p && p > 0 ? $"{text}, {p}% chance of rain" : text;
    }
}

internal static class AmbientJson
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? Num(v) : null;

    public static double? Num(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static IReadOnlyList<double?> NumArray(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<double?>();
        return arr.EnumerateArray().Select(Num).ToList();
    }
}
