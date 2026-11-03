using System.Text.RegularExpressions;

namespace Shammy.Core.Music;

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

    static readonly Regex Play = new(@"^(?:play|put\s+on|bajao|sunao)\s+(?<q>.+)$", Opts);

    // Hindi puts the verb last: "kesariya bajao", "tum hi ho spotify pe bajao", "arijit ke gaane play karo".
    // Not "chalao" or "laga do": those also start videos and apps ("youtube chalao") and set timers.
    static readonly Regex PlayLast = new(@"^(?<q>.+?)\s+(?:(?:gaana|gana|song)\s+)?" +
        @"(?:baja(?:o|u|ao|aao|do|\s+do)|suna(?:o|do|\s+do)|play\s+kar(?:o|do|\s+do|iye|ijiye|en)?)$", Opts);

    // "gaana bajao" / "koi song sunao": no song named, so nothing to search for.
    static readonly Regex NoSongNamed = new(@"^(?:koi|ek|(?:koi\s+|ek\s+)?(?:achha\s+|accha\s+)?(?:gaana|gana|gaane|gane|song|songs|music))$", Opts);
    static readonly Regex OnSpotify = new(@"(?:\s+(?:on|in|from|with|using)\s+spotify|\s+spotify\s+(?:pe|par|mein|me))$", Opts);
    static readonly Regex OnYouTube = new(@"(?:\s+(?:on|in|from|with|using)\s+(?:youtube\s+music|yt\s+music|youtube)|" +
        @"\s+(?:youtube\s+music|yt\s+music|youtube)\s+(?:pe|par|mein|me))$", Opts);
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
        bool hindi = m.Success && !t.StartsWith("p", StringComparison.OrdinalIgnoreCase);
        if (!m.Success)
        {
            m = PlayLast.Match(t);
            hindi = true;
        }
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
        if (q.Length == 0 || NotASong.IsMatch(q) || (hindi && NoSongNamed.IsMatch(q))) return null;
        // "play spotify" alone means the app, not a song called "spotify".
        if (q.Equals("spotify", StringComparison.OrdinalIgnoreCase) || q.Equals("youtube music", StringComparison.OrdinalIgnoreCase)) return null;

        return new MusicIntent(spotify ? MusicAction.PlaySpotify : MusicAction.PlayYouTubeMusic, q);
    }

    // Looser than Parse on purpose: a first pass that heard "play kesariya" as "Play. What would be?" still
    // starts with the verb, and that is enough to listen again for a song name.
    // Whisper also glues the verb to an unfamiliar word ("Playishk Bulava"); English words that merely start
    // with "play" (player, playstation, playlist...) don't count.
    static readonly Regex LeadVerb = new(@"^\W*(?:please\s+)?(?:(?:play|plays|put\s+on|bajao|sunao)\b|" +
        @"play(?!(?:er|ers|station|stations|list|lists|ground|grounds|ful|ing|ed|back|boy|off|offs|able|time|date|dates|house|wright|thing|things|mate|mates|book|books|store|room|set|pen)\b)(?=[a-z]))" +
        @"[\s.,!?:;-]*(?<rest>.*)$", Opts);
    static readonly Regex TailVerb = new(@"\s(?:baja(?:o|u|ao|aao|do|\s+do)|suna(?:o|do|\s+do)|play\s+kar(?:o|do|\s+do|iye|ijiye|en)?)$", Opts);

    /// <summary>
    /// True when the text sounds like asking for a song, even if the song itself came out garbled. Transport
    /// commands ("play the next song") and "play store"-style searches are not.
    /// </summary>
    public static bool LooksLikePlay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (Parse(text) is { } intent) return intent.Action is MusicAction.PlayYouTubeMusic or MusicAction.PlaySpotify;
        var t = Normalize(text);
        if (LeadVerb.Match(t) is { Success: true } lead)
        {
            var rest = lead.Groups["rest"].Value.Trim();
            return rest.Any(char.IsLetterOrDigit) && !NotASong.IsMatch(rest);
        }
        return TailVerb.IsMatch(t);
    }

    /// <summary>
    /// Puts a corrected song query back into the text it came from ("Play Arijih Singh's songs." becomes
    /// "Play Arijit Singh songs."), leaving the verb, the platform and the punctuation as they were. Returns the
    /// text unchanged when it isn't a play command or nothing needed correcting.
    /// </summary>
    public static string CorrectSong(string text, Func<string, string> correct)
    {
        if (Parse(text) is not { Action: MusicAction.PlayYouTubeMusic or MusicAction.PlaySpotify } intent) return text;
        int at = text.LastIndexOf(intent.Query, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return text;
        var fixedQuery = correct(intent.Query);
        return fixedQuery == intent.Query ? text : text[..at] + fixedQuery + text[(at + intent.Query.Length)..];
    }

    static readonly Regex RepeatedRequest = new(@"^(?<first>.*?\S)\s*[,.;!?]\s*(?:play|put\s+on)\b.*$", Opts);

    /// <summary>
    /// The first request when Whisper said it twice in one sentence ("Play Apna Bana Le, Play Apna Bana Le,"),
    /// which the sentence-level repeat cleaner doesn't see.
    /// </summary>
    public static string FirstRequest(string text)
    {
        var m = RepeatedRequest.Match(text ?? "");
        return m.Success && LooksLikePlay(m.Groups["first"].Value) ? m.Groups["first"].Value.Trim() + "." : text ?? "";
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
