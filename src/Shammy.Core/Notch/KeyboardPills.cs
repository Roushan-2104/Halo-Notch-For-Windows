using System.Globalization;

namespace Shammy.Core.Notch;

/// <summary>Texts and state for the Caps Lock and input-language pills.</summary>
public static class KeyboardPills
{
    /// <summary>
    /// Caps Lock state after a press. A background app's view of the toggle can lag behind the keyboard, but
    /// every press flips it: if the read still shows the old state, trust the flip instead.
    /// </summary>
    public static bool ResolveCaps(bool read, bool? previous) =>
        previous is { } p && read == p ? !p : read;

    public static string CapsText(bool on) => on ? "Caps Lock on" : "Caps Lock off";

    /// <summary>"ENG → हिन्दी".</summary>
    public static string SwitchText(string from, string to) => $"{from} → {to}";

    /// <summary>
    /// A short name for a keyboard language, like the taskbar's: three capitals for languages written in
    /// Latin letters ("ENG", "FRA"), the language's own name otherwise ("हिन्दी", "日本語").
    /// </summary>
    public static string Label(CultureInfo culture)
    {
        var neutral = culture.IsNeutralCulture || culture.Parent == CultureInfo.InvariantCulture ? culture : culture.Parent;
        var native = neutral.NativeName;
        int paren = native.IndexOf('(');
        if (paren > 0) native = native[..paren].Trim();
        if (native.Length == 0) return culture.Name;
        bool latin = native.All(c => c < 0x0250 || char.IsWhiteSpace(c));
        return latin ? neutral.ThreeLetterISOLanguageName.ToUpperInvariant() : native;
    }

    /// <summary>The label for a Windows language id (the low word of a keyboard layout handle).</summary>
    public static string Label(int languageId)
    {
        try
        {
            return Label(CultureInfo.GetCultureInfo(languageId));
        }
        catch (CultureNotFoundException)
        {
            return $"0x{languageId:X4}";
        }
    }
}
