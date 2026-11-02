using System.Globalization;
using System.Text.RegularExpressions;

namespace Halo.Core.Conversions;

/// <summary>Culture-invariant reading and writing of the numbers the converters see.</summary>
public static class NumberText
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, double> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100, ["thousand"] = 1000,
        ["half"] = 0.5, ["half a"] = 0.5, ["a half"] = 0.5, ["a quarter"] = 0.25, ["quarter"] = 0.25, ["a dozen"] = 12, ["dozen"] = 12,
    };

    static readonly Regex Mixed = new(@"^(-?\d+) (\d+)/(\d+)$");
    static readonly Regex Fraction = new(@"^(-?\d+)/(\d+)$");
    static readonly Regex DecimalComma = new(@"^-?\d+,\d{1,2}$");
    static readonly Regex Grouped = new(@"^-?\d{1,3}(?:,\d{2,3})*,\d{3}(?:\.\d+)?$");
    static readonly Regex DotGrouped = new(@"^-?\d{1,3}(?:\.\d{3}){2,}$");
    static readonly Regex Plain = new(@"^-?(?:\d+(?:\.\d+)?|\.\d+)$");

    /// <summary>
    /// "1,299", "1,00,000", "2.5", "15,50" (decimal comma), "1.299,99", "1/2", "1 1/2", and small
    /// number words ("five", "a", "twenty five", "two hundred") so spoken text works too.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace('\u2212', '-');

        if (TryParseWords(s, out value)) return true;

        var m = Mixed.Match(s);
        if (m.Success)
        {
            var whole = double.Parse(m.Groups[1].Value, Inv);
            var den = double.Parse(m.Groups[3].Value, Inv);
            if (den == 0) return false;
            var frac = double.Parse(m.Groups[2].Value, Inv) / den;
            value = whole < 0 ? whole - frac : whole + frac;
            return true;
        }
        m = Fraction.Match(s);
        if (m.Success)
        {
            var den = double.Parse(m.Groups[2].Value, Inv);
            if (den == 0) return false;
            value = double.Parse(m.Groups[1].Value, Inv) / den;
            return true;
        }

        bool hasComma = s.Contains(','), hasDot = s.Contains('.');
        if (hasComma && hasDot)
        {
            // Whichever separator comes last is the decimal point: "1,299.99" vs "1.299,99".
            s = s.LastIndexOf(',') > s.LastIndexOf('.')
                ? s.Replace(".", "").Replace(',', '.')
                : Grouped.IsMatch(s) ? s.Replace(",", "") : "";
        }
        else if (hasComma)
        {
            if (DecimalComma.IsMatch(s)) s = s.Replace(',', '.');
            else if (Grouped.IsMatch(s)) s = s.Replace(",", "");
            else return false;
        }
        else if (hasDot && DotGrouped.IsMatch(s))
        {
            s = s.Replace(".", "");
        }

        if (!Plain.IsMatch(s)) return false;
        value = double.Parse(s, NumberStyles.Float, Inv);
        return !double.IsInfinity(value);
    }

    static bool TryParseWords(string s, out double value)
    {
        value = 0;
        if (!char.IsLetter(s[0])) return false;
        if (Words.TryGetValue(s, out value)) return true;

        // "twenty five", "two hundred", "a hundred", "five thousand"
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!Words.TryGetValue(parts[0], out var a) || !Words.TryGetValue(parts[1], out var b)) return false;
        if (b is 100 or 1000 && a < 100) value = a * b;
        else if (a is >= 20 and < 100 && a % 10 == 0 && b is >= 1 and < 10 && b % 1 == 0) value = a + b;
        else return false;
        return true;
    }

    /// <summary>Readable precision: 2 decimals from 10 up, 3 below that, about 4 significant digits under 1.</summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "?";
        var a = Math.Abs(value);
        if (a == 0) return "0";
        if (a >= 1e15) return value.ToString("G6", Inv);
        int decimals = a >= 10 ? 2 : a >= 1 ? 3 : Math.Min(10, 3 - (int)Math.Floor(Math.Log10(a)));
        var r = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (r == 0) return "0";
        return r.ToString("#,0." + new string('#', decimals), Inv);
    }
}
