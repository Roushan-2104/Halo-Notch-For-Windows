using System.Reflection;
using System.Text.Json.Serialization;
using Shammy.Core.Settings;
using Shammy.Infrastructure;

namespace Shammy.Modules.Settings;

/// <summary>One setting as the card and the pipe see it.</summary>
internal sealed record SettingInfo(string Name, string Section, string Label, PropertyInfo Property, int Step = 1)
{
    public Type Type => Property.PropertyType;
}

/// <summary>
/// The settings by section, and get/set by name over <see cref="AppSettings"/>'s properties (so a new setting
/// needs only a line here to appear on the card). Setting a value normalises it, saves, and raises
/// <see cref="AppSettings.Changed"/> so features pick it up.
/// </summary>
internal static class SettingsCatalog
{
    public static readonly string[] Sections = { "General", "Notch", "Sounds", "Productivity", "Notifications", "Clipboard", "Voice", "Modules" };

    static readonly (string Name, string Section, string Label, int Step)[] Shown =
    {
        ("DefaultSearchMode", "General", "Search answers with", 1),
        ("Use24Hour", "General", "24-hour clock", 1),
        ("TemperatureUnit", "General", "Temperature", 1),
        ("HomeCurrency", "General", "Home currency", 1),
        ("TranslateTarget", "General", "Translate English to", 1),
        ("HoverSpeed", "Notch", "Hover speed", 1),
        ("PeekEnabled", "Notch", "Peek on hover", 1),
        ("GesturesEnabled", "Notch", "Wheel and swipe gestures", 1),
        ("NotchSounds", "Sounds", "Ping and chime", 1),
        ("NotchSoundVolume", "Sounds", "Volume (also alarms)", 10),
        ("PomodoroWorkMinutes", "Productivity", "Pomodoro focus (min)", 5),
        ("PomodoroShortBreakMinutes", "Productivity", "Short break (min)", 1),
        ("PomodoroLongBreakMinutes", "Productivity", "Long break (min)", 5),
        ("SnoozeMinutes", "Productivity", "Snooze (min)", 1),
        ("ReminderCardSeconds", "Productivity", "Reminder card stays (s)", 5),
        ("ShowNotifications", "Notifications", "Show notifications", 1),
        ("NotificationPopupSeconds", "Notifications", "Pop-up time (s)", 1),
        ("AlwaysHideNotificationText", "Notifications", "Always hide the text", 1),
        ("SystemAlertsEnabled", "Notifications", "Battery and Bluetooth alerts", 1),
        ("MutedNotificationApps", "Notifications", "Muted apps", 1),
        ("ClipboardHistoryEnabled", "Clipboard", "Clipboard history", 1),
        ("ClipboardRetentionHours", "Clipboard", "Keep history (hours)", 6),
        ("CopyPreviewEnabled", "Clipboard", "“Copied” preview", 1),
        ("MicrophoneDeviceId", "Voice", "Microphone", 1),
        ("DisabledModules", "Modules", "Turned off (on next start)", 1),
    };

    static readonly Lazy<List<SettingInfo>> All = new(() =>
    {
        var props = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .ToDictionary(p => p.Name);
        var list = Shown.Where(s => props.ContainsKey(s.Name))
            .Select(s => new SettingInfo(s.Name, s.Section, s.Label, props[s.Name], s.Step)).ToList();
        // Everything else (voice engine, shelf, ...) is still reachable from the pipe, filed under "Other".
        list.AddRange(props.Values.Where(p => list.All(i => i.Name != p.Name)).OrderBy(p => p.Name)
            .Select(p => new SettingInfo(p.Name, "Other", p.Name, p)));
        return list;
    });

    public static IReadOnlyList<SettingInfo> Settings => All.Value;

    public static IEnumerable<SettingInfo> In(string section) => All.Value.Where(s => s.Section == section);

    public static SettingInfo? Find(string name) =>
        All.Value.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static object? Get(AppSettings settings, SettingInfo info) => info.Property.GetValue(settings);

    public static string Describe(AppSettings settings, SettingInfo info) => $"{info.Name}={SettingValue.Format(Get(settings, info))}";

    /// <summary>Sets a value given as text; returns an error message, or null when it was set.</summary>
    public static string? TrySet(AppSettings settings, SettingInfo info, string text)
    {
        if (!SettingValue.TryParse(info.Type, text, out var value))
            return $"{info.Name} takes {SettingValue.Expected(info.Type)}, not '{text}'";
        Set(settings, info, value);
        return null;
    }

    public static void Set(AppSettings settings, SettingInfo info, object? value)
    {
        info.Property.SetValue(settings, value);
        settings.Normalize();
        settings.Save();
        settings.NotifyChanged(info.Name);
    }
}
