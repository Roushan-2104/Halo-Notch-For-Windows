using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class GlanceDedupTests
{
    sealed record Item(string Id, string Text, int Priority, bool Plain = true);

    [Fact]
    public void One_item_per_id_and_the_built_in_wins()
    {
        var items = new Item?[] { new("music", "Kesariya", 30, Plain: false), new("battery", "18% battery", 50), null, new("Music", "Kesariya · Arijit", 30) };
        var unique = GlanceOrder.Unique(items, i => i.Id);
        Assert.Equal(new[] { "music", "battery" }, unique.Select(i => i.Id));
        Assert.False(unique[0].Plain);
    }

    [Fact]
    public void Rotation_reaches_every_item_even_if_an_id_repeats()
    {
        var r = new GlanceRotator();
        var ids = new[] { "music", "battery", "music", "time" };
        var seen = Enumerable.Range(0, 8).Select(_ => ids[r.Advance(ids)]).ToList();
        Assert.Contains("time", seen);
        Assert.Equal(new[] { "music", "battery", "music", "time", "music", "battery", "music", "time" }, seen);
    }

    [Fact]
    public void Locate_keeps_to_the_copy_it_was_on()
    {
        var r = new GlanceRotator();
        var ids = new[] { "a", "b", "a" };
        r.Advance(ids);
        r.Advance(ids);
        Assert.Equal(2, r.Advance(ids));
        Assert.Equal(2, r.Locate(ids));
    }

    static readonly string[] Left = { "music", "time" };

    [Fact]
    public void Peek_slot_never_repeats_the_left_side()
    {
        var ordered = new[]
        {
            new Item("music", "Kesariya", 30, Plain: false),
            new Item("songs", "Kesariya · Arijit", 30),   // a module repeating the song under its own id
            new Item("calendar", "Standup 10:30", 20),
            new Item("time", "10:30", 0, Plain: false),
        };
        var top = GlanceOrder.PeekSlot(ordered, i => (i.Id, i.Text, i.Plain), Left, "Kesariya");
        Assert.Equal("calendar", top?.Id);
    }

    [Fact]
    public void Peek_slot_without_a_song_keeps_items_that_mention_the_time()
    {
        var ordered = new[] { new Item("calendar", "Standup 10:30", 20), new Item("time", "10:30", 0, Plain: false) };
        Assert.Equal("calendar", GlanceOrder.PeekSlot(ordered, i => (i.Id, i.Text, i.Plain), Left, "")?.Id);
        Assert.Null(GlanceOrder.PeekSlot(new[] { new Item("time", "10:30", 0, Plain: false) }, i => (i.Id, i.Text, i.Plain), Left, ""));
    }
}
