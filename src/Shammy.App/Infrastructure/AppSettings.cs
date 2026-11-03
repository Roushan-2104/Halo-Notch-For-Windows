using System.Text.Json;
using System.Text.Json.Serialization;
using Shammy.Core.Search;
using Microsoft.Win32;

namespace Shammy.Infrastructure;

internal sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Swallow the volume keys and show Shammy's HUD instead of Windows' flyout.</summary>
    public bool ReplaceVolumeFlyout { get; set; } = true;

    public bool ShowVisualizer { get; set; } = true;

    /// <summary>The animated multi-colour shammy around the notch.</summary>
    public bool ShammyGlow { get; set; } = true;

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

    public const string EnglishVoice = "english", HinglishVoice = "hinglish";

    /// <summary>
    /// "english" (default) or "hinglish": the opt-in Hinglish model (Hindi + English, written in Latin
    /// letters), only on PCs whose voice runs on a graphics card.
    /// </summary>
    public string VoiceMode { get; set; } = EnglishVoice;

    /// <summary>The voice model this PC was matched to, e.g. "large-v3-turbo@1" (GPU 1) or "small" (CPU).</summary>
    public string? VoiceEngine { get; set; }

    /// <summary>Set while the one-time hardware probe runs; left set means it crashed, so skip the GPUs.</summary>
    public bool VoiceProbeStarted { get; set; }

    public List<string> Shelf { get; set; } = new();

    /// <summary>
    /// Show other apps' notifications in the notch. Windows' own pop-ups can only be turned off by the
    /// user (Do Not Disturb or per-app banners in Settings); Shammy keeps showing notifications either way.
    /// </summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>The one-time "start with Windows?" question has been answered (or startup was already on).</summary>
    public bool StartupAsked { get; set; }

    /// <summary>Shammy's own soft ping for notifications and a chime for timers/reminders.</summary>
    public bool NotchSounds { get; set; } = true;

    /// <summary>App IDs the user muted from the notch.</summary>
    public List<string> MutedNotificationApps { get; set; } = new();

    [JsonIgnore]
    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Shammy") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Shammy", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Shammy", false);
        }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The startup entry used to be called "Halo" and point at Halo.exe. Replace it with "Shammy" pointing at
    /// this exe, and repair a "Shammy" entry whose exe no longer exists (it moved or was renamed).
    /// </summary>
    public static void MigrateStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null || Environment.ProcessPath is not { } exe) return;
            // Only the installed copy may claim the startup entry (a dev build would otherwise take it over).
            if (!exe.Contains(@"\HaloNotch\current\", StringComparison.OrdinalIgnoreCase)) return;
            bool wanted = key.GetValue(OldRunName) is string || key.GetValue("Shammy") is string old && !File.Exists(old.Trim('"'));
            key.DeleteValue(OldRunName, false);
            if (wanted) key.SetValue("Shammy", $"\"{exe}\"");
        }
        catch (Exception ex)
        {
            Log.Warn("Updating the startup entry failed", ex);
        }
    }

    const string OldRunName = "Halo";

    /// <summary>Called by the uninstaller, before Shammy's files are removed.</summary>
    public static void RemoveStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue("Shammy", false);
            key?.DeleteValue(OldRunName, false);
        }
        catch (Exception)
        {
            // Uninstall must not fail over this.
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(ReadSettingsText(), Json) ?? new AppSettings();
                // A hand-edited or half-written file can say null; every list must exist for the rest of Shammy.
                s.Shelf ??= new();
                s.MutedNotificationApps ??= new();
                if (string.IsNullOrWhiteSpace(s.VoiceLanguage)) s.VoiceLanguage = "en";
                if (s.VoiceMode != HinglishVoice) s.VoiceMode = EnglishVoice;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Settings unreadable, using defaults", ex);
            KeepUnreadable();
        }
        return new AppSettings();
    }

    /// <summary>A file another program holds open for a moment (a sync client, an antivirus scan) is read again.</summary>
    static string ReadSettingsText()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(Paths.SettingsFile); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(100); }
        }
    }

    /// <summary>
    /// The first save after starting on defaults would overwrite the unreadable file, and with it the user's pins,
    /// voice choice and muted apps. Keep a copy beside it so a hand-edit gone wrong can be put right.
    /// </summary>
    static void KeepUnreadable()
    {
        try
        {
            var kept = Paths.SettingsFile + ".unreadable";
            File.Copy(Paths.SettingsFile, kept, overwrite: true);
            Log.Warn($"Kept the unreadable settings as {Path.GetFileName(kept)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Couldn't keep a copy of the unreadable settings", ex);
        }
    }

    public void Save()
    {
        try
        {
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Paths.SettingsFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // e.g. settings.json marked read-only: keep running with the in-memory settings.
            Log.Warn("Settings save failed", ex);
        }
    }
}
