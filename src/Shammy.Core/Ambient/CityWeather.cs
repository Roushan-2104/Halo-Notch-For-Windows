using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shammy.Core.Search;

namespace Shammy.Core.Ambient;

/// <summary>"weather in mumbai tomorrow": the place as said and which day.</summary>
public sealed record CityWeatherQuery(string City, WeatherQuery When);

/// <summary>A geocoded place. <see cref="Label"/> is what the answer shows ("Mumbai, India").</summary>
public sealed record Place(string Name, string Country, double Latitude, double Longitude)
{
    public string Label => Country.Length > 0 && !Country.Equals(Name, StringComparison.OrdinalIgnoreCase) ? $"{Name}, {Country}" : Name;
}

/// <summary>
/// Weather for a named place: "weather in mumbai", "mumbai weather", "weather in delhi tomorrow", "will it rain in
/// pune", "pune ka mausam", "delhi mein kal mausam kaisa rahega". The place must look like a name (letters, at most
/// four words) and not like the rest of a search ("weather app download", "cold weather", "weather channel").
/// </summary>
public static class CityWeather
{
    const RegexOptions Opts = RegexOptions.CultureInvariant;

    const string Lead = @"(?:(?:what's|whats|what is|how's|hows|how is|show(?: me)?|check|tell me|get|what will be|how will be)\s+)?(?:the\s+)?";
    const string Word = @"(?:weather|forecast|weather forecast|weather report|temperature|temp)";
    const string When = @"(?<when>now|right now|today|tonight|tomorrow|for tomorrow|tmrw|kal)";

    static readonly Regex InCity = new(
        "^" + Lead + @"(?:(?<pre>today's|todays|tomorrow's|tomorrows)\s+)?" + Word + @"(?:\s+like)?(?:\s+" + When + @")?\s+(?:in|at|for|of)\s+(?<city>.+?)(?:\s+" + When.Replace("<when>", "<when2>") + @")?(?:\s+like)?$", Opts);

    // Only "weather"/"forecast" after a name: "oven temperature" or "cpu temp" are searches.
    static readonly Regex CityFirst = new(@"^(?<city>.+?)\s+(?:weather|forecast|weather forecast)(?:\s+" + When + ")?$", Opts);

    static readonly Regex Rain = new(@"^(?:will it|is it going to|is it gonna)\s+rain\s+(?:in|at)\s+(?<city>.+?)(?:\s+" + When + ")?$", Opts);

    // "pune ka mausam", "delhi ka kal ka mausam", "mumbai mein mausam kaisa hai", "kal delhi ka mausam kaisa rahega".
    const string Mausam = @"(?:mausam|mosam|mousam|mausum|mosum|weather|temperature)";
    const string Kaisa = @"(?:\s+(?:kaisa|kaisi|kesa|kaise)(?:\s+(?:hai|hoga|rahega|he|h))?)?(?:\s+(?:batao|bata\s*do|bataiye))?";
    static readonly Regex Hinglish = new(
        @"^(?:(?<when>aaj|aj|kal)\s+)?(?<city>.+?)\s+(?:ka|ki|kaa|mein|me|main|mai)\s+(?:(?<when2>aaj|aj|kal)\s+(?:ka\s+|ki\s+)?)?" + Mausam + Kaisa + "$", Opts);

    static readonly HashSet<string> NotPlaces = new(StringComparer.Ordinal)
    {
        // A different question ("weather app download", "weather in paris history").
        "app", "apps", "application", "download", "downloads", "widget", "channel", "api", "website", "site", "radar",
        "map", "maps", "news", "report", "station", "forecast", "forecasts", "update", "updates", "alert", "alerts",
        "warning", "history", "data", "today", "tomorrow", "tonight", "now", "outside", "here", "there", "vs", "versus",
        "and", "or", "with", "without", "near", "me", "my", "your", "our", "this", "that", "is", "are", "was", "will",
        "what", "how", "why", "when", "where", "which", "who", "best", "free", "live", "like", "girls", "with", "song",
        "songs", "movie", "film", "game", "games", "lyrics", "video", "wallpaper", "emoji", "symbol", "icon", "meaning",
        "definition", "pdf", "ppt", "essay", "class", "chapter", "project", "kaisa", "hai", "a", "an", "the", "it",
        "c", "f", "celsius", "fahrenheit", "degree", "degrees", "body", "room", "water", "cpu", "gpu", "core",
        "change", "control", "conversion", "converter", "check", "normal", "high", "low", "average",
        // Adjectives and seasons: "cold weather", "nice weather" (Nice is also a city, which makes this list matter).
        "bad", "good", "nice", "great", "cold", "hot", "warm", "cool", "wet", "dry", "rainy", "sunny", "cloudy",
        "stormy", "windy", "foggy", "humid", "extreme", "severe", "winter", "summer", "spring", "autumn", "fall",
        "monsoon", "current", "local", "latest", "hourly", "daily", "weekly", "monthly", "accurate", "accuweather",
        "google", "apple", "windows", "msn", "yahoo", "bbc", "fair", "mild", "perfect", "beautiful", "today's",
        "tomorrow's", "todays", "tomorrows", "weather", "mausam", "space", "solar", "under", "all", "any",
        "what's", "whats", "how's", "hows", "aaj", "aj", "kal", "mera", "meri", "mere", "yaha", "yahan", "idhar",
        "bahar", "abhi", "aapka", "apna", "apne",
    };

