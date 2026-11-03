namespace Shammy.Core.Cards;

/// <summary>
/// One idea of "low battery" for the whole notch: the glance, the pet looking tired, the System card's red
/// ring and the battery icon all say low at <see cref="Low"/> % or below, and the warning pills come at
/// 20, 10 and 5 %.
/// </summary>
public static class BatteryLevels
{
    public const int Low = 20;

    /// <summary>The levels a warning pill appears at, highest first.</summary>
    public static readonly IReadOnlyList<int> Warnings = new[] { 20, 10, 5 };

    public static bool IsLow(int percent) => percent <= Low;

    /// <summary>
    /// The warning to show now, if any: the lowest warning level the battery is at or below that hasn't been
    /// warned about yet (<paramref name="lastWarned"/> is the last level warned, above 100 for none since the
    /// charger came off). Dropping from 25 % straight to 8 % warns once, for 10 %.
    /// </summary>
    public static int? WarningFor(int percent, int lastWarned)
    {
        foreach (var level in Warnings.OrderBy(l => l))
            if (percent <= level && level < lastWarned) return level;
        return null;
    }
}
