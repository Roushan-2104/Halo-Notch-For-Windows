using System.Text.RegularExpressions;

namespace Shammy.Core.Music;

/// <summary>One song heard on this PC. Plain data so the app can store it as JSON.</summary>
public sealed class HeardSong
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public int Plays { get; set; }
    public DateTime LastPlayed { get; set; }
}

/// <summary>
/// The songs this PC played lately, so voice can expect their names ("play" + a song the user likes is
/// usually one they've played before). Deduplicated by title and artist, capped, and local only.
/// </summary>
public sealed class ListeningHistory
{
    public const int DefaultCap = 500;

    // Each extra play counts like a week of recency, so favourites outlive songs heard once a while ago.
    static readonly TimeSpan PlayWorth = TimeSpan.FromDays(7);

    static readonly Regex Bracketed = new(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", RegexOptions.CultureInvariant);
    static readonly Regex Noise = new(@"\b(?:official\s+(?:music\s+)?(?:video|audio|lyric(?:al)?\s+video|song)|lyric(?:al)?\s+video|lyrics?|full\s+(?:video\s+)?song|full\s+video|audio\s+song|video\s+song|hd|4k|remastered)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex Featuring = new(@"\s+(?:ft\.?|feat\.?|featuring)\s+.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    // Channels and labels that browser players report as the "artist" of a video.
    static readonly Regex NotAnArtist = new(@"^(?:t-series|zee music company|sony music india|tips official|saregama music|yrf|speed records|vevo|.*\bvevo|.*\s-\stopic)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    readonly List<HeardSong> _songs;
    readonly Dictionary<string, List<HeardSong>> _byKey = new(StringComparer.Ordinal);
    readonly int _cap;

    public ListeningHistory(IEnumerable<HeardSong>? songs = null, int cap = DefaultCap)
    {
        _cap = Math.Max(1, cap);
        _songs = new List<HeardSong>();
        foreach (var s in songs ?? Enumerable.Empty<HeardSong>())
            if (Clean(s.Title).Length > 0) Merge(new HeardSong { Title = Clean(s.Title), Artist = CleanArtist(s.Artist), Plays = Math.Max(1, s.Plays), LastPlayed = s.LastPlayed });
        Trim();
    }

    public IReadOnlyList<HeardSong> Songs => _songs;

    /// <summary>
    /// Notes that a track played. Returns false (and stores nothing) for things that aren't plausibly a song
    /// title: empty, very long (a video or podcast episode title), or mostly not words.
    /// </summary>
    public bool Record(string title, string artist, DateTime now)
    {
        var t = Clean(title);
        if (!LooksLikeSongTitle(t)) return false;
        Merge(new HeardSong { Title = t, Artist = CleanArtist(artist), Plays = 1, LastPlayed = now });
        Trim();
        return true;
    }

    /// <summary>Titles and artists, best first (most played and most recent), without duplicates.</summary>
    public IEnumerable<string> Names()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _songs.OrderByDescending(Score))
        {
            if (seen.Add(s.Title)) yield return s.Title;
            if (s.Artist.Length > 0 && seen.Add(s.Artist)) yield return s.Artist;
        }
    }

    /// <summary>
    /// A track title as a song name: no "(Official Video)", "[Lyrics]", "ft. …", and for video titles like
    /// "Kesariya - Brahmastra | Ranbir Kapoor | Arijit Singh" only the first part.
    /// </summary>
    public static string Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = Bracketed.Replace(title, " ");
        int bar = t.IndexOf('|');
        if (bar > 0) t = t[..bar];
        t = Noise.Replace(t, " ");
        t = Featuring.Replace(t, "");
        t = Spaces.Replace(t, " ").Trim().Trim('-', ':', ',', '.', ' ');
        return t;
    }

    static string CleanArtist(string? artist)
    {
        var a = Spaces.Replace(artist ?? "", " ").Trim();
        a = Featuring.Replace(a, "");
        // "Arijit Singh, Shreya Ghoshal" / "Pritam & Arijit Singh": the first name is the one people say.
        int split = a.IndexOfAny(new[] { ',', '&', ';' });
        if (split > 0) a = a[..split].Trim();
        if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) a = a[..^8].Trim();
        return a.Length > 40 || NotAnArtist.IsMatch(a) ? "" : a;
    }

    static bool LooksLikeSongTitle(string t)
    {
        if (t.Length < 2 || t.Length > 60) return false;
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 8 && t.Count(char.IsLetter) * 2 >= t.Length;
    }

    void Merge(HeardSong song)
    {
        var key = SongMatcher.Key(song.Title);
        if (!_byKey.TryGetValue(key, out var same)) _byKey[key] = same = new List<HeardSong>();
        // The same title is the same song unless both sides name different artists (covers, remakes).
        var existing = same.FirstOrDefault(s => s.Artist.Length == 0 || song.Artist.Length == 0 ||
                                                string.Equals(s.Artist, song.Artist, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _songs.Add(song);
            same.Add(song);
            return;
        }
        existing.Plays += song.Plays;
        if (song.LastPlayed > existing.LastPlayed) existing.LastPlayed = song.LastPlayed;
        if (existing.Artist.Length == 0) existing.Artist = song.Artist;
    }

    static DateTime Score(HeardSong s)
    {
        var bonus = TimeSpan.FromTicks(PlayWorth.Ticks * Math.Min(s.Plays - 1, 50));
        return s.LastPlayed > DateTime.MaxValue - bonus ? DateTime.MaxValue : s.LastPlayed + bonus;
    }

    void Trim()
    {
        if (_songs.Count <= _cap) return;
        var keep = _songs.OrderByDescending(Score).Take(_cap).ToHashSet();
        _songs.RemoveAll(s => !keep.Contains(s));
        foreach (var list in _byKey.Values) list.RemoveAll(s => !keep.Contains(s));
    }
}
