using Shammy.Core.Music;
using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class WallRailTests
{
    [Fact]
    public void Rail_shows_relevant_cards_in_order()
    {
        var tiles = WallRail.Tiles(new[]
        {
            new WallCard("music", 20, true, 50),
            new WallCard("weather", 8, true),
            new WallCard("clipboard", 60, false),
            new WallCard("calendar", 15, true, 80),
        });
        Assert.Equal(new[] { "weather", "calendar", "music" }, tiles);
    }

    [Fact]
    public void The_most_live_tile_is_focused_first()
    {
        Assert.Equal(2, WallRail.DefaultFocus(new[] { 0, 0, 50, 40, 0 }));
        Assert.Equal(1, WallRail.DefaultFocus(new[] { 0, 90, 90 }));
        Assert.Equal(0, WallRail.DefaultFocus(new[] { 0, 0, 0 }));
        Assert.Equal(-1, WallRail.DefaultFocus(Array.Empty<int>()));
    }

    [Fact]
    public void Focus_stays_on_the_same_card_when_the_rail_changes()
    {
        var tiles = new[] { "weather", "calendar", "music" };
        Assert.Equal(2, WallRail.Keep(tiles, "MUSIC", new[] { 0, 80, 0 }));
        // The focused card left: back to the most live one.
        Assert.Equal(1, WallRail.Keep(tiles, "timer", new[] { 0, 80, 0 }));
        Assert.Equal(1, WallRail.Keep(tiles, null, new[] { 0, 80, 0 }));
    }

    [Theory]
    [InlineData(0, 1, 5, 1)]
    [InlineData(4, 1, 5, 0)]
    [InlineData(0, -1, 5, 4)]
    [InlineData(-1, 1, 5, 0)]
    [InlineData(-1, -1, 5, 4)]
    [InlineData(2, 1, 0, -1)]
    public void Arrows_wrap_around_the_rail(int focus, int delta, int count, int expected) =>
        Assert.Equal(expected, WallRail.Step(focus, delta, count));

    [Fact]
    public void Five_tiles_fit_and_are_centred()
    {
        // 5 × 168 + 4 × 14 = 896, centred in 972.
        Assert.Equal(896, WallRail.Width(5));
        Assert.Equal(38, WallRail.Start(5, 2));
        Assert.Equal(38, WallRail.Start(5, 0));
    }

    [Fact]
    public void A_long_rail_scrolls_to_keep_the_focused_tile_in_view()
    {
        double grow = WallRail.TileWidth * (WallRail.FocusScale - 1) / 2 + WallRail.Gap;
        // First tile focused: the rail starts at its left end, with room for the tile to grow.
        Assert.Equal(grow, WallRail.Start(8, 0), 6);
        // Last tile focused: the right end, with the same room.
        double total = WallRail.Width(8);
        Assert.Equal(WallRail.RailWidth - total - grow, WallRail.Start(8, 7), 6);
        // In the middle: the focused tile is centred.
        double start = WallRail.Start(8, 4);
        double center = start + 4 * (WallRail.TileWidth + WallRail.Gap) + WallRail.TileWidth / 2;
        Assert.Equal(WallRail.RailWidth / 2, center, 6);
    }

    [Fact]
    public void Neighbours_step_away_from_the_focused_tile()
    {
        Assert.Equal(-WallRail.Shift, WallRail.Offset(0, 2));
        Assert.Equal(0, WallRail.Offset(2, 2));
        Assert.Equal(WallRail.Shift, WallRail.Offset(4, 2));
        Assert.Equal(0, WallRail.Offset(1, -1));
    }
}

public class ArtFieldTests
{
    static byte[] Pixels(params (byte R, byte G, byte B, int Count)[] runs)
    {
        var list = new List<byte>();
        foreach (var (r, g, b, n) in runs)
            for (int i = 0; i < n; i++) list.AddRange(new byte[] { b, g, r, 255 });
        return list.ToArray();
    }

    static double Hue(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return 0;
        double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return h < 0 ? h + 360 : h;
    }

    [Fact]
    public void Two_colours_on_a_cover_become_the_two_glows()
    {
        // Mostly rust with a good share of blue, on a grey background that must not win.
        var field = ArtField.Pick(Pixels((200, 80, 40, 120), (40, 70, 200, 60), (128, 128, 128, 200)));
        Assert.InRange(Hue(field.Glow1), 5, 30);
        Assert.InRange(Hue(field.Glow2), 200, 240);
    }

    [Fact]
    public void Field_colours_stay_dark_enough_for_white_text()
    {
        var field = ArtField.Pick(Pixels((255, 230, 0, 256)));
        foreach (var c in new[] { field.Glow1, field.Glow2, field.Base1, field.Base2 })
            Assert.True(Math.Max(c.R, Math.Max(c.G, c.B)) <= 160, c.Hex);
    }

    [Fact]
    public void A_grey_cover_gets_a_neutral_field()
    {
        var field = ArtField.Pick(Pixels((90, 90, 90, 128), (200, 200, 200, 128)));
        Assert.True(Math.Abs(field.Glow1.R - field.Glow1.G) <= 2);
        Assert.Equal(ArtFieldColors.Default, ArtField.Pick(Array.Empty<byte>()));
    }
}
