using System.Text.Json;
using System.Text.Json.Serialization;
using Halo.Core.Search;
using Microsoft.Win32;

namespace Halo.Infrastructure;

internal sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Swallow the volume keys and show Halo's HUD instead of Windows' flyout.</summary>
    public bool ReplaceVolumeFlyout { get; set; } = true;

    public bool ShowVisualizer { get; set; } = true;

    public SearchMode DefaultSearchMode { get; set; } = SearchMode.Ai;

    /// <summary>Render Google with a phone user agent so its compact layout fits the card.</summary>
    public bool CompactResults { get; set; } = true;

    /// <summary>
    /// false (default): sit at the top of the usable area, below a top taskbar.
    /// true: always on the screen's very top edge, over a top taskbar.
    /// </summary>
    public bool PinToScreenEdge { get; set; }

    /// <summary>Whisper language: "en" is ~4x faster than "auto" (which detects the language per query).</summary>
    public string VoiceLanguage { get; set; } = "en";

    public List<string> Shelf { get; set; } = new();

    [JsonIgnore]
    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Halo") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Halo", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Halo", false);
        }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn("Settings unreadable, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Paths.SettingsFile, true);
        }
        catch (IOException ex)
        {
            Log.Warn("Settings save failed", ex);
        }
    }
}
