namespace Shammy.Core.Wings;

/// <summary>
/// One app pinned to the left wing. <see cref="Launch"/> is what starts it: a shortcut Shammy keeps a copy
/// of (so arguments, working folder and icon survive), an exe path, or "shell:AppsFolder\&lt;AUMID&gt;".
/// <see cref="Aumid"/> and <see cref="ExePath"/> are how its running windows are recognised.
/// </summary>
public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    public string Launch { get; set; } = "";
    public string? Aumid { get; set; }
    public string? ExePath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => AppIdentity.Key(Aumid, ExePath, Launch);
}

/// <summary>
/// A top-level app window as the tracker saw it. <see cref="Order"/> is when it was first seen,
/// <see cref="LastActive"/> when it was last in front (0 = not since Shammy started); both only increase.
/// </summary>
public sealed record WindowInfo(
    long Handle,
    string Title,
    string? Aumid,
    string? ExePath,
    long Monitor,
    long Order,
    string? Launch = null,
    bool Flashing = false,
    long LastActive = 0);

/// <summary>An icon on a wing: a pin (maybe running) or an unpinned app with its windows on this monitor.</summary>
public sealed record WingItem(string Key, PinnedApp? Pin, IReadOnlyList<WindowInfo> Windows)
{
    public bool IsRunning => Windows.Count > 0;
    public bool IsFlashing => Windows.Any(w => w.Flashing);
    public bool Contains(long hwnd) => Windows.Any(w => w.Handle == hwnd);
}

public sealed record WingsContent(IReadOnlyList<WingItem> Pinned, IReadOnlyList<WingItem> Open);

/// <summary>How windows are told apart and matched to pins, like the taskbar's grouping.</summary>
public static class AppIdentity
{
    /// <summary>The explicit app ID wins (Store apps, Chrome profiles and web apps share an exe); else the exe.</summary>
    public static string Key(string? aumid, string? exePath, string? fallback = null) =>
        !string.IsNullOrEmpty(aumid) ? "id:" + aumid.ToLowerInvariant()
        : !string.IsNullOrEmpty(exePath) ? "exe:" + exePath.ToLowerInvariant()
        : "launch:" + (fallback ?? "").ToLowerInvariant();

    /// <summary>
    /// When both sides have an app ID it decides (so a Chrome web app isn't Chrome); when one side has none
    /// (most desktop apps set it per process, which other processes can't read) the exe path decides.
    /// </summary>
    public static bool Matches(PinnedApp pin, WindowInfo w)
    {
        if (!string.IsNullOrEmpty(pin.Aumid) && !string.IsNullOrEmpty(w.Aumid))
            return string.Equals(pin.Aumid, w.Aumid, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrEmpty(pin.ExePath) && !string.IsNullOrEmpty(w.ExePath) &&
               string.Equals(pin.ExePath, w.ExePath, StringComparison.OrdinalIgnoreCase);
    }
}

public static class WingsModel
{
    /// <summary>
    /// What one monitor's strip shows. Pins appear on every strip with all their windows (clicking a pin on
    /// any screen finds its window wherever it is). The right wing has the unpinned apps with a window on
    /// this monitor, grouped by app and ordered by when each app's first window appeared, so icons don't
    /// shuffle as focus moves. A window that belongs to a pin never shows on the right too.
    /// </summary>
    public static WingsContent Build(IReadOnlyList<PinnedApp> pins, IReadOnlyList<WindowInfo> windows, long monitor)
    {
        var byPin = pins.Select(_ => new List<WindowInfo>()).ToList();
        var open = new Dictionary<string, List<WindowInfo>>();
        foreach (var w in windows.OrderBy(w => w.Order))
        {
            int pin = -1;
            for (int i = 0; i < pins.Count && pin < 0; i++)
                if (AppIdentity.Matches(pins[i], w)) pin = i;
            if (pin >= 0)
            {
                byPin[pin].Add(w);
                continue;
            }
            if (w.Monitor != monitor) continue;
            var key = AppIdentity.Key(w.Aumid, w.ExePath, "hwnd:" + w.Handle);
            if (!open.TryGetValue(key, out var list)) open[key] = list = new List<WindowInfo>();
            list.Add(w);
        }

        var pinned = pins.Select((p, i) => new WingItem(p.Key, p, byPin[i])).ToList();
        var right = open.Select(kv => new WingItem(kv.Key, null, kv.Value))
            .OrderBy(item => item.Windows[0].Order)
            .ToList();
        return new WingsContent(pinned, right);
    }
}
