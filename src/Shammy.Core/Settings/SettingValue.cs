using System.Globalization;

namespace Shammy.Core.Settings;

public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>
/// Reads a setting's value from text ("settings-set Use24Hour on", the settings card's text fields): bools as
/// on/off/true/false/yes/no/1/0, numbers, enum names (any case), lists as comma-separated text, and
/// null/auto/default/- for "not set" on a nullable setting.
/// </summary>
public static class SettingValue
{
    public static bool TryParse(Type type, string text, out object? value)
    {
        value = null;
        text = text.Trim();
        var inner = Nullable.GetUnderlyingType(type);
        bool nullable = inner is not null || !type.IsValueType;
        if (nullable && text.ToLowerInvariant() is "null" or "auto" or "default" or "-" or "none")
        {
            // A list is never null: "none" clears it.
            if (IsList(type)) value = new List<string>();
            return true;
        }
        var t = inner ?? type;

        if (t == typeof(bool))
        {
            switch (text.ToLowerInvariant())
            {
                case "true" or "on" or "yes" or "1" or "enabled": value = true; return true;
                case "false" or "off" or "no" or "0" or "disabled": value = false; return true;
                default: return false;
            }
        }
        if (t == typeof(int))
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
            value = i;
            return true;
        }
        if (t == typeof(double))
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)) return false;
            value = d;
            return true;
        }
        if (t.IsEnum)
        {
            // Names only: "1" would also parse as an enum, and silently pick whatever has that number.
            if (text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-') return false;
            if (!Enum.TryParse(t, text, ignoreCase: true, out var e) || !Enum.IsDefined(t, e!)) return false;
            value = e;
            return true;
        }
        if (t == typeof(string))
        {
            value = text;
            return true;
        }
        if (IsList(t))
        {
            value = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return true;
        }
        return false;
    }

    static bool IsList(Type t) => t == typeof(List<string>);

    /// <summary>How a value reads back: "on"/"off", enum names, "a,b", "auto" for null.</summary>
    public static string Format(object? value) => value switch
    {
        null => "auto",
        bool b => b ? "on" : "off",
        IEnumerable<string> list => string.Join(",", list),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

/// <summary>The allowed ranges, applied when settings load and whenever one is set.</summary>
public static class SettingLimits
{
    public static int PomodoroMinutes(int v) => Math.Clamp(v, 1, 180);
    public static int SnoozeMinutes(int v) => Math.Clamp(v, 1, 60);
    public static int ReminderCardSeconds(int v) => Math.Clamp(v, 5, 600);
    public static int NotificationPopupSeconds(int v) => Math.Clamp(v, 2, 60);
    public static int ClipboardRetentionHours(int v) => Math.Clamp(v, 1, 24 * 30);
    public static int Volume(int v) => Math.Clamp(v, 0, 100);

    /// <summary>"usd" → "USD"; anything that isn't three letters falls back to INR.</summary>
    public static string CurrencyCode(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return c.Length == 3 && c.All(ch => ch is >= 'A' and <= 'Z') ? c : "INR";
    }

    /// <summary>A Google Translate language code ("hi", "en", "zh-CN"); anything else falls back to Hindi.</summary>
    public static string LanguageCode(string? code)
    {
        var c = (code ?? "").Trim();
        if (c.Length is < 2 or > 8) return "hi";
        foreach (var ch in c)
            if (!(char.IsAsciiLetter(ch) || ch == '-')) return "hi";
        int dash = c.IndexOf('-');
        return dash < 0 ? c.ToLowerInvariant() : c[..dash].ToLowerInvariant() + c[dash..].ToUpperInvariant();
    }
}

/// <summary>Picks the microphone a saved name refers to; WinMM truncates device names to 31 characters.</summary>
public static class DeviceMatch
{
    /// <summary>The index of the device called <paramref name="wanted"/>, or -1 for the Windows default.</summary>
    public static int Find(IReadOnlyList<string> names, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return -1;
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase)) return i;
        // A name saved from a fuller source (or the other way round) still matches its truncated form.
        for (int i = 0; i < names.Count; i++)
        {
            var n = names[i];
            if (n.Length > 0 && (wanted.StartsWith(n, StringComparison.OrdinalIgnoreCase) || n.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return -1;
    }
}
