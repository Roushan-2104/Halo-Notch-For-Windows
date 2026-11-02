using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Halo.Core.Music;
using Microsoft.Win32;

namespace Halo.Modules.Music;

/// <summary>Finds a song on YouTube Music and starts it in the installed YouTube Music app (a Chrome PWA).</summary>
internal sealed class YouTubeMusicPlayer
{
    // Chrome's id for the YouTube Music web app. The Start menu AUMID uses a shortened form
    // (Chrome._crx_cinhimbnkkghhklpknlkffjgod), but --app-id needs the full 32-letter id.
    const string AppId = "cinhimbnkkaeohfgghhklpknlkffjgod";

    readonly ModuleContext _ctx;

    public YouTubeMusicPlayer(ModuleContext ctx) => _ctx = ctx;

    /// <summary>The first song for <paramref name="query"/>, or null when offline or nothing matched.</summary>
    public async Task<YouTubeMusicSong?> SearchAsync(string query, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, YouTubeMusic.SearchUrl)
        {
            Content = new StringContent(YouTubeMusic.BuildSearchBody(query), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        request.Headers.Referrer = new Uri("https://music.youtube.com/");
        using var response = await _ctx.Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _ctx.Warn($"YouTube Music search returned {(int)response.StatusCode}");
            return null;
        }
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return YouTubeMusic.ParseFirstSong(json);
    }

    /// <summary>Open a music.youtube.com URL in the YouTube Music app, or the default browser without it.</summary>
    public string Open(string url)
    {
        if (FindApp() is { } app)
        {
            try
            {
                var psi = new ProcessStartInfo(app.Chrome) { UseShellExecute = false };
                psi.ArgumentList.Add("--profile-directory=" + app.Profile);
                psi.ArgumentList.Add("--app-id=" + AppId);
                // The same switch the app's jump-list shortcuts use: launches the app straight at a URL in its scope.
                psi.ArgumentList.Add("--app-launch-url-for-shortcuts-menu-item=" + url);
                Process.Start(psi)?.Dispose();
                return "app";
            }
            catch (Exception ex)
            {
                _ctx.Warn("Launching the YouTube Music app failed", ex);
            }
        }
        _ctx.Open(url);
        return "browser";
    }

    public string Describe() => FindApp() is { } app ? $"{app.Chrome} profile={app.Profile}" : "not installed";

    (string Chrome, string Profile)? _app;
    DateTime _appCheckedAt;

    (string Chrome, string Profile)? FindApp()
    {
        // Cheap enough, but re-check occasionally so installing or removing the app is noticed.
        if (DateTime.UtcNow - _appCheckedAt < TimeSpan.FromMinutes(2)) return _app;
        _appCheckedAt = DateTime.UtcNow;
        _app = null;
        try
        {
            var chrome = ChromePath();
            if (chrome is null) return null;
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");
            if (!Directory.Exists(userData)) return null;
            // The app lives in whichever Chrome profile installed it; prefer Default, then the others.
            var profiles = Directory.GetDirectories(userData)
                .Select(Path.GetFileName)
                .Where(n => n is not null && (n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal)))
                .OrderBy(n => n == "Default" ? 0 : 1)
                .ThenBy(n => n, StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                if (Directory.Exists(Path.Combine(userData, profile!, "Web Applications", "_crx_" + AppId)))
                    return _app = (chrome, profile!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _ctx.Warn("Looking for the YouTube Music app failed", ex);
        }
        return null;
    }

    static string? ChromePath()
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var k = root.OpenSubKey(key);
            if (k?.GetValue(null) is string path && File.Exists(path)) return path;
        }
        return null;
    }
}
