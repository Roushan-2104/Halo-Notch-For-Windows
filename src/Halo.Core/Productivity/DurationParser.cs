using System.Globalization;

namespace Halo.Core.Productivity;

/// <summary>
/// Durations as people type or say them: "25m", "1h 30m", "1:30", "90 seconds", "an hour and a half",
/// "half an hour", "1.5 hours", "twenty five minutes".
/// </summary>
public static class DurationParser
{
    static readonly TimeSpan Max = TimeSpan.FromDays(100);

    static readonly Dictionary<string, int> Small = new()
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19,
    };

    static readonly Dictionary<string, int> Tens = new()
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>The whole text must be a duration; null otherwise (also for zero or absurdly long ones).</summary>
    public static TimeSpan? Parse(string text)
    {
        var tokens = Tokenize(text);
        int i = 0;
        var d = Read(tokens, ref i);
        return d is not null && i == tokens.Count ? d : null;
    }

    /// <summary>A duration at the start of the text; <paramref name="rest"/> is whatever follows, as typed.</summary>
    public static TimeSpan? ParsePrefix(string text, out string rest)
    {
        var tokens = Tokenize(text);
        int i = 0;
        var d = Read(tokens, ref i);
        rest = d is null ? text.Trim() : i < tokens.Count ? text[tokens[i].Start..].Trim() : "";
        return d;
    }

    internal readonly record struct Token(string Text, int Start);

    /// <summary>Words and numbers, lower-cased. "25m" and "1h30m" split into number and unit; "1.5" and "1:30" stay whole.</summary>
    internal static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            if (char.IsDigit(text[i]))
            {
                while (i < text.Length && (char.IsDigit(text[i]) ||
                       (text[i] is '.' or ':' && i + 1 < text.Length && char.IsDigit(text[i + 1])))) i++;
            }
            else if (char.IsLetter(text[i]))
            {
                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'')) i++;
            }
            else
            {
                i++;
                continue;
            }
            tokens.Add(new Token(text[start..i].ToLowerInvariant(), start));
        }
        return tokens;
    }

    static TimeSpan? Read(List<Token> t, ref int i)
    {
        double seconds = 0;
        bool any = false;
        while (i < t.Count)
        {
            int j = i;
            if (any && t[j].Text == "and") j++;
            if (j >= t.Count || Component(t, ref j) is not { } part) break;
            seconds += part;
            any = true;
            i = j;
        }
        if (!any || seconds < 1 || seconds > Max.TotalSeconds) return null;
        return TimeSpan.FromSeconds(Math.Round(seconds));
    }

    static double? Component(List<Token> t, ref int i)
    {
        string first = t[i].Text;
        if (first.Contains(':'))
        {
            var clock = Colon(first);
            if (clock is not null) i++;
            return clock;
        }

        int j = i;
        // "half an hour", "a half hour", "a quarter of an hour"
        if (first is "a" or "an" && At(t, j + 1) is "half" or "quarter") j++;
        if (At(t, j) is "half" or "quarter")
        {
            double fraction = At(t, j) == "half" ? 0.5 : 0.25;
            j++;
            if (At(t, j) == "of") j++;
            if (At(t, j) is "a" or "an") j++;
            if (Unit(At(t, j)) is not { } u) return null;
            i = j + 1;
            return fraction * u;
        }

        if (Number(t, ref j) is not { } amount) return null;
        bool half = AndAHalf(t, ref j); // "one and a half hours"
        if (Unit(At(t, j)) is not { } unit) return null;
        j++;
        if (!half) half = AndAHalf(t, ref j); // "an hour and a half"
        i = j;
        return (amount + (half ? 0.5 : 0)) * unit;
    }

    static double? Number(List<Token> t, ref int j)
    {
        var w = At(t, j);
        if (w is null) return null;
        if (char.IsDigit(w[0]))
        {
            if (w.Contains(':') || !double.TryParse(w, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return null;
            j++;
            return v;
        }
        if (w is "a" or "an")
        {
            j++;
            if (At(t, j) is "couple" or "few")
            {
                var n = At(t, j) == "couple" ? 2 : 3;
                j++;
                if (At(t, j) == "of") j++;
                return n;
            }
            return 1;
        }
        if (Small.TryGetValue(w, out var small))
        {
            j++;
            return small;
        }
        if (Tens.TryGetValue(w, out var tens))
        {
            j++;
            // "twenty five" (and "twenty-five", which tokenizes the same way)
            if (At(t, j) is { } next && Small.TryGetValue(next, out var unitDigit) && unitDigit is >= 1 and <= 9)
            {
                j++;
                tens += unitDigit;
            }
            return tens;
        }
        return null;
    }

    static bool AndAHalf(List<Token> t, ref int j)
    {
        if (At(t, j) != "and") return false;
        if (At(t, j + 1) == "a" && At(t, j + 2) == "half")
        {
            j += 3;
            return true;
        }
        if (At(t, j + 1) == "half")
        {
            j += 2;
            return true;
        }
        return false;
    }

    static double? Unit(string? w) => w switch
    {
        "s" or "sec" or "secs" or "second" or "seconds" => 1,
        "m" or "min" or "mins" or "minute" or "minutes" => 60,
        "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
        "d" or "day" or "days" => 86400,
        _ => null,
    };

    /// <summary>"1:30" is minutes and seconds (as on a kitchen timer); "1:30:00" adds hours.</summary>
    static double? Colon(string w)
    {
        var parts = w.Split(':');
        var n = new int[parts.Length];
        for (int k = 0; k < parts.Length; k++)
            if (!int.TryParse(parts[k], NumberStyles.None, CultureInfo.InvariantCulture, out n[k])) return null;
        return parts.Length switch
        {
            2 when n[1] < 60 => n[0] * 60 + n[1],
            3 when n[1] < 60 && n[2] < 60 => n[0] * 3600 + n[1] * 60 + n[2],
            _ => null,
        };
    }

    static string? At(List<Token> t, int i) => i < t.Count ? t[i].Text : null;
}
