using Shammy.Core.Music;

namespace Shammy.Core.Tests;

public class SongPickTests
{
    static YouTubeMusicSong S(string title) => new(title, title, "Arijit Singh");

    [Fact]
    public void Parses_every_song_row_in_order()
    {
        const string json = """
        {"a":[
          {"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Kesariya (Diwali Remix)"}]}}}],"playlistItemData":{"videoId":"r1"}}},
          {"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Kesariya"}]}}}],"playlistItemData":{"videoId":"o1"}}},
          {"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Kesariya"}]}}}],"playlistItemData":{"videoId":"o1"}}}
        ]}
        """;
        var songs = YouTubeMusic.ParseSongs(json);
        Assert.Equal(new[] { "r1", "o1" }, songs.Select(s => s.VideoId));
        Assert.Equal("o1", SongPick.Choose(songs, "kesariya")?.VideoId);
        Assert.Empty(YouTubeMusic.ParseSongs("not json"));
    }

    [Fact]
    public void Prefers_the_original_over_remixes_and_festival_edits()
    {
        var results = new[] { S("Kesariya (Diwali Remix)"), S("Kesariya Lofi"), S("Kesariya (From \"Brahmastra\")"), S("Kesariya Slowed + Reverb") };
        Assert.Equal("Kesariya (From \"Brahmastra\")", SongPick.Choose(results, "kesariya")?.Title);
    }

    [Theory]
    [InlineData("kesariya lofi", "Kesariya Lofi")]
    [InlineData("kesariya lo-fi", "Kesariya Lofi")]
    [InlineData("kesariya slowed reverb", "Kesariya Slowed + Reverb")]
    [InlineData("kesariya diwali remix", "Kesariya (Diwali Remix)")]
    [InlineData("kesariya remix", "Kesariya (Diwali Remix)")]
    public void A_variant_the_query_asks_for_wins(string query, string expected)
    {
        var results = new[] { S("Kesariya"), S("Kesariya (Diwali Remix)"), S("Kesariya Lofi"), S("Kesariya Slowed + Reverb") };
        Assert.Equal(expected, SongPick.Choose(results, query)?.Title);
    }

    [Fact]
    public void A_missing_asked_variant_falls_back_to_the_clean_original()
    {
        var results = new[] { S("Kesariya (Remix)"), S("Kesariya") };
        Assert.Equal("Kesariya", SongPick.Choose(results, "kesariya lofi")?.Title);
    }

    [Fact]
    public void Never_swaps_to_a_different_song()
    {
        // The real "kesariya" results: the top hit is a Diwali edit of the right song, the first clean one is another song.
        var results = new[]
        {
            "Kesariya (From \"Brahmastra\") (Tu Fuljhadiyo Wale Tyohaaro Jesi - Happy Diwali)",
            "Kesariya Rajasthani Mashup",
            "Kesariya Balamva",
        };
        Assert.Equal(results[0], SongPick.Choose(results, t => t, "kesariya"));
    }

    [Fact]
    public void All_variants_falls_back_to_the_first()
    {
        var results = new[] { S("Kesariya (Remix)"), S("Kesariya Lofi"), S("Kesariya (Cover)") };
        Assert.Equal("Kesariya (Remix)", SongPick.Choose(results, "kesariya")?.Title);
        Assert.Null(SongPick.Choose(Array.Empty<YouTubeMusicSong>(), "kesariya"));
    }

    [Theory]
    [InlineData("Kesariya (Diwali Remix)", "remix,festival")]
    [InlineData("Tum Hi Ho - Live", "live")]
    [InlineData("Tum Hi Ho (Live at Mumbai)", "live")]
    [InlineData("Tum Hi Ho | Cover by Someone", "cover")]
    [InlineData("Channa Mereya (Unplugged)", "unplugged")]
    [InlineData("Apna Bana Le (8D Audio)", "8d")]
    [InlineData("Kesariya - Sped Up", "sped up")]
    [InlineData("Raataan Lambiyan Mashup", "mashup")]
    [InlineData("Kesariya Karaoke", "karaoke")]
    [InlineData("Kesariya Instrumental", "instrumental")]
    [InlineData("Kesariya Lo Fi", "lofi")]
    [InlineData("Live Forever", "")]
    [InlineData("Cover Me", "")]
    [InlineData("Alive", "")]
    [InlineData("Deliver", "")]
    [InlineData("Kesariya (From \"Brahmastra\")", "")]
    public void Variant_marks_in_titles(string title, string expected)
    {
        Assert.Equal(expected, string.Join(",", SongPick.VariantsIn(title)));
    }

    [Fact]
    public void Loose_words_count_anywhere_in_the_query()
    {
        var results = new[] { S("Kesariya"), S("Kesariya (Live)") };
        Assert.Equal("Kesariya (Live)", SongPick.Choose(results, "kesariya live")?.Title);
        Assert.Equal("Kesariya", SongPick.Choose(results, "kesariya")?.Title);
    }
}
