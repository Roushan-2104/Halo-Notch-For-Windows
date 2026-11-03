namespace Shammy.Core.Devices;

/// <summary>
/// Low-battery alerts for a Bluetooth headset: one at 20 % and one at 10 %, each once per discharge. A level
/// that climbs back (charged in the case) re-arms the thresholds it is now clearly above, with a little slack
/// so a reading that wobbles between 19 and 21 doesn't alert twice.
/// </summary>
public sealed class EarbudAlerts
{
    public static readonly int[] Thresholds = { 20, 10 };

    /// <summary>How far above a threshold the level must climb before it can alert again.</summary>
    public const int Rearm = 5;

    // Per device: the lowest threshold that has fired this discharge (every threshold above it counts as fired too).
    readonly Dictionary<string, int> _fired = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A new reading for a device (by address). Returns the threshold that was just crossed (20 or 10), or null.
    /// The first reading after connecting already low alerts too: that is exactly when it matters. Dropping
    /// straight past both alerts once, at the lower one.
    /// </summary>
    public int? Observe(string device, int percent)
    {
        if (percent is < 0 or > 100) return null;
        int fired = _fired.TryGetValue(device, out var f) ? f : int.MaxValue;

        // Charged since: a fired threshold re-arms once the level is Rearm points above it.
        if (fired != int.MaxValue)
        {
            var stillFired = Thresholds.Where(t => t >= fired && percent < t + Rearm).ToList();
            fired = stillFired.Count == 0 ? int.MaxValue : stillFired.Min();
        }

        int? crossed = null;
        foreach (var t in Thresholds)
            if (t < fired && percent <= t && (crossed is null || t < crossed)) crossed = t;
        if (crossed is { } c) fired = c;

        if (fired == int.MaxValue) _fired.Remove(device);
        else _fired[device] = fired;
        return crossed;
    }

    /// <summary>"Airdopes 311 Pro · 18% · charge soon" (or "· charge now" at 10 %).</summary>
    public static string Pill(string name, int percent, int threshold) =>
        $"{name} · {percent}% · {(threshold <= 10 ? "charge now" : "charge soon")}";
}
