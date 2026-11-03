using System.Globalization;

namespace Shammy.Core.Cards;

/// <summary>
/// Clock times the way the user's region writes them: "14:05" on a 24-hour clock, "2:05 PM" otherwise. The
/// words around them stay English (the app's language), so the pattern is fixed and only the clock follows
/// the culture.
/// </summary>
public static class ClockFormat
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Whether the culture writes times on a 24-hour clock. The app asks ClockPreference.Is24Hour, which puts the user's setting first.</summary>
    public static bool Uses24Hour(CultureInfo culture) => Productivity.ClockPreference.CultureUses24Hour(culture);

    /// <summary>"14:05" or "2:05 PM".</summary>
    public static string Time(DateTime t, bool twentyFourHour) => t.ToString(twentyFourHour ? "HH:mm" : "h:mm tt", Inv);

    /// <summary>"14:05" or "2:05", for places that show AM/PM separately or not at all.</summary>
    public static string Short(DateTime t, bool twentyFourHour) => t.ToString(twentyFourHour ? "HH:mm" : "h:mm", Inv);
}
