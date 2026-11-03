using Microsoft.Win32;
using Shammy.Core.Wings;
using Shammy.Infrastructure;

namespace Shammy.Modules.Wings;

/// <summary>
/// Copies the taskbar's pins into the wings, once. Desktop apps are pinned as shortcuts in the User
/// Pinned\TaskBar folder; Store apps often only appear in the Taskband registry blob, as app IDs. The
/// shortcuts are copied into Shammy's own folder, so unpinning from the (hidden) taskbar later doesn't
/// break the wings, and launching keeps the shortcut's arguments, start folder and icon.
/// </summary>
internal static class PinImporter
{
    public static string PinsFolder
    {
        get
        {
            var dir = Path.Combine(Paths.Root, "wings", "pins");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static string TaskbarFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>Adds the taskbar's pins that aren't pinned yet to <paramref name="pins"/>; returns how many.</summary>
    public static int Import(List<PinnedApp> pins, Action<string> log)
    {
        byte[]? blob = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
            blob = key?.GetValue("Favorites") as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            log("reading the taskbar's pin order failed: " + ex.Message);
        }

        var lnks = new List<string>();
        try
        {
            if (Directory.Exists(TaskbarFolder))
                lnks.AddRange(Directory.EnumerateFiles(TaskbarFolder, "*.lnk").Select(Path.GetFileName).OfType<string>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log("reading the taskbar's pinned shortcuts failed: " + ex.Message);
        }

        var candidates = new List<(PinnedApp App, string? CopyFrom)>();
        foreach (var name in TaskbandPins.Order(blob, lnks))
        {
            var source = Path.Combine(TaskbarFolder, name);
            var (target, appId) = WingsNative.ReadShortcut(source);
            candidates.Add((new PinnedApp
            {
                Name = Path.GetFileNameWithoutExtension(name),
                Launch = source,
                ExePath = target is not null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null,
                Aumid = appId,
            }, source));
        }
        foreach (var id in TaskbandPins.PackagedAppIds(blob))
        {
            var launch = "shell:AppsFolder\\" + id;
            candidates.Add((new PinnedApp
            {
                Name = WingsNative.DisplayName(launch) ?? TaskbandPins.FallbackName(id),
                Launch = launch,
                Aumid = id,
            }, null));
        }

        // Dedupe first, so only the shortcuts actually kept are copied.
        var accepted = new List<PinnedApp>(pins);
        int before = accepted.Count;
        PinList.Import(accepted, candidates.Select(c => c.App));
        int added = 0;
        foreach (var app in accepted.Skip(before))
        {
            var copyFrom = candidates.First(c => ReferenceEquals(c.App, app)).CopyFrom;
            if (copyFrom is not null)
            {
                try
                {
                    app.Launch = CopyShortcut(copyFrom);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log($"copying a pinned shortcut failed: {ex.Message}");
                    continue;
                }
            }
            pins.Add(app);
            added++;
        }
        log($"imported {added} taskbar pins ({lnks.Count} shortcuts, blob {(blob is null ? "missing" : blob.Length + " bytes")})");
        return added;
    }

    static string CopyShortcut(string source)
    {
        var name = Path.GetFileNameWithoutExtension(source);
        var dest = Path.Combine(PinsFolder, name + ".lnk");
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(PinsFolder, $"{name} ({i}).lnk");
        File.Copy(source, dest);
        return dest;
    }

    /// <summary>Unpinning removes Shammy's own copy of the shortcut (never anything outside its folder).</summary>
    public static void DeleteCopy(PinnedApp app)
    {
        try
        {
            var full = Path.GetFullPath(app.Launch);
            var folder = Path.GetFullPath(PinsFolder) + Path.DirectorySeparatorChar;
            if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