    static readonly Regex PlaceWord = new(@"^[\p{L}][\p{L}.'-]*$", Opts);

    public static CityWeatherQuery? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimEnd('.', '!', '?', '।').Trim().Replace('’', '\'').ToLowerInvariant();
        // Spoken Hindi can arrive in Devanagari ("पुणे का मौसम").
        if (Devanagari.Contains(t)) t = Devanagari.ToLatin(t);
        t = Regex.Replace(t, @"[,?]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0 || t.Length > 80) return null;

        foreach (var rx in new[] { InCity, Rain, Hinglish, CityFirst })
        {
            var m = rx.Match(t);
            if (!m.Success) continue;
            var city = Clean(m.Groups["city"].Value);
            if (city is null) continue;
            var when = m.Groups["when"].Value + " " + m.Groups["when2"].Value + " " + m.Groups["pre"].Value;
            return new CityWeatherQuery(city, when.Contains("tom") || when.Contains("tmrw") || when.Contains("kal") ? WeatherQuery.Tomorrow : WeatherQuery.Now);
        }
        return null;
    }

    /// <summary>The place as a title-cased name, or null when it doesn't look like one.</summary>
    static string? Clean(string raw)
    {
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0] == "the") words.RemoveAt(0);
        if (words.Count is 0 or > 4) return null;
        if (words.Any(w => !PlaceWord.IsMatch(w) || NotPlaces.Contains(w))) return null;
        var name = string.Join(' ', words);
        if (name.Length < 2 || name.Length > 40) return null;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    /// <summary>Old and colloquial names the geocoder gets wrong ("bangalore" finds a town in Pakistan).</summary>
    static readonly Dictionary<string, string> Renamed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bangalore"] = "Bengaluru", ["banglore"] = "Bengaluru", ["bombay"] = "Mumbai", ["calcutta"] = "Kolkata",
        ["madras"] = "Chennai", ["gurgaon"] = "Gurugram", ["poona"] = "Pune", ["banaras"] = "Varanasi",
        ["benares"] = "Varanasi", ["vizag"] = "Visakhapatnam", ["trivandrum"] = "Thiruvananthapuram",
        ["mysore"] = "Mysuru", ["baroda"] = "Vadodara", ["cochin"] = "Kochi", ["pondicherry"] = "Puducherry",
        ["dilli"] = "Delhi", ["new delhi"] = "New Delhi", ["mumbai city"] = "Mumbai", ["nyc"] = "New York",
        ["la"] = "Los Angeles", ["sf"] = "San Francisco", ["dubai city"] = "Dubai", ["prayagraj"] = "Prayagraj",
        ["allahabad"] = "Prayagraj", ["mangalore"] = "Mangaluru", ["simla"] = "Shimla", ["dehradoon"] = "Dehradun",
    };

    /// <summary>The name to look up: a known rename, otherwise the place as said.</summary>
    public static string LookupName(string city) => Renamed.TryGetValue(city.Trim(), out var renamed) ? renamed : city.Trim();

    /// <summary>The cache key for a place name ("Mumbai " and "mumbai" are one lookup).</summary>
    public static string Key(string city) => Regex.Replace(LookupName(city).ToLowerInvariant(), @"\s+", " ");
}

/// <summary>Open-Meteo's free, keyless geocoder.</summary>
public static class OpenMeteoGeocoding
{
    public static string SearchUrl(string name) =>
        "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) + "&count=5&language=en&format=json";

    /// <summary>
    /// The best of the results: a populated place (feature code PPL…) with the most people, so "delhi" is Delhi, India
    /// and not Delhi, NY; otherwise the first result (a state or region). Null when nothing was found.
    /// </summary>
    public static Place? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
            Place? first = null, best = null;
            double bestPopulation = -1;
            foreach (var r in results.EnumerateArray())
            {
                if (AmbientJson.Num(r, "latitude") is not { } lat || AmbientJson.Num(r, "longitude") is not { } lon) continue;
                if (lat is < -90 or > 90 || lon is < -180 or > 180) continue;
                var name = AmbientJson.Str(r, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var place = new Place(name.Trim(), AmbientJson.Str(r, "country")?.Trim() ?? "", lat, lon);
                first ??= place;
                if (!(AmbientJson.Str(r, "feature_code") ?? "").StartsWith("PPL", StringComparison.Ordinal)) continue;
                double population = AmbientJson.Num(r, "population") ?? 0;
                if (population > bestPopulation)
                {
                    best = place;
                    bestPopulation = population;
                }
            }
            return best ?? first;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
