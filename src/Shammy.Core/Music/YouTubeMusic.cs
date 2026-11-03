using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shammy.Core.Music;

public sealed record YouTubeMusicSong(string VideoId, string Title, string Artist);

/// <summary>
/// Request and response shapes for YouTube Music's own web search endpoint (the one music.youtube.com
/// calls; no key needed). The response is a deep renderer tree that changes often, so parsing walks it
/// for the first song row instead of following a fixed path.
/// </summary>
public static class YouTubeMusic
{
    public const string SearchUrl = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";
    public const string ClientVersion = "1.20261027.01.00";

    /// <summary>The "Songs" filter chip, so videos and podcasts don't win.</summary>
    public const string SongsFilter = "EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D";

    const string Bullet = " \u2022 ";

    public static string WatchUrl(string videoId) => "https://music.youtube.com/watch?v=" + Uri.EscapeDataString(videoId);
    public static string SearchPageUrl(string query) => "https://music.youtube.com/search?q=" + Uri.EscapeDataString(query);

    public static string BuildSearchBody(string query) => new JsonObject
    {
        ["context"] = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = ClientVersion,
                ["hl"] = "en",
            },
        },
        ["query"] = query,
        ["params"] = SongsFilter,
    }.ToJsonString();

    public static YouTubeMusicSong? ParseFirstSong(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (FindProperty(doc.RootElement, "musicResponsiveListItemRenderer") is { } row && FromRow(row) is { } song)
                return song;
            // No song rows (layout change?): settle for any playable id so "play" still does something.
            if (FindProperty(doc.RootElement, "videoId") is { ValueKind: JsonValueKind.String } id)
                return new YouTubeMusicSong(id.GetString()!, "", "");
        }
        catch (JsonException)
        {
        }
        return null;
    }

    static YouTubeMusicSong? FromRow(JsonElement row)
    {
        string? videoId = null;
        if (row.TryGetProperty("playlistItemData", out var pid) && pid.TryGetProperty("videoId", out var v) && v.ValueKind == JsonValueKind.String)
            videoId = v.GetString();
        if (string.IsNullOrEmpty(videoId) && FindProperty(row, "videoId") is { ValueKind: JsonValueKind.String } any)
            videoId = any.GetString();
        if (string.IsNullOrEmpty(videoId)) return null;

        var columns = new List<string>();
        if (row.TryGetProperty("flexColumns", out var cols) && cols.ValueKind == JsonValueKind.Array)
        {
            foreach (var col in cols.EnumerateArray())
                columns.Add(FindProperty(col, "runs") is { ValueKind: JsonValueKind.Array } runs ? JoinRuns(runs) : "");
        }

        var title = columns.Count > 0 ? columns[0] : "";
        var artist = "";
        if (columns.Count > 1)
        {
            // Bullet-separated "Artist, Artist & Artist / Album / 3:40"; unfiltered searches prefix the type ("Song").
            var parts = columns[1].Split(Bullet, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            int first = parts.Length > 1 && parts[0] is "Song" or "Video" or "Episode" ? 1 : 0;
            if (parts.Length > first) artist = parts[first];
        }
        return new YouTubeMusicSong(videoId!, title.Trim(), artist.Trim());
    }

    static string JoinRuns(JsonElement runs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in runs.EnumerateArray())
            if (run.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) sb.Append(text.GetString());
        return sb.ToString();
    }

    /// <summary>Depth-first, document-order search for the first property with this name.</summary>
    static JsonElement? FindProperty(JsonElement e, string name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name)) return p.Value;
                    if (FindProperty(p.Value, name) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (FindProperty(item, name) is { } found) return found;
                break;
        }
        return null;
    }
}
