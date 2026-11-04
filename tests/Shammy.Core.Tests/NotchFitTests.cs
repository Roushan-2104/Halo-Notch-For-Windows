using Shammy.Core.Connect;
using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class NotchFitTests
{
    [Theory]
    [InlineData(0, 720)]
    [InlineData(2, 720)]
    [InlineData(3, 720)]
    [InlineData(5, 1010)]
    [InlineData(9, 1012)]
    public void Open_notch_fits_its_tiles(int tiles, double expected)
    {
        Assert.Equal(expected, WallRail.OpenWidth(tiles));
    }

    [Fact]
    public void Four_tiles_take_only_what_they_need()
    {
        Assert.InRange(WallRail.OpenWidth(4), 721, 1011);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Tiles_stay_in_view_when_the_notch_shrinks(int tiles)
    {
        double rail = WallRail.OpenWidth(tiles) - WallRail.Padding;
        double grow = WallRail.TileWidth * (WallRail.FocusScale - 1) / 2;
        for (int focus = 0; focus < tiles; focus++)
        {
            double left = WallRail.Start(tiles, focus, rail) + focus * (WallRail.TileWidth + WallRail.Gap) - grow;
            Assert.True(left >= -WallRail.Padding / 2 && left + WallRail.TileWidth * WallRail.FocusScale <= rail + WallRail.Padding / 2);
        }
    }

    [Theory]
    [InlineData("meet.google.com is sharing your screen.", "meet.google.com")]
    [InlineData("meet.google.com is sharing your screen", "meet.google.com")]
    [InlineData("You are screen sharing", "Zoom")]
    [InlineData("Sharing control bar | Microsoft Teams", "Microsoft Teams")]
    public void Recognises_screen_sharing_windows(string title, string sharer)
    {
        Assert.Equal(sharer, ScreenShareWindows.Sharer(title));
    }

    [Theory]
    [InlineData("meet.google.com is sharing a window.")]
    [InlineData("meet.google.com is sharing this tab.")]
    [InlineData("How to stop Chrome is sharing your screen - Google Search")]
    [InlineData("Standup - Google Meet")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_everything_else(string? title)
    {
        Assert.Null(ScreenShareWindows.Sharer(title));
    }
}
