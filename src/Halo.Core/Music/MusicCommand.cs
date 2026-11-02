using System.Text.RegularExpressions;

namespace Halo.Core.Music;

public enum MusicAction { PlayYouTubeMusic, PlaySpotify, Pause, Resume, Next, Previous, ShowLyrics }

public sealed record MusicIntent(MusicAction Action, string Query = "");

/// <summary>
/// Recognises music commands in typed or spoken text. Deliberately strict: transport words only match
/// when they are the whole text, so "next week weather" or "pause meaning" still go to web search.
/// </summary>
public static class MusicCommand
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Dictionary<string, MusicAction> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pause"] = MusicAction.Pause,
        ["pause music"] = MusicAction.Pause,
        ["pause the music"] = MusicAction.Pause,
        ["pause song"] = MusicAction.Pause,
        ["pause the song"] = MusicAction.Pause,
        ["stop music"] = MusicAction.Pause,
        ["stop the music"] = MusicAction.Pause,
        ["resume"] = MusicAction.Resume,
        ["resume music"] = MusicAction.Resume,
        ["resume the music"] = MusicAction.Resume,
        ["play"] = MusicAction.Resume,
        ["play music"] = MusicAction.Resume,
        ["unpause"] = MusicAction.Resume,
        ["next"] = MusicAction.Next,
        ["next song"] = MusicAction.Next,
        ["next track"] = MusicAction.Next,
        ["play next song"] = MusicAction.Next,
        ["play the next song"] = MusicAction.Next,
        ["skip"] = MusicAction.Next,
        ["skip song"] = MusicAction.Next,
        ["skip this song"] = MusicAction.Next,
        ["skip track"] = MusicAction.Next,
        ["previous"] = MusicAction.Previous,
        ["previous song"] = MusicAction.Previous,
        ["previous track"] = MusicAction.Previous,
        ["last song"] = MusicAction.Previous,
        ["play previous song"] = MusicAction.Previous,
        ["play the previous song"] = MusicAction.Previous,
        ["go back a song"] = MusicAction.Previous,
        ["go back one song"] = MusicAction.Previous,
        ["lyrics"] = MusicAction.ShowLyrics,
        ["show lyrics"] = MusicAction.ShowLyrics,
        ["show the lyrics"] = MusicAction.ShowLyrics,
        ["open lyrics"] = MusicAction.ShowLyrics,
        ["show me the lyrics"] = MusicAction.ShowLyrics,
    };

    static readonly Regex Play = new(@"^(?:play|put\s+on)\s+(?<q>.+)$", Opts);
    static readonly Regex OnSpotify = new(@"\s+(?:on|in|from|with|using)\s+spotify$", Opts);
    static readonly Regex OnYouTube = new(@"\s+(?:on|in|from|with|using)\s+(?:youtube\s+music|yt\s+music|youtube)$", Opts);
    static readonly Regex Please = new(@"(?:^please\s+|[\s,]+please$)", Opts);

    // "play store", "play station 5 price" are searches, not songs.
    static readonly Regex NotASong = new(@"^(?:store|station|stations|games?|services)\b", Opts);

    public static MusicIntent? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Normalize(text);
        if (t.Length == 0) return null;

        if (Exact.TryGetValue(t, out var action)) return new MusicIntent(action);

        var m = Play.Match(t);
        if (!m.Success) return null;
        // Leading space so "play on spotify" leaves an empty query rather than a song called "on spotify".
        var q = " " + m.Groups["q"].Value.Trim();

        bool spotify = false;
        if (OnSpotify.IsMatch(q))
        {
            spotify = true;
            q = OnSpotify.Replace(q, "");
        }
        else
        {
            q = OnYouTube.Replace(q, "");
        }
        q = q.Trim();
        if (q.Length > 1 && IsQuote(q[0]) && IsQuote(q[^1])) q = q[1..^1].Trim();
        if (q.Length == 0 || NotASong.IsMatch(q)) return null;
        // "play spotify" alone means the app, not a song called "spotify".
        if (q.Equals("spotify", StringComparison.OrdinalIgnoreCase) || q.Equals("youtube music", StringComparison.OrdinalIgnoreCase)) return null;

        return new MusicIntent(spotify ? MusicAction.PlaySpotify : MusicAction.PlayYouTubeMusic, q);
    }

    static bool IsQuote(char c) => c is '"' or '\'' or '\u201C' or '\u201D' or '\u2018' or '\u2019';

    static string Normalize(string text)
    {
        var t = Regex.Replace(text.Trim(), @"\s+", " ");
        t = t.TrimEnd('.', '!', '?', ',', ';').Trim();
        t = Please.Replace(t, "").Trim();
        return t.TrimEnd('.', '!', '?', ',', ';').Trim();
    }
}
