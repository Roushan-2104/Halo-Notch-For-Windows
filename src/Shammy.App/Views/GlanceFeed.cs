using System.Globalization;
using Shammy.Core.Notch;
using Shammy.Infrastructure;
using Shammy.Modules;

namespace Shammy.Views;

/// <summary>How an item draws: a dot + text (Plain), bold time + date, bars + song; the pet's moods add an app
/// badge, listening bars and bare text.</summary>
internal enum GlanceKind { Plain, Time, Music, Badge, Listen, Text }

/// <summary>One item of the rotating glance: a module's <see cref="GlanceItem"/> or one of Shammy's own.</summary>
internal sealed record GlanceEntry(string Id, GlanceItem Item, GlanceKind Kind = GlanceKind.Plain, string? Secondary = null);

/// <summary>
/// Everything worth a glance right now: the modules' providers (asked fresh every time) plus the built-ins
/// (time and date, the paused song, the battery when low or charging), most important first.
/// </summary>
internal sealed class GlanceFeed
{
    const int MusicPriority = 30, LowBatteryPriority = 50, ChargingPriority = 10, TimePriority = 0;

    readonly ShammyServices _services;

    public GlanceFeed(ShammyServices services) => _services = services;

    /// <summary>The modules' providers (ModuleRegistry.Glances); set once the modules have started.</summary>
    public IReadOnlyDictionary<string, Func<GlanceItem?>>? Providers { get; set; }

    public List<GlanceEntry> Current()
    {
        var entries = new List<GlanceEntry?>();
        var media = _services.Media;
        if (media.HasSession)
            entries.Add(new GlanceEntry("music", new GlanceItem(media.Title, NotchPalette.Music, MusicPriority), GlanceKind.Music));

        var battery = _services.Battery;
        try
        {
            if (BuiltInGlances.Battery(battery.HasBattery, battery.Percent, battery.IsCharging) is { } text)
            {
                bool charging = battery.IsCharging;
                entries.Add(new GlanceEntry("battery", new GlanceItem(text, charging ? NotchPalette.Battery : NotchPalette.Alarm,
                    charging ? ChargingPriority : LowBatteryPriority)));
            }
        }
        catch (Exception ex)
        {
            // PowerManager can throw on unusual hardware; the glance just skips the battery.
            Log.Warn("Battery glance failed", ex);
        }

        if (Providers is { } providers)
        {
            foreach (var (id, provider) in providers.ToList())
            {
                try
                {
                    if (provider() is { } item) entries.Add(new GlanceEntry(id, item));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Glance '{id}' failed", ex);
                }
            }
        }

        var (time, date) = BuiltInGlances.Time(DateTime.Now, BuiltInGlances.Uses24Hour(CultureInfo.CurrentCulture));
        entries.Add(new GlanceEntry("time", new GlanceItem(time, NotchPalette.Text, TimePriority), GlanceKind.Time, date));

        return GlanceOrder.ByPriority(entries, e => e.Item.Priority);
    }

    /// <summary>The peek's single slot: the most important item that isn't the song or the clock (both already
    /// on the peek's left side).</summary>
    public GlanceEntry? Top() => Current().FirstOrDefault(e => e.Kind == GlanceKind.Plain);

    public string Describe() => string.Join(" | ", Current().Select(e =>
        $"{e.Id}:{e.Item.Text}{(e.Secondary is null ? "" : " " + e.Secondary)} (p{e.Item.Priority})"));
}
