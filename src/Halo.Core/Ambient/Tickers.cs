using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Halo.Core.Ambient;

public enum TickerKind { Cricket, Stocks, Crypto }

/// <summary>One line the compact ticker rotates through, e.g. "IND 287/4 v AUS".</summary>
public sealed record TickerItem(TickerKind Kind, string Text, bool Live = false);

public static class TickerFormat
{
    const string Up = "\u25B2", Down = "\u25BC";

    static readonly Dictionary<string, string> Indices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["^NSEI"] = "NIFTY",
        ["^NSEBANK"] = "BANKNIFTY",
        ["^BSESN"] = "SENSEX",
        ["^GSPC"] = "S&P 500",
        ["^IXIC"] = "NASDAQ",
        ["^DJI"] = "DOW",
        ["^FTSE"] = "FTSE",
        ["^N225"] = "NIKKEI",
    };

    /// <summary>"RELIANCE.NS" → "RELIANCE", "^NSEI" → "NIFTY", "BTC-USD" → "BTC-USD".</summary>
    public static string SymbolLabel(string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (Indices.TryGetValue(symbol, out var name)) return name;
        if (symbol.StartsWith('^')) return symbol[1..];
        foreach (var suffix in new[] { ".NS", ".BO" })
            if (symbol.EndsWith(suffix, StringComparison.Ordinal)) return symbol[..^suffix.Length];
        return symbol;
    }

    public static string CurrencySymbol(string? currency) => currency?.ToUpperInvariant() switch
    {
        "INR" => "\u20B9",
        "USD" => "$",
        "EUR" => "\u20AC",
        "GBP" => "\u00A3",
        "JPY" => "\u00A5",
        null or "" => "",
        var code => code + " ",
    };

    /// <summary>Whole units above 1000 (the ear is narrow), two decimals below.</summary>
    public static string Price(double price, string? currency, bool isIndex = false)
    {
        var number = price.ToString(Math.Abs(price) >= 1000 ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);
        return isIndex ? number : CurrencySymbol(currency) + number;
    }

    public static string Change(double percent)
    {
        var rounded = Math.Round(percent, 1, MidpointRounding.AwayFromZero);
        var digits = Math.Abs(rounded).ToString("0.0", CultureInfo.InvariantCulture);
        return rounded > 0 ? $"{Up}{digits}%" : rounded < 0 ? $"{Down}{digits}%" : $"{digits}%";
    }

    /// <summary>"RELIANCE ₹2,941 ▲1.2%".</summary>
    public static string Quote(StockQuote q)
    {
        var text = $"{SymbolLabel(q.Symbol)} {Price(q.Price, q.Currency, q.IsIndex)}";
        return q.ChangePercent is { } c ? $"{text} {Change(c)}" : text;
    }
}

public sealed record StockQuote(string Symbol, double Price, double? ChangePercent, string? Currency, bool IsIndex);

/// <summary>Yahoo Finance's public chart endpoint (no key; wants a browser user agent).</summary>
public static class YahooChart
{
    public static readonly Regex SymbolPattern = new(@"^[A-Z0-9\^][A-Z0-9\.\-=&\^]{0,19}$", RegexOptions.Compiled);

    public static string Url(string symbol) =>
        $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=5d";

    public static bool IsValidSymbol(string symbol) => SymbolPattern.IsMatch(symbol);

    public static StockQuote? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("chart", out var chart) || !chart.TryGetProperty("result", out var results)
                || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;
            var result = results[0];
            if (!result.TryGetProperty("meta", out var meta)) return null;
            var symbol = AmbientJson.Str(meta, "symbol");
            if (symbol is null || AmbientJson.Num(meta, "regularMarketPrice") is not { } price) return null;

            double? change = AmbientJson.Num(meta, "regularMarketChangePercent");
            if (change is null)
            {
                // Older responses lack the change; derive it from the last two daily closes instead.
                var closes = result.TryGetProperty("indicators", out var ind) && ind.TryGetProperty("quote", out var q)
                    && q.ValueKind == JsonValueKind.Array && q.GetArrayLength() > 0
                    ? AmbientJson.NumArray(q[0], "close").Where(c => c is not null).Select(c => c!.Value).ToList()
                    : new List<double>();
                if (closes.Count >= 2 && closes[^2] != 0) change = (price - closes[^2]) / closes[^2] * 100;
            }
            bool isIndex = AmbientJson.Str(meta, "instrumentType") == "INDEX" || symbol.StartsWith('^');
            return new StockQuote(symbol, price, change, AmbientJson.Str(meta, "currency"), isIndex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>CoinGecko's free simple-price endpoint.</summary>
public static class CoinGecko
{
    public const string Url = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum&vs_currencies=inr&include_24hr_change=true";

    static readonly (string Id, string Label)[] Coins = { ("bitcoin", "BTC"), ("ethereum", "ETH") };

    public static IReadOnlyList<TickerItem> Parse(string json)
    {
        var items = new List<TickerItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var (id, label) in Coins)
            {
                if (!doc.RootElement.TryGetProperty(id, out var coin) || AmbientJson.Num(coin, "inr") is not { } price) continue;
                var text = $"{label} {TickerFormat.Price(price, "INR")}";
                if (AmbientJson.Num(coin, "inr_24h_change") is { } change) text += " " + TickerFormat.Change(change);
                items.Add(new TickerItem(TickerKind.Crypto, text));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return items;
    }
}

/// <summary>One side of a match: "India A", "32 & 346/10", batting now.</summary>
public sealed record CricketSide(string Team, string? Score, bool Batting);

public sealed record CricketMatch(CricketSide Home, CricketSide Away)
{
    /// <summary>The feed marks the side currently batting with "*"; finished matches lose it.</summary>
    public bool Live => Home.Batting || Away.Batting;
}

/// <summary>
/// ESPNcricinfo's public live-scores RSS. Its JSON APIs sit behind bot protection, but this feed is
/// meant for readers. Each title looks like "New Zealand A 424/10  v India A 32 &amp;  346/10 *".
/// </summary>
public static class CricketFeed
{
    public const string Url = "https://static.cricinfo.com/rss/livescores.xml";

    static readonly Regex SideRx = new(@"^(?<team>.+?)\s+(?<score>\d+(?:/\d+)?d?(?:\s*&\s*\d+(?:/\d+)?d?)*)$", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["India"] = "IND", ["Australia"] = "AUS", ["England"] = "ENG", ["New Zealand"] = "NZ",
        ["South Africa"] = "SA", ["Pakistan"] = "PAK", ["Sri Lanka"] = "SL", ["Bangladesh"] = "BAN",
        ["West Indies"] = "WI", ["Afghanistan"] = "AFG", ["Zimbabwe"] = "ZIM", ["Ireland"] = "IRE",
        ["Netherlands"] = "NED", ["Scotland"] = "SCO", ["Nepal"] = "NEP", ["Oman"] = "OMA",
        ["United Arab Emirates"] = "UAE", ["UAE"] = "UAE", ["United States of America"] = "USA", ["USA"] = "USA",
        ["Namibia"] = "NAM", ["Canada"] = "CAN",
    };

    static readonly (string Suffix, string Short)[] Squads = { (" A", " A"), (" Women", " W"), (" Under-19s", " U19"), (" U19", " U19") };

    public static IReadOnlyList<CricketMatch> Parse(string xml)
    {
        var matches = new List<CricketMatch>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var title in doc.Descendants("item").Select(i => (string?)i.Element("title")))
                if (title is not null && ParseTitle(title) is { } m) matches.Add(m);
        }
        catch (XmlException) { }
        return matches;
    }

