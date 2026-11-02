using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Halo.Core.Conversions;

public sealed record Money(double Amount, string Code);

public sealed record CurrencyQuery(Money From, string To);

/// <summary>Exchange rates as units of each currency per one <see cref="Base"/> (the base itself is 1).</summary>
public sealed record RateTable(string Base, string Date, IReadOnlyDictionary<string, double> Rates);

/// <summary>
/// Money phrases ("100 usd to inr", "₹500 in dollars", "1.5 lakh rupees to usd") and circled prices
/// ("$49.99", "Rs. 1,299/-"). Conversion takes a rate table so it stays pure and testable.
/// </summary>
public static class Currency
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>What the free Frankfurter (ECB) feed publishes; anything else can't be converted.</summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        "AUD", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK",
        "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    };

    static readonly Dictionary<string, string> Aliases = BuildAliases();

    static readonly HashSet<string> NoDecimals = new(StringComparer.Ordinal) { "JPY", "KRW", "IDR", "HUF", "ISK" };

    static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["USD"] = "$", ["INR"] = "₹", ["EUR"] = "€", ["GBP"] = "£", ["JPY"] = "¥", ["KRW"] = "₩",
    };

    static readonly Regex MoneyRx = new(
        @"^(?<pre>[^\d\s.,-][^\d\s]{0,3})? ?(?<num>\d[\d,]*(?:\.\d+)?|\.\d+) ?(?:(?<mult>k|thousand|lakhs?|lacs?|crores?|cr|million|mn|billion|bn)(?![a-z]))? ?(?<post>[^\d\s].*)?$");

    static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string code, params string[] names)
        {
            foreach (var n in names) map[n] = code;
        }
        foreach (var code in Supported) map[code.ToLowerInvariant()] = code;
        Add("USD", "$", "us$", "dollar", "dollars", "us dollar", "us dollars", "american dollar", "american dollars", "buck", "bucks");
        Add("INR", "₹", "rs", "rs.", "inr.", "rupee", "rupees", "indian rupee", "indian rupees", "rupaye", "rupaiya");
        Add("EUR", "€", "euro", "euros");
        Add("GBP", "£", "pound", "pounds", "pound sterling", "pounds sterling", "british pound", "british pounds", "sterling", "quid");
        Add("JPY", "¥", "yen", "japanese yen");
        Add("CNY", "rmb", "yuan", "renminbi", "chinese yuan");
        Add("KRW", "₩", "won", "korean won", "south korean won");
        Add("AUD", "a$", "au$", "australian dollar", "australian dollars");
        Add("CAD", "c$", "ca$", "canadian dollar", "canadian dollars");
        Add("SGD", "s$", "singapore dollar", "singapore dollars");
        Add("HKD", "hk$", "hong kong dollar", "hong kong dollars");
        Add("NZD", "nz$", "new zealand dollar", "new zealand dollars");
        Add("CHF", "franc", "francs", "swiss franc", "swiss francs");
        Add("BRL", "r$", "real", "reais", "brazilian real", "brazilian reais");
        Add("MXN", "mex$", "peso", "pesos", "mexican peso", "mexican pesos");
        Add("PHP", "₱", "philippine peso", "philippine pesos");
        Add("THB", "฿", "baht", "thai baht");
        Add("TRY", "₺", "lira", "turkish lira");
        Add("ILS", "₪", "shekel", "shekels");
        Add("PLN", "zł", "zloty", "zlotys");
        Add("MYR", "rm", "ringgit", "malaysian ringgit");
        Add("IDR", "rp", "rupiah", "indonesian rupiah");
        Add("ZAR", "rand", "south african rand");
        Add("SEK", "swedish krona", "swedish kronor");
        Add("NOK", "norwegian krone", "norwegian kroner");
        Add("DKK", "danish krone", "danish kroner");
        Add("CZK", "koruna", "czech koruna");
        Add("HUF", "forint", "hungarian forint");
        Add("RON", "leu", "lei", "romanian leu");
        Add("ISK", "icelandic krona");
        return map;
    }

    /// <summary>"usd", "$", "Rs.", "indian rupees" → the ISO code.</summary>
    public static bool TryFindCode(string text, out string code)
    {
        var key = Phrase.Normalize(text);
        if (Aliases.TryGetValue(key, out code!)) return true;
        return Aliases.TryGetValue(key.Replace(".", ""), out code!);
    }

    /// <summary>"$49.99", "100 usd", "rs.500", "15€", "1.5 lakh rupees"; with <paramref name="requireAmount"/> false also "dollar" (= 1).</summary>
    public static bool TryParseMoney(string text, bool requireAmount, out Money money)
    {
        money = null!;
        var s = Phrase.Normalize(text);
        if (s.Length == 0) return false;
        if (!requireAmount && TryFindCode(s, out var alone))
        {
            money = new Money(1, alone);
            return true;
        }

        var m = MoneyRx.Match(s);
        if (!m.Success || !NumberText.TryParse(m.Groups["num"].Value, out var amount)) return false;
        string? pre = null, post = null;
        if (m.Groups["pre"].Success && !TryFindCode(m.Groups["pre"].Value, out pre!)) return false;
        if (m.Groups["post"].Success && !TryFindCode(m.Groups["post"].Value, out post!)) return false;
        if (pre is null && post is null) return false;
        if (pre is not null && post is not null && pre != post) return false;

        amount *= m.Groups["mult"].Value switch
        {
            "" => 1,
            "k" or "thousand" => 1e3,
            "lakh" or "lakhs" or "lac" or "lacs" => 1e5,
            "crore" or "crores" or "cr" => 1e7,
            "million" or "mn" => 1e6,
            _ => 1e9,
        };
        money = new Money(amount, pre ?? post!);
        return true;
    }

    /// <summary>"100 usd to inr", "convert 20 gbp to inr", "₹500 in dollars", "dollar to rupee". False for anything else.</summary>
    public static bool TryParseQuery(string text, out CurrencyQuery query)
    {
        query = null!;
        var s = Phrase.StripLead(Phrase.Normalize(text));
        if (s.Length == 0 || s.Length > 60) return false;
        foreach (var (left, right) in Phrase.Splits(s, Phrase.ToWords))
        {
            if (!TryFindCode(right, out var to) || !TryParseMoney(left, requireAmount: false, out var from) || from.Code == to) continue;
            query = new CurrencyQuery(from, to);
            return true;
        }
        return false;
    }

    /// <summary>A circled price and nothing much else: "$49.99", "€15", "₹1,299", "Price: Rs. 999/-".</summary>
    public static bool TryReadPrice(string text, out Money money)
    {
        money = null!;
        var s = Phrase.CleanSelection(text);
        return s.Length > 0 && TryParseMoney(s, requireAmount: true, out money) && money.Amount > 0;
    }

    /// <summary>Who a circled price converts to: rupees by default, dollars when it already is rupees.</summary>
    public static string DefaultTarget(string fromCode) => fromCode == "INR" ? "USD" : "INR";

    public static bool TryConvert(double amount, string from, string to, IReadOnlyDictionary<string, double> rates, out double result)
    {
        result = 0;
        if (!rates.TryGetValue(from, out var f) || !rates.TryGetValue(to, out var t) || f <= 0) return false;
        result = amount / f * t;
        return true;
    }

    /// <summary>"₹4,74,550.25" (Indian grouping), "$49.99", "¥7,895", "12.50 AUD"; whole amounts drop ".00".</summary>
    public static string Format(double amount, string code)
    {
        var a = Math.Abs(amount);
        int decimals = NoDecimals.Contains(code) && a >= 1 ? 0 : 2;
        if (a > 0 && a < 1) decimals = Math.Min(6, 2 - (int)Math.Floor(Math.Log10(a))); // 0.01054, not 0.01
        var r = Math.Round(a, decimals, MidpointRounding.AwayFromZero);
        bool whole = r % 1 == 0;
        string digits;
        if (code == "INR")
        {
            var intPart = Math.Floor(r);
            digits = IndianGrouping(intPart.ToString("0", Inv));
            if (!whole) digits += (r - intPart).ToString("." + new string('0', decimals), Inv).TrimStart('0');
        }
        else
        {
            digits = whole ? r.ToString("#,0", Inv) : r.ToString("#,0." + new string('0', decimals), Inv);
        }
        if (decimals > 2 && !whole) digits = digits.TrimEnd('0');
        var sign = amount < 0 && r != 0 ? "-" : "";
        return Symbols.TryGetValue(code, out var sym) ? $"{sign}{sym}{digits}" : $"{sign}{digits} {code}";
    }

    static string IndianGrouping(string digits)
    {
        if (digits.Length <= 3) return digits;
        var head = digits[..^3];
        var tail = digits[^3..];
        var groups = new List<string>();
        while (head.Length > 2)
        {
            groups.Insert(0, head[^2..]);
            head = head[..^2];
        }
        if (head.Length > 0) groups.Insert(0, head);
        return string.Join(",", groups) + "," + tail;
    }

    /// <summary>Reads Frankfurter's <c>{"base":"USD","date":"…","rates":{"INR":94.9,…}}</c>.</summary>
    public static bool TryParseRates(string json, out RateTable table)
    {
        table = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("base", out var b) || b.GetString() is not { Length: 3 } baseCode) return false;
            if (!root.TryGetProperty("rates", out var r) || r.ValueKind != JsonValueKind.Object) return false;
            var rates = new Dictionary<string, double>(StringComparer.Ordinal) { [baseCode] = 1 };
            foreach (var p in r.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetDouble() > 0) rates[p.Name] = p.Value.GetDouble();
            if (rates.Count < 2) return false;
            var date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
            table = new RateTable(baseCode, date, rates);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
