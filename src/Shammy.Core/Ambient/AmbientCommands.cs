using System.Text.RegularExpressions;

namespace Shammy.Core.Ambient;

public enum WeatherQuery { Now, Tomorrow }

public enum TickerAction { Status, On, Off, Enable, Disable, SetSymbols, AddSymbols, RemoveSymbols }

public sealed record TickerCommand(TickerAction Action, TickerKind? Kind = null, IReadOnlyList<string>? Symbols = null);

/// <summary>
/// Typed/spoken commands for weather, tickers and the pet. Everything is anchored to the whole text so
/// ordinary searches ("weather in paris", "best stocks to buy") still go to the web.
/// </summary>
public static class AmbientCommands
{
    static readonly Regex WeatherRx = new(
        @"^(?:(?:what's|whats|what is|how's|hows|how is|show(?: me)?|check|tell me|get)\s+)?(?:the\s+)?" +
        @"(?:weather|forecast|weather forecast|weather report)(?:\s+like)?" +
        @"(?:\s+(?<when>now|right now|today|outside|tomorrow|for tomorrow|tmrw))?$",
        RegexOptions.Compiled);

    static readonly Regex WeatherPossessiveRx = new(@"^(?<when>today|tomorrow)(?:'s|s)\s+(?:weather|forecast)$", RegexOptions.Compiled);
    static readonly Regex RainRx = new(@"^(?:will it|is it going to|is it gonna)\s+rain(?:\s+(?<when>today|tomorrow))?$", RegexOptions.Compiled);
    static readonly Regex PetRx = new(@"^(?:(?:pet|pet mode)\s+(?<state>on|off)|(?<verb>show|hide|enable|disable)\s+(?:the\s+)?pet)$", RegexOptions.Compiled);

    static string Normalize(string text) =>
        Regex.Replace(text.Trim().TrimEnd('.', '!', '?').Replace('\u2019', '\'').ToLowerInvariant(), @"\s+", " ");

    public static WeatherQuery? ParseWeather(string text)
    {
        var t = Normalize(text);
        var m = WeatherRx.Match(t);
        if (!m.Success) m = WeatherPossessiveRx.Match(t);
        if (!m.Success) m = RainRx.Match(t);
        if (!m.Success) return null;
        return m.Groups["when"].Value.Contains("tom") || m.Groups["when"].Value == "tmrw" ? WeatherQuery.Tomorrow : WeatherQuery.Now;
    }

    public static TickerCommand? ParseTicker(string text)
    {
        var words = Regex.Split(text.Trim().TrimEnd('.', '!', '?').Replace('\u2019', '\'').Trim(), @"\s+");
        if (words.Length == 0 || !words[0].Equals("ticker", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = words.Skip(1).ToList();
        if (rest.Count == 0) return new TickerCommand(TickerAction.Status);

        var verb = rest[0].ToLowerInvariant();
        var args = rest.Skip(1).ToList();
        switch (verb)
        {
            case "status" when args.Count == 0: return new TickerCommand(TickerAction.Status);
            case "on" when args.Count == 0: return new TickerCommand(TickerAction.On);
            case "off" or "stop" or "hide" when args.Count == 0: return new TickerCommand(TickerAction.Off);
            case "off" or "stop" or "hide" when args.Count == 1 && Kind(args[0]) is { } offKind:
                return new TickerCommand(TickerAction.Disable, offKind);
            case "add" or "remove" or "rm" or "delete" when args.Count > 0:
                if (Symbols(args) is not { } symbols) return null;
                return new TickerCommand(verb == "add" ? TickerAction.AddSymbols : TickerAction.RemoveSymbols, TickerKind.Stocks, symbols);
        }

        if (Kind(verb) is not { } kind) return null;
        if (args.Count == 0) return new TickerCommand(TickerAction.Enable, kind);
        if (args.Count == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) return new TickerCommand(TickerAction.Disable, kind);
        if (args.Count == 1 && args[0].Equals("on", StringComparison.OrdinalIgnoreCase)) return new TickerCommand(TickerAction.Enable, kind);
        if (kind == TickerKind.Stocks && Symbols(args) is { } list) return new TickerCommand(TickerAction.SetSymbols, kind, list);
        // "ticker cricket india": follow one team.
        if (kind == TickerKind.Cricket && args.Count <= 3 && args.All(a => a.All(char.IsLetter)))
            return new TickerCommand(TickerAction.Enable, kind, new[] { string.Join(' ', args) });
        return null;
    }

    static TickerKind? Kind(string word) => word.ToLowerInvariant() switch
    {
        "cricket" or "scores" or "score" => TickerKind.Cricket,
        "stocks" or "stock" or "shares" or "market" or "markets" => TickerKind.Stocks,
        "crypto" or "bitcoin" or "coins" => TickerKind.Crypto,
        _ => null,
    };

    static IReadOnlyList<string>? Symbols(IEnumerable<string> args)
    {
        var symbols = args.SelectMany(a => a.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(a => a.Trim().ToUpperInvariant()).ToList();
        return symbols.Count > 0 && symbols.Count <= 12 && symbols.All(YahooChart.IsValidSymbol) ? symbols : null;
    }

    /// <summary>"pet on" → true, "hide pet" → false.</summary>
    public static bool? ParsePet(string text)
    {
        var m = PetRx.Match(Normalize(text));
        if (!m.Success) return null;
        var word = m.Groups["state"].Success ? m.Groups["state"].Value : m.Groups["verb"].Value;
        return word is "on" or "show" or "enable";
    }
}