    public static CricketMatch? ParseTitle(string title)
    {
        var parts = Spaces.Replace(WebUtility.HtmlDecode(title), " ").Trim().Split(" v ");
        if (parts.Length != 2) return null;
        var home = ParseSide(parts[0]);
        var away = ParseSide(parts[1]);
        return home is null || away is null ? null : new CricketMatch(home, away);
    }

    static CricketSide? ParseSide(string text)
    {
        bool batting = text.Contains('*');
        text = text.Replace("*", "").Trim();
        if (text.Length == 0) return null;
        var m = SideRx.Match(text);
        return m.Success
            ? new CricketSide(m.Groups["team"].Value.Trim(), Spaces.Replace(m.Groups["score"].Value, " "), batting)
            : new CricketSide(text, null, batting);
    }

    /// <summary>"India A" → "IND A", "Hyderabad (India)" → "Hyderabad", unknown names unchanged.</summary>
    public static string ShortTeam(string team)
    {
        team = Regex.Replace(team, @"\s*\([^)]*\)", "").Trim();
        foreach (var (suffix, shortSuffix) in Squads)
            if (team.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && Codes.TryGetValue(team[..^suffix.Length], out var squad))
                return squad + shortSuffix;
        return Codes.TryGetValue(team, out var code) ? code : team;
    }

    /// <summary>0 for senior internationals, 1 for A/women/U19 national sides, 2 for everything else.</summary>
    public static int Rank(CricketMatch m)
    {
        static int SideRank(string team)
        {
            var s = ShortTeam(team);
            return Codes.ContainsValue(s) ? 0 : s.Contains(' ') && Codes.ContainsValue(s[..s.IndexOf(' ')]) ? 1 : 2;
        }
        return Math.Min(SideRank(m.Home.Team), SideRank(m.Away.Team));
    }

    /// <summary>Batting side first with its current innings: "IND A 32 v NZ A 424/10".</summary>
    public static string Format(CricketMatch m)
    {
        var (first, second) = m.Away.Batting && !m.Home.Batting ? (m.Away, m.Home) : (m.Home, m.Away);
        return $"{Side(first)} v {Side(second)}";

        static string Side(CricketSide s) => s.Score is null ? ShortTeam(s.Team) : $"{ShortTeam(s.Team)} {CurrentInnings(s)}";
    }

    /// <summary>
    /// The feed lists a side's innings in no fixed order ("32 &amp; 346/10" and "346/10 &amp; 32" both
    /// appear), so for the batting side the innings still in progress (not all out, not declared) wins.
    /// </summary>
    public static string CurrentInnings(CricketSide s)
    {
        var innings = (s.Score ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (innings.Length == 0) return "";
        if (s.Batting && innings.LastOrDefault(i => !i.EndsWith("/10") && !i.EndsWith('d')) is { } open) return open;
        return innings[^1];
    }

    /// <summary>Matches with a score, internationals and live ones first; optional team filter.</summary>
    public static IReadOnlyList<TickerItem> Items(IEnumerable<CricketMatch> matches, string? team = null, int max = 8) =>
        matches
            .Where(m => m.Home.Score is not null || m.Away.Score is not null)
            .Where(m => string.IsNullOrWhiteSpace(team) || Mentions(m, team!))
            .OrderBy(Rank)
            .ThenByDescending(m => m.Live)
            .Take(max)
            .Select(m => new TickerItem(TickerKind.Cricket, Format(m), m.Live))
            .ToList();

    static bool Mentions(CricketMatch m, string team) =>
        new[] { m.Home.Team, m.Away.Team }.Any(t =>
            t.Contains(team, StringComparison.OrdinalIgnoreCase) || ShortTeam(t).StartsWith(team, StringComparison.OrdinalIgnoreCase));
}
