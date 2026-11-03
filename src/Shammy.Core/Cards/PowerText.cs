using System.Globalization;

namespace Shammy.Core.Cards;

/// <summary>Battery wording for the System card: "About 4 h left", "Full by 11:40 AM".</summary>
public static class PowerText
{
    /// <summary>"About 4 h left", "About 1 h 20 min left", "About 35 min left"; rounded so it doesn't flicker.</summary>
    public static string TimeLeft(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return "Almost empty";
        int minutes = (int)Math.Round(left.TotalMinutes);
        if (minutes < 60) return $"About {minutes} min left";
        int h = minutes / 60, m = (int)Math.Round(minutes % 60 / 10.0) * 10;
        if (m == 60) (h, m) = (h + 1, 0);
        return m == 0 ? $"About {h} h left" : $"About {h} h {m} min left";
    }

    /// <summary>
    /// When a charging battery will be full, from the battery's own charge rate (%/h, positive while
    /// charging). Null when the rate is unknown or too small to mean anything.
    /// </summary>
    public static DateTime? FullAt(int percent, double? chargePerHour, DateTime now)
    {
        if (chargePerHour is not double r || r < 0.5 || percent >= 100) return null;
        return now + TimeSpan.FromHours(Math.Min((100 - percent) / r, 24));
    }

    /// <summary>"Full by 11:40 AM".</summary>
    public static string FullBy(DateTime at) => "Full by " + at.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>The compact card's footer while charging: "Charging · full by 11:40", or just "Charging".</summary>
    public static string ChargingLine(DateTime? fullAt) =>
        fullAt is { } at ? "Charging \u00B7 full by " + at.ToString("h:mm", CultureInfo.InvariantCulture) : "Charging";

    /// <summary>The line under the laptop ring: what the battery is doing.</summary>
    public static string Status(bool hasBattery, bool pluggedIn, bool charging, int percent) =>
        !hasBattery ? "No battery" : charging ? "Charging" : pluggedIn ? (percent >= 100 ? "Fully charged" : "Plugged in") : "On battery";

    /// <summary>The ring's colour band: low battery turns the ring to the alarm colour.</summary>
    public static bool IsLow(int percent, bool pluggedIn) => !pluggedIn && BatteryLevels.IsLow(percent);
}
